# Experiment 5: diminishing returns of the restarts

Answer to the referee comment: *the multi-restart scheme invests computation without analysis of diminishing
returns. Does the algorithm find 90% of its improvement in the first 3 restarts, or does it continue finding
improvements late? For the 400-customer instances (~3 hours) a practitioner needs to know whether halving the
budget would sacrifice 2% or 20% of the solution quality.*

The restarts are independent (restart `r` is seeded with `r`), so the incumbent after the first `r` restarts of
a 15-restart run is exactly what a run with `restarts = r` returns. The experiment therefore needs no extra
solves: it rebuilds, for every instance, the incumbent after each restart from the per-restart data of one full
run of the benchmark and reports the quality at budgets 1, 3, 5, 10 and 15 restarts.

## Solver side

`settings.logRestarts` (new, off by default; `--set logRestarts=true`) writes `restarts/<instance>.csv` into the
results folder, one row per restart: the restart's best cost and routes, the iteration it was found at, the
restart's wall time, the elapsed time, and the incumbent after the restart (`IO/RestartWriter.cs`, hook at the
end of the restart loop in `Search/Solver.cs`). It does not change the search.

The per-iteration trace of an earlier run (`traceIterations`) holds the same information except the times
(the restart-best cost and routes are the last row of every restart), so `analyze_exp5.py` also runs on
`results/exp3_*_all`; the time of restart `r` is then estimated as `r/15` of the solve's total time.

## Files

| File | What it is |
|---|---|
| `run-exp5.ps1` | VM: solves the whole benchmark (`-All`) or a subset with `logRestarts=true` into `results\exp5_<date>_all\` |
| `analyze_exp5.py` | the analysis: `--run <results folder>` (needs `runs.csv` and `restarts/` or `traces/`), writes `<run>/exp5/` |

## Outputs of `analyze_exp5.py`

| Output | Content |
|---|---|
| `per_restart.csv` | per instance and `r`: restart best, incumbent after `r` restarts, fleet excess and cost gap (%) to the final solution, elapsed time; `exp_*` columns: the same averaged over all orders of the restarts (order statistics, exact) |
| `r_star.csv` | per instance: `r_fleet` / `r_cost`, the restart at which the final fleet / the final solution was first reached, and the times to them |
| `tradeoff.csv` | quality at 1, 3, 5, 10, 15 restarts by benchmark, by size and by class: share at the final fleet, mean / median / max cost gap, share with the final solution found, expected values over orders, elapsed time |
| `class_found.csv`, `class_fleet.csv`, `class_gap.csv` | per class, `R1..R15`: share of instances whose final solution (final fleet) is already found; mean cost gap |
| `plots/convergence_gap.png` | mean cost gap vs restarts, by size (solid: the actual order; dashed: expected over orders) |
| `plots/convergence_found.png` | share of instances at the final solution / final fleet vs restarts |
| `plots/r_star_hist.png` | histogram of the restart that produced the final solution |
| `plots/trajectories_largest.png` | the 400-customer instances one by one |
| `summary.md` | the numbers and tables for the response letter |

## Run

VM (PowerShell, folder with `VrdpoProject.exe`, `settings.json`, the instances and this script):

    powershell -ExecutionPolicy Bypass -File .\run-exp5.ps1 -All -CheckOnly
    powershell -ExecutionPolicy Bypass -File .\run-exp5.ps1 -All

Locally, on the copied folder (or on the exp3 run, which has the traces):

    python3 experiments/exp5-restart-returns/analyze_exp5.py --run results/exp5_<date>_all
    python3 experiments/exp5-restart-returns/analyze_exp5.py --run results/exp3_2026-11-02_1645_all

## Definitions

* Incumbent after `r` restarts: the solver's rule applied to restarts `1..r`, fewer routes first, then cost
  (tolerance 0.001 on the Dumez et al. instances, exact on the Tilk et al. ones). The rebuilt 15-restart
  incumbent is checked against `runs.csv`.
* Cost gap (%): `(incumbent cost - final cost) / final cost`, reported only when the incumbent has the final
  fleet (a cost gap across different fleets is meaningless); `fleet_excess` is the number of extra vehicles.
* `found_final`: incumbent equals the final solution (same fleet, cost within 0.001).
* A restart can end without any solution meeting the service levels (about 1.5% of the restarts, on
  50/100-customer instances); until the first one that does, the instance has no incumbent (`no_solution_%`).
