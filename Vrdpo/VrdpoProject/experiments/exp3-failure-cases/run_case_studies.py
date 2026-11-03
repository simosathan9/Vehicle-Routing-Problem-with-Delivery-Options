#!/usr/bin/env python3
"""Experiment 3B of the review (referee 3, comment 3): picks the failure cases of a run and dissects each with
case_study.py.

Input: a results folder of run-exp3.ps1 (results/<tag>/: runs.csv, settings.json, the solution reports and
traces/). The BKS side comes from paper_results.csv. Selected, per benchmark set:
  * every fleet failure (more vehicles than the BKS),
  * the --worst-cost same-fleet cases with the largest positive cost gap (default 3),
  * one control per class (set x size) that has a fleet failure: the instance of that class with the BKS fleet
    and the best cost gap (--no-controls turns this off),
  * anything listed in --also (instance names).
Every selected instance gets case_study.py (--report, --trace when present, --bks-routes) with its output under
<out>/<instance>/. Then selection.csv (the instances and why) and case_studies.md (one row per instance: fleet
against the BKS and the bin-packing bound, the lightest routes' absorption and greedy-elimination results, with
links to the case.md files) are written to <out>.

Usage (from Vrdpo/VrdpoProject):
  python experiments/exp3-failure-cases/run_case_studies.py --run results/exp3_<date>_all [--out results/exp3_<date>_all/case_studies]
  python experiments/exp3-failure-cases/run_case_studies.py --run results/exp3_<date>_outliers --worst-cost 0
"""
import argparse
import csv
import glob
import os
import subprocess
import sys

import pandas as pd

HERE = os.path.dirname(os.path.abspath(__file__))
COST_TOL = 0.002  # same cost within the BKS tables' 3 decimals (as in analyze_exp3a.py)
SKIP_DIRS = {"logs", "traces", "coupling", "case_studies", "exp3a"}


def find_file(run, name, ext, sub=None):
    """The report (<run>/**/<name>.txt) or the trace (<run>/traces/**/<name>.csv) of an instance, if any."""
    root = os.path.join(run, sub) if sub else run
    if not os.path.isdir(root):
        return None
    for p in sorted(glob.glob(os.path.join(root, "**", f"{name}{ext}"), recursive=True)):
        rel = os.path.relpath(p, run).replace("\\", "/").split("/")
        if sub or not (set(rel[:-1]) & SKIP_DIRS):
            return p
    return None


def select(run, results, worst_cost, controls, also):
    r = pd.read_csv(results)
    u = pd.read_csv(os.path.join(run, "runs.csv"))
    u["instance"] = u["instance"].astype(str).str.replace("\\", "/", regex=False).str.split("/").str[-1]
    u = u.drop_duplicates("instance", keep="last")
    cols = ["instance", "cost", "routes", "sl0", "sl1"] + (["type"] if "type" in u.columns else [])
    d = r.merge(u[cols].rename(columns={"sl0": "run_sl0", "sl1": "run_sl1", "type": "metric"}), on="instance", how="inner")
    d = d[d["bks_routes"].notna()].copy()
    d["delta_veh"] = d["routes"] - d["bks_routes"]
    d["gap_cost"] = 100 * (d["cost"] - d["bks_cost"]) / d["bks_cost"]
    d["class"] = d["set"] + "_" + d["size"].astype(str) + d["tw_class"].where(d["tw_class"] != "-", "")
    why = {}

    def add(name, reason):
        why.setdefault(name, []).append(reason)

    fleet = d[d["delta_veh"] > 0]
    for n in fleet["instance"]:
        add(n, "fleet failure")
    same = d[(d["delta_veh"] == 0) & (d["cost"] - d["bks_cost"] > COST_TOL)]
    for b, g in same.groupby("benchmark"):
        for n in g.sort_values("gap_cost", ascending=False)["instance"].head(worst_cost):
            add(n, f"worst same-fleet cost gap ({b})")
    if controls:
        for cls in sorted(fleet["class"].unique()):
            ok = d[(d["class"] == cls) & (d["delta_veh"] == 0)]
            if not ok.empty:
                add(ok.sort_values("gap_cost")["instance"].iloc[0], f"control for {cls}")
    for n in also:
        add(n, "requested")
    rows = []
    for n, reasons in why.items():
        x = d[d["instance"] == n]
        if x.empty:
            print(f"warning: {n} is not in the run or has no BKS, skipped")
            continue
        x = x.iloc[0]
        rows.append({"instance": n, "class": x["class"], "benchmark": x["benchmark"], "why": "; ".join(reasons),
                     "run_routes": int(x["routes"]), "bks_routes": int(x["bks_routes"]), "delta_veh": int(x["delta_veh"]),
                     "run_cost": x["cost"], "bks_cost": x["bks_cost"], "gap_cost_%": round(x["gap_cost"], 3),
                     "metric": x.get("metric", "")})
    return pd.DataFrame(rows).sort_values(["delta_veh", "gap_cost_%"], ascending=False), d


def summarize_case(out_dir):
    """Absorption and greedy elimination of the tested routes, from case_study.py's CSVs."""
    info = {}
    try:
        el = pd.read_csv(os.path.join(out_dir, "elimination.csv"))
        info["tested_routes"] = len(el)
        info["stops_tested"] = int(el["stops"].sum())
        info["greedy_placed"] = int(el["placed"].sum())
        info["routes_emptied"] = int(el["emptied"].astype(str).str.lower().eq("true").sum())
        ab = pd.read_csv(os.path.join(out_dir, "absorption.csv"))
        info["absorbable_capacity"] = int(ab["absorbable_capacity"].sum())
        info["absorbable_same_option"] = int(ab["absorbable_same_option"].sum())
        info["absorbable_any_option"] = int(ab["absorbable_any_option"].sum())
        rt = pd.read_csv(os.path.join(out_dir, "routes.csv"))
        info["min_fill"] = round(rt["fill"].min(), 3)
        info["spare_total"] = int(rt["spare"].sum())
    except (FileNotFoundError, KeyError) as e:
        info["error"] = str(e)
    return info


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--run", required=True, help="results folder of run-exp3.ps1 (holds runs.csv)")
    ap.add_argument("--results", default=os.path.join(HERE, "paper_results.csv"), help="the BKS side")
    ap.add_argument("--instances", default="Instances", help="folder with the instance files")
    ap.add_argument("--worst-cost", type=int, default=3, help="same-fleet cost failures per benchmark to dissect")
    ap.add_argument("--no-controls", action="store_true")
    ap.add_argument("--also", nargs="*", default=[], help="instance names to dissect as well")
    ap.add_argument("--routes-tested", type=int, default=3)
    ap.add_argument("--out")
    ap.add_argument("--select-only", action="store_true", help="only write selection.csv")
    a = ap.parse_args()
    out = a.out or os.path.join(a.run, "case_studies")
    os.makedirs(out, exist_ok=True)

    sel, d = select(a.run, a.results, a.worst_cost, not a.no_controls, a.also)
    sel.to_csv(os.path.join(out, "selection.csv"), index=False)
    print(f"{len(d)} solved instances with a BKS; {int((d['delta_veh'] > 0).sum())} fleet failures, "
          f"{int(((d['delta_veh'] == 0) & (d['cost'] - d['bks_cost'] > COST_TOL)).sum())} same-fleet cost failures")
    print(sel.to_string(index=False))
    if a.select_only:
        return

    rows = []
    for _, s in sel.iterrows():
        n = s["instance"]
        report = find_file(a.run, n, ".txt")
        if not report:
            print(f"warning: no solution report for {n} under {a.run}, skipped")
            continue
        trace = find_file(a.run, n, ".csv", "traces")
        case_out = os.path.join(out, n)
        cmd = [sys.executable, os.path.join(HERE, "case_study.py"), "--report", report, "--instances", a.instances,
               "--bks-routes", str(int(s["bks_routes"])), "--routes-tested", str(a.routes_tested), "--out", case_out]
        if trace:
            cmd += ["--trace", trace]
        if s.get("metric") in ("int", "double"):
            cmd += ["--type", s["metric"]]  # the metric of this solve (the folder's settings.json has only the default)
        print(f"\n== {n}: {' '.join(cmd[1:])}")
        res = subprocess.run(cmd, capture_output=True, text=True)
        print(res.stdout.strip())
        if res.returncode != 0:
            print(res.stderr.strip())
        row = dict(s)
        row.update(summarize_case(case_out))
        row["case"] = f"[{n}/case.md]({n}/case.md)" if res.returncode == 0 else f"failed: {res.stderr.strip().splitlines()[-1] if res.stderr.strip() else res.returncode}"
        rows.append(row)

    t = pd.DataFrame(rows)
    t.to_csv(os.path.join(out, "case_studies.csv"), index=False)
    cols = ["instance", "class", "why", "run_routes", "bks_routes", "gap_cost_%", "min_fill", "spare_total", "stops_tested",
            "absorbable_capacity", "absorbable_any_option", "greedy_placed", "routes_emptied", "case"]
    cols = [c for c in cols if c in t.columns]
    lines = ["# Experiment 3B: case studies", "",
             f"Run: `{a.run}`. BKS: `{a.results}`. The absorption columns count the stops of the {a.routes_tested} lightest routes "
             "(`stops_tested`) that fit in another route by capacity (`absorbable_capacity`), that have a feasible insertion with "
             "any delivery option (`absorbable_any_option`), and that the greedy one-at-a-time elimination placed (`greedy_placed`); "
             "`routes_emptied` is how many of the tested routes it removed. `spare_total` is the spare capacity of the whole fleet.", "",
             t[cols].to_markdown(index=False), ""]
    with open(os.path.join(out, "case_studies.md"), "w") as f:
        f.write("\n".join(lines))
    print(f"\n{len(t)} case studies -> {out}/case_studies.md")


if __name__ == "__main__":
    main()
