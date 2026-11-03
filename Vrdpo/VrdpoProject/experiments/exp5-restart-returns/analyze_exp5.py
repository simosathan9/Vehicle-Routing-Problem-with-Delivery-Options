#!/usr/bin/env python3
"""Experiment 5 (review): diminishing returns of the multi-restart scheme.

Reads one run folder (results/<tag>/) and rebuilds, per instance, the incumbent after each restart:

* from restarts/<instance>.csv when the run was made with --set logRestarts=true (exact per-restart times), or
* from traces/<instance>.csv (last row of every restart: the restart's best cost and routes); the time of restart r
  is then estimated as r/R of total_time_s in runs.csv (every restart runs the same number of iterations).

Outputs (in --out, default <run>/exp5/):
  per_restart.csv   one row per instance and restart: restart best, incumbent, cost gap to the final solution,
                    fleet excess, elapsed time; plus the expectation over random restart orders
  r_star.csv        per instance: the restart at which the final fleet and the final solution were first reached
  tradeoff.csv/.md  quality at 1, 3, 5, 10 and 15 restarts by benchmark and size (and by class)
  class_found.csv   per class: share of instances whose final solution is already found after r restarts (cumulative)
  class_gap.csv     per class: mean cost gap (%) of the incumbent after r restarts to the final solution
  plots/            convergence curves, r* histograms, per-instance trajectories of the 400-customer instances
  summary.md        the numbers for the response letter
"""
import argparse
import glob
import math
import os
import re
import sys
from multiprocessing import Pool

import numpy as np
import pandas as pd

ROUTE_PENALTY = 10000.0
FOUND_TOL = 1e-3          # cost tolerance for "the final solution is already found" (traces hold 4 decimals)
NAME_RE = re.compile(r"^(U|V|UBC)_(\d+)(small|medium|large)?_(\d+)$")


def parse_name(name):
    m = NAME_RE.match(name)
    if not m:
        return None
    kind, size, window, idx = m.groups()
    bench = "Tilk" if window else "Dumez"
    cls = f"{kind}_{size}{window or ''}"
    return dict(instance=name, set=kind, size=int(size), window=window or "", benchmark=bench, cls=cls, idx=int(idx))


def restart_bests_from_trace(path):
    """Per restart: best cost / routes of the restart and the iteration they were reached at."""
    df = pd.read_csv(path, usecols=["restart", "iteration", "best_cost", "best_routes"])
    rows = []
    for r, g in df.groupby("restart", sort=True):
        last = g.iloc[-1]
        if pd.isna(last["best_cost"]):
            rows.append((int(r), np.nan, np.nan, np.nan))
            continue
        hit = g[(g["best_cost"] == last["best_cost"]) & (g["best_routes"] == last["best_routes"])]
        rows.append((int(r), float(last["best_cost"]), int(last["best_routes"]), int(hit["iteration"].iloc[0])))
    return os.path.splitext(os.path.basename(path))[0], rows


def load_restart_bests(run, jobs):
    """{instance: DataFrame[restart, best_cost, best_routes, found_iteration, restart_time_s, elapsed_s]}, source."""
    logs = sorted(glob.glob(os.path.join(run, "restarts", "**", "*.csv"), recursive=True))
    if logs:
        out = {}
        for p in logs:
            df = pd.read_csv(p)
            out[os.path.splitext(os.path.basename(p))[0]] = df[["restart", "best_cost", "best_routes", "found_iteration",
                                                                 "restart_time_s", "elapsed_s"]]
        return out, "restarts/ (measured times)"
    traces = sorted(glob.glob(os.path.join(run, "traces", "**", "*.csv"), recursive=True))
    if not traces:
        sys.exit(f"neither restarts/ nor traces/ found under {run}")
    print(f"reading {len(traces)} traces with {jobs} processes ...", flush=True)
    with Pool(jobs) as pool:
        got = pool.map(restart_bests_from_trace, traces, chunksize=4)
    out = {}
    for name, rows in got:
        df = pd.DataFrame(rows, columns=["restart", "best_cost", "best_routes", "found_iteration"])
        df["restart_time_s"] = np.nan
        df["elapsed_s"] = np.nan
        out[name] = df
    return out, "traces/ (times estimated as r/R of the total time)"


def penal(cost, routes):
    return cost + routes * ROUTE_PENALTY


def incumbents(df, tol):
    """The solver's rule: a restart replaces the incumbent when its penalised value is lower (tolerance tol)."""
    inc_cost, inc_routes, inc_restart = [], [], []
    bc, br, bidx = math.inf, np.nan, -1   # no incumbent yet (no restart met the service levels so far)
    for _, row in df.iterrows():
        c, k = row["best_cost"], row["best_routes"]
        if not pd.isna(c) and c < 100000 and (bidx < 0 or penal(c, k) < penal(bc, br) + tol):
            bc, br, bidx = float(c), int(k), int(row["restart"])
        inc_cost.append(bc)
        inc_routes.append(br)
        inc_restart.append(bidx)
    return np.array(inc_cost), np.array(inc_routes), np.array(inc_restart)


def expected_over_orders(values, final_cost, final_routes):
    """E[.] of the best of r restarts drawn at random (without replacement), r = 1..n, from the order statistics.

    values: list of (cost, routes) per restart (NaN cost = no feasible restart-best).
    Returns per r: expected fleet excess and cost gap (%) given that a solution exists, P(final fleet),
    P(final solution), P(no solution meeting the service levels).
    """
    keys = []
    for c, k in values:
        keys.append((math.inf, 10**6) if pd.isna(c) else (penal(c, k), c, k))
    keys.sort(key=lambda t: t[0])
    n = len(keys)
    out = []
    for r in range(1, n + 1):
        denom = math.comb(n, r)
        ex_fleet = ex_gap = p_fleet = p_found = p_none = 0.0
        for k_idx, key in enumerate(keys):
            p = math.comb(n - 1 - k_idx, r - 1) / denom
            if p == 0:
                continue
            if key[0] == math.inf:
                p_none += p
                continue
            _, c, k = key
            ex_fleet += p * (k - final_routes)
            ex_gap += p * (100 * (c - final_cost) / final_cost)
            p_fleet += p * (k == final_routes)
            p_found += p * (k == final_routes and abs(c - final_cost) <= FOUND_TOL)
        psol = 1 - p_none
        out.append((ex_fleet / psol if psol > 0 else np.nan, ex_gap / psol if psol > 0 else np.nan, p_fleet, p_found, p_none))
    return out


def build_per_restart(bests, runs):
    recs = []
    mismatches = []
    for name, df in bests.items():
        meta = parse_name(name)
        if meta is None or name not in runs.index:
            continue
        run = runs.loc[name]
        tol = 0.0 if run["type"] == "int" else 0.001
        n = len(df)
        inc_cost, inc_routes, inc_restart = incumbents(df, tol)
        final_cost, final_routes = float(inc_cost[-1]), int(inc_routes[-1])
        if final_routes != int(run["routes"]) or abs(final_cost - float(run["cost"])) > 2e-3:
            mismatches.append((name, final_cost, final_routes, run["cost"], run["routes"]))
        exp = expected_over_orders(list(zip(df["best_cost"], df["best_routes"])), final_cost, final_routes)
        measured = df["elapsed_s"].notna().all()
        for i in range(n):
            r = i + 1
            elapsed = float(df["elapsed_s"].iloc[i]) if measured else float(run["total_time_s"]) * r / n
            has = inc_restart[i] >= 0
            recs.append(dict(
                meta,
                r=r,
                restart_best_cost=df["best_cost"].iloc[i],
                restart_best_routes=df["best_routes"].iloc[i],
                found_iteration=df["found_iteration"].iloc[i],
                inc_cost=inc_cost[i] if has else np.nan, inc_routes=inc_routes[i], inc_restart=inc_restart[i],
                final_cost=final_cost, final_routes=final_routes,
                has_solution=int(has),
                fleet_excess=inc_routes[i] - final_routes if has else np.nan,
                gap_pct=100 * (inc_cost[i] - final_cost) / final_cost if has else np.nan,
                at_final_fleet=int(has and inc_routes[i] == final_routes),
                found_final=int(has and inc_routes[i] == final_routes and abs(inc_cost[i] - final_cost) <= FOUND_TOL),
                elapsed_s=elapsed, time_measured=int(measured),
                exp_fleet_excess=exp[i][0], exp_gap_pct=exp[i][1], exp_p_final_fleet=exp[i][2], exp_p_found=exp[i][3],
                exp_p_no_solution=exp[i][4],
            ))
    return pd.DataFrame(recs), mismatches


def r_star(per):
    rows = []
    for name, g in per.groupby("instance", sort=False):
        g = g.sort_values("r")
        rf = g.loc[g["at_final_fleet"] == 1, "r"].min()
        rc = g.loc[g["found_final"] == 1, "r"].min()
        first = g.iloc[0]
        rows.append(dict(
            instance=name, benchmark=first["benchmark"], set=first["set"], size=first["size"], cls=first["cls"],
            restarts=int(g["r"].max()),
            final_cost=first["final_cost"], final_routes=first["final_routes"],
            r_fleet=int(rf), r_cost=int(rc),
            time_to_fleet_s=float(g.loc[g["r"] == rf, "elapsed_s"].iloc[0]),
            time_to_final_s=float(g.loc[g["r"] == rc, "elapsed_s"].iloc[0]),
            total_time_s=float(g["elapsed_s"].max()),
            gap_r1_pct=float(first["gap_pct"]) if first["at_final_fleet"] else np.nan,
            fleet_excess_r1=first["fleet_excess"],
            restarts_without_solution=int(g["restart_best_cost"].isna().sum()),
            restarts_improving=int((g["inc_restart"].diff().fillna(1) != 0).sum()),
        ))
    return pd.DataFrame(rows)


def tradeoff(per, by, budgets):
    """Quality after r restarts (actual order and expected over orders), per group."""
    rows = []
    R = int(per["r"].max())
    for key, g in per.groupby(by, sort=True):
        key = key if isinstance(key, tuple) else (key,)
        n_inst = g["instance"].nunique()
        for r in budgets:
            h = g[g["r"] == r]
            if h.empty:
                continue
            same = h[h["at_final_fleet"] == 1]
            rows.append({**dict(zip(by, key)), "instances": n_inst, "r": r,
                         "time_share_%": round(100 * r / R, 1),
                         "no_solution_%": round(100 * (1 - h["has_solution"].mean()), 1),
                         "final_fleet_%": round(100 * h["at_final_fleet"].mean(), 1),
                         "mean_fleet_excess": round(h["fleet_excess"].mean(), 3),
                         "mean_gap_%": round(same["gap_pct"].mean(), 2) if len(same) else np.nan,
                         "median_gap_%": round(same["gap_pct"].median(), 2) if len(same) else np.nan,
                         "max_gap_%": round(same["gap_pct"].max(), 2) if len(same) else np.nan,
                         "found_final_%": round(100 * h["found_final"].mean(), 1),
                         "exp_final_fleet_%": round(100 * h["exp_p_final_fleet"].mean(), 1),
                         "exp_mean_gap_%": round(h["exp_gap_pct"].mean(), 2),
                         "exp_found_final_%": round(100 * h["exp_p_found"].mean(), 1),
                         "mean_elapsed_min": round(h["elapsed_s"].mean() / 60, 1)})
    return pd.DataFrame(rows)


def wide(per, value, agg, by="cls"):
    t = per.pivot_table(index=by, columns="r", values=value, aggfunc=agg)
    t.columns = [f"R{c}" for c in t.columns]
    t.insert(0, "instances", per.groupby(by)["instance"].nunique())
    return t


def sort_classes(index):
    def key(c):
        m = NAME_RE.match(c + "_1")
        kind, size, window, _ = m.groups()
        return ({"U": 0, "V": 1, "UBC": 2}[kind], int(size), {"": 0, "small": 1, "medium": 2, "large": 3}[window or ""])
    return sorted(index, key=key)


def md_table(df, floatfmt="{:.1f}"):
    cols = list(df.columns)
    lines = ["| " + " | ".join(str(c) for c in cols) + " |", "|" + "---|" * len(cols)]
    for _, row in df.iterrows():
        cells = []
        for c in cols:
            v = row[c]
            if isinstance(v, float):
                cells.append("" if np.isnan(v) else (floatfmt.format(v) if not float(v).is_integer() or "gap" in str(c) else f"{int(v)}"))
            else:
                cells.append(str(v))
        lines.append("| " + " | ".join(cells) + " |")
    return "\n".join(lines)


def plots(per, rs, out):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    pdir = os.path.join(out, "plots")
    os.makedirs(pdir, exist_ok=True)
    R = int(per["r"].max())
    xs = np.arange(1, R + 1)

    # 1. Convergence of the cost gap, Dumez by size and Tilk by size (instances at the final fleet only).
    fig, axes = plt.subplots(1, 2, figsize=(11, 4), sharey=False)
    for ax, bench in zip(axes, ["Dumez", "Tilk"]):
        g = per[(per["benchmark"] == bench)]
        for size, h in g.groupby("size"):
            same = h[h["at_final_fleet"] == 1].groupby("r")["gap_pct"].mean().reindex(xs)
            ax.plot(xs, same.values, marker="o", ms=3, label=f"{size} customers (actual order)")
            exp = h.groupby("r")["exp_gap_pct"].mean().reindex(xs)
            ax.plot(xs, exp.values, ls="--", lw=1, color=ax.lines[-1].get_color(), label=f"{size} (expected, random order)")
        ax.set_title(f"{bench} et al.: incumbent cost gap to the 15-restart solution")
        ax.set_xlabel("restarts used (r)")
        ax.set_ylabel("mean gap to final (%)")
        ax.set_xticks(xs)
        ax.grid(alpha=.3)
        ax.legend(fontsize=7, ncol=2)
    fig.tight_layout()
    fig.savefig(os.path.join(pdir, "convergence_gap.png"), dpi=150)
    plt.close(fig)

    # 2. Share of instances whose final solution / final fleet is already found after r restarts.
    fig, axes = plt.subplots(1, 2, figsize=(11, 4))
    for ax, bench in zip(axes, ["Dumez", "Tilk"]):
        g = per[per["benchmark"] == bench]
        for size, h in g.groupby("size"):
            found = h.groupby("r")["found_final"].mean().reindex(xs) * 100
            ax.plot(xs, found.values, marker="o", ms=3, label=f"{size}: final solution")
            fleet = h.groupby("r")["at_final_fleet"].mean().reindex(xs) * 100
            ax.plot(xs, fleet.values, ls=":", lw=1, color=ax.lines[-1].get_color(), label=f"{size}: final fleet")
        ax.set_title(f"{bench} et al.: share of instances already at the final result")
        ax.set_xlabel("restarts used (r)")
        ax.set_ylabel("% of instances")
        ax.set_ylim(0, 102)
        ax.set_xticks(xs)
        ax.grid(alpha=.3)
        ax.legend(fontsize=7, ncol=2)
    fig.tight_layout()
    fig.savefig(os.path.join(pdir, "convergence_found.png"), dpi=150)
    plt.close(fig)

    # 3. Histogram of r* (restart at which the final solution was found), by size.
    fig, axes = plt.subplots(1, 2, figsize=(11, 4), sharey=False)
    for ax, bench in zip(axes, ["Dumez", "Tilk"]):
        g = rs[rs["benchmark"] == bench]
        sizes = sorted(g["size"].unique())
        data = [g.loc[g["size"] == s, "r_cost"].values for s in sizes]
        ax.hist(data, bins=np.arange(0.5, R + 1.5, 1), stacked=True, label=[f"{s} customers" for s in sizes])
        ax.set_title(f"{bench} et al.: restart at which the final solution was found")
        ax.set_xlabel("r*")
        ax.set_ylabel("instances")
        ax.set_xticks(xs)
        ax.legend(fontsize=8)
    fig.tight_layout()
    fig.savefig(os.path.join(pdir, "r_star_hist.png"), dpi=150)
    plt.close(fig)

    # 4. Per-instance trajectories of the largest instances (the referee's case): one panel per set.
    size_max = per.loc[per["benchmark"] == "Dumez", "size"].max()
    big = per[(per["benchmark"] == "Dumez") & (per["size"] == size_max)]
    if not big.empty:
        sets = [k for k in ["UBC", "U", "V"] if k in set(big["set"])]
        fig, axes = plt.subplots(len(sets), 1, figsize=(8, 3.1 * len(sets)), sharex=True)
        axes = np.atleast_1d(axes)
        top = float(np.nanmax(big.loc[big["at_final_fleet"] == 1, "gap_pct"]))
        ymax = top * 1.35 + 0.5   # room for the legend (top band) and the fleet marks (below it)
        for ax, kind in zip(axes, sets):
            g = big[big["set"] == kind]
            names = sorted(g["instance"].unique(), key=lambda n: int(n.split("_")[-1]))
            for j, name in enumerate(names):
                h = g[g["instance"] == name].sort_values("r")
                y = np.where(h["at_final_fleet"] == 1, h["gap_pct"], np.nan)
                line, = ax.plot(h["r"], y, marker="o", ms=3, lw=1.2, label=name.split("_")[-1])
                extra = h[h["fleet_excess"] > 0]
                if not extra.empty:
                    ax.scatter(extra["r"], [top * 1.18 - 0.1 * j] * len(extra), marker="x", s=18, color=line.get_color(), zorder=3)
            ax.set_ylim(-0.1, ymax)
            ax.set_ylabel("gap to final (%)")
            ax.set_title(f"{kind}_{int(size_max)}: cost gap of the incumbent after r restarts", fontsize=10, loc="left")
            ax.grid(alpha=.3)
            ax.legend(title="instance", fontsize=7, title_fontsize=7, ncol=10, loc="upper center", frameon=True, handlelength=1.5, columnspacing=0.8)
        axes[-1].set_xticks(xs)
        axes[-1].set_xlabel("restarts used (r)")
        fig.text(0.01, 0.005, "x: incumbent still one vehicle above the final fleet (plotted at the top, in the instance's colour)", fontsize=7, color="#444")
        fig.tight_layout(rect=(0, 0.02, 1, 1))
        fig.savefig(os.path.join(pdir, "trajectories_largest.png"), dpi=160)
        plt.close(fig)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--run", required=True, help="results folder of the run (with runs.csv and restarts/ or traces/)")
    ap.add_argument("--out", help="output folder (default <run>/exp5)")
    ap.add_argument("--jobs", type=int, default=max(1, (os.cpu_count() or 2) - 2))
    ap.add_argument("--budgets", default="1,3,5,10,15")
    args = ap.parse_args()
    out = args.out or os.path.join(args.run, "exp5")
    os.makedirs(out, exist_ok=True)
    budgets = [int(b) for b in args.budgets.split(",")]

    runs = pd.read_csv(os.path.join(args.run, "runs.csv"))
    runs["instance"] = runs["instance"].str.replace("\\", "/", regex=False).str.split("/").str[-1]
    runs = runs.drop_duplicates("instance", keep="last").set_index("instance")

    bests, source = load_restart_bests(args.run, args.jobs)
    per, mismatches = build_per_restart(bests, runs)
    if per.empty:
        sys.exit("no instance of the run could be analysed")
    per.to_csv(os.path.join(out, "per_restart.csv"), index=False)
    rs = r_star(per)
    rs.to_csv(os.path.join(out, "r_star.csv"), index=False)

    R = int(per["r"].max())
    budgets = [b for b in budgets if b <= R] + ([R] if R not in budgets else [])
    t_bench = tradeoff(per, ["benchmark", "size"], budgets)
    t_cls = tradeoff(per, ["cls"], budgets)
    t_all = tradeoff(per, ["benchmark"], budgets)
    pd.concat([t_all, t_bench, t_cls]).to_csv(os.path.join(out, "tradeoff.csv"), index=False)

    found = wide(per, "found_final", lambda s: round(100 * s.mean(), 1)).loc[sort_classes(per["cls"].unique())]
    found.to_csv(os.path.join(out, "class_found.csv"))
    gap = wide(per[per["at_final_fleet"] == 1], "gap_pct", lambda s: round(s.mean(), 2))
    gap = gap.reindex(sort_classes(per["cls"].unique()))
    gap["instances"] = per.groupby("cls")["instance"].nunique()
    gap.to_csv(os.path.join(out, "class_gap.csv"))
    fleet = wide(per, "at_final_fleet", lambda s: round(100 * s.mean(), 1)).loc[sort_classes(per["cls"].unique())]
    fleet.to_csv(os.path.join(out, "class_fleet.csv"))

    plots(per, rs, out)

    # ---- summary ----
    L = []
    L.append("# Experiment 5: diminishing returns of the restarts\n")
    L.append(f"Run: `{os.path.abspath(args.run)}`; per-restart data from {source}; {per['instance'].nunique()} instances, "
             f"{R} restarts each. The incumbent after r restarts is the solver's own rule (fewer routes first, then cost) "
             f"applied to restarts 1..r in the order they ran (seed = restart index), which is exactly what a run with "
             f"`restarts = r` would return. 'Expected' columns average over all orders of the restarts.\n")
    if mismatches:
        L.append(f"**{len(mismatches)} instances where the rebuilt final incumbent differs from runs.csv** (check): "
                 + ", ".join(f"{m[0]} ({m[1]:.3f}/{m[2]} vs {m[3]:.3f}/{m[4]})" for m in mismatches[:10]) + "\n")
    else:
        L.append("The rebuilt 15-restart incumbent equals runs.csv (cost and routes) on every instance.\n")

    L.append("## Where the final result comes from\n")
    for bench, g in rs.groupby("benchmark"):
        n = len(g)
        L.append(f"**{bench} et al.** ({n} instances):")
        L.append(f"- final fleet already reached by restart 1 on {int((g['r_fleet'] == 1).sum())} instances "
                 f"({100 * (g['r_fleet'] == 1).mean():.0f}%); by restart 3 on {int((g['r_fleet'] <= 3).sum())}; "
                 f"by restart 5 on {int((g['r_fleet'] <= 5).sum())}; last at restart {int(g['r_fleet'].max())}.")
        L.append(f"- final solution found by restart 1 on {int((g['r_cost'] == 1).sum())} ({100 * (g['r_cost'] == 1).mean():.0f}%), "
                 f"by 3 on {int((g['r_cost'] <= 3).sum())} ({100 * (g['r_cost'] <= 3).mean():.0f}%), "
                 f"by 5 on {int((g['r_cost'] <= 5).sum())} ({100 * (g['r_cost'] <= 5).mean():.0f}%), "
                 f"by 10 on {int((g['r_cost'] <= 10).sum())} ({100 * (g['r_cost'] <= 10).mean():.0f}%); "
                 f"median r* = {g['r_cost'].median():.0f}, mean {g['r_cost'].mean():.1f}.")
        L.append(f"- restarts that improved the incumbent: mean {g['restarts_improving'].mean():.1f} of {R}.")
        nos = g[g["restarts_without_solution"] > 0]
        L.append(f"- restarts that ended without a solution meeting the service levels: {int(g['restarts_without_solution'].sum())} "
                 f"of {n * R} ({100 * g['restarts_without_solution'].sum() / (n * R):.1f}%), on {len(nos)} instances"
                 + (" (" + ", ".join(f"{t.instance}: {t.restarts_without_solution}" for t in nos.sort_values('restarts_without_solution', ascending=False).itertuples()) + ")" if len(nos) else "") + ".")
        g1 = g.dropna(subset=["gap_r1_pct"])
        L.append(f"- gap of the first restart alone (instances at the final fleet after 1 restart, n={len(g1)}): "
                 f"mean {g1['gap_r1_pct'].mean():.2f}%, median {g1['gap_r1_pct'].median():.2f}%, max {g1['gap_r1_pct'].max():.2f}%.\n")

    L.append("## Trade-off table: quality after r restarts\n")
    L.append("`no_solution_%`: instances with no solution meeting the service levels yet; `final_fleet_%`: instances whose incumbent has the final fleet; `mean/median/max_gap_%`: cost gap to the final "
             "solution among those; `found_final_%`: instances whose incumbent already is the final solution; `exp_*`: the "
             "same averaged over all orders of the restarts; `time_share_%` = r/R; `mean_elapsed_min`: wall time to the end "
             "of restart r" + (" (estimated as r/R of the total, under the VM's concurrency)" if "estimated" in source else "") + ".\n")
    cols = ["r", "time_share_%", "no_solution_%", "final_fleet_%", "mean_fleet_excess", "mean_gap_%", "median_gap_%", "max_gap_%",
            "found_final_%", "exp_final_fleet_%", "exp_mean_gap_%", "exp_found_final_%", "mean_elapsed_min"]
    for bench, g in t_all.groupby("benchmark"):
        L.append(f"### {bench} et al. (all sizes, {int(g['instances'].iloc[0])} instances)\n")
        L.append(md_table(g[cols], "{:.2f}") + "\n")
    for (bench, size), g in t_bench.groupby(["benchmark", "size"]):
        L.append(f"### {bench} et al., {size} customers ({int(g['instances'].iloc[0])} instances)\n")
        L.append(md_table(g[cols], "{:.2f}") + "\n")

    L.append("## Per class: share of instances (%) whose final solution is already found after r restarts\n")
    L.append(md_table(found.reset_index().rename(columns={"index": "class"})) + "\n")
    L.append("## Per class: share of instances (%) whose incumbent has the final fleet after r restarts\n")
    L.append(md_table(fleet.reset_index().rename(columns={"index": "class"})) + "\n")
    L.append("## Per class: mean cost gap (%) of the incumbent after r restarts (instances at the final fleet)\n")
    L.append(md_table(gap.reset_index().rename(columns={"index": "class"}), "{:.2f}") + "\n")

    L.append("## Instances whose final result came late\n")
    late = rs[rs["r_cost"] > 10].sort_values(["r_cost", "size"], ascending=False)
    if late.empty:
        L.append("None: every final solution was found by restart 10.\n")
    else:
        L.append(f"{len(late)} instances found their final solution after restart 10:\n")
        L.append(md_table(late[["instance", "final_routes", "final_cost", "r_fleet", "r_cost", "gap_r1_pct", "fleet_excess_r1"]], "{:.2f}") + "\n")
    late_fleet = rs[rs["r_fleet"] > 1].sort_values(["r_fleet"], ascending=False)
    L.append(f"{len(late_fleet)} instances needed more than one restart for the final fleet"
             + (": " + ", ".join(f"{r.instance} (r={r.r_fleet})" for r in late_fleet.itertuples()) if len(late_fleet) else "") + ".\n")

    L.append("## Files\n")
    L.append("per_restart.csv, r_star.csv, tradeoff.csv, class_found.csv, class_fleet.csv, class_gap.csv, "
             "plots/convergence_gap.png, plots/convergence_found.png, plots/r_star_hist.png, plots/trajectories_largest.png\n")
    with open(os.path.join(out, "summary.md"), "w") as f:
        f.write("\n".join(L))
    print("\n".join(L[:40]))
    print(f"\nwritten to {out}")


if __name__ == "__main__":
    main()
