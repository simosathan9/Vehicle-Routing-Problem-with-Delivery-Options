#!/usr/bin/env python3
"""Experiment 2 of the review (referee 3, comment 1b): inter-route coupling.

Reads the coupling counts of one or more result folders (setting countCoupling) and writes:

  feasibility_by_instance.csv   per instance, operator and category: evaluated, capacity-feasible and feasible
                                candidates, feasibility rate (%), capacity pass rate (%), time-window pass rate
                                given capacity (%)
  summary.csv                   the same pooled over all instances and per instance set (Dumez et al. / Tilk
                                et al.), plus the mean and std of the per-instance rates and the ratio of each
                                category's rate to the IDL-only category of its operator
  summary.md                    the tables above, ready to read
  plots/feasibility_rate.png            grouped bars: feasibility rate and time-window pass rate per category
                                        (mean over instances, whiskers = std)
  plots/feasibility_rate_by_set.png     the same per instance set
  plots/feasibility_by_instance.png     feasibility rate per instance and category, one panel per operator
  plots/feasibility_over_iterations.png feasibility rate along the search (iteration bins), per category

Usage:
  python analyze_exp2.py --results results/exp2_..._coupling [results/other_tag ...] [--out exp2_analysis]

Definitions (see Search/CouplingStats.cs): an inter-route relocation or swap candidate is *evaluated* when
the scan generates it, whatever its cost; *capacity-feasible* when the receiving route(s) stay within the
vehicle capacity; *feasible* when in addition they stay time-window feasible. Categories by the location type
of the transferred stop(s): IDL = private address, SDL = shared locker.
  feasibility rate            = feasible / evaluated
  capacity pass rate          = capacity-feasible / evaluated
  time-window pass rate       = feasible / capacity-feasible   (the coupling measure net of the load)
"""
import argparse
import glob
import os

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
import pandas as pd  # noqa: E402

OPERATORS = {"relocate": ["idl", "sdl"], "swap": ["idl_idl", "idl_sdl", "sdl_sdl"]}
LABELS = {"idl": "IDL", "sdl": "SDL", "idl_idl": "IDL-IDL", "idl_sdl": "IDL-SDL", "sdl_sdl": "SDL-SDL"}
# Number of shared locations a category involves; the baseline of each operator is the category with none.
SDL_COUNT = {"idl": 0, "sdl": 1, "idl_idl": 0, "idl_sdl": 1, "sdl_sdl": 2}
BASELINE = {"relocate": "idl", "swap": "idl_idl"}
# One colour per number of SDLs involved (fixed, never cycled).
COLORS = {0: "#2a78d6", 1: "#eb6834", 2: "#1baf7a"}
SDL_LEGEND = {0: "no shared location involved", 1: "one shared location", 2: "two shared locations"}
RATES = {
    "feasibility_rate": ("feasible", "evaluated", "feasibility rate (%)"),
    "capacity_rate": ("capacity_ok", "evaluated", "capacity pass rate (%)"),
    "tw_rate_given_capacity": ("feasible", "capacity_ok", "time-window pass rate given capacity (%)"),
}


def instance_set(name):
    return "Tilk et al." if any(s in name for s in ("small", "medium", "large")) else "Dumez et al."


def columns():
    for op, cats in OPERATORS.items():
        for cat in cats:
            yield op, cat


def load(results_dirs):
    """Per-iteration frames by instance (when the coupling/ folder exists) and the per-instance totals."""
    per_iteration, totals = {}, []
    for d in results_dirs:
        files = sorted(glob.glob(os.path.join(d, "coupling", "**", "*.csv"), recursive=True))
        if files:
            for path in files:
                name = os.path.splitext(os.path.basename(path))[0]
                df = pd.read_csv(path)
                per_iteration[name] = df
                totals.extend(totals_row(name, df.drop(columns=["restart", "iteration", "routes"]).sum(), df["restart"].nunique(), len(df)))
        elif os.path.exists(os.path.join(d, "coupling_totals.csv")):
            t = pd.read_csv(os.path.join(d, "coupling_totals.csv"))
            for _, r in t.iterrows():
                totals.extend(totals_row(os.path.basename(r["instance"]), r, r["restarts"], r["iterations"]))
        else:
            raise SystemExit(f"{d}: no coupling/ folder and no coupling_totals.csv; run with --set countCoupling=true")
    if not totals:
        raise SystemExit("no coupling counts found")
    return per_iteration, pd.DataFrame(totals)


def totals_row(name, sums, restarts, iterations):
    rows = []
    for op, cat in columns():
        rows.append({
            "instance": name, "set": instance_set(name), "restarts": int(restarts), "iterations": int(iterations),
            "operator": op, "category": cat, "sdl_count": SDL_COUNT[cat],
            "evaluated": int(sums[f"eval_{op}_{cat}"]),
            "capacity_ok": int(sums[f"cap_{op}_{cat}"]),
            "feasible": int(sums[f"feas_{op}_{cat}"]),
        })
    return rows


def add_rates(df):
    df = df.copy()
    for rate, (num, den, _) in RATES.items():
        df[rate] = 100 * df[num] / df[den].replace(0, np.nan)
    return df


def pooled(df, by):
    """Counts summed over instances within `by`, rates recomputed, plus mean/std of the per-instance rates."""
    g = df.groupby(by + ["operator", "category", "sdl_count"], sort=False)
    out = g[["evaluated", "capacity_ok", "feasible"]].sum().reset_index()
    out["instances"] = g.size().values
    out = add_rates(out)
    per_inst = add_rates(df)
    for rate in RATES:
        stats = per_inst.groupby(by + ["operator", "category"], sort=False)[rate].agg(["mean", "std", "min", "max"]).reset_index()
        stats = stats.rename(columns={"mean": f"{rate}_mean", "std": f"{rate}_std", "min": f"{rate}_min", "max": f"{rate}_max"})
        out = out.merge(stats, on=by + ["operator", "category"], how="left")
    # Ratio to the IDL-only category of the operator (pooled rates and mean of per-instance ratios).
    for rate in ("feasibility_rate", "tw_rate_given_capacity"):
        base = out[out["category"].isin(BASELINE.values())][by + ["operator", rate]].rename(columns={rate: "base"})
        out = out.merge(base, on=by + ["operator"], how="left")
        out[f"{rate}_ratio_to_idl"] = out[rate] / out["base"]
        out = out.drop(columns="base")
        pi = per_inst[by + ["instance", "operator", "category", rate]]
        pb = pi[pi["category"].isin(BASELINE.values())].drop(columns="category").rename(columns={rate: "base"})
        pi = pi.merge(pb, on=by + ["instance", "operator"], how="left")
        pi["ratio"] = pi[rate] / pi["base"]
        r = pi.groupby(by + ["operator", "category"], sort=False)["ratio"].agg(["mean", "min", "max"]).reset_index()
        r = r.rename(columns={"mean": f"{rate}_ratio_mean", "min": f"{rate}_ratio_min", "max": f"{rate}_ratio_max"})
        out = out.merge(r, on=by + ["operator", "category"], how="left")
    return out


def bar_chart(ax, table, rate, title):
    """Grouped bars over the five (operator, category) slots; height = mean over instances, whisker = std."""
    x, heights, errs, cols, ticks = [], [], [], [], []
    pos = 0
    for op, cats in OPERATORS.items():
        for cat in cats:
            r = table[(table["operator"] == op) & (table["category"] == cat)]
            if r.empty:
                continue
            x.append(pos)
            heights.append(float(r[f"{rate}_mean"].iloc[0]))
            errs.append(float(r[f"{rate}_std"].fillna(0).iloc[0]))
            cols.append(COLORS[SDL_COUNT[cat]])
            ticks.append(f"{op}\n{LABELS[cat]}")
            pos += 1
        pos += 0.6
    bars = ax.bar(x, heights, width=0.8, color=cols, yerr=errs, capsize=3, error_kw={"linewidth": 1, "color": "#555555"})
    for b, h in zip(bars, heights):
        ax.annotate(f"{h:.1f}", (b.get_x() + b.get_width() / 2, h), xytext=(0, 3), textcoords="offset points", ha="center", va="bottom", fontsize=8, color="#333333")
    ax.set_xticks(x)
    ax.set_xticklabels(ticks, fontsize=8)
    ax.set_ylabel(RATES[rate][2])
    ax.set_title(title, fontsize=10)
    ax.spines[["top", "right"]].set_visible(False)
    ax.grid(axis="y", color="#dddddd", linewidth=0.6)
    ax.set_axisbelow(True)
    ax.set_ylim(0, max(100, ax.get_ylim()[1]) if rate != "feasibility_rate" else None)


def legend_handles():
    return [plt.Rectangle((0, 0), 1, 1, color=COLORS[k], label=SDL_LEGEND[k]) for k in (0, 1, 2)]


def plot_rates(table, title, path):
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.2))
    bar_chart(axes[0], table, "feasibility_rate", "Feasible / evaluated")
    bar_chart(axes[1], table, "tw_rate_given_capacity", "Feasible / capacity-feasible")
    fig.legend(handles=legend_handles(), loc="lower center", ncol=3, frameon=False, fontsize=8, bbox_to_anchor=(0.5, -0.02))
    fig.suptitle(title, fontsize=11)
    fig.tight_layout(rect=(0, 0.06, 1, 0.95))
    fig.savefig(path, dpi=150)
    plt.close(fig)


def plot_by_set(by_set, path):
    sets = list(dict.fromkeys(by_set["set"]))
    fig, axes = plt.subplots(len(sets), 2, figsize=(11, 4 * len(sets)), squeeze=False)
    for i, s in enumerate(sets):
        t = by_set[by_set["set"] == s]
        n = int(t["instances"].iloc[0])
        bar_chart(axes[i][0], t, "feasibility_rate", f"{s} ({n} instances): feasible / evaluated")
        bar_chart(axes[i][1], t, "tw_rate_given_capacity", f"{s} ({n} instances): feasible / capacity-feasible")
    fig.legend(handles=legend_handles(), loc="lower center", ncol=3, frameon=False, fontsize=8, bbox_to_anchor=(0.5, -0.01))
    fig.tight_layout(rect=(0, 0.04, 1, 1))
    fig.savefig(path, dpi=150)
    plt.close(fig)


def plot_by_instance(per_instance, path):
    names = sorted(per_instance["instance"].unique(), key=lambda n: (instance_set(n), n))
    fig, axes = plt.subplots(2, 1, figsize=(max(8, 0.35 * len(names)), 7.5), sharex=True)
    for ax, (op, cats) in zip(axes, OPERATORS.items()):
        width = 0.8 / len(cats)
        idx = np.arange(len(names))
        for k, cat in enumerate(cats):
            vals = [per_instance[(per_instance["instance"] == n) & (per_instance["operator"] == op) & (per_instance["category"] == cat)]["feasibility_rate"].iloc[0] for n in names]
            ax.bar(idx + (k - (len(cats) - 1) / 2) * width, vals, width=width * 0.95, color=COLORS[SDL_COUNT[cat]], label=LABELS[cat])
        ax.set_title(f"{op}: feasibility rate per instance", fontsize=10)
        ax.set_ylabel("feasible / evaluated (%)")
        ax.legend(frameon=False, fontsize=8, ncol=len(cats))
        ax.spines[["top", "right"]].set_visible(False)
        ax.grid(axis="y", color="#dddddd", linewidth=0.6)
        ax.set_axisbelow(True)
    axes[1].set_xticks(np.arange(len(names)))
    axes[1].set_xticklabels(names, rotation=90, fontsize=7)
    fig.tight_layout()
    fig.savefig(path, dpi=150)
    plt.close(fig)


def plot_over_iterations(per_iteration, bins, path):
    """Pooled feasibility rate per iteration bin (all instances and restarts), per operator and category."""
    frames = []
    for name, df in per_iteration.items():
        d = df.copy()
        d["bin"] = (d["iteration"] // bins) * bins
        frames.append(d.drop(columns=["restart", "iteration", "routes"]).groupby("bin").sum())
    s = pd.concat(frames).groupby(level=0).sum()
    fig, axes = plt.subplots(1, 2, figsize=(11, 4), sharey=True)
    for ax, (op, cats) in zip(axes, OPERATORS.items()):
        for cat in cats:
            rate = 100 * s[f"feas_{op}_{cat}"] / s[f"eval_{op}_{cat}"].replace(0, np.nan)
            ax.plot(s.index, rate, color=COLORS[SDL_COUNT[cat]], linewidth=2, label=LABELS[cat])
            ax.annotate(LABELS[cat], (s.index[-1], rate.iloc[-1]), xytext=(4, 0), textcoords="offset points", fontsize=8, va="center", color=COLORS[SDL_COUNT[cat]])
        ax.set_title(f"{op}: feasibility rate along the search (bins of {bins} iterations)", fontsize=10)
        ax.set_xlabel("iteration")
        ax.legend(frameon=False, fontsize=8)
        ax.spines[["top", "right"]].set_visible(False)
        ax.grid(axis="y", color="#dddddd", linewidth=0.6)
        ax.set_axisbelow(True)
    axes[0].set_ylabel("feasible / evaluated (%)")
    fig.suptitle("Pooled over instances and restarts", fontsize=10)
    fig.tight_layout()
    fig.savefig(path, dpi=150)
    plt.close(fig)


def md_table(table, by, cols):
    t = table[by + ["operator", "category"] + cols].copy()
    t["category"] = t["category"].map(LABELS)
    return t.to_markdown(index=False, floatfmt=".2f")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--results", nargs="+", required=True, help="results/<tag> folder(s) of runs with countCoupling=true")
    ap.add_argument("--bins", type=int, default=500, help="iteration bin width of the over-iterations plot")
    ap.add_argument("--out", default="exp2_analysis")
    a = ap.parse_args()
    os.makedirs(os.path.join(a.out, "plots"), exist_ok=True)

    per_iteration, totals = load(a.results)
    per_instance = add_rates(totals)
    per_instance.to_csv(os.path.join(a.out, "feasibility_by_instance.csv"), index=False)

    overall = pooled(totals, [])
    overall.insert(0, "set", "all")
    by_set = pooled(totals, ["set"])
    summary = pd.concat([overall, by_set], ignore_index=True)
    summary.to_csv(os.path.join(a.out, "summary.csv"), index=False)

    n = totals["instance"].nunique()
    plot_rates(overall, f"Inter-route candidates by location type ({n} instances; bars = mean over instances, whiskers = std)",
               os.path.join(a.out, "plots", "feasibility_rate.png"))
    plot_by_set(by_set, os.path.join(a.out, "plots", "feasibility_rate_by_set.png"))
    plot_by_instance(per_instance, os.path.join(a.out, "plots", "feasibility_by_instance.png"))
    if per_iteration:
        plot_over_iterations(per_iteration, a.bins, os.path.join(a.out, "plots", "feasibility_over_iterations.png"))

    counts = ["instances", "evaluated", "capacity_ok", "feasible"]
    rates = ["feasibility_rate", "feasibility_rate_mean", "feasibility_rate_std", "feasibility_rate_ratio_to_idl", "feasibility_rate_ratio_mean",
             "feasibility_rate_ratio_min", "feasibility_rate_ratio_max"]
    tw = ["capacity_rate", "tw_rate_given_capacity", "tw_rate_given_capacity_mean", "tw_rate_given_capacity_std", "tw_rate_given_capacity_ratio_to_idl",
          "tw_rate_given_capacity_ratio_mean"]
    with open(os.path.join(a.out, "summary.md"), "w") as f:
        f.write(f"# Experiment 2: inter-route coupling ({n} instances, {int(totals['iterations'].sum() / len(list(columns())))} scanned iterations)\n\n")
        f.write("Rates in %. `feasibility_rate` is pooled (sum of counts over instances), `_mean`/`_std` are over the per-instance rates. "
                "`ratio_to_idl` divides the pooled rate by the operator's IDL-only category; `ratio_mean/min/max` summarise the per-instance ratios.\n\n")
        f.write("## Feasibility rate (feasible / evaluated), all instances\n\n")
        f.write(md_table(overall, [], counts + rates) + "\n\n")
        f.write("## Capacity pass rate and time-window pass rate given capacity, all instances\n\n")
        f.write(md_table(overall, [], tw) + "\n\n")
        f.write("## Per instance set\n\n")
        f.write(md_table(by_set, ["set"], ["instances", "feasibility_rate", "feasibility_rate_ratio_to_idl", "tw_rate_given_capacity", "tw_rate_given_capacity_ratio_to_idl"]) + "\n\n")
        f.write("## Per instance\n\n")
        f.write(md_table(per_instance, ["instance"], ["evaluated", "feasible", "feasibility_rate", "tw_rate_given_capacity"]) + "\n")
    print(open(os.path.join(a.out, "summary.md")).read().split("## Per instance\n")[0])
    print(f"written to {a.out}/")


if __name__ == "__main__":
    main()
