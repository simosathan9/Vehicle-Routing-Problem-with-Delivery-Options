#!/bin/bash
# Captures Phase-0 baselines for the fast (25/50-customer) smoke-subset instances.
INSTANCES=(
  "Instances/U/25medium/U_25medium_1.txt"
  "Instances/U/25large/U_25large_1.txt"
  "Instances/U/50small/U_50small_1.txt"
  "Instances/V/25small/V_25small_1.txt"
  "Instances/V/25medium/V_25medium_1.txt"
  "Instances/V/25large/V_25large_1.txt"
  "Instances/V/50small/V_50small_1.txt"
  "Instances/UBC/50/UBC_50_1.txt"
)
for inst in "${INSTANCES[@]}"; do
  base=$(basename "$inst" .txt)
  /c/Source/VRPDO/tools/run_instance.sh "$inst" "/c/Source/VRPDO/baselines/phase0/${base}.txt"
done
echo "DONE_ALL_SMOKE_BASELINE"
