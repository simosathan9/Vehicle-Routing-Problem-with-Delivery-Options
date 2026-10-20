# Experiment 2: inter-route coupling

Answer to referee 3, comment 1b: *do shared locations with wide time windows reduce inter-route coupling?*
The hypothesis is that a stop at a shared delivery location (a locker, with a wide time window) can be
transferred between routes far more often than a stop at a private address, so routes are more
independently optimizable, which is the condition under which local search competes with global
restructuring.

The solver runs as a VRPDO (defaults) with `countCoupling=true` on the tuning subset of the paper
(experiment 1's `subset.txt`). For every iteration the relocation and swap scans count their
**inter-route** candidates by the type of the location(s) they transfer, and how many pass the
feasibility checks.

## Definitions

* **IDL / SDL.** Individual delivery location = private address (location type 2); shared delivery
  location = locker / pickup point (location type 1).
* **Categories** (`Search/CouplingStats.cs`). A relocation transfers one stop, a swap two:

  | Operator | Category | Meaning |
  |---|---|---|
  | relocate | `idl` | the moved stop is at a private address |
  | relocate | `sdl` | the moved stop is at a shared locker |
  | swap | `idl_idl` | both exchanged stops are at private addresses |
  | swap | `idl_sdl` | one at a private address, one at a shared locker |
  | swap | `sdl_sdl` | both at shared lockers |

  The IDL-only category of each operator (`idl`, `idl_idl`) is the pure routing baseline the others are
  compared with.
* **Counts**, per candidate, whatever its cost:
  * *evaluated*: the scan generated the candidate (origin and target route differ; relocations into an
    empty route are not counted, they open a vehicle rather than transfer between two routes).
  * *capacity-feasible*: the receiving route(s) stay within the vehicle capacity.
  * *feasible*: capacity-feasible and the receiving route(s) stay time-window feasible.

  Inter-route relocations and swaps change neither the served options nor the locations, so shared-location
  capacities and service levels are untouched; capacity and time windows are the only constraints.
* **Rates.**
  * *feasibility rate* = feasible / evaluated: the share of the inter-route neighbourhood that is usable.
  * *capacity pass rate* = capacity-feasible / evaluated.
  * *time-window pass rate given capacity* = feasible / capacity-feasible: the coupling measure net of the
    load. The vehicle load does not depend on the location type, so this isolates the time-window effect
    the hypothesis is about.
* **No cost-first pruning.** The scans normally check feasibility only for candidates cheaper than the
  best one found so far. For the counters every inter-route candidate is checked, whatever its cost. The
  applied move is unchanged: with `countCoupling` on, a run follows exactly the same trajectory as without
  (checked on U_50_1 and V_25small_6: identical final cost and routes).

## Running it on the VM

1. Publish the `exp2-inter-route-coupling` branch and copy `VrdpoProject.exe`, `VrdpoProject.dll`,
   `VrdpoProject.runtimeconfig.json` and `settings.json` to the run folder (e.g. `Desktop\VRPDO`).
2. Copy `run-exp2.ps1`, `analyze_exp2.py` and experiment 1's `subset.txt` to the same folder. For the analysis
   at the end of the run the VM needs Python 3 with `pip install pandas matplotlib tabulate`.
3. Check that every instance is found, then run:
   ```powershell
   cd $HOME\Desktop\VRPDO
   powershell -ExecutionPolicy Bypass -File .\run-exp2.ps1 -CheckOnly
   powershell -ExecutionPolicy Bypass -File .\run-exp2.ps1
   ```
   Options: `-Jobs N` (parallel solves, default = number of cores), `-Restarts N`, `-Repetitions N`
   (default: the values in settings.json, 15 x 15000). The Tilk et al. instances run with `type=int`,
   the others with `type=double`, chosen by the script from the instance name.

Results go to `results\exp2_<date>_<time>_coupling`. Nothing is overwritten: every run gets its own date
and time. The folder holds `runs.csv` (final solutions), `coupling\<instance>.csv` (one row per scanned
iteration, about 15 MB per instance at 15 x 15000), `coupling_totals.csv` (one row per instance with the
sums), `logs\`, `settings.json` and `batch.txt` (what ran). When the last solve is done the script runs
`analyze_exp2.py` on the folder and writes `analysis\` (the outputs listed below) next to it, so the run folder
holds the formatted result as well. `-NoAnalysis` skips this step; the command to run it later is printed.

On macOS/Linux the same runs can be made with the batch script (one batch per `type`):
```bash
scripts/run-batch.sh --tag exp2_dumez Instances/U/50/U_50_1.txt ... -- --set countCoupling=true
scripts/run-batch.sh --tag exp2_tilk Instances/U/25small/U_25small_1.txt ... -- --set countCoupling=true --set type=int
```

## Analysis

```bash
pip install pandas matplotlib tabulate
python analyze_exp2.py --results results/exp2_..._coupling --out exp2_analysis
```
Several result folders can be given (e.g. the two macOS batches). Without a `coupling/` folder the script
falls back to `coupling_totals.csv` (then no over-iterations plot).

| Output | Content |
|---|---|
| `summary.md` | Pooled feasibility rate per operator and category, mean/std of the per-instance rates, ratio to the IDL-only category (pooled, and mean/min/max of the per-instance ratios); the same for the capacity and time-window pass rates; per instance set (Dumez et al. / Tilk et al.); per instance |
| `summary.csv` | The pooled and per-set tables with every column |
| `feasibility_by_instance.csv` | Counts and rates per instance, operator and category |
| `plots/feasibility_rate.png` | Grouped bars: feasibility rate and time-window pass rate given capacity, per category (mean over instances, whiskers = std) |
| `plots/feasibility_rate_by_set.png` | The same per instance set |
| `plots/feasibility_by_instance.png` | Feasibility rate per instance and category, one panel per operator |
| `plots/feasibility_over_iterations.png` | Pooled feasibility rate along the search (iteration bins), per category: does the gap persist once the fleet is consolidated? |

## Coupling file columns

`restart, iteration, routes` (open routes of the solution the scan was made on), then for op in
`relocate, swap` and each category of the operator: `eval_<op>_<cat>, cap_<op>_<cat>, feas_<op>_<cat>`.
Kick iterations (the flip applied alone when the search is stuck) make no scan and have no row.
`coupling_totals.csv` has `instance, restarts, iterations` followed by the same counter columns, summed.
