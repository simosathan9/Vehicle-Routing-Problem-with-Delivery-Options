<div align="center">

# VRPDO
**Vehicle Routing Problem with Delivery Options – Local Search / Metaheuristic Implementation**

</div>

## 1. Overview
This repository contains a research‑grade C# implementation of a (mat)heuristic solver for the Vehicle Routing Problem with Delivery Options (VRPDO). The VRPDO extends the classical Vehicle Routing Problem with Time Windows (VRPTW) by allowing each customer to specify *multiple alternative delivery options* (e.g., home, workplace, parcel locker / pickup point) each with its own location, time window, service duration and priority (preference) level. The solver assigns exactly one option per customer and builds capacity‑feasible, time‑feasible vehicle routes that satisfy *service level constraints* on customer preferences while minimizing (lexicographically) the number of active routes and then total travel cost.

The codebase implements:
* A randomized cheapest-insertion construction.
* A multi‑restart local search with the neighborhoods Relocation, Swap, Two‑Opt (intra/inter), Flip (option change) and Exchange (paired option change at the service-level boundary).
* A promise matrix acting as a lightweight adaptive memory to avoid cycling.
* Penalized objective components that encourage route consolidation (implicit vehicle count minimization) prior to pure distance minimization.
* Service level checks ensuring at least 80% of customers receive their first‑priority option and 90% receive first or second priority combined.

## 2. Problem Description
### 2.1 Core Elements
Given:
* A homogeneous fleet with capacity `Cap` and a single depot.
* Customers \(N\), each with a set of delivery options \(O_c\). An option defines: location, preference (priority) rank, service times, and inherits the time window of its location.
* Locations can be private (exclusive; used at most once) or shared (parcel lockers / pickup points) with capacity (max number of options served) and generally wider time windows.
* Travel times / distances between locations (symmetric, derived from Euclidean coordinates here and stored in triangular form).

Decisions:
1. Select exactly one option per customer.
2. Sequence selected options into vehicle routes respecting capacity, time windows, shared location capacity, and service levels.

Objectives (lexicographic):
1. Minimize number of used routes (vehicles).
2. Minimize total travel cost (distance / time surrogate).

### 2.2 Service Levels
Priorities: `0` = first / highest, `1` = second, `2` = third (lower).

Definitions (informal):
* `SL0 = (# customers served with a priority-0 option) / (total served customers)`
* `SL1 = (# customers served with a priority-0 OR priority-1 option) / (total served customers)`

Feasibility thresholds enforced in the code (`Solver.MinServiceLevel0/1`):

| Metric | Constraint | Interpretation |
|--------|------------|----------------|
| SL0    | ≥ 0.80     | At least 80% of customers receive their first‑priority option |
| SL1    | ≥ 0.90     | At least 90% receive either first or second priority |

Moves that would drop the solution below these thresholds are rejected unless they improve the priority mix (e.g., replace a low priority with a higher one).

### 2.3 Notation
The table below summarizes principal sets, indices, parameters, and conceptual decision elements referenced in the implementation and manuscript. (Subscripts use plain text for GitHub legibility.)

| Symbol | Description |
|--------|-------------|
| `C` | Set of customers (index `c`) |
| `O_c` | Delivery option set for customer `c` |
| `O = ⋃_c O_c` | Full set of all delivery options |
| `L` | Set of physical locations (locker, private address, depot) |
| `V = O ∪ {0,0'}` | Working vertex set (start / end depot plus option vertices) |
| `R` | Set of vehicle routes constructed by the heuristic |
| `Cap` | Vehicle capacity (common to all vehicles) |
| `dem_c` | Demand of customer `c` |
| `prio_o` | Priority (preference level) of option `o` (0 = best) |
| `a_o`, `b_o` | Earliest (ready) and latest (due) allowable completion times for option/location `o` |
| `s_o` | Service (parking + handling + delivery) duration at option `o` |
| `cap_l^max` | Maximum number of customers that can be served at shared location `l` (if applicable) |
| `cap_l` | Current number of options already assigned at shared location `l` (dynamic during search) |
| `c_ij`, `t_ij` | Distance / cost and travel time between vertices `i` and `j` (symmetric, Euclidean‑derived) |
| `SL0`, `SL1` | Service level indicators (see Section 2.2) |
| `Promises[a,b]` | Solution cost at which directed arc (a,b) was last created (adaptive memory) |

Conceptual decision variables (not explicitly materialized as full matrices):
* Option selection: for each customer `c`, exactly one option in `O_c` is chosen (enforced procedurally during construction and by the Flip / Exchange moves).
* Routing arcs: implicit via per‑route ordered sequences of options; feasibility (capacity, time windows, shared location usage) maintained incrementally.

Lexicographic objective (implemented through penalties and ordering of acceptance criteria):
1. Minimize number of non‑empty routes `|R|`.
2. Minimize total travel cost `∑ c_ij` over consecutive option pairs on all routes.

Feasibility dimensions enforced:
* Vehicle capacity: cumulative demand on each route ≤ `Cap`.
* Time windows: earliest completion / latest allowable times (`ECT` / `LAT`) maintained via forward / backward passes after hypothetical insertions or moves.
* Shared location capacity: `cap_l ≤ cap_l^max` for each shared location.
* Option uniqueness: one served option per customer.
* Service levels: thresholds on `SL0` and `SL1` maintained during improvement.

The full mathematical programming model (in the manuscript) formalizes these with explicit binary variables and linear constraints. The code prioritizes efficient constructive and local improvement heuristics rather than exhaustive mixed‑integer enumeration, embedding constraints directly in neighborhood feasibility checks.

### 2.4 Time model
Travel times, time windows and service times are stored multiplied by 10. In `type: "int"` mode (Tilk et al. instances) travel time and cost are both `ceil(10 * Euclidean distance)`; in `type: "double"` mode (Dumez et al. instances) the cost is the raw Euclidean distance and the travel time is 10 times it. A delivery takes 5 minutes at a private address and 2 minutes at a shared location; the time window bounds the *start* of the delivery, so the delivery duration is added to every due date when the instance is loaded (`Instance.BuildModel`). The parking/handling time of a location is paid once per visit, not again for consecutive stops at the same location.

## 3. Algorithmic Framework
### 3.1 High‑Level Flow (`Search/Solver.cs`)
For each of `restarts` restarts (seeded by the restart index, so runs are reproducible on a deterministic runtime):
1. Construct a solution (`Search/Construction.cs`): repeatedly pick one of the three cheapest feasible (option, route, position) insertions at random until every customer is routed, keeping a spare empty route available. A failed construction is repeated with the next seed.
2. Run `repetitions` iterations of local search. Each iteration scans the Swap, Relocation, Two‑Opt, Flip and Exchange neighborhoods, applies the best move overall (compared on `moveCost + openRoutes * 10000`), and records the solution as the restart's best when it has fewer routes, or equal routes and lower cost, and meets the service levels.
3. Every `|Options| * promisesRestartRatio` iterations the promise matrix is reset. After 500 iterations without improvement, 1% of the iterations apply only the best Flip (a kick).
4. The best solution over all restarts (fewer routes, then cost) is reported.

### 3.2 Neighborhood Operators (`Search/LocalSearch.*.cs`)
| Operator | Purpose | Key Feasibility / Cost Components |
|----------|---------|-----------------------------------|
| Relocation | Move one stop to another position of any route | Capacity, time windows, promises, route count penalty |
| Swap | Exchange two stops (intra/inter) | Capacity, time windows, promises |
| Two‑Opt | Segment reversal (intra) or tail exchange (inter) | Capacity, time windows via forward/backward ECT/LAT recomputation, promises |
| Flip | Replace a customer's served option by another of its options, inserted anywhere | Service level thresholds, shared location capacity, time windows, promises |
| Exchange | One customer to a worse and another to a better priority option together, both re-inserted at their best position | Keeps the service level at its boundary; capacity, time windows, shared capacity, promises |

Each operator evaluates a move cost augmented by:
* Route opening/closing penalty: `openRoutes * 10000` (aligning with lexicographic min vehicles objective).
* Route utilization metric: quadratic slack `(Cap - load)^2` summed over the routes. The ratio of old to new metric multiplies the move cost and biases the search towards consolidated loads; it is 1 once the fleet is at the demand lower bound.

### 3.3 Adaptive Memory (Promises)
`Promises[a,b]` records the solution cost when arc (a,b) was last created by an applied move. A candidate move creating an arc (a,b) at a cost that is not below the stored promise is discarded, which suppresses cycling without tabu tenure bookkeeping.

### 3.4 Service Level Handling
The counts of served options per priority are maintained incrementally. A Flip to a lower priority is only accepted if the thresholds still hold afterwards; the Exchange move pairs a down-flip with an up-flip so that the thresholds hold even at the boundary.

## 4. Project Structure
```
Vrdpo/
  Vrdpo.sln
  VrdpoProject/
    Program.cs                 Entry point: parses the command line, loads settings and instance, runs, writes outputs
    Settings.cs                Run configuration (settings.json keys)
    settings.json              Default configuration
    Model/
      Instance.cs              Parsed instance (immutable) and BuildModel() -> fresh mutable objects
      Location.cs / Customer.cs / Option.cs / Route.cs
      Solution.cs              Routes, promises, service-level counts, time-window checks, consistency checks, deep copy
    IO/
      InstanceReader.cs        Instance file parser and distance/time matrices
      RunOptions.cs            Command line
      ResultsWriter.cs         results/<tag>/... outputs
      TraceWriter.cs           results/<tag>/traces/... per-iteration trace (traceIterations)
    Moves/                     Move records: Relocation, Swap, TwoOpt, Flip, ExchangeMove, Insertion
    scripts/run-batch.sh       Concurrent batch over many instances under one tag
    experiments/               Review experiments: run scripts (Windows), instance subsets, analysis
    Search/
      Solver.cs                Restart loop, move selection, acceptance, console reporting
      Construction.cs          Randomized cheapest insertion
      LocalSearch.cs           Shared helpers (promises, utilization, feasibility assertion)
      LocalSearch.Relocation.cs / .Swap.cs / .TwoOpt.cs / .Flip.cs / .Exchange.cs
    Instances/                 Benchmark instances (U, V, UBC = Dumez et al.; 25/50 small/medium/large = Tilk et al.)
```

## 5. Building & Running
### Prerequisites
* .NET SDK 8.0+ (the project targets `net8.0`).

### Quick Start
```bash
cd Vrdpo/VrdpoProject
dotnet build -c Release
dotnet run -c Release -- Instances/U/50/U_50_1.txt --tag baseline
```

### Command line
```
VrdpoProject <instance.txt> [--settings <file>] [--tag <name>] [--results <dir>] [--set <key>=<value>]...
  --settings  settings file (default: settings.json)
  --tag       experiment name; outputs go to <results>/<tag>/ (default: current timestamp)
  --results   results root directory (default: results)
  --set       override one setting, e.g. --set exchangeCandidates=0 (repeatable)
```
Paths are relative to the working directory (the project directory when using `dotnet run` or the launch profiles in `Properties/launchSettings.json` and `.vscode/launch.json`).

Example experiment over a set of instances:
```bash
for f in Instances/U/50/*.txt; do
  dotnet bin/Release/net8.0/VrdpoProject.dll "$f" --tag exchange20
done
for f in Instances/U/50/*.txt; do
  dotnet bin/Release/net8.0/VrdpoProject.dll "$f" --tag exchange0 --set exchangeCandidates=0
done
```

### Output
Everything a run produces goes under `results/<tag>/` (git-ignored):
```
results/
  exchange20/
    settings.json          the effective settings of the experiment (written once per tag)
    runs.csv               one row per solve: timestamp, instance, feasible, cost, routes, sl0, sl1,
                           restart, repetition, restart_time_s, total_time_s, and the main settings
    U/50/U_50_1.txt        full solution: summary and per-route listing (location, option, customer)
```
The console shows the progress per restart and, with `verbal: true`, one line per iteration (`iteration cost restartBestCost restartBestRoutes`). Numbers use a dot as decimal separator on every machine.

Solves of the same tag can run concurrently (one process per instance): `runs.csv` and the settings copy are written under a file lock, and every instance has its own report file. `scripts/run-batch.sh` runs a whole set as one experiment:
```bash
scripts/run-batch.sh Instances/U/50/*.txt Instances/V/50/*.txt
scripts/run-batch.sh --tag exchange0 --jobs 8 Instances/U/50/*.txt -- --set exchangeCandidates=0
```
The tag defaults to the current date and time and the number of concurrent solves to the CPU count. The script forces `verbal=false`, writes `results/<tag>/batch.txt` (commit, options, instance list, start and end time), keeps each solve's console in `results/<tag>/logs/<instance>.log`, and prints which instances finished or failed. Re-running with the same `--tag` appends to that experiment's `runs.csv`.

## 6. Configuration (`settings.json`)
| Key | Description | Default |
|-----|-------------|---------|
| `restarts` | Number of independent construction + local search restarts. | 15 |
| `repetitions` | Local search iterations per restart. | 15000 |
| `verbal` | Per-iteration console trace. Set `false` for batch experiments. | true |
| `promisesRestartRatio` | The promise matrix is reset every `|Options| * ratio` iterations. | 1.5 |
| `type` | Distance metric: `int` (Tilk et al.: `ceil(10 * distance)` for time and cost) or `double` (Dumez et al.: raw distance as cost, 10x as time). | "double" |
| `noImprovementLimit` | Iterations without a new restart-best after which a restart stops; `0` disables the rule. A value such as 2000 is about 5x faster with a slightly worse mean gap; until the first acceptable solution is found the count runs from iteration 0, so large instances may need a larger value or none. | 0 |
| `exchangeCandidates` | Exchange move: the `N` cheapest down/up flip pairs (by estimate) are evaluated exactly in every iteration; `0` switches the move off. Measured with 15 x 15000 against the same runs without it (cost change, SE; instances better / worse): Dumez-50 -0.81% +/- 0.30 (19 / 2), Dumez-100 -1.14% +/- 0.20 (28 / 1), Tilk-50 small -0.19% +/- 0.22 (8 / 2). Run time about 2 to 4 times longer. | 20 |

| `lockToHome` | VRPTW setup: every customer can only be served at its home option (best-priority private address; best-priority option if it has none). Service levels are not enforced. | false |
| `routingMovesOnly` | Only relocation, swap and 2-opt: no flip, no exchange, no flip kick. | false |
| `traceIterations` | One CSV row per iteration in `results/<tag>/traces/<instance>.csv` (applied move, current and restart-best cost and routes, service levels). | false |
| `countMoves` | Adds per-operator counts of evaluated, improving and admissible moves to the trace, without the scans' cost-first pruning. Does not change the trajectory. Needs `traceIterations`. | false |

With the four keys above at false the solver runs exactly as before. They are used by the review experiments in `experiments/` (see `experiments/exp1-vrpdo-vs-vrptw/README.md`).

Any key can be overridden for one run with `--set key=value`; the effective values are saved with the results.

### Reproducing the published tables
The published results (Tilk 25/50 and Dumez 50/100/200/400 tables) were produced by an earlier model of the problem (completion-time windows, travel time equal to the raw distance in `double` mode) and an earlier search (relocation never targeting the first route, no same-route 2-opt, no exchange move). This version solves the model of Tilk et al., Dumez et al. and Yang et al. and includes the search corrections, so it does not reproduce those tables run for run. To reproduce them, check out commit `b3bc63b` (the last one with the compatibility switches) and set `consistentTravelTime`, `windowBindsServiceStart`, `fixCloneSideEffect`, `fixSameRouteTwoOpt`, `relocateIntoFirstRoute`, `exactRelocationFeasibility` to `false` and `exchangeCandidates` to `0` in its `settings.json`.

Note on the runtime: on an arm64 Mac with the .NET 8.0.2 runtime the solver is not reproducible run for run (same seed, same binary, different trajectories after about 1000 iterations). Built for .NET 10 the runs are bit-identical. Measure any change on a current runtime, e.g. with `dotnet build -c Release -p:TargetFramework=net10.0`.

## 7. Instance Format
The parser (`IO/InstanceReader.cs`) expects a structured plain text file containing (see `Instances/README.txt` for the time conventions of the Tilk et al. instances):
1. Header line (line 4) with vehicle count, capacity and the numbers of locations, customers and options.
2. A block of customer definitions: customer id, demand.
3. The depot line, then a block of location definitions: id, coordinates, capacity (shared locations), ready, due, type (1 shared, 2 private), service time.
4. A block of option definitions: option id, location, customer, priority, service time, cost.
5. Distance & time matrices are *not* provided; they are computed from the coordinates and stored in an upper triangular compact form.

Extend `InstanceReader` / `Instance.BuildModel()` if your data diverges (e.g., explicit travel times, asymmetric costs, multiple depots, heterogeneous fleet).

## 8. Extending the Solver
* New move: add a record class under `Moves/`, a `LocalSearch.<Name>.cs` partial with `FindBest<Name>Move` / `Apply<Name>Move`, and wire it into the move selection in `Solver.RunRestarts`.
* New setting: add a property with a `JsonPropertyName` to `Settings.cs`; it is then readable from `settings.json`, overridable with `--set`, and saved with the results.
* Telemetry: the restart loop in `Solver.RunRestarts` is the single place where moves are selected and accepted; `ResultsWriter` is the single place that writes outputs.

## 9. Performance & Tuning Tips
* Increase `restarts` for diversification; moderate `repetitions` to control runtime.
* `noImprovementLimit` trades a little quality for a large time saving on small instances.
* Adjust the route penalty (`Solver.RoutePenalty`, 10000) if the total distance magnitude of your instances is of a different order.

## 10. Code Quality & Design Notes
* `Instance` is immutable; every `Solution` gets a fresh mutable copy of the locations, customers and options because their capacity-use and served flags change during the search.
* `Solution.DeepCopy` clones the whole object graph and re-links it by id; the promise matrix is shared on purpose (copies are never searched).
* Time window feasibility uses earliest / latest completion passes (`Solution.UpdateTimes`, `RespectsTimeWindow*`), memoized per neighborhood scan.
* Every applied move is followed by a route feasibility assertion that prints the violation (`[check] ...`); `Infeasible Solution!!!` on the console means a bug.

## 11. Citation
If you use this code or ideas, please cite the associated manuscript (preprint forthcoming). Placeholder BibTeX entry:
```bibtex
@article{Author2025VRPDO,
	title   = {A Local Search Matheuristic for the Vehicle Routing Problem with Delivery Options},
	author  = {First Author and Second Author and Third Author},
	journal = {European Journal of Operational Research},
	year    = {2025},
	note    = {Under review}
}
```

## 12. License
Distributed under the terms of the repository `LICENSE` (see file). Ensure compatibility if integrating into closed-source systems.

## 13. Disclaimer
This is research software: correctness and performance have been validated on internal benchmarks but no warranty is provided. Review and adapt before production deployment.

## 14. Contact
For questions regarding algorithms, instances, or experimental reproduction, open an issue or submit a pull request.

---
Contributions (bug fixes, new operators, performance optimizations, documentation improvements) are welcome.
