[CmdletBinding()]
param(
    # Mirror projects for the host and the guest (the main project is locked by an open Editor).
    [ValidateRange(2, 4)][int]$HostLane = 2,
    [ValidateRange(2, 4)][int]$GuestLane = 3,
    [int]$Port = 18793,
    [string]$Prefix = 'net2p'
)

# Two real game processes (host and guest, both with -Graphics) against one real relay.
# Each plays the scenario in GymChaosClassSessionPlay (netHost / netGuest) through the real
# start menu and they hand over to each other through files in a shared folder.
# Prints each process's summary and the evidence PNGs (<lane>\Logs\agent\classes-net-*.png).

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$check = Join-Path $PSScriptRoot 'Invoke-UnityCheck.ps1'
$sync = Join-Path ([IO.Path]::GetTempPath()) ('GymChaosNet-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sync | Out-Null
$env:GYMCHAOS_NET_SYNC_DIR = $sync

$env:PORT = "$Port"
$relay = Start-Process node -ArgumentList ("`"" + (Join-Path $root "Server\relay\relay.js") + "`"") -WorkingDirectory (Join-Path $root "Server\relay") -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2

function Start-Lane([int]$lane, [string]$method, [string]$name)
{
    Start-Job -ArgumentList $root, $check, $lane, $method, $name, $sync -ScriptBlock {
        param($root, $check, $lane, $method, $name, $sync)
        $env:GYMCHAOS_NET_SYNC_DIR = $sync
        Set-Location $root
        & $check -Method $method -ProjectPath "$root\.lanes\lane$lane\GymChaos" -Graphics -Name $name 2>&1 |
            Where-Object { $_ -match 'UNITY_CHECK|FAIL|COMPILER_ERRORS [1-9]|GYMCHAOS_NET_|error CS' }
    }
}

try
{
    $guest = Start-Lane $GuestLane 'GymChaosNetTwoProcessVerifier.RunGuest' "$Prefix-guest"
    $hostJob = Start-Lane $HostLane 'GymChaosNetTwoProcessVerifier.RunHost' "$Prefix-host"
    $finished = Wait-Job $guest, $hostJob -Timeout 1500
    $hostText = Receive-Job $hostJob
    $guestText = Receive-Job $guest
    'HOST'; $hostText
    'GUEST'; $guestText
    $failed = ($finished.Count -lt 2) -or (($hostText + $guestText) -match 'GYMCHAOS_NET_TWO_PROCESS_FAILED|exit=[1-9]') -or
        -not (($hostText -match 'GYMCHAOS_NET_TWO_PROCESS_HOST_OK') -and ($guestText -match 'GYMCHAOS_NET_TWO_PROCESS_GUEST_OK'))
    if ($failed) { 'NET_TWO_PROCESS_FAILED'; $script:exitCode = 1 } else { 'NET_TWO_PROCESS_OK'; $script:exitCode = 0 }
}
finally
{
    if ($relay -and -not $relay.HasExited) { Stop-Process -Id $relay.Id -Force }
    Get-Job | Stop-Job -ErrorAction SilentlyContinue
    Remove-Item $sync -Recurse -Force -ErrorAction SilentlyContinue
}
exit $script:exitCode

