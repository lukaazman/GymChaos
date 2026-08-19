[CmdletBinding()]
param(
    [string]$UnityExecutable = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [string]$UnityProjectPath = '',
    [string]$VerificationLog = ''
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($UnityProjectPath))
{
    $UnityProjectPath = Join-Path $repositoryRoot 'GymChaos'
}

if ([string]::IsNullOrWhiteSpace($VerificationLog))
{
    $VerificationLog = Join-Path $UnityProjectPath 'Logs\GymChaosVisitorVerifier.log'
}

$licenseFile = 'C:\ProgramData\Unity\Unity_lic.ulf'
if (-not (Test-Path -LiteralPath $UnityExecutable))
{
    throw "Unity editor not found: $UnityExecutable"
}
if (-not (Test-Path -LiteralPath $UnityProjectPath))
{
    throw "Unity project not found: $UnityProjectPath"
}
if (-not (Test-Path -LiteralPath $licenseFile))
{
    throw "Unity Personal license file is missing: $licenseFile"
}

$licenseXml = [xml](Get-Content -LiteralPath $licenseFile -Raw)
$personalEntitlement = $licenseXml.SelectSingleNode(
    "//*[local-name()='Entitlement' and @Tag='UnityPersonal' and @Type='EDITOR']")
if ($null -eq $personalEntitlement -or
    $personalEntitlement.GetAttribute('ValidTo') -notlike '9999-12-31*')
{
    throw 'The local Unity license file does not contain the expected perpetual UnityPersonal editor entitlement.'
}

$hubProcess = Get-Process -Name 'Unity Hub' -ErrorAction SilentlyContinue
if ($null -eq $hubProcess)
{
    throw 'Unity Hub is not running. Start Hub, sign in to the existing Personal account, and run this wrapper again.'
}

$verificationDirectory = Split-Path -Parent $VerificationLog
if (-not (Test-Path -LiteralPath $verificationDirectory))
{
    New-Item -ItemType Directory -Path $verificationDirectory -Force | Out-Null
}

$windowsUser = [Environment]::UserName
$editorInstallFolder = Split-Path -Parent (Split-Path -Parent $UnityExecutable)
$editorVersionFolder = Split-Path -Leaf $editorInstallFolder
$editorVersionMatch = [regex]::Match($editorVersionFolder, '^\d+\.\d+\.\d+')
$licensingPipe = if ($editorVersionMatch.Success)
{
    "LicenseClient-$windowsUser-$($editorVersionMatch.Value)"
}
else
{
    "LicenseClient-$windowsUser"
}

$existingUnityIds = @(
    Get-Process -Name 'Unity' -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Id }
)
$launchStarted = Get-Date
$unityArguments = @(
    '-batchmode',
    '-nographics',
    '-acceptSoftwareTermsForThisRunOnly',
    '-useHub',
    '-hubIPC',
    '-cloudEnvironment', 'production',
    '-licensingIpc', $licensingPipe,
    '-projectPath', $UnityProjectPath,
    '-executeMethod', 'GymChaosVisitorVerifier.Run',
    '-logFile', $VerificationLog
)

Write-Host "Unity Personal entitlement: verified"
Write-Host "Unity licensing channel: $licensingPipe via Unity Hub"
Write-Host "Verification log: $VerificationLog"

$null = & $UnityExecutable @unityArguments
$launcherExitCode = if ($null -eq $LASTEXITCODE) { 0 } else { [int]$LASTEXITCODE }

function Test-VerificationSuccessMarker
{
    if (-not (Test-Path -LiteralPath $VerificationLog))
    {
        return $false
    }

    $logInfo = Get-Item -LiteralPath $VerificationLog -ErrorAction SilentlyContinue
    if ($null -eq $logInfo -or $logInfo.LastWriteTime -lt $launchStarted.AddSeconds(-1))
    {
        return $false
    }

    $recentLog = Get-Content -LiteralPath $VerificationLog -Tail 200 -ErrorAction SilentlyContinue
    return $null -ne ($recentLog | Where-Object { $_ -like '*GYMCHAOS_VISITOR_VERIFICATION_OK*' })
}

$verificationDeadline = (Get-Date).AddMinutes(8)
$seenVerificationProcess = $false
$verificationCompleted = $false
$activeVerificationProcesses = @()
while ((Get-Date) -lt $verificationDeadline)
{
    $activeVerificationProcesses = @(
        Get-Process -Name 'Unity' -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -notin $existingUnityIds }
    )
    if ($activeVerificationProcesses.Count -gt 0)
    {
        $seenVerificationProcess = $true
    }

    $verificationCompleted = Test-VerificationSuccessMarker
    if ($verificationCompleted -and
        ($activeVerificationProcesses.Count -eq 0 -or $seenVerificationProcess))
    {
        break
    }

    if (-not $seenVerificationProcess -and
        $activeVerificationProcesses.Count -eq 0 -and
        $launcherExitCode -ne 0)
    {
        throw "Unity verifier launcher exited with code $launcherExitCode. Inspect $VerificationLog."
    }

    Start-Sleep -Seconds 2
}

$activeVerificationProcesses = @(
    Get-Process -Name 'Unity' -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -notin $existingUnityIds }
)
$verificationCompleted = Test-VerificationSuccessMarker
if (-not $verificationCompleted)
{
    if ($activeVerificationProcesses.Count -gt 0)
    {
        throw "Unity verifier did not finish within 8 minutes. Inspect $VerificationLog and close only the verification Unity process before retrying."
    }
    if ($launcherExitCode -ne 0)
    {
        throw "Unity verifier exited with code $launcherExitCode. Inspect $VerificationLog."
    }
    throw "Unity verifier finished without the success marker. Inspect $VerificationLog."
}

Write-Host 'Unity visitor verification completed successfully.'
exit 0
