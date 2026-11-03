# Experiment 5 of the review (referee comment: diminishing returns of the multi-restart scheme): the whole benchmark
# solved with the per-restart log on (--set logRestarts=true), so that the incumbent after every restart and the
# measured time of every restart are known. Nothing else changes: default settings, 15 x 15000.
#
# Two modes:
#   -All      every instance file found in the run folder (VRPDO_instances\VRPDO_instances\<set>\<size>\ and
#             Instances\<set>\<size>\), once -> results\exp5_<date>_all\
#   default   only the instances of a subset file (one name per line) -> results\exp5_<date>_subset\
# The results folder holds runs.csv, settings.json, one solution report per instance, restarts\<instance>.csv (one row
# per restart: restart best, time, incumbent) and logs\. -Trace adds traces\<instance>.csv (one row per iteration,
# about 8 MB per instance; not needed for experiment 5). A new run never overwrites an old one.
# Nothing is analysed on the VM: zip the results folder and run analyze_exp5.py --run on it locally (see README.md).
#
# Usage (PowerShell, in the folder with VrdpoProject.exe, settings.json and this script):
#   powershell -ExecutionPolicy Bypass -File .\run-exp5.ps1 -All -CheckOnly   # list the instance files found
#   powershell -ExecutionPolicy Bypass -File .\run-exp5.ps1 -All              # the full benchmark (240 instances)
#   powershell -ExecutionPolicy Bypass -File .\run-exp5.ps1 -Subset my.txt    # a subset
# Note: analyze_exp5.py also works on a run made with the trace on (e.g. results\exp3_*_all), with the restart times
# estimated as r/15 of the total time; this script's run gives the measured times.
param(
    [string]$Base = $PSScriptRoot,                 # folder with VrdpoProject.exe and settings.json
    [string]$Subset = "",                          # instance names, one per line (default mode)
    [int]$Jobs = [Environment]::ProcessorCount,    # solves running at the same time
    [int]$Restarts = 0,                            # 0 = value of settings.json (15)
    [int]$Repetitions = 0,                         # 0 = value of settings.json (15000)
    [switch]$All,                                  # every instance file found instead of the subset
    [switch]$Trace,                                # also write the per-iteration trace (about 8 MB per instance)
    [switch]$CheckOnly
)
$ErrorActionPreference = "Stop"

$exe = Join-Path $Base "VrdpoProject.exe"
if (-not (Test-Path $exe)) { throw "VrdpoProject.exe not found in $Base" }
if (-not (Test-Path (Join-Path $Base "settings.json"))) { throw "settings.json not found in $Base (copy it from the publish folder)" }
if (-not $All -and (-not $Subset -or -not (Test-Path $Subset))) { throw "give -All or -Subset <file>: subset file not found: $Subset" }

# The instance folders searched, in order of preference (the VM's copy of the benchmark, then the repository's).
$roots = @((Join-Path $Base "VRPDO_instances/VRPDO_instances"), (Join-Path $Base "Instances"))
# Instance names look like U_400_4 or V_25small_7.
$namePattern = '^(U|V|UBC)_\d+(small|medium|large)?_\d+$'

if ($All) {
    # Every instance file under the roots, one per name (the first root wins).
    $seen = @{}
    $names = @()
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        foreach ($f in (Get-ChildItem -Path $root -Recurse -Filter *.txt | Sort-Object FullName)) {
            if ($f.BaseName -match $namePattern -and -not $seen.ContainsKey($f.BaseName)) {
                $seen[$f.BaseName] = $true
                $names += $f.BaseName
            }
        }
    }
    if (-not $names) { throw "no instance files found under $($roots -join ', ')" }
    $Subset = "all instance files"
} else {
    # Instance names, one per line (e.g. U_400_4); '#' starts a comment.
    $names = Get-Content $Subset | ForEach-Object { ($_ -replace '#.*', '').Trim() } | Where-Object { $_ }
    if (-not $names) { throw "$Subset lists no instances" }
}

# Finds an instance file in the usual places; Tilk et al. instances (25/50 small/medium/large) use type=int.
function Resolve-Instance([string]$name) {
    $parts = $name -split "_"
    $kind = $parts[0]; $size = $parts[1]
    $candidates = @((Join-Path $Base "$name.txt")) + ($roots | ForEach-Object { Join-Path $_ "$kind/$size/$name.txt" })
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    # Anywhere under the roots (e.g. Tilk et al. files in Instances\U\200\).
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $hit = Get-ChildItem -Path $root -Recurse -Filter "$name.txt" | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
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
Write-Host "$($work.Count) instances found (subset: $Subset):" -ForegroundColor Green
$work | ForEach-Object { Write-Host ("  {0,-16} type={1,-6} {2}" -f $_.Name, $_.Type, $_.File) }
if ($CheckOnly) { return }

$stamp = Get-Date -Format "yyyy-MM-dd_HHmm"
$tag = if ($All) { "exp5_${stamp}_all" } else { "exp5_${stamp}_subset" }
$common = @("--set", "verbal=false", "--set", "logRestarts=true", "--set", "traceIterations=$(if ($Trace) { 'true' } else { 'false' })", "--set", "countCoupling=false", "--set", "countMoves=false")
if ($Restarts -gt 0) { $common += @("--set", "restarts=$Restarts") }
if ($Repetitions -gt 0) { $common += @("--set", "repetitions=$Repetitions") }

# What ran, for the record.
$dir = Join-Path $Base "results/$tag"
New-Item -ItemType Directory -Force -Path (Join-Path $dir "logs") | Out-Null
@(
    "experiment: 5 (restart returns: $(if ($All) { 'the full benchmark' } else { 'a subset' })), $tag",
    "started:    $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "host:       $env:COMPUTERNAME",
    "exe:        $exe ($((Get-Item $exe).LastWriteTime.ToString('yyyy-MM-dd HH:mm')), product version $((Get-Item (Join-Path $Base 'VrdpoProject.dll')).VersionInfo.ProductVersion))",
    "jobs:       $Jobs",
    "options:    $($common -join ' ')",
    "subset:     $Subset",
    "instances:  $($work.Count)"
) + ($work | ForEach-Object { "  $($_.Name) type=$($_.Type)" }) | Set-Content (Join-Path $dir "batch.txt")

# Largest instances first: they take the longest.
$queue = New-Object System.Collections.Queue
foreach ($w in ($work | Sort-Object { [int](($_.Name -split "_")[1] -replace '\D', '') } -Descending)) { $queue.Enqueue($w) }
$total = $queue.Count
$running = @()
$done = 0
Write-Host "$(Get-Date -Format HH:mm:ss)  $total solves, $Jobs at a time. Results: $Base\results\$tag" -ForegroundColor Cyan

while ($queue.Count -gt 0 -or $running.Count -gt 0) {
    while ($queue.Count -gt 0 -and $running.Count -lt $Jobs) {
        $w = $queue.Dequeue()
        $log = Join-Path $Base "results/$tag/logs/$($w.Name)"
        $argList = @("`"$($w.File)`"", "--tag", $tag, "--set", "type=$($w.Type)") + $common
        $p = Start-Process -FilePath $exe -WorkingDirectory $Base -ArgumentList $argList -PassThru `
                -RedirectStandardOutput "$log.log" -RedirectStandardError "$log.err"
        $null = $p.Handle   # keeps the exit code readable after the process ends
        $running += [pscustomobject]@{ P = $p; W = $w }
    }
    Start-Sleep -Seconds 5
    foreach ($r in @($running | Where-Object { $_.P.HasExited })) {
        $done++
        if ($r.P.ExitCode -eq 0) {
            Write-Host "$(Get-Date -Format HH:mm:ss)  [$done/$total] done   $($r.W.Name)" -ForegroundColor Green
        } else {
            Write-Host "$(Get-Date -Format HH:mm:ss)  [$done/$total] FAILED $($r.W.Name) (see logs)" -ForegroundColor Red
        }
    }
    $running = @($running | Where-Object { -not $_.P.HasExited })
}

Add-Content (Join-Path $dir "batch.txt") "finished:   $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

Write-Host "All done. Zip the folder results\$tag and copy it back: analyze_exp5.py --run <folder>." -ForegroundColor Yellow
