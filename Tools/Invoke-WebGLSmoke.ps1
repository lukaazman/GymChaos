[CmdletBinding()]
param(
    # Folder with index.html from BuildScript.BuildWebGL (UNITY_BUILD_PATH).
    # Default: Logs\agent\webgl.
    [string]$BuildPath,
    # Build first through GymChaosLocalWebGLBuild.Run in the .lanes\lane5
    # mirror project, which stays on the WebGL target (warm IL2CPP cache, and
    # the main project never switches target). Uncompressed unless -Release.
    [switch]$Build,
    # Brotli like CI (adds about 8 minutes); use for the final size check only.
    [switch]$Release,
    # Gameplay benchmark (?bench=1, GymWebGLBench): skips the start screen and
    # measures the GymChaosPerformanceVerifier views; prints WEBGL_BENCH lines.
    [switch]$Bench,
    [int]$Port = 8765,
    # GYMCHAOS_WEBGL_FPS windows to collect after boot (5 s each).
    [ValidateRange(1, 20)]
    [int]$FpsWindows = 3,
    [int]$TimeoutSeconds = 240,
    # Headless Chromium renders WebGL in software; use -Headed for real FPS.
    [switch]$Headed,
    # Headless on the real GPU (ANGLE D3D11), for when the headed browser cannot start.
    [switch]$GpuHeadless,
    # Saves the collected browser console text here (for later checks).
    [string]$ConsoleOut
)

# Serves the WebGL build, opens it with playwright-cli (?fps=1 enables the
# GymWebGLFpsMarker), waits for a boot marker and FPS windows in the browser
# console, then prints WEBGL_SMOKE_OK with FPS and build size, or
# WEBGL_SMOKE_FAILED with the reason. FPS windows that overlap loading (up to
# the last runtime GLB) are skipped. FPS is measured on the start screen (the
# scene renders behind the menu); gameplay FPS needs a manual session.

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
# $PSScriptRoot is empty in a param default under Windows PowerShell 5.1 -File.
if (-not $BuildPath) { $BuildPath = Join-Path $repositoryRoot 'Logs\agent\webgl' }

if ($Build)
{
    $mainProject = Join-Path $repositoryRoot 'GymChaos'
    $laneProject = Join-Path $repositoryRoot '.lanes\lane5\GymChaos'
    New-Item -ItemType Directory -Path $laneProject -Force | Out-Null
    foreach ($shared in 'Assets', 'Packages')
    {
        $link = Join-Path $laneProject $shared
        if (-not (Test-Path $link)) { New-Item -ItemType Junction -Path $link -Target (Join-Path $mainProject $shared) | Out-Null }
    }
    # Fresh settings so quality and player edits in the main project apply.
    $settings = Join-Path $laneProject 'ProjectSettings'
    if (Test-Path $settings) { Remove-Item $settings -Recurse -Force -Confirm:$false }
    Copy-Item (Join-Path $mainProject 'ProjectSettings') $settings -Recurse
    # Old files of the other compression mode would stay next to the new ones.
    Remove-Item (Join-Path $BuildPath 'Build') -Recurse -Force -ErrorAction SilentlyContinue
    $env:UNITY_BUILD_PATH = $BuildPath
    $env:GYMCHAOS_WEBGL_COMPRESSION = if ($Release) { 'brotli' } else { 'none' }
    try
    {
        & (Join-Path $PSScriptRoot 'Invoke-UnityCheck.ps1') -Method GymChaosLocalWebGLBuild.Run -Name webgl-build `
            -ProjectPath $laneProject | Out-Host
        $buildExit = $LASTEXITCODE
    }
    finally { Remove-Item Env:UNITY_BUILD_PATH, Env:GYMCHAOS_WEBGL_COMPRESSION -ErrorAction SilentlyContinue }
    if ($buildExit -ne 0) { 'WEBGL_SMOKE_FAILED reason=build'; exit 1 }
}
if (-not (Test-Path (Join-Path $BuildPath 'index.html')))
{
    "WEBGL_SMOKE_FAILED reason=no_build path=$BuildPath (run with -Build)"
    exit 1
}
if (-not (Get-Command playwright-cli -ErrorAction SilentlyContinue))
{
    'WEBGL_SMOKE_FAILED reason=playwright-cli_missing (npm install -g @playwright/cli)'
    exit 1
}

$buildBytes = (Get-ChildItem $BuildPath -Recurse -File | Measure-Object Length -Sum).Sum
$session = "gymchaos-webgl-$Port"
$server = Start-Process python -ArgumentList '-m', 'http.server', $Port, '--bind', '127.0.0.1', '--directory', "`"$BuildPath`"" `
    -WindowStyle Hidden -PassThru
$failure = $null
$fps = @()
$console = ''
# playwright-cli may write to stderr; under 'Stop' Windows PowerShell would
# turn that into a terminating error before the failure marker prints.
$ErrorActionPreference = 'Continue'
# playwright-cli writes snapshots into the working directory and defaults to
# the Chrome channel; run from TEMP with Playwright's bundled Chromium
# (playwright-cli install-browser chromium).
$config = Join-Path $env:TEMP 'gymchaos-webgl-smoke.json'
$launch = if ($GpuHeadless) { ',"launchOptions":{"args":["--use-angle=d3d11","--enable-gpu","--ignore-gpu-blocklist"]}' } else { '' }
'{"browser":{"browserName":"chromium"' + $launch + '}}' | Set-Content $config -Encoding ascii
Push-Location $env:TEMP
$started = Get-Date
try
{
    Start-Sleep -Seconds 1
    $openArgs = @("-s=$session", 'open', "--config=$config", "http://127.0.0.1:$Port/index.html?fps=1$(if ($Bench) { '&bench=1' })")
    if ($Headed) { $openArgs += '--headed' }
    playwright-cli @openArgs | Out-Null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $booted = $false
    while ((Get-Date) -lt $deadline)
    {
        Start-Sleep -Seconds 5
        # The command flushes new entries to .playwright-cli\console-*.log
        # and prints only a link to that file.
        playwright-cli "-s=$session" console 2>&1 | Out-Null
        $log = Get-ChildItem (Join-Path $env:TEMP '.playwright-cli\console-*.log') -ErrorAction SilentlyContinue |
            Where-Object LastWriteTime -ge $started | Sort-Object LastWriteTime | Select-Object -Last 1
        $console = if ($log) { Get-Content $log.FullName -Raw } else { '' }
        $booted = $console -match 'GYMCHAOS_EXTERNAL_RIG_OK'
        $fps = @([regex]::Matches($console, '\[\s*(\d+)ms\][^\n]*GYMCHAOS_WEBGL_FPS avg=([\d.]+) avgMs=([\d.]+) p95Ms=([\d.]+)') |
            ForEach-Object { [pscustomobject]@{ AtMs = [double]$_.Groups[1].Value; Avg = [double]$_.Groups[2].Value; P95Ms = [double]$_.Groups[4].Value } })
        # Steady windows start after the last runtime GLB finished loading.
        $loadEndMs = ([regex]::Matches($console, '\[\s*(\d+)ms\][^\n]*GYMCHAOS_RUNTIME_GLB\w*_READY') |
            ForEach-Object { [double]$_.Groups[1].Value } | Measure-Object -Maximum).Maximum
        $steady = @($fps | Where-Object { $_.AtMs - 5000 -ge $loadEndMs })
        if ($Bench) { if ($console -match 'GYMCHAOS_WEBGL_BENCH_DONE') { break } }
        elseif ($booted -and $steady.Count -ge $FpsWindows) { break }
        if ($console -match 'RuntimeError|Uncaught|Aborted\(') { $failure = 'runtime_error'; break }
    }
    if (-not $failure -and -not $booted) { $failure = 'no_boot_marker' }
    elseif (-not $failure -and $Bench -and $console -notmatch 'GYMCHAOS_WEBGL_BENCH_DONE') { $failure = 'bench_unfinished' }
    elseif (-not $failure -and -not $Bench -and $steady.Count -lt $FpsWindows) { $failure = "steady_fps_windows=$($steady.Count)" }
    if ($failure)
    {
        if ($ConsoleOut) { $console | Set-Content $ConsoleOut -Encoding utf8 }
        $tail = ($console -split "`n" | Select-Object -Last 8) -join ' | '
        "WEBGL_SMOKE_FAILED reason=$failure console_tail=$tail"
        exit 1
    }
}
finally
{
    playwright-cli "-s=$session" close 2>$null | Out-Null
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    Pop-Location
}

if ($ConsoleOut) { $console | Set-Content $ConsoleOut -Encoding utf8 }
$avg = ($steady | Measure-Object Avg -Average).Average
$p95 = ($steady | Measure-Object P95Ms -Maximum).Maximum
'WEBGL_SMOKE_OK fpsAvg={0:F1} worstP95Ms={1:F1} windows={2} buildMB={3:F1} headed={4} gpuHeadless={5} loadedAtS={6:F1} loadingWorstP95Ms={7:F1}' -f `
    $avg, $p95, $steady.Count, ($buildBytes / 1MB), [bool]$Headed, [bool]$GpuHeadless, ($loadEndMs / 1000),
    ($fps | Where-Object { $_.AtMs - 5000 -lt $loadEndMs } | Measure-Object P95Ms -Maximum).Maximum
if ($Bench)
{
    $benchRows = @([regex]::Matches($console, 'GYMCHAOS_WEBGL_BENCH scenario=(\w+) fps=([\d.]+) avgMs=([\d.]+) p95Ms=([\d.]+)') |
        ForEach-Object { [pscustomobject]@{ Name = $_.Groups[1].Value; Fps = [double]$_.Groups[2].Value; P95Ms = [double]$_.Groups[4].Value } })
    foreach ($b in $benchRows) { 'WEBGL_BENCH scenario={0} fps={1:F1} p95Ms={2:F1}' -f $b.Name, $b.Fps, $b.P95Ms }
    'WEBGL_BENCH_DONE scenarios={0} worstFps={1:F1} worstP95Ms={2:F1}' -f $benchRows.Count,
        ($benchRows | Measure-Object Fps -Minimum).Minimum, ($benchRows | Measure-Object P95Ms -Maximum).Maximum
}
