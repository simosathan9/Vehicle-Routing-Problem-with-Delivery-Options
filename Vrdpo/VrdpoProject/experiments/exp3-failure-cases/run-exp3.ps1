# Experiment 3B of the review (referee 3, comment 3): the outlier instances solved again, with the trace on.
#
# Runs every instance of outliers.txt once, as a VRPDO with the default settings and the per-iteration trace
# (--set traceIterations=true) -> results\exp3_<date>_outliers\
# The results folder holds runs.csv, settings.json, one solution report per instance, traces\<instance>.csv
# (one row per iteration) and logs\. A new run never overwrites an old one: the date and time are in the tag.
# Nothing is analysed on the VM: zip the results folder and run case_study.py on it locally (see README.md).
#
# Usage (PowerShell, in the folder with VrdpoProject.exe, settings.json and this script):
#   powershell -ExecutionPolicy Bypass -File .\run-exp3.ps1
#   powershell -ExecutionPolicy Bypass -File .\run-exp3.ps1 -Jobs 6
#   powershell -ExecutionPolicy Bypass -File .\run-exp3.ps1 -CheckOnly      # only check that every instance is found
# The 400-customer instances take about 3 hours each at 15 x 15000 (the paper's budget); -Restarts / -Repetitions
# shorten a trial run.
param(
    [string]$Base = $PSScriptRoot,                 # folder with VrdpoProject.exe and settings.json
    [string]$Subset = (Join-Path $PSScriptRoot "outliers.txt"),
    [int]$Jobs = [Environment]::ProcessorCount,    # solves running at the same time
    [int]$Restarts = 0,                            # 0 = value of settings.json (15)
    [int]$Repetitions = 0,                         # 0 = value of settings.json (15000)
    [switch]$CheckOnly
)
$ErrorActionPreference = "Stop"

$exe = Join-Path $Base "VrdpoProject.exe"
if (-not (Test-Path $exe)) { throw "VrdpoProject.exe not found in $Base" }
if (-not (Test-Path (Join-Path $Base "settings.json"))) { throw "settings.json not found in $Base (copy it from the publish folder)" }
if (-not (Test-Path $Subset)) { throw "subset file not found: $Subset" }

# Instance names, one per line (e.g. U_400_4); '#' starts a comment.
$names = Get-Content $Subset | ForEach-Object { ($_ -replace '#.*', '').Trim() } | Where-Object { $_ }
if (-not $names) { throw "$Subset lists no instances" }

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
Write-Host "$($work.Count) instances found (subset: $Subset):" -ForegroundColor Green
$work | ForEach-Object { Write-Host ("  {0,-16} type={1,-6} {2}" -f $_.Name, $_.Type, $_.File) }
if ($CheckOnly) { return }

$stamp = Get-Date -Format "yyyy-MM-dd_HHmm"
$tag = "exp3_${stamp}_outliers"
$common = @("--set", "verbal=false", "--set", "traceIterations=true")
if ($Restarts -gt 0) { $common += @("--set", "restarts=$Restarts") }
if ($Repetitions -gt 0) { $common += @("--set", "repetitions=$Repetitions") }

# What ran, for the record.
$dir = Join-Path $Base "results/$tag"
New-Item -ItemType Directory -Force -Path (Join-Path $dir "logs") | Out-Null
@(
    "experiment: 3B (failure cases: outlier instances), $tag",
    "started:    $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "host:       $env:COMPUTERNAME",
    "exe:        $exe ($((Get-Item $exe).LastWriteTime.ToString('yyyy-MM-dd HH:mm')), product version $((Get-Item (Join-Path $Base 'VrdpoProject.dll')).VersionInfo.ProductVersion))",
    "jobs:       $Jobs",
    "options:    $($common -join ' ')",
    "instances:  $($work.Count)"
) + ($work | ForEach-Object { "  $($_.Name) type=$($_.Type)" }) | Set-Content (Join-Path $dir "batch.txt")

# Largest instances first: they take the longest.
$queue = New-Object System.Collections.Queue
foreach ($w in ($work | Sort-Object { [int]($_.Name -split "_")[1] } -Descending)) { $queue.Enqueue($w) }
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

Write-Host "All done. Zip the folder results\$tag and copy it back for the case studies (case_study.py)." -ForegroundColor Yellow
