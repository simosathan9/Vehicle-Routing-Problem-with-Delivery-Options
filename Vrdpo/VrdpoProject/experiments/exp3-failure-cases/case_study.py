#!/usr/bin/env python3
"""Experiment 3B of the review (referee 3, comment 3): structural case study of one instance and one
solution of the solver, e.g. a large-scale outlier that needed one vehicle more than the BKS.

The question is whether the extra vehicle is an "unlucky bin-packing tail": the vehicles are so full that
the stops of the lightest route fit by capacity somewhere, but the time windows leave no feasible single
insertion, so no relocation or flip can empty it, while a set-partitioning reassembly could.

Usage (from Vrdpo/VrdpoProject):
  python experiments/exp3-failure-cases/case_study.py --report results/<tag>/U/400/U_400_4.txt
         [--instance Instances/U/400/U_400_4.txt] [--trace results/<tag>/traces/U/400/U_400_4.csv]
         [--bks-routes 40] [--routes-tested 3] [--out exp3b_U_400_4]

The instance file is found from the report's name when --instance is not given; --bks-routes defaults to
the value in paper_results.csv. The distance metric (settings key `type`) is read from the settings.json of
the results folder the report lies in, or given with --type int|double. The schedule is recomputed with the
solver's time model (vrpdo.py), the report is checked for feasibility and its cost is compared with the
recomputed one.

Outputs (in --out):
  case.md              the tables below with a short reading of them
  routes.csv           one row per route: stops, load, fill, distance, duration, waiting, total and minimum
                       slack (latest - earliest completion), stops at lockers, priorities served
  absorption.csv       one row per stop of the --routes-tested lightest routes: can it be inserted into
                       another route by capacity alone; by capacity and time windows keeping its option;
                       keeping its option or flipping to another of its options (locker capacity kept,
                       service levels ignored); and the number of feasible (route, position) slots
  elimination.csv      greedy attempt to empty each tested route one stop at a time (cheapest feasible
                       insertion, any option, routes updated as it goes): how many stops found a place
  plots/map.png        the routes on the plane; lockers as squares, private addresses as dots, the
                       lightest routes drawn thick
  plots/loads.png      load per route against the capacity, with the bin-packing bound
  plots/time.png       the time windows of the private addresses and lockers (left) and every route's
                       schedule with its slack (right)
  plots/trace.png      with --trace: vehicles of the current and the restart-best solution per iteration
"""
import argparse
import csv
import json
import math
import os
import re
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vrpdo import PRIVATE, SHARED, find_instance, read_instance, read_report, route_cost, schedule  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))


def metric_of(report):
    """The settings.json of the results folder (results/<tag>/) that holds the report; None when not found."""
    d = os.path.dirname(os.path.abspath(report))
    for _ in range(4):
        p = os.path.join(d, "settings.json")
        if os.path.exists(p):
            with open(p) as f:
                return json.load(f).get("type")
        d = os.path.dirname(d)
    return None


def bks_routes_of(name):
    p = os.path.join(HERE, "paper_results.csv")
    if os.path.exists(p):
        with open(p) as f:
            for r in csv.DictReader(f):
                if r["instance"] == name and r["bks_routes"]:
                    return int(float(r["bks_routes"]))
    return None


# ---------------------------------------------------------------- route statistics

def route_rows(inst, sol):
    rows = []
    for rt in sol.routes:
        seq = sol.sequence(inst, rt)
        ect, lat, feas = schedule(inst, seq)
        slack = [lat[i] - ect[i] for i in range(1, len(seq) - 1)]
        wait = 0.0
        t = 0.0
        for i in range(1, len(seq)):
            arrive = t + inst.travel(seq[i - 1], seq[i]) + (seq[i].service if seq[i - 1].id != seq[i].id else 0)
            wait += max(0.0, seq[i].ready - arrive)
            t = max(arrive, seq[i].ready) + seq[i].delivery
        load = sum(o.customer.demand for o in rt.options)
        rows.append({
            "route": rt.index, "stops": len(rt.options), "load": load, "fill": load / inst.capacity,
            "spare": inst.capacity - load, "distance": route_cost(inst, seq), "duration_min": ect[-1] / 10,
            "waiting_min": wait / 10, "slack_total_min": sum(slack) / 10, "slack_min_min": min(slack) / 10,
            "stops_shared": sum(o.location.type == SHARED for o in rt.options),
            "prio0": sum(o.prio == 0 for o in rt.options), "prio1": sum(o.prio == 1 for o in rt.options),
            "prio2": sum(o.prio >= 2 for o in rt.options), "feasible": feas,
        })
    return rows


# ---------------------------------------------------------------- absorption test

def locker_uses(sol):
    uses = {}
    for rt in sol.routes:
        for o in rt.options:
            if o.location.type == SHARED:
                uses[o.location.id] = uses.get(o.location.id, 0) + 1
    return uses


def feasible_slots(inst, routes_locs, loads, exclude, option, demand, uses, current_loc):
    """(route index, position) pairs where `option` fits: capacity, time windows, locker capacity."""
    loc = option.location
    if loc.type == SHARED and loc.id != current_loc.id and uses.get(loc.id, 0) >= loc.max_cap:
        return []
    slots = []
    for r, locs in enumerate(routes_locs):
        if r == exclude or loads[r] + demand > inst.capacity:
            continue
        for pos in range(1, len(locs)):
            if schedule(inst, locs[:pos] + [loc] + locs[pos:])[2]:
                slots.append((r, pos))
    return slots


def absorption(inst, sol, tested):
    uses = locker_uses(sol)
    routes_locs = [sol.sequence(inst, rt) for rt in sol.routes]
    loads = [sum(o.customer.demand for o in rt.options) for rt in sol.routes]
    rows = []
    for rt in tested:
        for o in rt.options:
            c = o.customer
            cap_ok = sum(1 for r in range(len(sol.routes)) if r != rt.index and loads[r] + c.demand <= inst.capacity)
            same = feasible_slots(inst, routes_locs, loads, rt.index, o, c.demand, uses, o.location)
            any_slots, not_worse = 0, 0
            for alt in c.options:
                if alt is o:
                    continue
                s = feasible_slots(inst, routes_locs, loads, rt.index, alt, c.demand, uses, o.location)
                any_slots += len(s)
                if alt.prio <= o.prio:
                    not_worse += len(s)
            rows.append({
                "route": rt.index, "customer": c.id, "option": o.id, "prio": o.prio,
                "location_type": "shared" if o.location.type == SHARED else "private",
                "tw_width_min": (o.location.raw_due - o.location.raw_ready), "demand": c.demand,
                "n_options": len(c.options), "routes_with_spare_capacity": cap_ok,
                "slots_same_option": len(same), "slots_other_options": any_slots,
                "slots_other_not_worse_prio": not_worse,
                "absorbable_capacity": cap_ok > 0, "absorbable_same_option": len(same) > 0,
                "absorbable_any_option": len(same) + any_slots > 0,
            })
    return rows


def greedy_elimination(inst, sol, tested):
    """Tries to empty a route by cheapest feasible insertion of its stops, one at a time, any option."""
    rows = []
    for rt in tested:
        uses = locker_uses(sol)
        routes_locs = [sol.sequence(inst, r) for r in sol.routes]
        loads = [sum(o.customer.demand for o in r.options) for r in sol.routes]
        remaining = list(rt.options)
        placed = 0
        order = []
        while remaining:
            best = None
            for o in remaining:
                c = o.customer
                for alt in c.options:
                    for r, pos in feasible_slots(inst, routes_locs, loads, rt.index, alt, c.demand, uses, o.location):
                        locs = routes_locs[r]
                        delta = (inst.dist(locs[pos - 1], alt.location) + inst.dist(alt.location, locs[pos])
                                 - inst.dist(locs[pos - 1], locs[pos]))
                        if best is None or delta < best[0]:
                            best = (delta, o, alt, r, pos)
            if best is None:
                break
            _, o, alt, r, pos = best
            routes_locs[r] = routes_locs[r][:pos] + [alt.location] + routes_locs[r][pos:]
            loads[r] += o.customer.demand
            if alt.location.type == SHARED:
                uses[alt.location.id] = uses.get(alt.location.id, 0) + 1
            if o.location.type == SHARED and alt.location.id != o.location.id:
                uses[o.location.id] -= 1
            remaining.remove(o)
            placed += 1
            order.append(f"c{o.customer.id}->r{r}" + ("" if alt is o else f"(flip p{o.prio}->p{alt.prio})"))
        rows.append({"route": rt.index, "stops": len(rt.options), "placed": placed, "left": len(remaining),
                     "emptied": not remaining, "sequence": " ".join(order)})
    return rows


# ---------------------------------------------------------------- plots

def plot_map(inst, sol, tested, out):
    fig, ax = plt.subplots(figsize=(8, 8))
    cmap = plt.get_cmap("tab20")
    tested_ids = {rt.index for rt in tested}
    for rt in sol.routes:
        seq = sol.sequence(inst, rt)
        xs = [l.x for l in seq]
        ys = [l.y for l in seq]
        if rt.index in tested_ids:
            ax.plot(xs, ys, color="red", lw=2.2, zorder=3, label=f"route {rt.index} (tested)")
        else:
            ax.plot(xs, ys, color=cmap(rt.index % 20), lw=0.8, alpha=0.8, zorder=2)
    priv = [l for l in inst.locations if l.type == PRIVATE]
    shar = [l for l in inst.locations if l.type == SHARED]
    ax.scatter([l.x for l in priv], [l.y for l in priv], s=6, color="black", zorder=4, label="private address")
    ax.scatter([l.x for l in shar], [l.y for l in shar], s=[12 + 6 * l.max_cap for l in shar], marker="s",
               facecolor="none", edgecolor="green", zorder=5, label="locker (size = capacity)")
    ax.scatter([inst.depot.x], [inst.depot.y], s=120, marker="*", color="orange", zorder=6, label="depot")
    ax.set_title(f"{inst.name}: {len(sol.routes)} routes, cost {sol.cost:.1f}")
    ax.legend(fontsize=7, loc="upper right")
    ax.set_aspect("equal")
    fig.tight_layout()
    fig.savefig(os.path.join(out, "plots", "map.png"), dpi=130)
    plt.close(fig)


def plot_loads(inst, rows, tested, bks, lb, out):
    rows = sorted(rows, key=lambda r: r["load"])
    tested_ids = {rt.index for rt in tested}
    fig, ax = plt.subplots(figsize=(max(6, 0.22 * len(rows)), 4.4))
    ax.bar(range(len(rows)), [r["load"] for r in rows], color=["red" if r["route"] in tested_ids else "#2a6fdb" for r in rows])
    ax.axhline(inst.capacity, color="black", lw=1, label=f"capacity {inst.capacity}")
    ax.set_xticks(range(len(rows)))
    ax.set_xticklabels([str(r["route"]) for r in rows], fontsize=6, rotation=90)
    ax.set_xlabel("route (sorted by load)")
    ax.set_ylabel("load")
    total = sum(r["load"] for r in rows)
    ax.set_title(f"{inst.name}: {len(rows)} vehicles (BKS {bks}, bin-packing bound {lb}); tested routes in red\n"
                 f"total demand {total} = {100 * total / (inst.capacity * len(rows)):.1f}% of the fleet, "
                 f"{100 * total / (inst.capacity * lb):.1f}% of {lb} vehicles", fontsize=8)
    ax.legend(fontsize=7)
    fig.tight_layout()
    fig.savefig(os.path.join(out, "plots", "loads.png"), dpi=130)
    plt.close(fig)


def plot_time(inst, sol, rows, out):
    fig, (a1, a2) = plt.subplots(1, 2, figsize=(13, 6))
    priv = sorted([l for l in inst.locations if l.type == PRIVATE], key=lambda l: (l.raw_ready, l.raw_due))
    shar = sorted([l for l in inst.locations if l.type == SHARED], key=lambda l: (l.raw_ready, l.raw_due))
    for i, l in enumerate(priv):
        a1.plot([l.raw_ready, l.raw_due], [i, i], color="black", lw=0.6)
    for j, l in enumerate(shar):
        a1.plot([l.raw_ready, l.raw_due], [len(priv) + 2 + j, len(priv) + 2 + j], color="green", lw=1.2)
    a1.set_xlabel("time (min)")
    a1.set_ylabel("location (sorted by ready time)")
    a1.set_title(f"time windows: {len(priv)} private (black), {len(shar)} lockers (green)", fontsize=9)
    a1.set_xlim(0, inst.depot.raw_due)
    order = sorted(rows, key=lambda r: r["load"])
    for i, r in enumerate(order):
        a2.barh(i, r["duration_min"], color="red" if i == 0 else "#2a6fdb", alpha=0.6)
        a2.barh(i, r["slack_total_min"], left=r["duration_min"], color="grey", alpha=0.4)
    a2.set_yticks(range(len(order)))
    a2.set_yticklabels([f"r{r['route']} ({r['load']})" for r in order], fontsize=6)
    a2.set_xlabel("minutes: route duration (colour) and total slack over its stops (grey)")
    a2.set_title("routes sorted by load; lightest in red", fontsize=9)
    fig.tight_layout()
    fig.savefig(os.path.join(out, "plots", "time.png"), dpi=130)
    plt.close(fig)


def plot_trace(path, bks, out):
    import pandas as pd
    t = pd.read_csv(path)
    fig, ax = plt.subplots(figsize=(9, 4))
    for r, g in t.groupby("restart"):
        line, = ax.plot(g["iteration"], g["best_routes"], lw=1.0, alpha=0.8, label=f"restart {r}" if r < 10 else None)
        ax.plot(g["iteration"], g["routes"], lw=0.4, alpha=0.4, color=line.get_color())
    if bks:
        ax.axhline(bks, color="black", lw=1, ls="--", label=f"BKS {bks}")
    ax.set_xlabel("iteration")
    ax.set_ylabel("vehicles")
    ax.set_title("fleet over the search, per restart: restart-best solution (thick), current solution (thin)", fontsize=9)
    ax.legend(fontsize=6, ncol=2)
    fig.tight_layout()
    fig.savefig(os.path.join(out, "plots", "trace.png"), dpi=130)
    plt.close(fig)
    first = t[t["best_routes"].notna()].groupby("restart").agg(final=("best_routes", "min"))
    reach = t[t["best_routes"] == t["best_routes"].min()].groupby("restart")["iteration"].min()
    return int(t["best_routes"].min()), first["final"].value_counts().sort_index().to_dict(), reach.describe().to_dict()


# ---------------------------------------------------------------- report

def md_table(rows, cols, fmt=None):
    fmt = fmt or {}
    head = "| " + " | ".join(cols) + " |\n|" + "|".join("---" for _ in cols) + "|\n"
    body = ""
    for r in rows:
        body += "| " + " | ".join(fmt.get(c, "{}").format(r[c]) for c in cols) + " |\n"
    return head + body


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--report", required=True)
    ap.add_argument("--instance")
    ap.add_argument("--instances", default="Instances")
    ap.add_argument("--trace")
    ap.add_argument("--type", choices=["int", "double"], help="distance metric; default: the settings.json next to the report")
    ap.add_argument("--bks-routes", type=int)
    ap.add_argument("--routes-tested", type=int, default=3, help="the k lightest routes get the absorption test")
    ap.add_argument("--out")
    a = ap.parse_args()

    name = os.path.splitext(os.path.basename(a.report))[0]
    inst_path = a.instance or find_instance(name, (a.instances,))
    if not inst_path:
        raise SystemExit(f"instance file of {name} not found under {a.instances}; give --instance")
    metric = a.type or metric_of(a.report)
    inst = read_instance(inst_path, None if metric is None else metric == "int")
    sol = read_report(a.report, inst)
    bks = a.bks_routes or bks_routes_of(name)
    out = a.out or f"exp3b_{name}"
    os.makedirs(os.path.join(out, "plots"), exist_ok=True)

    rows = route_rows(inst, sol)
    served = {o.customer.id for rt in sol.routes for o in rt.options}
    if len(served) != len(inst.customers) or not all(r["feasible"] for r in rows):
        print(f"warning: the report serves {len(served)}/{len(inst.customers)} customers, "
              f"time-infeasible routes: {[r['route'] for r in rows if not r['feasible']]}")
    cost = sum(r["distance"] for r in rows)
    if abs(cost - sol.cost) > 1e-4 * max(1, sol.cost):
        print(f"warning: recomputed cost {cost:.3f} differs from the report's {sol.cost:.3f}: wrong metric? (using type={'int' if inst.integer_metric else 'double'})")
    demand = sum(c.demand for c in inst.customers)
    lb = math.ceil(demand / inst.capacity)
    tested = sorted(sol.routes, key=lambda rt: sum(o.customer.demand for o in rt.options))[:a.routes_tested]

    with open(os.path.join(out, "routes.csv"), "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    absorb = absorption(inst, sol, tested)
    with open(os.path.join(out, "absorption.csv"), "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(absorb[0].keys()))
        w.writeheader()
        w.writerows(absorb)
    elim = greedy_elimination(inst, sol, tested)
    with open(os.path.join(out, "elimination.csv"), "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(elim[0].keys()))
        w.writeheader()
        w.writerows(elim)

    plot_map(inst, sol, tested, out)
    plot_loads(inst, rows, tested, bks, lb, out)
    plot_time(inst, sol, rows, out)
    trace_info = plot_trace(a.trace, bks, out) if a.trace else None

    spare_others = lambda rt: sum(r["spare"] for r in rows if r["route"] != rt.index)  # noqa: E731
    lines = [f"# Case study: {name}", "",
             f"Solution: {len(sol.routes)} vehicles, cost {cost:.2f} (report: {sol.cost:.2f}), service levels {sol.sl0:.2f} / {sol.sl1:.2f}; "
             f"BKS: {bks} vehicles. Capacity {inst.capacity}, total demand {demand}: bin-packing bound {lb} vehicles, "
             f"fleet fill {100 * demand / (inst.capacity * len(sol.routes)):.1f}% ({100 * demand / (inst.capacity * lb):.1f}% with {lb} vehicles).", "",
             f"Instance: {len(inst.customers)} customers, {sum(1 for l in inst.locations if l.type == PRIVATE)} private addresses "
             f"(mean window {np.mean([l.raw_due - l.raw_ready for l in inst.locations if l.type == PRIVATE]):.0f} min), "
             f"{sum(1 for l in inst.locations if l.type == SHARED)} lockers with {sum(l.max_cap for l in inst.locations if l.type == SHARED)} slots, "
             f"horizon {inst.depot.raw_due} min.", "",
             "## Routes", "",
             md_table(sorted(rows, key=lambda r: r["load"]),
                      ["route", "stops", "load", "fill", "distance", "duration_min", "waiting_min", "slack_total_min", "slack_min_min", "stops_shared", "prio0", "prio1", "prio2"],
                      {"fill": "{:.3f}", "distance": "{:.1f}", "duration_min": "{:.0f}", "waiting_min": "{:.0f}", "slack_total_min": "{:.0f}", "slack_min_min": "{:.0f}"}),
             "", "## Can the lightest routes be absorbed?", "",
             "For every stop of the tested routes: the number of other routes with spare capacity for it; the feasible (route, position) "
             "insertions keeping its option (what a relocation sees), and with its other options (what a flip sees; locker capacity "
             "kept, service levels ignored). A stop with spare capacity somewhere but no feasible slot is blocked by the time windows alone.", ""]
    for rt in tested:
        rs = [r for r in absorb if r["route"] == rt.index]
        lines += [f"### Route {rt.index}: {len(rs)} stops, load {sum(r['demand'] for r in rs)}, spare capacity in the other routes {spare_others(rt)}", "",
                  f"* absorbable by capacity: {sum(r['absorbable_capacity'] for r in rs)} / {len(rs)}",
                  f"* feasible insertion keeping the option: {sum(r['absorbable_same_option'] for r in rs)} / {len(rs)}",
                  f"* feasible insertion with any option: {sum(r['absorbable_any_option'] for r in rs)} / {len(rs)}",
                  f"* greedy one-at-a-time elimination: {[e for e in elim if e['route'] == rt.index][0]['placed']} / {len(rs)} stops placed"
                  + (" (route emptied)" if [e for e in elim if e['route'] == rt.index][0]["emptied"] else ""), "",
                  md_table(rs, ["customer", "prio", "location_type", "tw_width_min", "demand", "n_options", "routes_with_spare_capacity",
                                "slots_same_option", "slots_other_options", "slots_other_not_worse_prio"]), ""]
    if trace_info:
        best, finals, reach = trace_info
        lines += ["## Fleet over the search", "",
                  f"Best fleet reached: {best}. Final fleet per restart (vehicles: restarts): {finals}. "
                  f"Iteration at which a restart first reaches the best fleet: median {reach.get('50%', float('nan')):.0f}, "
                  f"min {reach.get('min', float('nan')):.0f}, max {reach.get('max', float('nan')):.0f}.", ""]
    lines += ["## Figures", "", "![map](plots/map.png)", "", "![loads](plots/loads.png)", "", "![time](plots/time.png)", ""]
    if trace_info:
        lines += ["![trace](plots/trace.png)", ""]
    with open(os.path.join(out, "case.md"), "w") as f:
        f.write("\n".join(lines))
    print("\n".join(lines[:4]))
    for rt in tested:
        rs = [r for r in absorb if r["route"] == rt.index]
        print(f"route {rt.index}: {len(rs)} stops; capacity {sum(r['absorbable_capacity'] for r in rs)}, "
              f"same option {sum(r['absorbable_same_option'] for r in rs)}, any option {sum(r['absorbable_any_option'] for r in rs)}, "
              f"greedy placed {[e for e in elim if e['route'] == rt.index][0]['placed']}")
    print(f"-> {out}/case.md")


if __name__ == "__main__":
    main()
