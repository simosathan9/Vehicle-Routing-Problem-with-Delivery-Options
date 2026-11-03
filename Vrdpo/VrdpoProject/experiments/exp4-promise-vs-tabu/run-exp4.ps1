# Experiment 4 of the review (referee 3, comment 4): the promise memory of the paper against standard tabu tenure.
#
# Solves every instance of subset.txt (the 33 calibration instances of Section 5.3.1) once per memory setup, with the
# default budget (15 x 15000) and the per-restart log and state counts on:
#   promise   the paper's rule (memory=promise, promisesRestartRatio=1.5)        -> results\exp4_<date>_promise\
#   tabu5/10/15/20   fixed tenure (memory=tabu, tabuTenure=theta, aspiration on)  -> results\exp4_<date>_tabu<theta>\
#   tabudyn   dynamic tenure in [0.1|N|, 0.2|N|] (tabuTenure=0)                 -> results\exp4_<date>_tabudyn\
#   none      no arc memory at all (tabuTenure=-1): plain best-move descent    -> results\exp4_<date>_none\
# Each results folder holds runs.csv, settings.json, one solution report per instance, restarts\<instance>.csv (one row
# per restart with the distinct states visited and the revisits) and logs\. A new run never overwrites an old one.
# Nothing is analysed on the VM: zip the results\exp4_<date>_* folders and run analyze_exp4.py locally (see README.md).
#
# Usage (PowerShell, in the folder with VrdpoProject.exe, settings.json, the instances and this script):
#   powershell -ExecutionPolicy Bypass -File .\run-exp4.ps1 -CheckOnly
#   powershell -ExecutionPolicy Bypass -File .\run-exp4.ps1
#   powershell -ExecutionPolicy Bypass -File .\run-exp4.ps1 -Tenures 10,20 -Jobs 6     # a subset of the setups
param(
    [string]$Base = $PSScriptRoot,                 # folder with VrdpoProject.exe and settings.json
    [string]$Subset = (Join-Path $PSScriptRoot "subset.txt"),
    [int]$Jobs = [Environment]::ProcessorCount,    # solves running at the same time
    [int]$Restarts = 0,                            # 0 = value of settings.json (15)
    [int]$Repetitions = 0,                         # 0 = value of settings.json (15000)
    [int[]]$Tenures = @(5, 10, 15, 20),            # fixed tabu tenures to run
    [switch]$NoDynamic,                            # skip the dynamic-tenure setup
    [switch]$NoNone,                               # skip the no-memory setup
    [switch]$NoPromise,                            # skip the promise setup (e.g. already run)
    [switch]$CheckOnly
)
$ErrorActionPreference = "Stop"

$exe = Join-Path $Base "VrdpoProject.exe"
if (-not (Test-Path $exe)) { throw "VrdpoProject.exe not found in $Base" }
if (-not (Test-Path (Join-Path $Base "settings.json"))) { throw "settings.json not found in $Base (copy it from the publish folder)" }
if (-not (Test-Path $Subset)) { throw "subset file not found: $Subset" }

# Instance names, one per line (e.g. U_50_3 or V_25medium_7); '#' starts a comment.
$names = Get-Content $Subset | ForEach-Object { ($_ -replace '#.*', '').Trim() } | Where-Object { $_ }
if (-not $names) { throw "$Subset lists no instances: add the names of the tuning subset, one per line" }

# Finds an instance file in the usual places; Tilk et al. instances (25/50 small/medium/large) use type=int.
function Resolve-Instance([string]$name) {
    $parts = $name -split "_"
    $kind = $parts[0]; $size = $parts[1]
    $candidates = @(
        (Join-Path $Base "$name.txt"),
        (Join-Path $Base "VRPDO_instances/VRPDO_instances/$kind/$size/$name.txt"),
        (Join-Path $Base "Instances/$kind/$size/$name.txt")
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    return $null
}

$work = @()
$missing = @()
foreach ($n in $names) {
    $file = Resolve-Instance $n
    if (-not $file) { $missing += $n; continue }
    $type = if ($n -match "small|medium|large") { "int" } else { "double" }
    $work += [pscustomobject]@{ Name = $n; File = $file; Type = $type }
}
if ($missing) {
    Write-Host "Not found: $($missing -join ', ')" -ForegroundColor Red
    throw "fix the names in $Subset or put the instance files in $Base"
}
Write-Host "$($work.Count) instances found:" -ForegroundColor Green
$work | ForEach-Object { Write-Host ("  {0,-16} type={1,-6} {2}" -f $_.Name, $_.Type, $_.File) }
if ($CheckOnly) { return }

$stamp = Get-Date -Format "yyyy-MM-dd_HHmm"
$setups = @()
if (-not $NoPromise) { $setups += [pscustomobject]@{ Tag = "exp4_${stamp}_promise"; Extra = @("--set", "memory=promise") } }
foreach ($t in $Tenures) { $setups += [pscustomobject]@{ Tag = "exp4_${stamp}_tabu$t"; Extra = @("--set", "memory=tabu", "--set", "tabuTenure=$t") } }
if (-not $NoDynamic) { $setups += [pscustomobject]@{ Tag = "exp4_${stamp}_tabudyn"; Extra = @("--set", "memory=tabu", "--set", "tabuTenure=0") } }
if (-not $NoNone) { $setups += [pscustomobject]@{ Tag = "exp4_${stamp}_none"; Extra = @("--set", "memory=tabu", "--set", "tabuTenure=-1") } }
if (-not $setups) { throw "no setup selected" }
$common = @("--set", "verbal=false", "--set", "traceIterations=false", "--set", "countMoves=false", "--set", "countCoupling=false", "--set", "logRestarts=true", "--set", "countStates=true")
if ($Restarts -gt 0) { $common += @("--set", "restarts=$Restarts") }
if ($Repetitions -gt 0) { $common += @("--set", "repetitions=$Repetitions") }

# What ran, for the record.
foreach ($s in $setups) {
    $dir = Join-Path $Base "results/$($s.Tag)"
    New-Item -ItemType Directory -Force -Path (Join-Path $dir "logs") | Out-Null
    @(
        "experiment: 4 (promise vs tabu), $($s.Tag)",
        "started:    $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "host:       $env:COMPUTERNAME",
        "exe:        $exe ($((Get-Item $exe).LastWriteTime.ToString('yyyy-MM-dd HH:mm')), product version $((Get-Item (Join-Path $Base 'VrdpoProject.dll')).VersionInfo.ProductVersion))",
        "jobs:       $Jobs",
        "options:    $($common + $s.Extra -join ' ')",
        "instances:  $($work.Count)"
    ) + ($work | ForEach-Object { "  $($_.Name) type=$($_.Type)" }) | Set-Content (Join-Path $dir "batch.txt")
}

# All setups of an instance are queued together, so a partial run still compares like with like.
$queue = New-Object System.Collections.Queue
foreach ($w in $work) { foreach ($s in $setups) { $queue.Enqueue([pscustomobject]@{ W = $w; S = $s }) } }
$total = $queue.Count
$running = @()
$done = 0
Write-Host "$(Get-Date -Format HH:mm:ss)  $total solves, $Jobs at a time. Results: $Base\results\exp4_${stamp}_*" -ForegroundColor Cyan

while ($queue.Count -gt 0 -or $running.Count -gt 0) {
    while ($queue.Count -gt 0 -and $running.Count -lt $Jobs) {
        $j = $queue.Dequeue()
        $log = Join-Path $Base "results/$($j.S.Tag)/logs/$($j.W.Name)"
        $argList = @("`"$($j.W.File)`"", "--tag", $j.S.Tag, "--set", "type=$($j.W.Type)") + $common + $j.S.Extra
        $p = Start-Process -FilePath $exe -WorkingDirectory $Base -ArgumentList $argList -PassThru `
                -RedirectStandardOutput "$log.log" -RedirectStandardError "$log.err"
        $null = $p.Handle   # keeps the exit code readable after the process ends
        $running += [pscustomobject]@{ P = $p; J = $j }
    }
    Start-Sleep -Seconds 5
    foreach ($r in @($running | Where-Object { $_.P.HasExited })) {
        $done++
        $label = "{0} {1}" -f $r.J.W.Name, ($r.J.S.Tag -replace '.*_', '')
        if ($r.P.ExitCode -eq 0) {
            Write-Host "$(Get-Date -Format HH:mm:ss)  [$done/$total] done   $label" -ForegroundColor Green
        } else {
            Write-Host "$(Get-Date -Format HH:mm:ss)  [$done/$total] FAILED $label (see logs)" -ForegroundColor Red
        }
    }
    $running = @($running | Where-Object { -not $_.P.HasExited })
}

foreach ($s in $setups) {
    Add-Content (Join-Path $Base "results/$($s.Tag)/batch.txt") "finished:   $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
}
Write-Host "All done. Zip the folders results\exp4_${stamp}_* and copy them back: analyze_exp4.py --runs <folder>/exp4_${stamp}_*." -ForegroundColor Yellow
