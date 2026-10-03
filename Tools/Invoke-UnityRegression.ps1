[CmdletBinding()]
param(
    # Verifier classes to run (each must expose a static Run()).
    [string[]]$Verifiers = @(
        'GymChaosPoliceVerifier', 'GymChaosProteinStoreVerifier', 'GymChaosOutdoorPerimeterVerifier',
        'GymChaosLockerVisitVerifier', 'GymChaosVisitorVerifier', 'GymChaosEnemyBehaviorVerifier',
        'GymChaosVehicleTrafficVerifier', 'GymChaosVehicleDepartureVerifier', 'GymChaosDavieBusVerifier',
        'GymChaosDoorwayPriorityVerifier', 'GymChaosDeadliftPathVerifier', 'GymChaosMembersCountVerifier',
        'GymChaosVisitorUnstickVerifier', 'GymChaosPlayModeVerifier'),
    [string]$Prefix = 'regression',
    # Parallel Unity processes. Lane 1 is the repository project; lanes 2..N
    # are mirror projects under .lanes/ (Assets and Packages are junctions,
    # ProjectSettings is copied with a distinct productName so each lane has
    # its own PlayerPrefs, Library is per lane).
    [ValidateRange(1, 4)]
    [int]$Lanes = 4,
    # Failed runs are retried this many times, sequentially on the main
    # project (no parallel load). A pass on retry is reported as flaky.
    [ValidateRange(0, 2)]
    [int]$Retries = 1
)

# Prints one "REGRESSION <index> <verifier> exit=<n> seconds=<s> <first error>"
# line per run, then REGRESSION_DONE. Runs are balanced across lanes by the
# durations recorded in Logs/verify/regression-durations.json (longest first).

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$mainProject = Join-Path $repositoryRoot 'GymChaos'
$check = Join-Path $PSScriptRoot 'Invoke-UnityCheck.ps1'
$durationsPath = Join-Path $repositoryRoot 'Logs\verify\regression-durations.json'
New-Item -ItemType Directory -Path (Split-Path $durationsPath) -Force | Out-Null

function Get-LaneProject([int]$lane)
{
    if ($lane -eq 1) { return $mainProject }
    $laneRoot = Join-Path $repositoryRoot ".lanes\lane$lane"
    $project = Join-Path $laneRoot 'GymChaos'
    New-Item -ItemType Directory -Path $project -Force | Out-Null
    foreach ($shared in 'Assets', 'Packages')
    {
        $link = Join-Path $project $shared
        if (-not (Test-Path $link))
        {
            New-Item -ItemType Junction -Path $link -Target (Join-Path $mainProject $shared) | Out-Null
        }
    }
    # Fresh settings each run (they are tiny) so edits in the main project
    # reach the lane; a distinct productName isolates PlayerPrefs.
    $settings = Join-Path $project 'ProjectSettings'
    if (Test-Path $settings) { Remove-Item $settings -Recurse -Force -Confirm:$false }
    Copy-Item (Join-Path $mainProject 'ProjectSettings') $settings -Recurse
    $projectSettingsFile = Join-Path $settings 'ProjectSettings.asset'
    $text = (Get-Content $projectSettingsFile -Raw) -replace '(?m)^(\s*productName:\s*).*$', "`${1}GymChaos-lane$lane"
    # No BOM: Unity parses the YAML header literally.
    [IO.File]::WriteAllText($projectSettingsFile, $text, (New-Object Text.UTF8Encoding($false)))
    $library = Join-Path $project 'Library'
    if (-not (Test-Path $library))
    {
        # One-time warm start; afterwards each lane only reimports changes.
        robocopy (Join-Path $mainProject 'Library') $library /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    }
    return $project
}

$durations = @{}
if (Test-Path $durationsPath)
{
    (Get-Content $durationsPath -Raw | ConvertFrom-Json).PSObject.Properties |
        ForEach-Object { $durations[$_.Name] = [double]$_.Value }
}

# Longest-processing-time-first assignment keeps the lanes balanced.
$runs = for ($i = 0; $i -lt $Verifiers.Count; $i++)
{
    $estimate = if ($durations.ContainsKey($Verifiers[$i])) { $durations[$Verifiers[$i]] } else { 60 }
    [pscustomobject]@{ Index = $i + 1; Verifier = $Verifiers[$i]; Estimate = $estimate }
}
$laneCount = [Math]::Min($Lanes, [Math]::Max(1, $runs.Count))
$laneLoad = @(0) * $laneCount
$laneRuns = @{}
for ($lane = 1; $lane -le $laneCount; $lane++) { $laneRuns[$lane] = New-Object System.Collections.ArrayList }
foreach ($run in ($runs | Sort-Object Estimate -Descending))
{
    $target = 0
    for ($lane = 1; $lane -lt $laneCount; $lane++) { if ($laneLoad[$lane] -lt $laneLoad[$target]) { $target = $lane } }
    $laneLoad[$target] += $run.Estimate
    [void]$laneRuns[$target + 1].Add($run)
}

# The main project must be compiled first so new .meta files exist before
# lanes open the shared Assets junction.
& $check -Name "$Prefix-compile" | Out-Null

$prefsRoot = 'HKCU\Software\Unity\UnityEditor\DefaultCompany'
$started = Get-Date
$jobs = foreach ($lane in $laneRuns.Keys)
{
    if ($laneRuns[$lane].Count -eq 0) { continue }
    $project = Get-LaneProject $lane
    $product = if ($lane -eq 1) { 'GymChaos' } else { "GymChaos-lane$lane" }
    Start-Job -ArgumentList $check, $project, $lane, @($laneRuns[$lane]), $Prefix, "$prefsRoot\$product" -ScriptBlock {
        param($check, $project, $lane, $runs, $prefix, $prefsKey)
        $backup = Join-Path $env:TEMP "gymchaos-prefs-lane$lane.reg"
        reg export $prefsKey $backup /y 2>$null | Out-Null
        foreach ($run in $runs)
        {
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $output = & $check -Method "$($run.Verifier).Run" -Name "$prefix-$($run.Index)-$($run.Verifier)" -ProjectPath $project
            $exit = $LASTEXITCODE
            $watch.Stop()
            if (Test-Path $backup) { reg import $backup 2>$null | Out-Null }
            # UnityEditor.Search indexing in a fresh lane logs a harmless
            # ArgumentOutOfRangeException; never report it as the failure.
            $firstError = $output | Where-Object { $_ -match 'Exception:' -and $_ -notmatch '^ArgumentOutOfRangeException: Index was out of range' } | Select-Object -First 1
            if (-not $firstError) { $firstError = $output | Where-Object { $_ -match '_FAIL' } | Select-Object -First 1 }
            if (-not $firstError -and $exit -ne 0)
            {
                # No verifier error: name the infrastructure cause (e.g. the
                # ILPP pipe race when several Unity processes compile at once).
                $logPath = Join-Path (Split-Path $check -Parent | Split-Path -Parent) "Logs\verify\$prefix-$($run.Index)-$($run.Verifier).log"
                $firstError = if (Test-Path $logPath) {
                    Select-String -Path $logPath -Pattern 'ILPPTrigger: .*|Scripts have compiler errors|error CS\d+.*' |
                        Select-Object -First 1 | ForEach-Object { 'INFRA ' + $_.Matches[0].Value }
                } else { $null }
            }
            [pscustomobject]@{
                Index = $run.Index; Verifier = $run.Verifier; Exit = $exit; Lane = $lane
                Seconds = [int]$watch.Elapsed.TotalSeconds
                # Passing runs print no detail: shorter output per regression.
                Detail = if ($exit -ne 0 -and $firstError) { $firstError.Substring(0, [Math]::Min(260, $firstError.Length)) } else { '' }
            }
        }
    }
}

$results = $jobs | Wait-Job | Receive-Job
$jobs | Remove-Job
$failed = 0
$flaky = 0
foreach ($result in ($results | Sort-Object Index))
{
    $durations[$result.Verifier] = $result.Seconds
    $status = "exit=$($result.Exit)"
    for ($attempt = 1; $result.Exit -ne 0 -and $attempt -le $Retries; $attempt++)
    {
        $output = & $check -Method "$($result.Verifier).Run" -Name "$Prefix-$($result.Index)-$($result.Verifier)-retry$attempt"
        if ($LASTEXITCODE -eq 0)
        {
            $status = "exit=$($result.Exit) retry$attempt=0 FLAKY"
            $flaky++
            $result.Exit = 0
        }
        else
        {
            $status += " retry$attempt=$LASTEXITCODE"
        }
    }
    if ($result.Exit -ne 0) { $failed++ }
    "REGRESSION $($result.Index) $($result.Verifier) $status seconds=$($result.Seconds) lane=$($result.Lane) $($result.Detail)"
}
$durations | ConvertTo-Json | Set-Content $durationsPath -Encoding utf8
"REGRESSION_DONE failed=$failed flaky=$flaky total=$($Verifiers.Count) lanes=$laneCount wallSeconds=$([int]((Get-Date) - $started).TotalSeconds)"
exit $failed
