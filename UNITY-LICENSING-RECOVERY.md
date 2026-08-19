# Unity licensing recovery for GymChaos

Last verified: 2026-08-19 on Windows, Unity `6000.5.8f1`, Unity Hub `3.20.1`.

## Verified state

The Personal entitlement is already valid locally and is not time-limited:

- `C:\ProgramData\Unity\Unity_lic.ulf` is present and contains the `UnityPersonal` editor entitlement through `9999-12-31`.
- Unity Hub successfully returned `SameMachine` with status `200` for the `UnityPersonal` seat.
- The Unity license client can parse both the ULF and Hub entitlement files.

The earlier verifier failure was an IPC startup problem, not an expired Personal license. A headless editor started without the Hub IPC arguments attempted the wrong channel and reported `Connection to channel LicenseClient-Luka refused` / `Licensing is not yet initialized`, while the normal Hub-launched editor used the version-specific channel `LicenseClient-Luka-6000.5.8` successfully.

## Project workaround

Run the verifier through the Hub-owned licensing channel and keep Unity Hub signed in:

```powershell
.\Tools\Run-UnityVisitorVerification.ps1
```

The wrapper checks that the local Personal entitlement is present, then starts Unity with `-useHub -hubIPC` and the version-specific `-licensingIpc LicenseClient-<WindowsUser>-<UnityEditorVersion>` channel. This keeps future project verification from creating a second, incompatible licensing-client channel and waits for the Hub-delegated Unity process to finish before reporting success.

Do not delete `Unity_lic.ulf`, enter a serial, or run `-returnlicense` for this Personal entitlement. Those actions can remove the working activation and are unnecessary here.

## If the local client is genuinely stuck

1. Save and close every Unity Editor and Unity Hub window.
2. Confirm no `Unity.exe` or `Unity Hub.exe` process remains.
3. Stop only the remaining `Unity.Licensing.Client.exe` processes.
4. Start Unity Hub, sign in to the existing Personal account, and open this project through Hub.
5. Re-run the wrapper above.

Keep the ULF as-is unless Hub cannot reactivate after the clean restart. The authoritative Windows diagnostics are:

- `%LOCALAPPDATA%\Unity\Editor\Editor.log`
- `%LOCALAPPDATA%\Unity\Unity.Licensing.Client.log`
- `%LOCALAPPDATA%\Unity\Unity.Entitlements.Audit.log`
- `%APPDATA%\UnityHub\logs\info-log.json`

No serial, access token, or license signature is stored in this repository.
