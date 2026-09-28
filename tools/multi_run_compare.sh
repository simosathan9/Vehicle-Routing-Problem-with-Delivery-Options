#!/bin/bash
# Runs N trials of a given binary on U_400_1, sequentially, reporting each wall-clock time.
EXE="$1"
CWD="$2"
LABEL="$3"
N="${4:-3}"
cd "$CWD"
for i in $(seq 1 "$N"); do
  rm -f log.txt
  START=$(date +%s)
  "$EXE" Instances/U/400/U_400_1.txt > /tmp/multirun_${LABEL}_${i}.log 2>&1
  END=$(date +%s)
  echo "$LABEL trial $i: $(( END - START ))s"
done
