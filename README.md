<p align="center">
  <img src="assets/monitor-switch.png" width="112" height="112" alt="MonitorSwitch icon">
</p>
<h1 align="center">MonitorSwitch</h1>
<p align="center">Remember your monitors. Choose your screens. Keep your resolution.</p>
<p align="center">
  <a href="https://github.com/carellice/MonitorSwitch/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/carellice/MonitorSwitch"></a>
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-8b75ff"></a>
  <img alt="Windows 10 and 11 x64" src="https://img.shields.io/badge/Windows-10%20%2F%2011%20x64-0078D4">
</p>
<p align="center">
  <strong><a href="https://github.com/carellice/MonitorSwitch/releases/latest/download/MonitorSwitch-portable-win-x64.zip">Download portable ZIP</a></strong>
  &nbsp; · &nbsp;
  <a href="https://github.com/carellice/MonitorSwitch/releases/latest/download/MonitorSwitch.exe">Download standalone EXE</a>
</p>

MonitorSwitch remembers the monitors connected to your Windows PC, lets you give them useful names, and switches between profiles with explicit screens and resolutions. For example, use the desk monitor at 1920 x 1080 for games and at 3440 x 1440 for work.

**Windows 10/11 x64 · Portable · No administrator rights required · Italian interface**

![Monitor history and profiles](docs/screenshot.png)

## Get started

1. Download and extract the portable ZIP, then open `MonitorSwitch/MonitorSwitch.exe`.
2. In **Storico monitor**, select a display and click **Rinomina**. Names are shared by all profiles.
3. Click **Nuovo**, check the monitors to enable, enter or choose each resolution, and choose the primary monitor.
4. Save and activate the profile. Confirm within 20 seconds; an unconfirmed change rolls back.

Unchecked monitors are disabled. Multiple checked monitors extend the desktop; the primary starts at the origin and the others are arranged horizontally. The aspect ratio is calculated from the selected resolution. Monitor names stay in history after disconnection.

**Profilo attivo** shows a profile only when the actual monitor identities, dimensions, placement, orientation, refresh rate and required aspect preservation match. A selected list row alone does not activate a profile. If multiple saved profiles match and none was explicitly activated, the app avoids guessing which one is active.

## Monitor history

History includes registered monitor interfaces remembered by Windows, monitors observed while the app runs, and displays in old saved profiles. It does not require every display to be connected at once. **Ultimo rilevamento** records when MonitorSwitch last observed a connected display; Windows-only historical entries have no invented date or resolution.

Names and cached modes are saved in `%LOCALAPPDATA%\MonitorSwitch\profiles.xml`. A disconnected monitor can be included in a profile; it must be reconnected before that profile can be applied. If its resolution has never been observed, enter it manually. Changing ports, docks or drivers can change the Windows device identity and create another entry.

## Resolution protection and games

New profiles enable **Proteggi la risoluzione mentre il profilo e attivo**. MonitorSwitch requests the exact desktop modes without allowing Windows to substitute resolutions, requests aspect-preserving scaling, and verifies the actual result. A driver that substitutes a resolution or stretches the requested aspect is rejected and the previous configuration is restored. Equal-aspect physical signal timings and centered/aspect-preserving GPU scaling are accepted.

After a confirmed activation, keep MonitorSwitch running in the tray. It watches display-change notifications and checks every 1.5 seconds while a protected profile is armed. Detected drift is reapplied and verified, using the recovery backup and guardian. Disconnecting a required monitor pauses protection; reconnecting resumes it. After three corrections within one minute, the next conflict suspends protection and shows a notification instead of causing continuous mode changes. Reactivate the profile to retry.

**For games, use Borderless / Finestra senza bordi and select the profile resolution in the game.** This protects the Windows output mode. Fullscreen exclusive games can independently choose a display mode or scaling, and the app cannot universally lock their internal rendering resolution, aspect settings or field of view. Protection reacts after a detected change; it is not a driver-level mode lock. Some drivers may refuse changes during exclusive fullscreen. GPU/monitor scaling must also support aspect preservation; monitor-side processing is outside the app's control.

These limits follow the Windows APIs: [desktop scaling and DirectX games](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/scaling-the-desktop-image), [SetDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig), and [DXGI fullscreen behavior](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/dxgi-best-practices).

**Disattiva profilo** stops protection and clears the active selection while leaving the screens as configured. Closing the app keeps the display configuration and stops protection. On restart, a saved protected profile is rearmed only if it still matches the current configuration; startup never applies a different layout automatically.

## Profile switching fix in 3.0.1

Virtual-mode display paths are handled explicitly, including their separate desktop image. Profile changes wait for two matching readings before confirmation. Existing profiles do not need to be recreated. Genuine mismatches still roll back and now show the requested and received resolution, refresh rate, placement, scaling and signal dimensions.

## Upgrading from 2.x

Existing profiles, shortcuts and update preferences are preserved. The original settings file is backed up as `profiles.xml.v1.bak` before the first migration save. Exact saved layouts remain usable. Old generic profiles such as duplicate/extend/external-only show **Configura** until edited to choose physical monitors. Saving a profile in the new editor uses explicit monitors and an extended layout. Legacy profiles have protection disabled until you enable it. The old quick topology buttons and two-profile switch have been removed; their reserved switch shortcut is released.

Fresh installations start with no preset profiles. You can remove every profile without losing monitor history.

## Tray, startup and updates

Each profile can have a global shortcut. Applying it from a shortcut or the tray keeps the main window hidden; only the temporary confirmation appears when enabled. The tray menu and tooltip show the verified active profile.

Enable **Avvia con Windows (minimizzato)** to launch in the tray at sign-in. The setting is per Windows user and is off by default. Keep the portable executable in a permanent location; if you move it, disable and re-enable startup. Startup does not apply a display profile.

**Aggiornamenti** checks the latest stable GitHub release and offers **Scarica e riavvia**. Downloads are checked against the GitHub SHA-256 digest, x64 executable header and embedded version. A temporary updater replaces the executable, preserves settings and retains the previous executable as `.previous`. Startup update checks are optional and limited to once per day after a successful check. Updates are installed only after the download action is selected. Version 2.1.0 and earlier require one manual download to gain the updater.

Downloads include `SHA256SUMS.txt`. Binaries are currently unsigned. Display management works offline; updates connect to GitHub.

## Build and verification

Use the .NET Framework C# compiler included with Windows:

```powershell
./build.ps1
./release.ps1
./tools/test-updater.ps1
```

`dist/MonitorSwitch.exe` is the development build. `release/` contains the standalone EXE, portable ZIP and checksums. There are no NuGet or external runtime dependencies.

Self-tests cover persistence, legacy migration, aliases, disconnected history, source routing, exact modes, aspect verification, active-profile persistence, resolution drift, reconnection, conflict limits, rollback and the updater. They do not apply display changes. `--validate-current <report>`, `--validate-editor <report>` and `--validate-profiles <report>` use driver validation without applying modes. Physical driver/game behavior still requires hardware testing.

Version tags such as `v3.0.0` trigger Windows build/tests and the GitHub release workflow. Add release notes at `docs/releases/<tag>.md`. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Data and limitations

| File | Purpose |
| --- | --- |
| `%LOCALAPPDATA%\MonitorSwitch\profiles.xml` | Profiles, monitor history, aliases and preferences |
| `%LOCALAPPDATA%\MonitorSwitch\pending-display.bin` | Recovery for an unfinished display change |
| `%LOCALAPPDATA%\MonitorSwitch\errors.log` | Diagnostics |

Do not delete a pending recovery backup. Reconnect the original monitors and reopen the app if recovery cannot complete. Review diagnostics before sharing them publicly.

Windows 10/11 x64 with .NET Framework 4.x is required. Hardware and drivers must support the requested modes. HDR, VRR, DPI scaling, brightness, color profiles and internal game rendering are not managed. No virtual monitor, EDID override, game injection or resolution-list filtering is installed.

Full instructions: [Italian guide](LEGGIMI.txt) · [English portable guide](PORTABLE.txt).

[MIT](LICENSE) © 2026 carellice.
