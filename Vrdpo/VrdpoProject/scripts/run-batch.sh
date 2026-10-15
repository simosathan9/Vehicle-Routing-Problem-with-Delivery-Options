#!/usr/bin/env bash
# Runs the solver on many instances concurrently, all under one experiment tag.
#
#   scripts/run-batch.sh <tag> <jobs> <instance.txt>... [-- <solver options>...]
#
# Example (from Vrdpo/VrdpoProject, after `dotnet build -c Release`):
#   scripts/run-batch.sh exchange20 8 Instances/U/50/*.txt Instances/V/50/*.txt
#   scripts/run-batch.sh exchange0  8 Instances/U/50/*.txt -- --set exchangeCandidates=0
#
# Outputs: results/<tag>/runs.csv (one row per instance), results/<tag>/<set>/<instance>.txt,
# results/<tag>/settings.json, and the console of each solve in results/<tag>/logs/<instance>.log.
set -euo pipefail

if [ $# -lt 3 ]; then
  sed -n '2,12p' "$0"; exit 2
fi
tag=$1; jobs=$2; shift 2
instances=()
while [ $# -gt 0 ] && [ "$1" != "--" ]; do instances+=("$1"); shift; done
[ $# -gt 0 ] && shift   # drop "--"
extra=("$@")

here=$(cd "$(dirname "$0")/.." && pwd)
cd "$here"
dll=$(ls bin/Release/net*/VrdpoProject.dll 2>/dev/null | sort | tail -1)
if [ -z "$dll" ]; then echo "no Release build found; run: dotnet build -c Release" >&2; exit 1; fi

mkdir -p "results/$tag/logs"
echo "tag=$tag jobs=$jobs instances=${#instances[@]} dll=$dll extra=${extra[*]:-}"
printf '%s\n' "${instances[@]}" | xargs -P "$jobs" -I{} sh -c '
  inst="$1"; name=$(basename "$inst" .txt)
  dotnet "'"$dll"'" "$inst" --tag "'"$tag"'" --set verbal=false '"${extra[*]:-}"' > "results/'"$tag"'/logs/$name.log" 2>&1 \
    && echo "done  $name" || echo "FAILED $name (see results/'"$tag"'/logs/$name.log)"
' _ {}
echo "results: results/$tag/runs.csv"
