[CmdletBinding()]
param(
    # Editor method to run, e.g. GymChaosPoliceVerifier.Run. Omit for import/compile only.
    [string]$Method = '',
    # Log name without extension; written to Logs/agent/<Name>.log (ignored by git).
    [string]$Name = '',
    [string]$UnityExecutable = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.com',
    [int]$Tail = 8,
    # Re-summarize an existing log without running Unity.
    [switch]$SummaryOnly,
    # Keep the GPU device (drops -nographics) for render captures.
    [switch]$Graphics,
    # Unity project to open; defaults to the repository project. Regression
    # lanes pass their mirror project here.
    [string]$ProjectPath = ''
)

# Runs Unity in batch mode and prints only a compact summary (exit code, compiler
# errors, GYMCHAOS_* markers, exceptions) so agents do not read multi-megabyte logs.

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
# PowerShell names are case-insensitive: $ProjectPath is the parameter itself.
$isMainProject = [string]::IsNullOrWhiteSpace($ProjectPath)
if ($isMainProject) { $ProjectPath = Join-Path $repositoryRoot 'GymChaos' }
$projectPath = $ProjectPath
$logDirectory = Join-Path $repositoryRoot 'Logs\agent'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($Name))
{
    $Name = if ($Method) { ($Method -replace '[^A-Za-z0-9]+', '-').Trim('-') } else { 'compile' }
}
$logFile = Join-Path $logDirectory "$Name.log"

# An open Editor locks the project. Compile checks then go through the live Editor;
# verifiers call EditorApplication.Exit, so they must not run inside the user's Editor.
$liveEditor = Get-Process Unity -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowTitle -like 'GymChaos - *' } | Select-Object -First 1
if (-not $liveEditor -and -not $SummaryOnly)
{
    # The window title is not always readable (it was empty from this shell),
    # so also treat a held project lock file as an open Editor.
    $lockFile = Join-Path $projectPath 'Temp\UnityLockfile'
    if (Test-Path $lockFile)
    {
        try { [IO.File]::Open($lockFile, 'Open', 'ReadWrite', 'None').Close() }
        catch
        {
            $liveEditor = Get-Process Unity -ErrorAction SilentlyContinue |
                Where-Object { $_.Path -like '*\Editor\Unity.exe' } | Select-Object -First 1
        }
    }
}
if ($liveEditor -and -not $SummaryOnly -and $isMainProject)
{
    if ($Method)
    {
        "UNITY_CHECK method=$Method exit=blocked reason=editor-open pid=$($liveEditor.Id)"
        'Close the GymChaos Editor to run batch verifiers.'
        exit 3
    }
    $cli = "unity"
    & $cli command --project-path $projectPath --no-banner eval 'UnityEditor.AssetDatabase.Refresh();' | Out-Null
    & $cli command --project-path $projectPath --no-banner recompile | Out-Null
    $status = ''
    for ($i = 0; $i -lt 90; $i++)
    {
        Start-Sleep -Seconds 2
        $status = (& $cli command --project-path $projectPath --no-banner --json console_status) -join ''
        if ($status -match '"compiling":false' -and $status -notmatch '"domainReloadInProgress":true') { break }
    }
    $failed = $status -match '"compilationFailed":true'
    $errors = (& $cli command --project-path $projectPath --no-banner --json console --level error --tail 20) -join ''
    $messages = [regex]::Matches($errors, '"message":"((?:[^"\\]|\\.)*)"') | ForEach-Object { $_.Groups[1].Value } |
        Where-Object { $_ -match 'error CS\d+' } | Select-Object -Unique
    "UNITY_CHECK method=compile-live exit=$(if ($failed) { 1 } else { 0 }) editorPid=$($liveEditor.Id)"
    "COMPILER_ERRORS $(@($messages).Count)"
    $messages | Select-Object -First 20
    exit $(if ($failed) { 1 } else { 0 })
}

$arguments = @('-batchmode', '-projectPath', $projectPath, '-logFile', $logFile)
if (-not $Graphics) { $arguments = @('-nographics') + $arguments }
if ($Method) { $arguments += @('-executeMethod', $Method) } else { $arguments += '-quit' }

# Agent runs must stay silent: GymChaosBatchAudioMute mutes the editor's
# audio output in the child Unity process (lanes inherit it through this script).
$env:GYMCHAOS_MUTE_AUDIO = '1'

$watch = [Diagnostics.Stopwatch]::StartNew()
$exitCode = 0
if (-not $SummaryOnly)
{
    & $UnityExecutable @arguments | Out-Null
    $exitCode = $LASTEXITCODE
}
$watch.Stop()

$lines = if (Test-Path -LiteralPath $logFile) { Get-Content -LiteralPath $logFile } else { @() }
$compilerErrors = $lines | Where-Object { $_ -match 'error CS\d+|Compilation failed|CompilerError' } | Select-Object -Unique
$markers = $lines | Where-Object { $_ -match 'GYMCHAOS_[A-Z0-9_]+' } | ForEach-Object { $_.Trim() } | Select-Object -Unique
$exceptions = $lines | Where-Object { $_ -match '^\w*(Exception|Error):|NullReferenceException|MissingReferenceException' } |
    Where-Object { $_ -notmatch 'RenderTexture.Create failed|access token' } | Select-Object -Unique -First $Tail

"UNITY_CHECK method=$(if ($Method) { $Method } else { 'compile' }) exit=$(if ($SummaryOnly) { 'not-run' } else { $exitCode }) seconds=$([int]$watch.Elapsed.TotalSeconds) log=$logFile"
"COMPILER_ERRORS $(@($compilerErrors).Count)"
$compilerErrors | Select-Object -First 20
# Scene-boot contract markers every gameplay verifier emits. They are
# collapsed into one count so each run costs a few lines, not ~40 names.
$bootMarkers = @(
    'PLAYER_FIT', 'DOOR', 'MIRRORS', 'MECCA_POSTERS', 'BACK_ROOM', 'LIGHTING', 'TIME', 'SURFACE_MATERIALS',
    'BUS_STOP_SECTION', 'DAVIE_PEDESTRIAN_GATE', 'OUTDOOR_SURFACE', 'OUTDOOR_CONTRACT', 'FENCE_JOINS',
    'OUTDOOR_LAYOUT', 'OUTDOOR', 'DEADLIFT_STATION', 'RECEPTION_SCREEN', 'DIRECT_AUTHORED_ANIMATION',
    'EXTERNAL_RIG', 'LOCKER_ROUTE', 'DAVIE_BUS', 'CITY_DYSTOPIA_CONTRACT', 'LOOSE_ITEMS', 'PROTEIN_STORE_CONTRACT'
) | ForEach-Object { "GYMCHAOS_$($_)_OK" }
# Warning-level AI diagnostics: counted, never listed as failures.
$diagnosticPattern = 'GYMCHAOS_ROAM_ROUTE_FAILED'
$failMarkers = $markers | Where-Object { $_ -match 'GYMCHAOS_[A-Z0-9_]*(FAIL|FAILED|ERROR)\b' -and $_ -notmatch $diagnosticPattern }
$diagnostics = @($markers | Where-Object { $_ -match $diagnosticPattern })
$markerNames = $markers | ForEach-Object { [regex]::Match($_, 'GYMCHAOS_[A-Z0-9_]+').Value } | Select-Object -Unique
$okNames = @($markerNames | Where-Object { $_ -match '_OK$' })
$bootSeen = @($okNames | Where-Object { $bootMarkers -contains $_ })
"MARKERS $(@($markers).Count) unique=$(@($markerNames).Count)"
if ($bootSeen.Count -gt 0)
{
    $bootMissing = @($bootMarkers | Where-Object { $okNames -notcontains $_ })
    "BOOT_OK $($bootSeen.Count)/$($bootMarkers.Count)$(if ($bootMissing.Count) { ' missing=' + ($bootMissing -join ',') })"
}
"OK_MARKERS $(($okNames | Where-Object { $bootMarkers -notcontains $_ }) -join ' ')"
if ($diagnostics.Count -gt 0) { "DIAG_MARKERS $($diagnostics.Count) ($diagnosticPattern)" }
if (@($failMarkers).Count -gt 0)
{
    "FAIL_MARKERS $(@($failMarkers).Count)"
    $failMarkers | Select-Object -First 20 | ForEach-Object { if ($_.Length -gt 400) { $_.Substring(0, 400) + '...' } else { $_ } }
}
if (@($exceptions).Count -gt 0)
{
    "EXCEPTIONS (first $Tail)"
    $exceptions
}
if ($exitCode -ne 0 -and @($markers).Count -eq 0 -and @($compilerErrors).Count -eq 0)
{
    "LOG_TAIL"
    $lines | Select-Object -Last $Tail
}
exit $exitCode
