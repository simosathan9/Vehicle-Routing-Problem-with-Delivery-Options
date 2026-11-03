# Experiment 4: promise memory vs tabu tenure

Answer to referee 3, comment 4: *the promise-based tabu mechanism is described as tenure-free and purely
quality-driven, but why is it particularly suited to the VRPDO? Option-modifying moves cause large structural
changes; a fixed tenure might be too restrictive (blocking useful revisitations after option changes) or too
permissive (allowing cycling). A discussion of why cost-based promises handle option reassignment better than a
fixed tenure would strengthen the design rationale.*

The experiment replaces the promise rule by a standard attribute tabu with fixed or dynamic tenure, in the same
solver, at the same place in the code, under the same budget, and compares the two on the 33 calibration
instances of Section 5.3.1 (`subset.txt`): routing cost gap to the BKS, fleet vs the BKS, and search mobility
(distinct solution states visited, revisits).

## Solver side (`settings.memory`)

Both memories live in the arc matrix `Solution.Promises` and are consulted at the single check every scan makes
on the arcs a move creates (`LocalSearch.PromiseIsBroken`) and updated at the single call every move makes after
it is applied (`ArcsCreated` / `ArcsRemoved`):

| setting | promise (paper) | tabu |
|---|---|---|
| rule | an arc may be created only if the solution after the move is better than the solution in which the arc was last created | an arc removed by a move may not be re-created for `tabuTenure` iterations |
| update | on the created arcs: the new solution cost | on the removed arcs: the iteration until which the arc is tabu |
| aspiration | none (the cost rule is the aspiration) | `tabuAspiration` (default on): a tabu arc is allowed when the move's solution beats the restart's best cost so far |
| forgetting | the matrix is reset every `|Options| x promisesRestartRatio` iterations | the tenure expires; no reset |
| tenure | - | `tabuTenure` > 0 fixed; 0 = dynamic, drawn per arc from `[tabuTenureMin, tabuTenureMax] x |N|` (0.1, 0.2); -1 = no memory |
| direction | directed arcs | undirected (symmetric distances) |

With `memory = promise` (the default) the solver is unchanged, byte for byte, on the instances checked. One
difference is deliberate: for a same-route 2-opt (segment reversal) the paper's code records the promise on the
arcs (A, L) and (B, K), which are the arcs the inter-route tail exchange creates; a reversal actually creates
(A, K) and (B, L). The cost rule still blocks the undo move whichever arcs carry it, so the promise mode is left
as published; the tabu mode checks the arcs the reversal really creates (otherwise a tenure never bites on the
undo and the search 2-cycles).

Two more rules were needed for the tabu to be a fair opponent, both found by tracing cycles on U_50_1:

* a removed arc that the same move re-creates in the other direction (adjacent stops of one route) is still in
  the solution and is not made tabu;
* a move whose distance change is numerically zero (floating-point noise of 1e-13 that the scans' `moveCost != 0`
  test lets through, e.g. swapping two stops at the same locker) is never admissible under the tabu rule. The
  promise rule rejects such moves by its strict-improvement test; without the explicit rule, chains of zero-cost
  swaps between stops at one location cycled for ever under any tenure.

What remains after these fixes is the behaviour the experiment is about: with a tenure shorter than the cycle the
search falls into (22 iterations on one restart of U_50_1 with tenure 10) the tabu cycles, with tenure 20 or the
dynamic tenure it does not, and the promise never does whatever the instance.

`countStates` (needs `logRestarts`) hashes the arc set of the current solution every iteration and writes, per
restart, the number of distinct states visited, of iterations that revisited one, and of iterations in which no
admissible move existed (`restarts/<instance>.csv`, columns `states_unique`, `states_revisits`, `states_stalls`).

## Files

| File | What it is |
|---|---|
| `subset.txt` | the 33 calibration instances (9 Dumez et al., 24 Tilk et al.) |
| `run-exp4.ps1` | VM: solves the subset once per setup: promise, tabu5/10/15/20, tabudyn, none, into `results\exp4_<date>_<setup>\` |
| `run-exp4.sh` | the same on macOS/Linux with `dotnet run` |
| `analyze_exp4.py` | `--runs results/exp4_<date>_*`: results per setup, paired comparison with the promise setup, mobility, plots, summary |

## Run

VM (PowerShell, folder with `VrdpoProject.exe`, `settings.json`, the instances, `subset.txt` and this script):

    powershell -ExecutionPolicy Bypass -File .\run-exp4.ps1 -CheckOnly
    powershell -ExecutionPolicy Bypass -File .\run-exp4.ps1

Locally (from `Vrdpo/VrdpoProject`):

    experiments/exp4-promise-vs-tabu/run-exp4.sh 8
    python3 experiments/exp4-promise-vs-tabu/analyze_exp4.py --runs "results/exp4_<date>_*"

231 solves (33 x 7); the instances have 25 to 100 customers, so the whole run takes about an hour on 8 cores.

## Outputs of `analyze_exp4.py` (in `results/exp4_<date>/`)

| Output | Content |
|---|---|
| `results.csv` | per setup and instance: cost, routes, gap to BKS, fleet vs BKS, distinct-state share, revisits, restarts without a solution |
| `setups.csv` | per setup: mean/median/max gap, instances at/better/worse than the BKS, fleet counts, mobility, wins/ties/losses and Wilcoxon p against promise |
| `setups_by_benchmark.csv` | the same split by Tilk et al. / Dumez et al. |
| `pairs.csv` | per instance: cost and routes of every setup side by side, best setup |
| `plots/gap_and_mobility.png` | gap to BKS by setup (box plot) and distinct states visited by setup |
| `plots/gap_by_benchmark.png` | mean gap by setup and benchmark |
| `summary.md` | the numbers for the response letter |
