#!/usr/bin/env python3
"""Experiment 4 (review, referee 3 comment 4): the promise memory against standard tabu tenure.

Input: the results folders of one run of run-exp4.ps1 / run-exp4.sh (--runs results/exp4_<date>_*), each with runs.csv
and restarts/<instance>.csv (states_unique, states_revisits per restart). The setup name is the suffix of the folder
(promise, tabu5, tabu10, tabu15, tabu20, tabudyn, none). The BKS come from experiments/exp3-failure-cases/paper_results.csv.

Outputs (--out, default <parent of the runs>/exp4_<date>/):
  results.csv       one row per setup and instance: cost, routes, gap to BKS (%), fleet vs BKS, mobility
  setups.csv/.md    one row per setup: mean/median gap, wins, fleet counts, mobility, paired test against promise
  pairs.csv         per instance: cost of every setup side by side, best setup
  plots/            gap by setup (box), mobility by setup, gap vs tenure
  summary.md
"""
import argparse
import glob
import os
import re
import sys

import numpy as np
import pandas as pd

HERE = os.path.dirname(os.path.abspath(__file__))
BKS = os.path.join(HERE, "..", "exp3-failure-cases", "paper_results.csv")
COST_TOL = 0.002
ORDER = ["none", "tabu5", "tabu10", "tabu15", "tabu20", "tabudyn", "promise"]


def setup_name(folder):
    return os.path.basename(folder.rstrip("/\\")).split("_")[-1]


def load_run(folder):
    runs = pd.read_csv(os.path.join(folder, "runs.csv"))
    runs["instance"] = runs["instance"].str.replace("\\", "/", regex=False).str.split("/").str[-1]
    runs = runs.drop_duplicates("instance", keep="last")
    rows = []
    for p in glob.glob(os.path.join(folder, "restarts", "**", "*.csv"), recursive=True):
        r = pd.read_csv(p)
        name = os.path.splitext(os.path.basename(p))[0]
        tot = r["states_unique"].fillna(0) + r["states_revisits"].fillna(0)
        rows.append(dict(instance=name,
                         states_unique_share=float((r["states_unique"].sum() / tot.sum())) if tot.sum() > 0 else np.nan,
                         revisits_share=float((r["states_revisits"].sum() / tot.sum())) if tot.sum() > 0 else np.nan,
                         stalls_share=float(r["states_stalls"].sum() / tot.sum()) if "states_stalls" in r and tot.sum() > 0 else np.nan,
                         restarts_without_solution=int(r["best_cost"].isna().sum()),
                         restarts_improving=int(r["improved"].sum()),
                         found_iteration_mean=float(r["found_iteration"].mean())))
    mob = pd.DataFrame(rows) if rows else pd.DataFrame(columns=["instance"])
    return runs.merge(mob, on="instance", how="left")


def md_table(df, fmt="{:.2f}"):
    cols = list(df.columns)
    out = ["| " + " | ".join(map(str, cols)) + " |", "|" + "---|" * len(cols)]
    for _, row in df.iterrows():
        cells = []
        for c in cols:
            v = row[c]
            if isinstance(v, float):
                cells.append("" if np.isnan(v) else (fmt.format(v) if not v.is_integer() else str(int(v))))
            else:
                cells.append(str(v))
        out.append("| " + " | ".join(cells) + " |")
    return "\n".join(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--runs", nargs="+", required=True, help="results folders, one per setup (results/exp4_<date>_*)")
    ap.add_argument("--out")
    ap.add_argument("--bks", default=BKS)
    args = ap.parse_args()
    folders = sorted(set(f for pat in args.runs for f in glob.glob(pat)))
    folders = [f for f in folders if os.path.exists(os.path.join(f, "runs.csv"))]
    if not folders:
        sys.exit("no results folder with runs.csv found")
    stamp = re.sub(r"_[^_]+$", "", os.path.basename(folders[0].rstrip("/\\")))
    out = args.out or os.path.join(os.path.dirname(folders[0].rstrip("/\\")), stamp)
    os.makedirs(out, exist_ok=True)

    bks = pd.read_csv(args.bks)[["instance", "benchmark", "bks_cost", "bks_routes"]]
    frames = []
    for f in folders:
        d = load_run(f)
        d["setup"] = setup_name(f)
        frames.append(d)
    res = pd.concat(frames, ignore_index=True).merge(bks, on="instance", how="left")
    res["benchmark"] = res["benchmark"].fillna("?")
    res["same_fleet"] = res["routes"] == res["bks_routes"]
    res["delta_veh"] = res["routes"] - res["bks_routes"]
    res["gap_pct"] = np.where(res["same_fleet"], 100 * (res["cost"] - res["bks_cost"]) / res["bks_cost"], np.nan)
    res["at_bks"] = res["same_fleet"] & ((res["cost"] - res["bks_cost"]).abs() <= COST_TOL)
    res["better_bks"] = (res["delta_veh"] < 0) | (res["same_fleet"] & (res["cost"] < res["bks_cost"] - COST_TOL))
    res["worse_bks"] = (res["delta_veh"] > 0) | (res["same_fleet"] & (res["cost"] > res["bks_cost"] + COST_TOL))
    res["value"] = res["cost"] + 10000 * res["routes"]   # the solver's objective (fleet first)
    setups = [s for s in ORDER if s in set(res["setup"])] + sorted(set(res["setup"]) - set(ORDER))
    res["setup"] = pd.Categorical(res["setup"], setups, ordered=True)
    res.sort_values(["setup", "instance"]).to_csv(os.path.join(out, "results.csv"), index=False)

    # pairs: cost per setup side by side, relative to promise
    piv = res.pivot_table(index="instance", columns="setup", values="value", observed=True)
    piv_cost = res.pivot_table(index="instance", columns="setup", values="cost", observed=True)
    piv_routes = res.pivot_table(index="instance", columns="setup", values="routes", observed=True)
    pairs = piv_cost.copy()
    pairs.columns = [f"cost_{c}" for c in pairs.columns]
    for c in piv_routes.columns:
        pairs[f"routes_{c}"] = piv_routes[c]
    pairs["best_setup"] = piv.idxmin(axis=1)
    pairs.to_csv(os.path.join(out, "pairs.csv"))

    from scipy import stats
    rows = []
    for s in setups:
        g = res[res["setup"] == s]
        row = dict(setup=s, instances=len(g),
                   mean_gap_pct=g["gap_pct"].mean(), median_gap_pct=g["gap_pct"].median(), max_gap_pct=g["gap_pct"].max(),
                   at_bks=int(g["at_bks"].sum()), better_than_bks=int(g["better_bks"].sum()), worse_than_bks=int(g["worse_bks"].sum()),
                   fleet_below_bks=int((g["delta_veh"] < 0).sum()), fleet_at_bks=int((g["delta_veh"] == 0).sum()), fleet_above_bks=int((g["delta_veh"] > 0).sum()),
                   unique_states_pct=100 * g["states_unique_share"].mean(), revisits_pct=100 * g["revisits_share"].mean(),
                   stalls_pct=100 * g["stalls_share"].mean(),
                   restarts_without_solution=int(g["restarts_without_solution"].fillna(0).sum()),
                   found_iteration_mean=g["found_iteration_mean"].mean())
        if "promise" in piv.columns and s != "promise":
            a = piv["promise"]; b = piv[s]
            both = a.notna() & b.notna()
            row["wins_vs_promise"] = int((b[both] < a[both] - COST_TOL).sum())
            row["ties_vs_promise"] = int(((b[both] - a[both]).abs() <= COST_TOL).sum())
            row["losses_vs_promise"] = int((b[both] > a[both] + COST_TOL).sum())
            d = (b[both] - a[both])
            row["wilcoxon_p"] = float(stats.wilcoxon(d[d != 0]).pvalue) if (d != 0).sum() >= 5 else np.nan
            same = res[(res["setup"] == s)].set_index("instance")["gap_pct"]
            prom = res[(res["setup"] == "promise")].set_index("instance")["gap_pct"]
            row["mean_gap_diff_pct"] = float((same - prom).mean())
        rows.append(row)
    tab = pd.DataFrame(rows)
    tab.to_csv(os.path.join(out, "setups.csv"), index=False)

    # by benchmark
    by = []
    for (s, b), g in res.groupby(["setup", "benchmark"], observed=True):
        by.append(dict(setup=s, benchmark=b, instances=len(g), mean_gap_pct=g["gap_pct"].mean(), at_bks=int(g["at_bks"].sum()),
                       worse_than_bks=int(g["worse_bks"].sum()), fleet_above_bks=int((g["delta_veh"] > 0).sum()),
                       unique_states_pct=100 * g["states_unique_share"].mean()))
    byb = pd.DataFrame(by)
    byb.to_csv(os.path.join(out, "setups_by_benchmark.csv"), index=False)

    # plots
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    pdir = os.path.join(out, "plots"); os.makedirs(pdir, exist_ok=True)
    fig, axes = plt.subplots(1, 2, figsize=(11, 4))
    data = [res.loc[res["setup"] == s, "gap_pct"].dropna().values for s in setups]
    axes[0].boxplot(data, labels=setups, showmeans=True)
    axes[0].axhline(0, color="k", lw=.6)
    axes[0].set_ylabel("cost gap to BKS (%), same fleet")
    axes[0].set_title("Gap to the best-known solution by memory")
    axes[0].tick_params(axis="x", rotation=30)
    axes[1].bar(setups, tab["unique_states_pct"], color="#4C72B0")
    axes[1].set_ylabel("distinct states visited (% of iterations)")
    axes[1].set_title("Search mobility by memory")
    axes[1].tick_params(axis="x", rotation=30)
    fig.tight_layout(); fig.savefig(os.path.join(pdir, "gap_and_mobility.png"), dpi=150); plt.close(fig)

    fig, ax = plt.subplots(figsize=(6, 4))
    for b, g in res.groupby("benchmark", observed=True):
        m = g.groupby("setup", observed=True)["gap_pct"].mean()
        ax.plot(range(len(setups)), [m.get(s, np.nan) for s in setups], marker="o", label=f"{b} ({g['instance'].nunique()} inst.)")
    ax.set_xticks(range(len(setups))); ax.set_xticklabels(setups, rotation=30)
    ax.set_ylabel("mean gap to BKS (%)"); ax.axhline(0, color="k", lw=.6); ax.grid(alpha=.3); ax.legend()
    ax.set_title("Mean gap by memory and benchmark")
    fig.tight_layout(); fig.savefig(os.path.join(pdir, "gap_by_benchmark.png"), dpi=150); plt.close(fig)

    # summary
    L = [f"# Experiment 4: promise memory vs tabu tenure\n",
         f"Runs: {', '.join(os.path.basename(f) for f in folders)}; {res['instance'].nunique()} instances, setups: {', '.join(setups)}. "
         f"Gap = cost gap to the BKS on the same fleet (BKS costs to 3 decimals; within {COST_TOL} counts as equal). "
         f"Mobility = distinct solution states (hash of the arc set) visited per iteration; revisits = iterations whose state had been seen before in the restart. "
         f"The paired test is a Wilcoxon signed-rank test on the solver's objective (cost + 10000 x routes) against the promise setup.\n",
         "## Setups\n",
         md_table(tab[["setup", "mean_gap_pct", "median_gap_pct", "max_gap_pct", "at_bks", "better_than_bks", "worse_than_bks", "fleet_above_bks", "unique_states_pct", "revisits_pct", "stalls_pct", "restarts_without_solution"]]) + "\n",
         "## Against the promise setup (paired by instance)\n"]
    if "wins_vs_promise" in tab.columns:
        L.append(md_table(tab.loc[tab["setup"] != "promise", ["setup", "wins_vs_promise", "ties_vs_promise", "losses_vs_promise", "mean_gap_diff_pct", "wilcoxon_p"]], "{:.3f}") + "\n")
    L.append("## By benchmark\n")
    L.append(md_table(byb) + "\n")
    L.append("## Instances where a tabu setup beats the promise memory\n")
    if "promise" in piv.columns:
        better = []
        for s in setups:
            if s == "promise": continue
            b = piv[s] < piv["promise"] - COST_TOL
            for inst in piv.index[b.fillna(False)]:
                better.append(dict(instance=inst, setup=s, cost=piv_cost.loc[inst, s], routes=piv_routes.loc[inst, s], promise_cost=piv_cost.loc[inst, "promise"], promise_routes=piv_routes.loc[inst, "promise"]))
        L.append((md_table(pd.DataFrame(better), "{:.3f}") if better else "None.") + "\n")
    L.append("## Files\n\nresults.csv, setups.csv, setups_by_benchmark.csv, pairs.csv, plots/gap_and_mobility.png, plots/gap_by_benchmark.png\n")
    with open(os.path.join(out, "summary.md"), "w") as f:
        f.write("\n".join(L))
    print("\n".join(L[:8]))
    print("written to", out)


if __name__ == "__main__":
    main()
