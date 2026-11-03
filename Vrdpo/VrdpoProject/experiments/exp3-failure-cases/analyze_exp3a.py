#!/usr/bin/env python3
"""Experiment 3A of the review (referee 3, comment 3): which instance features go with the failures?

Joins the paper's per-instance results (paper_results.csv: local search vs BKS) with the structural
features of the instances (instance_features.csv) and writes:

  claim_check.md / .csv      the referee's numbers re-derived from the tables: per benchmark and per
                             class, how many cases have the BKS fleet, and of those how many are worse /
                             equal / better in cost, and how many need more / fewer vehicles
  merged.csv                 one row per instance with the features, the results and the degradation
                             metrics
  correlations.csv           Pearson r and Spearman rho (with p-values) of every feature against every
                             degradation metric, pooled and per benchmark
  tightness.csv              Dumez et al. instances by the slack of the BKS fleet over the bin-packing
                             bound (bks_routes - demand_lb) and by the fill of the BKS vehicles: how many
                             of them lose a vehicle (the fleet failures are expected at slack 0, fill > 0.99)
  groups.csv                 median of every feature in the failing and the non-failing instances, with
                             the Mann-Whitney p-value (fleet failures among the Dumez et al. instances;
                             cost failures among the same-fleet cases of each benchmark)
  summary.md                 the tables above, features ordered by |rho|, ready to read
  plots/scatter_<metric>.png the strongest features against the metric, one panel per feature
  plots/heatmap.png          Spearman rho of feature x metric, per benchmark

Degradation metrics (one row per instance with a BKS):
  delta_veh       = ls_routes - bks_routes (vehicle inflation; > 0 is a fleet failure)
  gap_cost        = 100 * (ls_cost - bks_cost) / bks_cost, every instance (a fleet failure has a large gap)
  gap_cost_same   = gap_cost on the same-fleet cases only (NaN otherwise); > 0 is a cost failure
  fail_fleet      = delta_veh > 0;   fail_cost = same fleet and ls_cost > bks_cost + 0.002 (the BKS is quoted to 3 decimals)

Derived features that need the BKS: fill_bks = demand_total / (capacity * bks_routes), fleet_slack_bks =
bks_routes - demand_lb (0: the BKS fleet is at the bin-packing bound), customers_per_vehicle = n / bks_routes.

Solver side: by default the paper's results (paper_results.csv, from the submitted spreadsheets). With
--runs results/<tag>/runs.csv the solver's cost, fleet and service levels come from that run instead (the
last solve of every instance), the BKS side stays the one of paper_results.csv, and instances the run did
not solve are left out. The joined table is written to results.csv in --out, in the paper_results.csv
format: that is the per-instance result table of the revised solver.

Usage (from Vrdpo/VrdpoProject):
  python experiments/exp3-failure-cases/analyze_exp3a.py [--results ...paper_results.csv] [--features ...instance_features.csv] [--out exp3a_analysis]
  python experiments/exp3-failure-cases/analyze_exp3a.py --runs results/exp3_<date>_all/runs.csv --out results/exp3_<date>_all/exp3a
"""
import argparse
import os
import warnings

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
import pandas as pd  # noqa: E402
from scipy import stats  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ID_COLS = {"instance", "benchmark", "set", "size", "tw_class", "result"}
METRICS = ["delta_veh", "gap_cost", "gap_cost_same"]
# The BKS costs of the tables are quoted to 3 decimals and the solver's cost is a double: a difference within
# this is the same cost (the run reproduces the paper's costs to 0.0005).
COST_TOL = 0.002
COLORS = {"tilk": "#d9822b", "dumez": "#2a6fdb"}


def with_run(r, runs):
    """Replaces the solver side of the paper's results with a run's runs.csv (last solve per instance)."""
    u = pd.read_csv(runs)
    u["instance"] = u["instance"].astype(str).str.replace("\\", "/", regex=False).str.split("/").str[-1]
    u = u.drop_duplicates("instance", keep="last").set_index("instance")
    if "feasible" in u.columns and (u["feasible"] == 0).any():
        print(f"warning: infeasible solves in the run: {', '.join(u.index[u['feasible'] == 0])}")
    missing = [n for n in r["instance"] if n not in u.index]
    if missing:
        print(f"{len(missing)} instances of the result tables not in the run, left out: {', '.join(missing)}")
    r = r[r["instance"].isin(u.index)].copy()
    extra = [n for n in u.index if n not in set(r["instance"])]
    if extra:
        print(f"{len(extra)} solves of the run have no row in the result tables (no BKS), left out: {', '.join(extra)}")
    r["ls_cost"] = u.loc[r["instance"], "cost"].to_numpy()
    r["ls_routes"] = u.loc[r["instance"], "routes"].to_numpy()
    r["sl0"] = u.loc[r["instance"], "sl0"].to_numpy()
    r["sl1"] = u.loc[r["instance"], "sl1"].to_numpy()
    r["result"] = np.select([r["ls_routes"] > r["bks_routes"], r["ls_routes"] < r["bks_routes"],
                             r["ls_cost"] > r["bks_cost"] + COST_TOL, r["ls_cost"] < r["bks_cost"] - COST_TOL],
                            ["MoreVehicles", "FewerVehicles", "Worse", "Better"], default="Optimal")
    r.loc[r["bks_cost"].isna(), "result"] = ""
    return r


def load(results, features, runs=None):
    r = pd.read_csv(results)
    if runs:
        r = with_run(r, runs)
    f = pd.read_csv(features)
    d = r.merge(f.drop(columns=["benchmark", "set"]), on="instance", how="left")
    d["has_features"] = d["n_customers"].notna()
    d["has_bks"] = d["bks_cost"].notna()
    d["delta_veh"] = d["ls_routes"] - d["bks_routes"]
    d["gap_cost"] = 100 * (d["ls_cost"] - d["bks_cost"]) / d["bks_cost"]
    d["same_fleet"] = d["delta_veh"] == 0
    d["gap_cost_same"] = d["gap_cost"].where(d["same_fleet"])
    d["fail_fleet"] = d["delta_veh"] > 0
    d["fail_cost"] = d["same_fleet"] & (d["ls_cost"] - d["bks_cost"] > COST_TOL)
    d["better_cost"] = d["same_fleet"] & (d["ls_cost"] - d["bks_cost"] < -COST_TOL)
    d["fill_bks"] = d["demand_total"] / (d["vehicle_capacity"] * d["bks_routes"])
    d["fleet_slack_bks"] = d["bks_routes"] - d["demand_lb"]
    d["customers_per_vehicle"] = d["n_customers"] / d["bks_routes"]
    return d


def claim_check(d):
    rows = []

    def row(label, g):
        with_bks = g[g["has_bks"]]
        same = with_bks[with_bks["same_fleet"]]
        rows.append({
            "class": label, "instances": len(g), "with_bks": len(with_bks),
            "same_fleet": len(same), "worse_cost": int(same["fail_cost"].sum()),
            "equal_cost": int((~same["fail_cost"] & ~same["better_cost"]).sum()),
            "better_cost": int(same["better_cost"].sum()),
            "more_vehicles": int((with_bks["delta_veh"] > 0).sum()),
            "fewer_vehicles": int((with_bks["delta_veh"] < 0).sum()),
            "mean_gap_same_fleet_%": round(same["gap_cost"].mean(), 3) if len(same) else np.nan,
            "worse_share_%": round(100 * same["fail_cost"].sum() / len(same), 1) if len(same) else np.nan,
        })

    for b in ["tilk", "dumez"]:
        g = d[d["benchmark"] == b]
        row(f"{b} (all)", g)
        for key, gg in g.groupby(["set", "size", "tw_class"], sort=True):
            label = f"{key[0]}_{key[1]}" + (f"_{key[2]}" if key[2] != "-" else "")
            row(label, gg)
    row("all", d)
    return pd.DataFrame(rows)


def feature_columns(d):
    skip = ID_COLS | {"ls_cost", "ls_routes", "bks_cost", "bks_routes", "sl0", "sl1", "has_features", "has_bks",
                      "same_fleet", "fail_fleet", "fail_cost", "better_cost"} | set(METRICS)
    return [c for c in d.columns if c not in skip and pd.api.types.is_numeric_dtype(d[c])]


def corr_table(d, feats):
    rows = []
    scopes = [("all", d)] + [(b, d[d["benchmark"] == b]) for b in ["tilk", "dumez"]]
    for scope, g in scopes:
        for m in METRICS:
            for f in feats:
                x = g[[f, m]].dropna()
                if len(x) < 5 or x[f].nunique() < 2 or x[m].nunique() < 2:
                    continue
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore")
                    pr, pp = stats.pearsonr(x[f], x[m])
                    sr, sp = stats.spearmanr(x[f], x[m])
                rows.append({"scope": scope, "metric": m, "feature": f, "n": len(x),
                             "pearson_r": pr, "pearson_p": pp, "spearman_rho": sr, "spearman_p": sp})
    return pd.DataFrame(rows, columns=["scope", "metric", "feature", "n", "pearson_r", "pearson_p", "spearman_rho", "spearman_p"])


def group_table(d, feats):
    rows = []
    cases = [
        ("fleet failure (dumez, all with BKS)", d[(d["benchmark"] == "dumez") & d["has_bks"]], "fail_fleet"),
        ("cost failure (dumez, same-fleet cases)", d[(d["benchmark"] == "dumez") & d["same_fleet"]], "fail_cost"),
        ("cost failure (tilk, same-fleet cases)", d[(d["benchmark"] == "tilk") & d["same_fleet"]], "fail_cost"),
    ]
    for label, g, flag in cases:
        a, b = g[g[flag]], g[~g[flag]]
        for f in feats:
            xa, xb = a[f].dropna(), b[f].dropna()
            if len(xa) < 2 or len(xb) < 2:
                continue
            with warnings.catch_warnings():
                warnings.simplefilter("ignore")
                p = stats.mannwhitneyu(xa, xb, alternative="two-sided").pvalue if xa.nunique() + xb.nunique() > 2 else np.nan
            rows.append({"case": label, "feature": f, "n_fail": len(xa), "n_ok": len(xb),
                         "median_fail": xa.median(), "median_ok": xb.median(), "mannwhitney_p": p})
    return pd.DataFrame(rows, columns=["case", "feature", "n_fail", "n_ok", "median_fail", "median_ok", "mannwhitney_p"])


def tightness_table(d):
    g = d[(d["benchmark"] == "dumez") & d["has_bks"]].copy()
    g["fill_bin"] = pd.cut(g["fill_bks"], [0, 0.95, 0.98, 0.99, 0.995, 1.0001],
                           labels=["<=0.95", "0.95-0.98", "0.98-0.99", "0.99-0.995", ">0.995"])
    t = g.groupby(["fleet_slack_bks", "fill_bin"], observed=True).agg(
        instances=("instance", "count"), fleet_failures=("fail_fleet", "sum"),
        cost_failures=("fail_cost", "sum"), mean_gap=("gap_cost", "mean")).reset_index()
    t["fleet_failure_rate_%"] = 100 * t["fleet_failures"] / t["instances"]
    return t


def md(df, floatfmt=".3f"):
    return df.to_markdown(index=False, floatfmt=floatfmt)


def write_summary(out, claim, corr, groups, tight, d, source):
    lines = ["# Experiment 3A: instance features against the failures", "", f"Solver results: {source}.", ""]
    lines += ["## The referee's claim, re-derived from the result tables", "",
              "Same-fleet cases are the instances whose solution has the BKS number of vehicles; worse / equal / better",
              "compare the distance on those cases. Tilk et al. instances without a reference solution are excluded.", "",
              md(claim), ""]
    for m, title in [("gap_cost", "cost gap (%) on every instance with a BKS"),
                     ("delta_veh", "vehicle inflation (LS - BKS vehicles)"),
                     ("gap_cost_same", "cost gap (%) on the same-fleet cases")]:
        lines += [f"## Correlations with {title}", ""]
        for scope in ["all", "dumez", "tilk"]:
            c = corr[(corr["metric"] == m) & (corr["scope"] == scope)]
            if c.empty:
                continue
            c = c.reindex(c["spearman_rho"].abs().sort_values(ascending=False).index).head(15)
            lines += [f"### {scope} (top 15 by |rho|)", "", md(c.drop(columns=["scope", "metric"])), ""]
    lines += ["## Fleet tightness of the BKS (Dumez et al.)", "",
              "fleet_slack_bks = BKS vehicles - ceil(total demand / capacity); fill = total demand / (capacity x BKS vehicles).", "",
              md(tight), ""]
    lines += ["## Failing vs non-failing instances (medians, Mann-Whitney p)", ""]
    for case, g in groups.groupby("case", sort=False):
        g = g.sort_values("mannwhitney_p").head(15)
        lines += [f"### {case}", "", md(g.drop(columns=["case"])), ""]
    fails = d[d["fail_fleet"]][["instance", "ls_routes", "bks_routes", "demand_lb", "fleet_slack_bks", "fill_bks",
                                "tw_private_mean", "tw_compat_private", "locker_tightness", "gap_cost"]]
    lines += ["## The fleet failures", "", md(fails), ""]
    with open(os.path.join(out, "summary.md"), "w") as f:
        f.write("\n".join(lines))


def scatter(out, d, corr, m, k=8):
    c = corr[(corr["metric"] == m) & (corr["scope"] == "all")]
    if c.empty:
        return
    top = c.reindex(c["spearman_rho"].abs().sort_values(ascending=False).index)["feature"].head(k).tolist()
    cols = 4
    rows = int(np.ceil(len(top) / cols))
    fig, axes = plt.subplots(rows, cols, figsize=(4.2 * cols, 3.4 * rows), squeeze=False)
    for ax, f in zip(axes.flat, top):
        for b, g in d.dropna(subset=[f, m]).groupby("benchmark"):
            ax.scatter(g[f], g[m], s=16, alpha=0.7, color=COLORS[b], label=b)
        rho = c[c["feature"] == f]["spearman_rho"].iloc[0]
        ax.set_title(f"{f}  (rho = {rho:.2f})", fontsize=9)
        ax.set_xlabel(f, fontsize=8)
        ax.set_ylabel(m, fontsize=8)
        ax.tick_params(labelsize=7)
        ax.axhline(0, color="grey", lw=0.5)
    for ax in axes.flat[len(top):]:
        ax.axis("off")
    axes.flat[0].legend(fontsize=7)
    fig.tight_layout()
    fig.savefig(os.path.join(out, "plots", f"scatter_{m}.png"), dpi=130)
    plt.close(fig)


def heatmap(out, corr, feats):
    if corr.empty:
        return
    scopes = ["all", "dumez", "tilk"]
    fig, axes = plt.subplots(1, len(scopes), figsize=(4.5 * len(scopes), 0.28 * len(feats) + 1.5), sharey=True)
    for ax, scope in zip(axes, scopes):
        c = corr[corr["scope"] == scope].pivot(index="feature", columns="metric", values="spearman_rho")
        c = c.reindex(index=feats, columns=METRICS)
        im = ax.imshow(c.values.astype(float), cmap="RdBu_r", vmin=-1, vmax=1, aspect="auto")
        ax.set_xticks(range(len(METRICS)))
        ax.set_xticklabels(METRICS, rotation=30, fontsize=8)
        ax.set_yticks(range(len(feats)))
        ax.set_yticklabels(feats, fontsize=7)
        ax.set_title(f"Spearman rho, {scope}", fontsize=9)
        for i in range(len(feats)):
            for j in range(len(METRICS)):
                v = c.values[i, j]
                if not np.isnan(v):
                    ax.text(j, i, f"{v:.2f}", ha="center", va="center", fontsize=6)
    fig.colorbar(im, ax=axes, fraction=0.02)
    fig.savefig(os.path.join(out, "plots", "heatmap.png"), dpi=130, bbox_inches="tight")
    plt.close(fig)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--results", default=os.path.join(HERE, "paper_results.csv"))
    ap.add_argument("--features", default=os.path.join(HERE, "instance_features.csv"))
    ap.add_argument("--runs", help="runs.csv of a run: its cost / fleet replace the paper's solver side")
    ap.add_argument("--out", default="exp3a_analysis")
    a = ap.parse_args()
    os.makedirs(os.path.join(a.out, "plots"), exist_ok=True)

    d = load(a.results, a.features, a.runs)
    source = f"the run {a.runs}, BKS from {a.results}" if a.runs else f"the paper's tables ({a.results})"
    if a.runs:
        cols = ["instance", "benchmark", "set", "size", "tw_class", "ls_cost", "ls_routes", "bks_cost", "bks_routes", "sl0", "sl1", "result"]
        d[cols].to_csv(os.path.join(a.out, "results.csv"), index=False)
    missing = d[~d["has_features"]]["instance"].tolist()
    if missing:
        print(f"{len(missing)} instances without features (no instance file): {', '.join(missing)}")
    claim = claim_check(d)
    claim.to_csv(os.path.join(a.out, "claim_check.csv"), index=False)
    with open(os.path.join(a.out, "claim_check.md"), "w") as f:
        f.write(md(claim))

    dd = d[d["has_bks"] & d["has_features"]].copy()
    dd.to_csv(os.path.join(a.out, "merged.csv"), index=False)
    feats = feature_columns(dd)
    corr = corr_table(dd, feats)
    corr.to_csv(os.path.join(a.out, "correlations.csv"), index=False)
    groups = group_table(dd, feats)
    groups.to_csv(os.path.join(a.out, "groups.csv"), index=False)
    tight = tightness_table(dd)
    tight.to_csv(os.path.join(a.out, "tightness.csv"), index=False)
    write_summary(a.out, claim, corr, groups, tight, dd, source)
    for m in METRICS:
        scatter(a.out, dd, corr, m)
    heatmap(a.out, corr, feats)
    print(claim.to_string(index=False))
    print(f"\n{len(dd)} instances analysed -> {a.out}/summary.md")


if __name__ == "__main__":
    main()
