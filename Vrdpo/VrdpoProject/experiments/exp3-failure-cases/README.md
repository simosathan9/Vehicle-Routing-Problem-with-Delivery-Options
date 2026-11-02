# Experiment 3: failure cases

Answer to referee 3, comment 3: *what characterizes the instances where the method underperforms (worse
cost at the same fleet; one vehicle more on some U400 / V100 instances)? Instance size, constraint tightness,
option density, or something else?* Two parts:

* **3A, feature correlation.** Structural features of every benchmark instance, extracted from the instance
  files, against the degradation of the paper's result on it (vehicle inflation, cost gap to the BKS).
  Needs no solver run: it uses the paper's result tables.
* **3B, case study.** The failure instances solved with the trace on, then the final solution is dissected:
  load profile, time-window and spatial structure, and an absorption test of the lightest routes that shows
  whether the extra vehicle is a bin-packing tail that no single relocation or flip can remove.

Since the solver changed after the submission, the revision reports the new results, so both parts run on
**one full run of the benchmark** (`run-exp3.ps1 -All` on the VM, 240 instances): 3A takes the solver side
from that run's `runs.csv` (`--runs`), and 3B picks the failures out of the same run (`run_case_studies.py`).
The analysis of the submitted tables (default inputs, no run needed) stays for the response letter, which
has to answer the referee's numbers as they stand.

## Files

| File | What it is |
|---|---|
| `export_results.py` | Writes `paper_results.csv` from `new_sols_paper1.xlsx` (Tilk et al.) and `new_sols_paper2.xlsx` (Dumez et al.): per instance the solver's cost and routes, the BKS cost and routes |
| `paper_results.csv` | 240 instances; 13 Tilk et al. instances have no reference solution (empty `bks_*`) |
| `vrpdo.py` | Instance parser, the solver's time model and the solution report parser, in Python (mirrors `IO/InstanceReader.cs`, `Model/Instance.cs`, `Model/Solution.cs`, `IO/ResultsWriter.cs`) |
| `instance_features.py` | Writes `instance_features.csv`: one row of structural features per instance file |
| `instance_features.csv` | The features of the 220 instances in `Instances/` (UBC_400 and V_400 files are not in the repository) |
| `analyze_exp3a.py` | 3A: joins features and results, correlation tables, group comparisons, plots |
| `run-exp3.ps1` | VM: solves the whole benchmark (`-All`) or `outliers.txt` with the trace on |
| `outliers.txt` | the fleet failures of the submitted tables and controls of the same classes (the subset mode of `run-exp3.ps1`) |
| `run_case_studies.py` | 3B: picks the failures of a run (fleet failures, worst cost gaps, controls) and runs `case_study.py` on each |
| `case_study.py` | 3B: dissects one solution report (tables, absorption test, plots) |

## 3A: features

From the instance file alone (`instance_features.py`, times in minutes as in the files):

| Group | Features |
|---|---|
| scale | `n_customers`, `n_locations`, `n_shared`, `n_private`, `n_options` |
| options | `options_per_customer`, `share_cust_1opt/2opt/3opt`, `share_cust_with_shared` (customers with a locker option), `share_cust_locker_only` (no private address), `share_options_shared`, `share_prio0_shared` |
| time windows | `horizon`, `tw_private_mean/median/min`, `tw_shared_mean`, `tw_option_mean` (over all options), `tw_prio0_mean`, `tw_slack_norm` = `tw_option_mean / horizon`, `tw_compat_private` and `tw_compat_all`: share of ordered location pairs that can be served one after the other (the classic time-window tightness of the routing part) |
| lockers | `locker_cap_total`, `locker_tightness` = customers with a locker option / locker slots (the referee's sum q / sum D, one slot per customer), `locker_forced` = locker-only customers / slots |
| capacity | `vehicle_capacity`, `demand_total`, `demand_mean`, `demand_lb` = ceil(demand / capacity), the bin-packing bound on the fleet |
| geometry | `depot_dist_mean`, `nn_dist_mean` (nearest neighbour among private addresses), `area`, `density` |

Added by `analyze_exp3a.py` from the BKS: `fill_bks` = demand / (capacity x BKS vehicles), `fleet_slack_bks` =
BKS vehicles - `demand_lb`, `customers_per_vehicle`.

Degradation metrics: `delta_veh` = LS - BKS vehicles; `gap_cost` = 100 (LS - BKS cost) / BKS cost on every
instance with a BKS; `gap_cost_same` = the same on the same-fleet cases only. A fleet failure is
`delta_veh > 0`, a cost failure is the same fleet with `gap_cost > 0`.

For every feature and metric: Pearson r and Spearman rho with p-values, pooled and per benchmark;
medians of the failing and non-failing instances with a Mann-Whitney test; scatter plots of the strongest
features; a Spearman heat map. The result tables also give the referee's counts again (`claim_check`).

### Running 3A

On the submitted tables (the response letter):

```bash
cd Vrdpo/VrdpoProject
pip install numpy pandas scipy matplotlib tabulate openpyxl
python experiments/exp3-failure-cases/export_results.py                      # paper_results.csv (done)
python experiments/exp3-failure-cases/instance_features.py --names experiments/exp3-failure-cases/paper_results.csv   # instance_features.csv (done)
python experiments/exp3-failure-cases/analyze_exp3a.py --out results/exp3a_analysis
```
When the UBC_400 and V_400 instance files are available, put them under `Instances/UBC/400/` and
`Instances/V/400/` and run the last two commands again: they are 20 of the 120 Dumez et al. instances, among
them 4 of the 14 fleet failures.

On a run of the revised solver (the paper): the same with `--runs`, which takes the cost, fleet and service
levels from the run's `runs.csv` (last solve per instance), keeps the BKS of `paper_results.csv`, and leaves
out the instances the run did not solve:
```bash
python experiments/exp3-failure-cases/analyze_exp3a.py --runs results/exp3_<date>_all/runs.csv --out results/exp3_<date>_all/exp3a
```

Outputs in `--out`: `summary.md` (everything, ready to read), `claim_check.csv`, `merged.csv`,
`correlations.csv`, `groups.csv`, `tightness.csv`, `plots/scatter_<metric>.png`, `plots/heatmap.png`; with
`--runs` also `results.csv`, the per-instance result table of the run in the `paper_results.csv` format.

### What the tables say so far (from the result spreadsheets, 207 instances with features and a BKS)

* **The referee's count.** Same-fleet cases worse than the BKS: Dumez et al. 27 of 105 (Tilk et al. 3 of
  102). The referee's "28 of 115 on small-scale instances" is the Dumez et al. table (the paper's numbers may
  differ slightly from the spreadsheets: check against the submitted tables). The 14 fleet failures are all
  Dumez et al. instances; Tilk et al. has none.
* **Cost failures are a small-instance effect.** Worse-cost shares per class: U_50 4/9, U_100 7/10, V_50
  4/10, V_100 6/7, UBC_100 1/10, UBC_200 1/10, V_200 2/8, and none on U_200, U_400, UBC_50, UBC_400. The
  matheuristics' advantage is where their set-partitioning component can be solved close to optimality; from
  200 customers on the local search is better on nearly every same-fleet case.
* **Fleet failures are a bin-packing effect.** Every fleet failure has `fleet_slack_bks = 0` (the BKS fleet is
  at the demand bound) and `fill_bks >= 0.99`. Of the Dumez et al. instances with `fill_bks > 0.995`, 9 of 14
  lose a vehicle; with `fill_bks <= 0.99`, 0 of 82. `fleet_slack_bks` and `fill_bks` are the strongest
  correlates of `delta_veh` (Spearman -0.52 and +0.44 pooled).
* **Time windows.** The V_100 and V_200 sets have 60-minute private windows (every other Dumez et al. set
  about 235 minutes), which is where 5 of the 10 fleet failures with instance files are. Tilk et al.'s
  small / medium / large classes are 60 / 120 / 240-minute private windows; the three Tilk cost failures are
  spread over them, and no Tilk instance loses a vehicle.

## 3B: case study

`case_study.py` takes a solution report (`results/<tag>/<set>/<size>/<instance>.txt`) and the instance
file, recomputes the schedule with the solver's time model (the distance metric is read from the run's
`settings.json`), and writes `case.md` with:

* **Routes.** Per route: stops, load and fill, distance, duration, waiting, total and minimum slack
  (latest minus earliest completion over its stops), stops at lockers, priorities served; against the
  capacity, the bin-packing bound and the BKS fleet.
* **Absorption test** of the k lightest routes (`--routes-tested`, default 3). For every stop: how many other
  routes have spare capacity for it; the number of feasible (route, position) insertions keeping its option
  (the relocation neighbourhood), and with its other options (the flip neighbourhood; locker capacity kept,
  service levels ignored). A stop that fits by capacity but has no feasible slot is blocked by the time
  windows alone: that is the bin-packing tail.
* **Greedy elimination.** Tries to empty each tested route one stop at a time (cheapest feasible insertion,
  any option, routes updated as it goes) and reports how many stops found a place. Every stop can be
  absorbable on its own and the route still not emptied, which is what a set-partitioning reassembly can do
  and a sequence of single moves cannot.
* **Plots.** `map.png` (routes, lockers as squares scaled by capacity, tested routes thick), `loads.png`
  (load per route against the capacity), `time.png` (time windows of the private addresses and lockers;
  every route's duration and slack), and with `--trace` `trace.png` (vehicles of the current and the
  restart-best solution per iteration: when the fleet stalls one above the BKS).

### The run on the VM

1. Publish the current solver and copy `VrdpoProject.exe`, `VrdpoProject.dll`,
   `VrdpoProject.runtimeconfig.json` and `settings.json` to the run folder (e.g. `Desktop\VRPDO`). The
   experiment adds nothing to the solver: it only switches the per-iteration trace on.
2. Copy `run-exp3.ps1` (and `outliers.txt` for the subset mode) to the same folder. Nothing else is needed on
   the VM: the analysis runs locally on the results.
3. The instance files are looked up in `VRPDO_instances\VRPDO_instances\<set>\<size>\` and
   `Instances\<set>\<size>\` under the run folder; `-All` takes every instance file found there, so the
   UBC_400 and V_400 files must be present for the full benchmark.
4. Check what is found, then run:
   ```powershell
   cd $HOME\Desktop\VRPDO
   powershell -ExecutionPolicy Bypass -File .\run-exp3.ps1 -All -CheckOnly
   powershell -ExecutionPolicy Bypass -File .\run-exp3.ps1 -All
   ```
   Options: `-Jobs N`, `-NoTrace` (no per-iteration trace, see below), `-Restarts N`, `-Repetitions N` (default: settings.json, 15 x 15000; a 400-customer
   instance takes about 3 hours, the whole benchmark one to two days on 8 cores). Without `-All` only
   `outliers.txt` is solved.

Results go to `results\exp3_<date>_<time>_all\` (`runs.csv`, `settings.json`, one solution report per
instance, `traces\`, `logs\`, `batch.txt`). Zip that folder and copy it here under `results/`; also copy
back the UBC_400 / V_400 instance files if they are not in the repository yet (3A needs their features).

### Running 3B

```bash
cd Vrdpo/VrdpoProject
python experiments/exp3-failure-cases/run_case_studies.py --run results/exp3_<date>_all
```
picks, from the run's `runs.csv` against the BKS: every fleet failure, the 3 worst same-fleet cost gaps per
benchmark (`--worst-cost`), one control per class with a fleet failure (the instance of that class with the
BKS fleet and the best gap; `--no-controls`), plus anything named in `--also`. It runs `case_study.py` on
each (report, trace, BKS fleet) into `<run>/case_studies/<instance>/` and writes `selection.csv` and
`case_studies.md`, one row per instance with the fleet against the BKS and the absorption and greedy
elimination counts of the lightest routes, linking to each `case.md`. The failures should show stops that
fit by capacity but cannot be placed one at a time; the controls should show the lightest routes emptying.

A single report can still be dissected by hand:
```bash
python experiments/exp3-failure-cases/case_study.py --report results/<tag>/U/400/U_400_4.txt \
       --trace results/<tag>/traces/U/400/U_400_4.csv --out results/<tag>/case_studies/U_400_4
```

On macOS/Linux:
```bash
scripts/run-batch.sh --tag exp3_outliers Instances/U/400/U_400_4.txt ... -- --set traceIterations=true
python experiments/exp3-failure-cases/case_study.py --report results/exp3_outliers/U/400/U_400_4.txt \
       --trace results/exp3_outliers/traces/U/400/U_400_4.csv --out results/exp3_outliers/case_studies/U_400_4
```

The solutions behind the submitted tables are not stored (only their cost and fleet), which is the other
reason the analysis is done on a new full run rather than on the submitted results.
