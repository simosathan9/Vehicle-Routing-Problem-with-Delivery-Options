#!/usr/bin/env python3
"""Exports the per-instance results of the paper (local search vs best-known solution) from the two
spreadsheets new_sols_paper1.xlsx (Tilk et al. 25/50) and new_sols_paper2.xlsx (Dumez et al. 50-400) to
paper_results.csv, the input of analyze_exp3a.py.

Usage (from Vrdpo/VrdpoProject):
  python experiments/exp3-failure-cases/export_results.py [--out experiments/exp3-failure-cases/paper_results.csv]

Columns: instance, benchmark (tilk / dumez), set (U / V / UBC), size (customers), tw_class (small / medium /
large for Tilk et al., '-' otherwise), ls_cost, ls_routes, bks_cost, bks_routes, sl0, sl1, result (the
spreadsheet's Optimal / Better / Worse). Tilk et al. instances without a reference solution (the exact method
hit its time limit) keep an empty bks_cost / bks_routes.
"""
import argparse
import csv
import os
import re

import openpyxl

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))


def num(v):
    if v is None or str(v).strip() in ("", "---"):
        return ""
    return str(v).strip()


def read_sheet(path, benchmark):
    ws = openpyxl.load_workbook(path, data_only=True)["Sheet"]
    header = [str(c).strip() for c in next(ws.iter_rows(min_row=1, max_row=1, values_only=True))]
    col = {h: i for i, h in enumerate(header)}
    rows = []
    for r in ws.iter_rows(min_row=2, values_only=True):
        if not r[0]:
            continue
        name = re.sub(r"greedy\.txt$|\.txt$", "", str(r[0]).strip())
        parts = name.split("_")
        m = re.match(r"(\d+)(small|medium|large)?", parts[1])
        rows.append({
            "instance": name,
            "benchmark": benchmark,
            "set": parts[0],
            "size": int(m.group(1)),
            "tw_class": m.group(2) or "-",
            "ls_cost": num(r[col["Total cost"]]),
            "ls_routes": num(r[col["Number of Routes"]]),
            "bks_cost": num(r[col["Their cost"]]),
            "bks_routes": num(r[col["Their routes"]]),
            "sl0": num(r[col["Priority 1"]]),
            "sl1": num(r[col["Priority 2"]]),
            "result": num(r[col["Result"]]),
        })
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--tilk", default=os.path.join(PROJECT, "new_sols_paper1.xlsx"))
    ap.add_argument("--dumez", default=os.path.join(PROJECT, "new_sols_paper2.xlsx"))
    ap.add_argument("--out", default=os.path.join(HERE, "paper_results.csv"))
    a = ap.parse_args()
    rows = read_sheet(a.tilk, "tilk") + read_sheet(a.dumez, "dumez")
    with open(a.out, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)
    print(f"{len(rows)} rows -> {a.out}")


if __name__ == "__main__":
    main()
