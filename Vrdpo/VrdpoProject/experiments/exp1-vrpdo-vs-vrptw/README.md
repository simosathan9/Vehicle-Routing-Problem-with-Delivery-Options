# Experiment 1: VRPDO vs VRPTW

Answer to referee 3, comment 1a. The question is how much the delivery options change the search: the same
solver is run on the same instances once as a VRPDO and once as a plain VRPTW, and the per-iteration
behaviour is compared.

| Setup | What it is | Settings |
|---|---|---|
| 1. VRPDO | The solver as is | (defaults) |
| 2. VRPTW | Every customer is locked to its home option; only routing moves | `lockToHome=true`, `routingMovesOnly=true` |

Both setups run with `traceIterations=true` and `countMoves=true`, on the tuning subset of the paper
(`subset.txt`).

## Definitions

* **Home option.** The customer's best-priority private address (location type 2). About 20% of the
  customers have no private address; they keep their best-priority option (a shared locker). Locking
  everybody to the priority-0 option instead is not possible: it overloads a locker in 142 of the 223
  benchmark instances. With the home lock no locker ever exceeds its capacity. The construction opens as
  many routes as it needs; service levels are not enforced in setup 2, since there is no choice left.
* **Routing moves.** Relocation, swap and 2-opt. Setup 2 turns off the flip, the exchange and the flip kick
  (the flip applied alone after 500 iterations without improvement).
* **Move counts** (per iteration, per operator; `Search/MoveStats.cs`):
  * *evaluated*: candidates whose cost the scan computed (capacity-feasible ones). The exchange counts the
    pairs it evaluates exactly, at most `exchangeCandidates` per iteration.
  * *improving*: evaluated candidates that improve the solution lexicographically (fewer used vehicles, or
    the same and less distance) and are feasible (capacity, time windows, locker capacity, service levels).
  * *admissible*: improving candidates that the promise memory does not forbid.

  The scans normally check feasibility only for candidates cheaper than the best one found so far
  ("cost-first" pruning). For the counters that pruning is removed: every improving candidate is checked.
  The applied move is unchanged; with `countMoves` on, a run follows exactly the same trajectory as without
  (checked on 4 instances, both metrics). Counting adds about 10% run time.
* **Used vehicles** are the non-empty routes. The scans' own route penalty counts the spare empty route as
  open, so to the search a 2-opt that moves a whole tail into the empty route looks like it saves a route,
  while it actually uses one more. The counters use the real change.

## Running it on the VM

1. Publish the `review-experiments` branch and copy `VrdpoProject.exe`, `VrdpoProject.dll`,
   `VrdpoProject.runtimeconfig.json` and `settings.json` to the run folder (e.g. `Desktop\VRPDO`).
2. Copy `run-exp1.ps1` and `subset.txt` (with the tuning instances filled in) to the same folder.
3. Check that every instance is found, then run:
   ```powershell
   cd $HOME\Desktop\VRPDO
   powershell -ExecutionPolicy Bypass -File .\run-exp1.ps1 -CheckOnly
   powershell -ExecutionPolicy Bypass -File .\run-exp1.ps1
   ```
   Options: `-Jobs N` (parallel solves, default = number of cores), `-Restarts N`, `-Repetitions N`
   (default: the values in settings.json, 15 x 15000).

Results go to two new folders, `results\exp1_<date>_<time>_vrpdo` and `..._vrptw`. Nothing is
overwritten: every run gets its own date and time. Each holds `runs.csv` (final solutions),
`traces\<instance>.csv` (one row per iteration, about 23 MB per instance at 15 x 15000),
`logs\`, `settings.json` and `batch.txt` (what ran, including the commit of the build).

On macOS/Linux the same runs can be made with the batch script:
```bash
scripts/run-batch.sh --tag exp1_vrpdo $(cat subset-paths.txt) -- --set traceIterations=true --set countMoves=true
scripts/run-batch.sh --tag exp1_vrptw $(cat subset-paths.txt) -- --set traceIterations=true --set countMoves=true --set lockToHome=true --set routingMovesOnly=true
```
(one batch per `type`: add `--set type=int` for the Tilk et al. instances).

## Analysis

```bash
pip install pandas matplotlib tabulate
python analyze_exp1.py --vrpdo results/exp1_..._vrpdo --vrptw results/exp1_..._vrptw --k 10 --out exp1_analysis
```

| Output | Content |
|---|---|
| `summary.md` | Mean improving / admissible / evaluated moves per iteration and share of iterations at a local optimum, per setup; improving routing moves around option changes |
| `summary_improving.csv` | The same per instance and setup |
| `after_option_moves.csv` | VRPDO: improving routing moves in the k iterations after a flip / exchange / kick flip, before it, and in all other iterations |
| `plots/trajectory_<instance>.png` | Gap (%) of the current solution to the best VRPDO solution, per iteration: mean over restarts, band = min..max over restarts (the variance); dashed = restart best |
| `plots/trajectory_all.png` | Mean gap (%) over all instances, band = 10..90% over instances |
| `plots/improving_<instance>.png` | Improving moves per iteration (rolling mean), mean and band over restarts |
| `plots/after_option_moves.png` | Improving routing moves at offsets -k..k around an option change |

## Trace columns

`restart, iteration, move, move_cost, cost, routes, best_cost, best_routes, sl0, sl1`, then
`eval_<op>, imp_<op>, adm_<op>` for op in swap, relocate, twoopt, flip, exchange, and
`eval_total, imp_total, adm_total`. `move` is the applied move (`swap`, `relocate`, `twoopt`, `flip`,
`exchange`, `kick_flip` or `none`). The counts describe the neighbourhood of the solution *before* the move
of the row; they are empty on `kick_flip` rows (no scan is made there).
