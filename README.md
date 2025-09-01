<div align="center">

# VRPDO
**Vehicle Routing Problem with Delivery Options – Local Search / Metaheuristic Implementation**

</div>

## 1. Overview
This repository contains a research‑grade C# implementation of a metaheuristic solver for the Vehicle Routing Problem with Delivery Options (VRPDO). The VRPDO extends the classical Vehicle Routing Problem with Time Windows (VRPTW) by allowing each customer to specify *multiple alternative delivery options* (e.g., home, workplace, parcel locker / pickup point) each with its own location, time window, service duration and priority (preference) level. The solver assigns exactly one option per customer and builds capacity‑feasible, time‑feasible vehicle routes that satisfy *service level constraints* on customer preferences while minimizing (lexicographically) the number of active routes and then total travel cost.

The codebase implements:
* A constructive multi‑option selection and insertion procedure biased toward feasibility and high service levels.
* A multi‑restart Local Search framework with a rich neighborhood portfolio (Relocation, Swap, Two‑Opt (intra/inter), Flip (option change), and Priority Swap (paired option exchange guided by priority structure)).
* A promise (sparse memory) matrix acting as a lightweight adaptive memory / aspiration mechanism to avoid cycling and filter unpromising move patterns.
* Penalized objective components that encourage route consolidation (implicit vehicle count minimization) prior to pure distance minimization.
* Service level checks ensuring (for the provided experimental configuration) at least 80% of customers receive their first‑priority option and 90% receive first or second priority combined.

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

Default feasibility thresholds enforced in the code:

| Metric | Constraint | Interpretation |
|--------|------------|----------------|
| SL0    | ≥ 0.80     | At least 80% of customers receive their first‑priority option |
| SL1    | ≥ 0.90     | At least 90% receive either first or second priority |

Moves that would drop the solution below these thresholds are generally rejected unless they simultaneously improve the priority mix (e.g., replace a low priority with a higher one) or are part of construction steps needed to reach feasibility.

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
| `Promises[a,b]` | Lowest global solution cost at which directed edge (a,b) last appeared (adaptive memory) |

Conceptual decision variables (not explicitly materialized as full matrices):
* Option selection: for each customer `c`, exactly one option in `O_c` is chosen (enforced procedurally during construction and Flip/PrioritySwap moves).
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

## 3. Algorithmic Framework
### 3.1 High‑Level Flow
1. (Optional) Multiple restarts (independent constructions) when `multiRestart = true`.
2. Construct an initial feasible solution (option assignment + route insertion) emphasizing early satisfaction of priority service level thresholds and route consolidation.
3. Apply Iterative Local Search with a set of neighborhoods; at each iteration evaluate best (or filtered best) move across operators.
4. Maintain and update a promise matrix `Promises[a,b]` storing the best (lowest) global cost at which an ordered edge (option adjacency) has appeared; reject moves that would *not* improve beyond the stored promise to reduce cycling.
5. Track the best solution (lexicographic: fewer routes, then cost) across repetitions and restarts; output detailed report.

### 3.2 Construction Heuristics
* Identify customers with single options; force selection early.
* Greedy multi‑criteria scoring for candidate first options combining: distance to already selected options (dispersion / clustering control), time window width, and temporal overlap to foster route packability.
* Shared location capacity respected incrementally; randomization among top scoring candidates introduces diversification.
* Incremental insertion: For each not yet routed customer, evaluate cheapest feasible insertion positions using time feasibility checks (`RespectsTimeWindow2`) and maintain the top few (e.g., three) cost candidates before choosing.
* Penalized insertion variant augments pure travel cost with route index penalty and load factor to encourage filling earlier routes (vehicle minimization proxy).

### 3.3 Neighborhood Operators
| Operator | Purpose | Key Feasibility / Cost Components |
|----------|---------|-----------------------------------|
| Relocation | Move single option to new position/route | Capacity, time windows, service levels, route count penalty |
| Swap | Exchange two options (intra/inter) | Dual feasibility check with time windows & capacity |
| Two‑Opt (generalized) | Segment reversal (intra) or tail exchange (inter) | Time rewrite using forward/backward ECT/LAT recomputation |
| Flip | Replace served option of a customer by an alternative (possibly moving it) | Maintains service level thresholds; adjusts shared capacities |
| PrioritySwap | Paired option substitution for two customers swapping priority patterns | Guides improvement in service distribution & cost |

Each operator evaluates a move cost augmented by:
* Route opening/closing penalty: `openRoutes * 10000` (aligning with lexicographic min vehicles objective).
* Route utilization metric: quadratic slack penalty (square of unused capacity) to bias toward balanced loads. Ratio of old/new utilization modulates move acceptance (acts like cost smoothing / secondary objective integration).

### 3.4 Adaptive Memory (Promises)
`Promises[a,b]` records the global solution cost when edge (a,b) was last accepted. A candidate move introducing an adjacency (a,b) at a *higher* or equal cost is discarded (unless it yields route reduction or improved lexicographic score), mimicking a strategic oscillation / aspiration filter without full tabu tenure bookkeeping.

### 3.5 Service Level Handling
During local search, moves (notably Flip) are only accepted if resulting temporary service levels remain above thresholds; if below, only improving priority replacements are considered. A temporary evaluation function recomputes adjusted service levels under hypothetical leave/enter priorities.

### 3.6 Similarity & Diversification
For multi‑restart mode, constructed solutions are compared via a simple Jaccard‑style index over ordered option IDs to quantify similarity; this can be extended to seed adaptive restart strategies (not yet automated in code but foundation present).

## 4. Project Structure
```
Vrdpo/
	VrdpoProject.sln / .csproj       Solution & project metadata
	VrdpoProject/
		Solver.cs                      Main orchestration (construction + local search loops)
		Solution.cs                    Data model for a solution, deep copy, timing, feasibility checks
		Route.cs / Customer.cs / Option.cs / Location.cs  Core entities
		LocalSearch.cs                 Neighborhood definitions & application logic
		*Move Classes* (Relocation, Swap, TwoOpt, Flip, PrioritySwap) – state containers & validation
		InstanceReader.cs              Instance parsing & model building (locations, options, matrices)
		Settings.cs / settings.json    Runtime configuration
		Resources.*                    (If present) ancillary resources
		Instances/                     Benchmark instance directory (subfolders by class/size)
```

## 5. Building & Running
### Prerequisites
* .NET SDK 8.0+

### Quick Start
```bash
dotnet build Vrdpo/VrdpoProject/VrdpoProject.csproj -c Release
dotnet run --project Vrdpo/VrdpoProject/VrdpoProject.csproj
```

The solver reads `settings.json` and (by default) expects an instance file path to be configured inside the code (`InstanceReader`), or you can modify `Solver` to set `solver.Instance = "path/to/instance.txt"` before calling `Solve()`.

By default the active instance file is passed through the launch profile in `Vrdpo/VrdpoProject/Properties/launchSettings.json` under `commandLineArgs`. Example:

```json
{
	"profiles": {
		"VrdpoProject": {
			"commandName": "Project",
			"commandLineArgs": "instances/U/25large/U_25large_1.txt"
		}
	}
}
```
Update that path (or override on the command line) to switch instances without touching source code.

### Output
* Console log: progress per restart / repetition, service levels, incremental best.
* Text report per instance: summary, per‑route listing, final cost, service levels.
* `log.txt`: append‑only log of best solutions found (instance, cost, route count, timestamp).

## 6. Configuration (`settings.json`)
Runtime behaviour is controlled via this JSON file placed alongside the executable. Current keys and intent (with representative defaults from the repository) are below.

| Field | Description | Typical Effect / Guidance | Default* |
|-------|-------------|---------------------------|----------|
| `restarts` | Number of independent construction + local search restarts (only if `multiRestart=true`). | Increase for diversification on harder / larger instances. | 15 |
| `repetitions` | Maximum local search iterations per restart. | Higher values deepen intensification; time grows roughly linearly. | 15000 |
| `verbal` | Toggle verbose console logging. | Set `false` for batch experiments to reduce I/O overhead. | true |
| `promisesRestartRatio` | Multiple determining how often the promise (edge memory) matrix is re‑initialized: trigger after `|Options| * ratio` iterations. | Larger value = longer memory (more aggressive pruning), smaller = more flexibility. | 1.5 |
| `multiRestart` | Enable multi‑restart strategy. | Use `true` for robustness; `false` for quick single run. | false |
| `schema` | Move selection scheme. Currently only `greedy` implemented (future: adaptive, randomized variants). | Controls how the best move among operators is chosen. | "greedy" |
| `type` | Distance/time metric mode: `int` (rounded/scaled) or `double` (raw Euclidean). | Use `int` for speed, `double` for precision / final polishing. | "int" |

*Defaults shown are those in the committed `settings.json` at the time of writing.

After changing `settings.json`, simply re‑run the executable; no rebuild is required unless you modified source code.

## 7. Instance Format (Abstract)
While full specification resides in the manuscript, the parser (`InstanceReader`) expects a structured plain text file containing:
1. Header lines including capacity, counts: number of locations, customers, options.
2. A block of customer definitions: customer id, demand.
3. A block of location definitions: id, coordinates, time window bounds, service / parking / type indicators, capacity (for shared locations), preparation times.
4. A block of option definitions: option id, location reference, customer reference, priority, (possibly) service / delivery times, and induced time window.
5. (Implicit) Distance & time matrices are *not* provided; they are computed on the fly via Euclidean distance and stored in an upper triangular compact form.

Extend / adapt `InstanceReader.BuildModel()` if your data diverges (e.g., explicit travel times, asymmetric costs, multiple depots, heterogeneous fleet).

## 8. Extending the Solver
Potential enhancements:
* Add Large Neighborhood Search (ruin & recreate) layer atop existing neighborhoods.
* Introduce adaptive penalty adjustment for service level violations enabling soft constraints.
* Integrate exact pricing (column generation) for route set refinement (matheuristic hybridization).
* Multi‑objective weighting of carbon footprint (e.g., distance vs. locker usage) with Pareto archive.
* Parallel evaluation of neighborhoods (ensure thread‑safe clones of solution state).

## 9. Performance & Tuning Tips
* Increase `restarts` for diversification; moderate `repetitions` to control runtime.
* Start with `type = "int"` (coarser metric) for faster move evaluation; refine with `double` for final polishing.
* Adjust route penalty (currently hard‑coded 10000) if instance scale (total distance magnitude) changes significantly.
* For large instances, consider pruning candidate insertion positions beyond top‑k (already partially implemented) to reduce O(n²) loops.

## 10. Reproducing Experiments
1. Collect benchmark instances into `Instances/` preserving subfolder taxonomy.
2. Set `multiRestart = true`; choose `restarts` (e.g., 10–30) and `repetitions` (e.g., 10000–20000) per instance size.
3. Run the solver sequentially or script batch executions (e.g., shell loop invoking `dotnet run`).
4. Parse generated reports and aggregate KPIs: cost, route count, `SL0`, `SL1`.
5. (Optional) Compare against reference best‑known solutions from literature (cited in manuscript) for validation.

## 11. Code Quality & Design Notes
* Deep copy mechanisms in `Solution.DeepCopy` ensure isolation of candidate states across moves / restarts.
* Time window feasibility uses earliest completion (ECT) / latest allowable (LAT) forward/backward passes after hypothetical insertions (`RespectsTimeWindow2`).
* The *promise matrix* is a sparse memory comparable conceptually to edge-based tabu tenure but deterministic and cost‑threshold driven.
* Quadratic capacity slack fosters consolidation without explicit penalty tuning (acts similar to ejection chain biasing).

## 12. Citation
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

## 13. License
Distributed under the terms of the repository `LICENSE` (see file). Ensure compatibility if integrating into closed-source systems.

## 14. Disclaimer
This is research software: correctness and performance have been validated on internal benchmarks but no warranty is provided. Review and adapt before production deployment.

## 15. Contact
For questions regarding algorithms, instances, or experimental reproduction, open an issue or submit a pull request.

---
Contributions (bug fixes, new operators, performance optimizations, documentation improvements) are welcome.
