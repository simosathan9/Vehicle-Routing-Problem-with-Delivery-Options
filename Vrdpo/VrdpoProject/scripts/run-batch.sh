#!/usr/bin/env bash
# Runs the solver on many instances concurrently, as one experiment.
#
#   scripts/run-batch.sh [--tag <name>] [--jobs <n>] <instance.txt>... [-- <solver options>...]
#
#   --tag   experiment name; default: the current date and time, e.g. 2026-10-15_143005
#   --jobs  solves running at the same time; default: number of CPU cores
#
# Examples (from Vrdpo/VrdpoProject, after `dotnet build -c Release`):
#   scripts/run-batch.sh Instances/U/50/*.txt Instances/V/50/*.txt
#   scripts/run-batch.sh --tag exchange0 --jobs 8 Instances/U/50/*.txt -- --set exchangeCandidates=0
#
# Outputs under results/<tag>/: batch.txt (what ran: commit, options, instances), settings.json,
# runs.csv (one row per instance), <set>/<instance>.txt (full solution), logs/<instance>.log (console).
set -euo pipefail

tag=""
jobs=""
instances=()
while [ $# -gt 0 ]; do
  case "$1" in
    --tag)  tag=$2; shift 2 ;;
    --jobs) jobs=$2; shift 2 ;;
    --)     shift; break ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *)      instances+=("$1"); shift ;;
  esac
done
extra=("$@")
if [ ${#instances[@]} -eq 0 ]; then sed -n '2,15p' "$0"; exit 2; fi

[ -n "$tag" ] || tag=$(date +%Y-%m-%d_%H%M%S)
[ -n "$jobs" ] || jobs=$( (nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4) )

here=$(cd "$(dirname "$0")/.." && pwd)
cd "$here"
dll=$(ls bin/Release/net*/VrdpoProject.dll 2>/dev/null | sort | tail -1)
if [ -z "$dll" ]; then echo "no Release build found; run: dotnet build -c Release" >&2; exit 1; fi

out="results/$tag"
mkdir -p "$out/logs"
{
  echo "started:   $(date '+%Y-%m-%d %H:%M:%S')"
  echo "host:      $(hostname)"
  echo "commit:    $(git rev-parse --short HEAD 2>/dev/null || echo unknown)$(git diff --quiet 2>/dev/null || echo ' (uncommitted changes)')"
  echo "runtime:   $(dotnet --version 2>/dev/null || echo unknown) sdk, dll $dll"
  echo "jobs:      $jobs"
  echo "options:   ${extra[*]:-}"
  echo "instances: ${#instances[@]}"
  printf '  %s\n' "${instances[@]}"
} >> "$out/batch.txt"

echo "experiment: $out  (jobs=$jobs, instances=${#instances[@]}, options: ${extra[*]:-none})"
printf '%s\n' "${instances[@]}" | xargs -P "$jobs" -I{} sh -c '
  inst="$1"; name=$(basename "$inst" .txt)
  dotnet "'"$dll"'" "$inst" --tag "'"$tag"'" --set verbal=false '"${extra[*]:-}"' > "'"$out"'/logs/$name.log" 2>&1 \
    && echo "done  $name" || echo "FAILED $name (see '"$out"'/logs/$name.log)"
' _ {}
echo "finished:  $(date '+%Y-%m-%d %H:%M:%S')" >> "$out/batch.txt"
echo "results: $out/runs.csv"
