#!/usr/bin/env python3
"""Experiment 3A, step 1: structural features of every benchmark instance, from the instance files alone.

Usage (from Vrdpo/VrdpoProject):
  python experiments/exp3-failure-cases/instance_features.py [--instances Instances] [--out experiments/exp3-failure-cases/instance_features.csv]
  python experiments/exp3-failure-cases/instance_features.py --names experiments/exp3-failure-cases/paper_results.csv

With --names only the instances listed in that CSV (column 'instance') are processed and the missing files
are reported; without it every Instances/<set>/<size>/*.txt is processed.

One row per instance (times in minutes, as in the files; the solver's x10 and delivery durations are not
applied here so that the numbers read like the instance definitions):

  scale        n_customers, n_locations, n_shared, n_private, n_options, options_per_customer,
               share_cust_1opt / _2opt / _3opt, share_cust_with_shared, share_cust_locker_only
               (customers without a private address: they must use a locker), share_options_shared,
               share_prio0_shared (first-priority options that are at a locker)
  time windows horizon (depot due), tw_private_mean / _median / _min (width of the private windows),
               tw_shared_mean, tw_option_mean (width averaged over options, i.e. what the solver may choose
               from), tw_prio0_mean (width of the first-priority options), tw_slack_norm (tw_option_mean /
               horizon), tw_compat_private: share of ordered pairs (i, j) of private locations that can be
               served one after the other (ready_i + delivery + travel <= due_j), the classic
               time-window tightness of the routing part; tw_compat_all: the same over all locations
  lockers      locker_cap_total (sum of the shared capacities), locker_tightness = customers with a shared
               option / locker_cap_total (the referee's sum q / sum D, with demand = one slot per customer),
               locker_forced = locker-only customers / locker_cap_total
  capacity     vehicle_capacity, demand_total, demand_mean, demand_lb = ceil(demand_total / capacity)
               (the bin-packing lower bound on the fleet), vehicles (header)
  geometry     depot_dist_mean (mean distance of the private addresses to the depot), nn_dist_mean (mean
               nearest-neighbour distance among private addresses), area (bounding box of all locations),
               density = n_customers / area
"""
import argparse
import csv
import glob
import math
import os
import statistics as st

import numpy as np

from vrpdo import DEPOT, PRIVATE, SHARED, is_tilk, read_instance

HERE = os.path.dirname(os.path.abspath(__file__))


def tw_compat(inst, locs):
    """Share of ordered pairs (i, j), i != j, with ready_i + delivery_i + service_j + travel_ij <= due_j - delivery_j
    (the window bounds the start of the delivery, due here is the file's due + delivery)."""
    n = len(locs)
    if n < 2:
        return float("nan")
    x = np.array([l.x for l in locs], float)
    y = np.array([l.y for l in locs], float)
    d = np.hypot(x[:, None] - x[None, :], y[:, None] - y[None, :])
    t = np.ceil(10 * d) if inst.integer_metric else 10 * d
    start_after = np.array([l.ready + l.delivery for l in locs])[:, None]      # earliest departure from i
    latest_start = np.array([l.due - l.delivery - l.service for l in locs])[None, :]  # latest arrival at j
    ok = start_after + t <= latest_start
    np.fill_diagonal(ok, False)
    return ok.sum() / (n * (n - 1))


def features(inst):
    locs = inst.locations
    shared = [l for l in locs if l.type == SHARED]
    private = [l for l in locs if l.type == PRIVATE]
    cust = inst.customers
    opts = inst.options
    n = len(cust)

    def width(l):
        return l.raw_due - l.raw_ready

    n_opts = [len(c.options) for c in cust]
    with_shared = [c for c in cust if any(o.location.type == SHARED for o in c.options)]
    locker_only = [c for c in cust if not any(o.location.type == PRIVATE for o in c.options)]
    prio0 = [o for o in opts if o.prio == 0]
    demand_total = sum(c.demand for c in cust)
    locker_cap = sum(l.max_cap for l in shared)

    xs = [l.x for l in locs]
    ys = [l.y for l in locs]
    area = (max(xs) - min(xs)) * (max(ys) - min(ys))
    depot = inst.depot
    px = np.array([l.x for l in private], float)
    py = np.array([l.y for l in private], float)
    if len(private) > 1:
        dd = np.hypot(px[:, None] - px[None, :], py[:, None] - py[None, :])
        np.fill_diagonal(dd, np.inf)
        nn = float(dd.min(axis=1).mean())
    else:
        nn = float("nan")

    return {
        "instance": inst.name,
        "benchmark": "tilk" if is_tilk(inst.name) else "dumez",
        "set": inst.name.split("_")[0],
        "n_customers": n,
        "n_locations": len(locs) - 1,
        "n_shared": len(shared),
        "n_private": len(private),
        "n_options": len(opts),
        "options_per_customer": len(opts) / n,
        "share_cust_1opt": sum(k == 1 for k in n_opts) / n,
        "share_cust_2opt": sum(k == 2 for k in n_opts) / n,
        "share_cust_3opt": sum(k >= 3 for k in n_opts) / n,
        "share_cust_with_shared": len(with_shared) / n,
        "share_cust_locker_only": len(locker_only) / n,
        "share_options_shared": sum(o.location.type == SHARED for o in opts) / len(opts),
        "share_prio0_shared": sum(o.location.type == SHARED for o in prio0) / max(1, len(prio0)),
        "horizon": depot.raw_due,
        "tw_private_mean": st.mean(width(l) for l in private) if private else float("nan"),
        "tw_private_median": st.median(width(l) for l in private) if private else float("nan"),
        "tw_private_min": min(width(l) for l in private) if private else float("nan"),
        "tw_shared_mean": st.mean(width(l) for l in shared) if shared else float("nan"),
        "tw_option_mean": st.mean(width(o.location) for o in opts),
        "tw_prio0_mean": st.mean(width(o.location) for o in prio0) if prio0 else float("nan"),
        "tw_slack_norm": st.mean(width(o.location) for o in opts) / depot.raw_due,
        "tw_compat_private": tw_compat(inst, private),
        "tw_compat_all": tw_compat(inst, [l for l in locs if l.type != DEPOT]),
        "locker_cap_total": locker_cap,
        "locker_tightness": len(with_shared) / locker_cap if locker_cap else float("nan"),
        "locker_forced": len(locker_only) / locker_cap if locker_cap else float("nan"),
        "vehicle_capacity": inst.capacity,
        "vehicles": inst.vehicles,
        "demand_total": demand_total,
        "demand_mean": demand_total / n,
        "demand_lb": math.ceil(demand_total / inst.capacity),
        "depot_dist_mean": float(np.hypot(px - depot.x, py - depot.y).mean()) if len(private) else float("nan"),
        "nn_dist_mean": nn,
        "area": area,
        "density": n / area if area else float("nan"),
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--instances", default="Instances", help="root folder (Instances/<set>/<size>/<name>.txt)")
    ap.add_argument("--names", help="CSV with an 'instance' column: process these and report missing files")
    ap.add_argument("--out", default=os.path.join(HERE, "instance_features.csv"))
    a = ap.parse_args()

    if a.names:
        with open(a.names) as f:
            names = [r["instance"] for r in csv.DictReader(f)]
        paths, missing = [], []
        for nme in names:
            parts = nme.split("_")
            p = os.path.join(a.instances, parts[0], parts[1], nme + ".txt")
            (paths if os.path.exists(p) else missing).append(p if os.path.exists(p) else nme)
        if missing:
            print(f"{len(missing)} instance files not found under {a.instances}: {', '.join(missing)}")
    else:
        paths = sorted(glob.glob(os.path.join(a.instances, "*", "*", "*.txt")))

    rows = []
    for p in paths:
        try:
            rows.append(features(read_instance(p)))
        except Exception as e:  # a stray file in the folder
            print(f"skipped {p}: {e}")
    rows.sort(key=lambda r: (r["benchmark"], r["set"], r["n_customers"], r["instance"]))
    with open(a.out, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    print(f"{len(rows)} instances -> {a.out}")


if __name__ == "__main__":
    main()
