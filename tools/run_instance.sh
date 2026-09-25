#!/bin/bash
# Runs the VRPDO solver on one instance and captures a stable (timing-stripped) report.
# Usage: run_instance.sh <relative-instance-path e.g. Instances/U/25small/U_25small_1.txt> <output-baseline-file>
set -e
PROJ_DIR=/c/Source/VRPDO/Vrdpo/VrdpoProject
EXE=/c/Source/VRPDO/build_out/VrdpoProject.exe
INSTANCE_REL="$1"
OUT_FILE="$2"

cd "$PROJ_DIR"
INSTANCE_BASENAME=$(basename "$INSTANCE_REL" .txt)
REPORT_FILE="$(dirname "$INSTANCE_REL")/${INSTANCE_BASENAME}greedy.txt"

START=$(date +%s)
"$EXE" "$INSTANCE_REL" > /tmp/run_console.log 2>&1
RC=$?
END=$(date +%s)
WALL=$(( END - START ))

if [ ! -f "$REPORT_FILE" ]; then
  echo "FAIL: no report produced for $INSTANCE_REL (exit code $RC)" >&2
  exit 1
fi

# Strip the "Total time / Restart time" line (varies run to run) to get a stable diff target.
grep -v "^Total time:" "$REPORT_FILE" > "$OUT_FILE"
echo "wall_clock_seconds=$WALL" >> "$OUT_FILE.meta"
echo "exit_code=$RC" >> "$OUT_FILE.meta"
echo "$INSTANCE_REL -> $OUT_FILE  (${WALL}s)"
