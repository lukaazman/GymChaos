[CmdletBinding()]
param(
    # Verifier classes (each needs a public static Run()). Default: every GymChaos*Verifier except tools that need a display or a build.
    [string[]]$Verifiers = @(),
    # Mirror projects to run on in parallel (the main project is locked by an open Editor).
    [int[]]$Lanes = @(2, 3, 4),
    [string]$Prefix = 'all',
    # Verifiers that need -Graphics (a real GPU window).
    [string[]]$GraphicsVerifiers = @('GymChaosProgressionVerifier', 'GymChaosSquatFootPlantVerifier', 'GymChaosPerformanceVerifier',
        'GymChaosArtFidelityVerifier', 'GymChaosDeadliftMemberVerifier', 'GymChaosLightingVerifier', 'GymChaosSettingsMatrixVerifier',
        'GymChaosSquatBarGripVerifier', 'GymChaosCityDystopiaVerifier')
)

# Runs every verifier once, spread over the lanes, and prints one line per run:
#   ALL <verifier> exit=<n> seconds=<s> lane=<l> <first failure marker>
# then ALL_DONE failed=<n>. Logs: Logs\verify\<Prefix>-<verifier>.log. No retries: a failure is a failure.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$check = Join-Path $PSScriptRoot 'Invoke-UnityCheck.ps1'
$editor = Join-Path $root 'GymChaos\Assets\Editor'
$tools = @('GymChaosFaceCensorCapture', 'GymChaosLayoutDump', 'GymChaosLocalWebGLBuild', 'GymChaosPoliceGunPoseCapture',
    'GymChaosRenderAudit', 'GymChaosShowcaseShots', 'GymChaosStartMenuCapture', 'GymChaosVisitorRouteProbe',
    'GymChaosRaceCaptureVerifier', 'PlayerPresentationCheck', 'GymChaosVerifierHooks')
if ($Verifiers.Count -eq 0)
{
    $Verifiers = Get-ChildItem $editor -Filter 'Gym*.cs' |
        Where-Object { (Select-String -Path $_.FullName -Pattern 'public static void Run\(\)' -Quiet) } |
        ForEach-Object { $_.BaseName } | Where-Object { $tools -notcontains $_ } | Sort-Object
}

$jobs = @()
for ($i = 0; $i -lt $Lanes.Count; $i++)
{
    $mine = @(); for ($k = $i; $k -lt $Verifiers.Count; $k += $Lanes.Count) { $mine += $Verifiers[$k] }
    $jobs += Start-Job -ArgumentList $root, $check, $Lanes[$i], $mine, $Prefix, $GraphicsVerifiers -ScriptBlock {
        param($root, $check, $lane, $list, $prefix, $graphics)
        Set-Location $root
        foreach ($v in $list)
        {
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $args = @{ Method = "$v.Run"; ProjectPath = "$root\.lanes\lane$lane\GymChaos"; Name = "$prefix-$v" }
            if ($graphics -contains $v) { $args.Graphics = $true }
            $out = & $check @args 2>&1 | Out-String
            $exit = if ($out -match 'exit=(\d+)') { [int]$Matches[1] } else { 99 }
            $fail = ($out -split "`n" | Where-Object { $_ -match 'FAIL|error CS|blocked' } | Select-Object -First 1)
            "ALL $v exit=$exit seconds=$([int]$watch.Elapsed.TotalSeconds) lane=$lane $fail"
        }
    }
}
$jobs | Wait-Job | Out-Null
$lines = $jobs | Receive-Job
$lines | Sort-Object
'ALL_DONE failed=' + @($lines | Where-Object { $_ -notmatch 'exit=0 ' }).Count
