#!/usr/bin/env python3
"""Experiment 1 of the review (referee 3, comment 1a): VRPDO vs VRPTW.

Reads the per-iteration traces of the two setups (settings traceIterations + countMoves) and writes:

  summary_improving.csv      per instance and setup: iterations, mean / median / std of the improving
                             moves per iteration, share of iterations at a local optimum (no improving
                             move), mean evaluated moves, final best cost and routes
  after_option_moves.csv     VRPDO only: improving routing moves in the k iterations after a flip /
                             exchange / kick flip, against the k iterations before it and against all
                             other iterations
  summary.md                 the two tables above aggregated over all instances, ready to read
  plots/trajectory_<inst>.png       gap (%) of the current solution to the best VRPDO solution found,
                                    per iteration: mean over restarts, band = min..max over restarts;
                                    dashed = best solution of the restart so far (mean over restarts)
  plots/improving_<inst>.png        improving moves per iteration (rolling mean), mean and band
  plots/trajectory_all.png          mean gap (%) over all instances, both setups
  plots/after_option_moves.png      improving routing moves around an option change (offset -k..k)

Usage:
  python analyze_exp1.py --vrpdo results/exp1_..._vrpdo --vrptw results/exp1_..._vrptw [--k 10] [--out exp1_analysis]

Gap (%) = 100 * (cost - ref) / ref, ref = best cost reached by the VRPDO setup on that instance (with its
fewest routes). Costs are distances; the route count is reported separately.
"""
import argparse
import glob
import os

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
import pandas as pd  # noqa: E402

ROUTING = ["swap", "relocate", "twoopt"]
OPTION_MOVES = {"flip", "exchange", "kick_flip"}
SETUPS = ["VRPDO", "VRPTW"]
COLORS = {"VRPDO": "#2a6fdb", "VRPTW": "#d9822b"}


def load_traces(results_dir):
    traces = {}
    for path in sorted(glob.glob(os.path.join(results_dir, "traces", "**", "*.csv"), recursive=True)):
        name = os.path.splitext(os.path.basename(path))[0]
        df = pd.read_csv(path)
        if "imp_total" not in df.columns:
            raise SystemExit(f"{path}: no move counts; run with --set countMoves=true")
        df["imp_routing"] = df[[f"imp_{op}" for op in ROUTING]].sum(axis=1, min_count=1)
        df["adm_routing"] = df[[f"adm_{op}" for op in ROUTING]].sum(axis=1, min_count=1)
        traces[name] = df
    if not traces:
        raise SystemExit(f"no traces under {results_dir}/traces")
    return traces


def scanned(df):
    # kick_flip rows have no scan, hence no counts
    return df[df["move"] != "kick_flip"]


def reference_cost(df):
    best = df.dropna(subset=["best_cost"])
    best = best[best["best_routes"] == best["best_routes"].min()]
    return best["best_cost"].min(), int(best["best_routes"].min())


def summary_rows(name, setup, df):
    s = scanned(df)
    ref, routes = reference_cost(df)
    return {
        "instance": name,
        "setup": setup,
        "restarts": df["restart"].nunique(),
        "iterations": len(s),
        "improving_mean": s["imp_total"].mean(),
        "improving_median": s["imp_total"].median(),
        "improving_std": s["imp_total"].std(),
        "improving_routing_mean": s["imp_routing"].mean(),
        "admissible_mean": s["adm_total"].mean(),
        "evaluated_mean": s["eval_total"].mean(),
        "local_optimum_share": (s["imp_total"] == 0).mean(),
        "option_moves": int(df["move"].isin(OPTION_MOVES).sum()),
        "best_cost": ref,
        "best_routes": routes,
    }


def after_option_moves(name, df, k):
    """Improving routing moves at offsets -k..k around each option move (same restart only)."""
    rows = []
    profile = {off: [] for off in range(-k, k + 1)}
    for _, g in df.groupby("restart"):
        g = g.reset_index(drop=True)
        imp = g["imp_routing"].to_numpy(dtype=float)  # NaN on kick rows
        is_opt = g["move"].isin(OPTION_MOVES).to_numpy()
        near = np.zeros(len(g), dtype=bool)
        for i in np.flatnonzero(is_opt):
            for off in range(-k, k + 1):
                j = i + off
                if 0 <= j < len(g) and not np.isnan(imp[j]):
                    profile[off].append(imp[j])
                if off and 0 <= j < len(g):
                    near[j] = True
            after = imp[i + 1:i + 1 + k]
            before = imp[max(0, i - k):i]
            rows.append({"after": np.nanmean(after) if len(after) else np.nan,
                         "before": np.nanmean(before) if len(before) else np.nan})
        other = imp[~near & ~is_opt]
        profile.setdefault("other", []).extend(other[~np.isnan(other)])
    ev = pd.DataFrame(rows)
    baseline = np.mean(profile["other"]) if profile["other"] else np.nan
    summary = {
        "instance": name,
        "option_moves": len(ev),
        f"improving_routing_after_{k}": ev["after"].mean() if len(ev) else np.nan,
        f"improving_routing_before_{k}": ev["before"].mean() if len(ev) else np.nan,
        "improving_routing_other_iterations": baseline,
    }
    summary["after_vs_other_ratio"] = summary[f"improving_routing_after_{k}"] / baseline if baseline else np.nan
    return summary, {off: profile[off] for off in range(-k, k + 1)}, baseline


def per_iteration(df, column):
    """Matrix restarts x iterations of a column (iterations aligned on the iteration index)."""
    piv = df.pivot_table(index="iteration", columns="restart", values=column, aggfunc="last")
    return piv.sort_index()


def band_plot(ax, piv, label, color, smooth=1):
    if smooth > 1:
        piv = piv.rolling(smooth, min_periods=1).mean()
    ax.fill_between(piv.index, piv.min(axis=1), piv.max(axis=1), color=color, alpha=0.18, linewidth=0)
    ax.plot(piv.index, piv.mean(axis=1), color=color, linewidth=1.4, label=f"{label} (mean of {piv.shape[1]} restarts)")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--vrpdo", required=True, help="results/<tag> of setup 1 (VRPDO as is)")
    ap.add_argument("--vrptw", required=True, help="results/<tag> of setup 2 (lockToHome + routingMovesOnly)")
    ap.add_argument("--k", type=int, default=10, help="window after an option move (iterations)")
    ap.add_argument("--smooth", type=int, default=100, help="rolling window for the improving-moves plots")
    ap.add_argument("--out", default="exp1_analysis")
    a = ap.parse_args()

    os.makedirs(os.path.join(a.out, "plots"), exist_ok=True)
    data = {"VRPDO": load_traces(a.vrpdo), "VRPTW": load_traces(a.vrptw)}
    names = sorted(set(data["VRPDO"]) & set(data["VRPTW"]))
    missing = sorted(set(data["VRPDO"]) ^ set(data["VRPTW"]))
    if missing:
        print("only in one setup (skipped):", ", ".join(missing))

    summary, after, profiles, gaps = [], [], [], {s: [] for s in SETUPS}
    for name in names:
        ref, _ = reference_cost(data["VRPDO"][name])
        fig, ax = plt.subplots(figsize=(8, 4.5))
        fig2, ax2 = plt.subplots(figsize=(8, 4.5))
        for setup in SETUPS:
            df = data[setup][name]
            summary.append(summary_rows(name, setup, df))
            gap = 100 * (per_iteration(df, "cost") - ref) / ref
            gaps[setup].append(gap.mean(axis=1).rename(name))
            band_plot(ax, gap, setup, COLORS[setup])
            best_gap = 100 * (per_iteration(df, "best_cost").ffill() - ref) / ref
            ax.plot(best_gap.index, best_gap.mean(axis=1), color=COLORS[setup], linewidth=1, linestyle="--", label=f"{setup} restart best (mean)")
            band_plot(ax2, per_iteration(scanned(df), "imp_total"), setup, COLORS[setup], a.smooth)
        ax.set(title=f"{name}: trajectory", xlabel="iteration", ylabel="gap to best VRPDO solution (%)")
        ax.axhline(0, color="grey", linewidth=0.8)
        ax.legend()
        fig.tight_layout()
        fig.savefig(os.path.join(a.out, "plots", f"trajectory_{name}.png"), dpi=150)
        ax2.set(title=f"{name}: improving moves per iteration (rolling mean {a.smooth})", xlabel="iteration", ylabel="improving moves")
        ax2.legend()
        fig2.tight_layout()
        fig2.savefig(os.path.join(a.out, "plots", f"improving_{name}.png"), dpi=150)
        plt.close(fig)
        plt.close(fig2)

        s, prof, base = after_option_moves(name, data["VRPDO"][name], a.k)
        after.append(s)
        profiles.append((prof, base))

    sm = pd.DataFrame(summary)
    sm.to_csv(os.path.join(a.out, "summary_improving.csv"), index=False)
    af = pd.DataFrame(after)
    af.to_csv(os.path.join(a.out, "after_option_moves.csv"), index=False)

    # Mean trajectory over instances
    fig, ax = plt.subplots(figsize=(8, 4.5))
    for setup in SETUPS:
        m = pd.concat(gaps[setup], axis=1)
        ax.fill_between(m.index, m.quantile(0.1, axis=1), m.quantile(0.9, axis=1), color=COLORS[setup], alpha=0.18, linewidth=0)
        ax.plot(m.index, m.mean(axis=1), color=COLORS[setup], label=f"{setup} (mean over {m.shape[1]} instances, band 10-90%)")
    ax.axhline(0, color="grey", linewidth=0.8)
    ax.set(title="Trajectory over all instances", xlabel="iteration", ylabel="gap to best VRPDO solution (%)")
    ax.legend()
    fig.tight_layout()
    fig.savefig(os.path.join(a.out, "plots", "trajectory_all.png"), dpi=150)
    plt.close(fig)

    # Improving routing moves around an option move
    offs = list(range(-a.k, a.k + 1))
    pooled = {o: [v for prof, _ in profiles for v in prof[o]] for o in offs}
    base = np.nanmean([b for _, b in profiles])
    fig, ax = plt.subplots(figsize=(8, 4.5))
    ax.bar(offs, [np.mean(pooled[o]) if pooled[o] else np.nan for o in offs], color=COLORS["VRPDO"], alpha=0.8)
    ax.axhline(base, color="black", linestyle="--", linewidth=1, label="other iterations")
    ax.set(title="Improving routing moves around an option change (VRPDO)", xlabel="iterations after the flip / exchange (0 = the move)", ylabel="improving routing moves")
    ax.legend()
    fig.tight_layout()
    fig.savefig(os.path.join(a.out, "plots", "after_option_moves.png"), dpi=150)
    plt.close(fig)

    # Markdown summary
    agg = sm.groupby("setup")[["improving_mean", "improving_routing_mean", "admissible_mean", "evaluated_mean", "local_optimum_share", "best_cost", "best_routes"]].mean()
    with open(os.path.join(a.out, "summary.md"), "w") as f:
        f.write(f"# Experiment 1: VRPDO vs VRPTW ({len(names)} instances)\n\n")
        f.write("## Improving moves per iteration (mean over instances)\n\n")
        f.write(agg.to_markdown(floatfmt=".3f") + "\n\n")
        f.write(f"## Improving routing moves around an option change (VRPDO, k = {a.k})\n\n")
        f.write(af.drop(columns="instance").mean().to_frame("mean over instances").to_markdown(floatfmt=".3f") + "\n\n")
        f.write("## Per instance\n\n")
        f.write(sm.to_markdown(index=False, floatfmt=".3f") + "\n")
    print(open(os.path.join(a.out, "summary.md")).read().split("## Per instance")[0])
    print(f"written to {a.out}/")


if __name__ == "__main__":
    main()
