#!/usr/bin/env bash
# Local (macOS/Linux) counterpart of run-exp4.ps1: solves subset.txt once per memory setup with `dotnet run`.
# Usage, from Vrdpo/VrdpoProject:  experiments/exp4-promise-vs-tabu/run-exp4.sh [jobs] [tenures...]
#   e.g.  experiments/exp4-promise-vs-tabu/run-exp4.sh 8            # promise, tabu 5/10/15/20, tabudyn, none
#         experiments/exp4-promise-vs-tabu/run-exp4.sh 8 10 20      # promise, tabu10, tabu20, tabudyn, none
set -euo pipefail
cd "$(dirname "$0")/../.."
JOBS=${1:-$(( $(sysctl -n hw.ncpu 2>/dev/null || nproc) - 2 ))}; shift || true
TENURES=("$@"); [ ${#TENURES[@]} -eq 0 ] && TENURES=(5 10 15 20)
STAMP=$(date +%Y-%m-%d_%H%M)
dotnet build -c Release -nologo -v q > /dev/null
EXE=$(ls -t bin/Release/net*/VrdpoProject.dll | head -1)   # the newest build (the csproj targets net8.0)
COMMON=(--set verbal=false --set traceIterations=false --set countMoves=false --set countCoupling=false --set logRestarts=true --set countStates=true)
SETUPS=("promise|--set memory=promise")
for t in "${TENURES[@]}"; do SETUPS+=("tabu$t|--set memory=tabu --set tabuTenure=$t"); done
SETUPS+=("tabudyn|--set memory=tabu --set tabuTenure=0" "none|--set memory=tabu --set tabuTenure=-1")
NAMES=$(sed 's/#.*//' experiments/exp4-promise-vs-tabu/subset.txt | tr -d ' \r' | grep -v '^$')
JOBDIR=$(mktemp -d); k=0
for n in $NAMES; do
  f=$(find Instances -name "$n.txt" | head -1); [ -z "$f" ] && { echo "missing $n" >&2; exit 1; }
  case $n in *small*|*medium*|*large*) type=int;; *) type=double;; esac
  for s in "${SETUPS[@]}"; do
    tag="exp4_${STAMP}_${s%%|*}"; extra=${s#*|}
    mkdir -p "results/$tag/logs"
    k=$((k+1)); printf '#!/bin/sh\ncd "%s"\ndotnet "%s" "%s" --tag %s --set type=%s %s %s > results/%s/logs/%s.log 2> results/%s/logs/%s.err\n' "$PWD" "$EXE" "$f" "$tag" "$type" "${COMMON[*]}" "$extra" "$tag" "$n" "$tag" "$n" > "$JOBDIR/$(printf %04d $k).sh"
  done
done
echo "$k solves, $JOBS at a time -> results/exp4_${STAMP}_*"
for s in "${SETUPS[@]}"; do printf 'experiment: 4 (promise vs tabu)\nstarted: %s\nhost: %s\noptions: %s %s\n' "$(date)" "$(hostname)" "${COMMON[*]}" "${s#*|}" > "results/exp4_${STAMP}_${s%%|*}/batch.txt"; done
ls "$JOBDIR"/*.sh | xargs -P "$JOBS" -n 1 sh
for s in "${SETUPS[@]}"; do echo "finished: $(date)" >> "results/exp4_${STAMP}_${s%%|*}/batch.txt"; done
echo "done: python3 experiments/exp4-promise-vs-tabu/analyze_exp4.py --runs results/exp4_${STAMP}_*"
