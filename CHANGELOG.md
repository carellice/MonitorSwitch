# Changelog

## 3.0.0

- Redesigned the app around persistent monitor history, including disconnected interfaces remembered by Windows, and aliases shared by all profiles.
- Profiles now select explicit monitors, a resolution per monitor and a primary display. Multiple selected displays extend the desktop; quick topology buttons and the profile pair switch have been removed.
- Added a verified active profile indicator in the main window, list and tray, with an explicit deactivation action.
- Added optional resolution protection, enabled for new profiles: reapply detected display changes, preserve aspect ratio, suspend on missing monitors and stop after repeated conflicts. Driver mode substitutions are rejected.
- Preserved legacy profiles and shortcuts. Generic legacy profiles must be edited to select physical monitors; existing exact layouts remain available. Legacy protection is opt-in.
- Added history, migration, scaling, protection, retry and reconnection tests; updated previews and documentation.
- Fullscreen exclusive games can still request their own modes. Use borderless and the profile resolution; internal game rendering and unsupported GPU scaling cannot be universally forced.

## 2.3.0

- Added an optional **Avvia con Windows (minimizzato)** checkbox in the main window and tray menu.
- Windows sign-in launches the app directly in the tray with global shortcuts active, without showing the main window or requiring administrator rights.
- Added isolated registry and tray startup tests. Startup remains disabled until explicitly enabled.

## 2.2.0

- Added in-app update checking against the latest stable GitHub release.
- Added optional startup checks, limited to once per day after a successful check.
- Added download progress, cancellation, SHA-256 and executable version validation.
- Added a temporary updater that replaces the portable executable on restart, retains the previous EXE, and preserves profiles and tray state.
- Added update parsing/replacement/rollback tests and a GitHub download validation command.

## 2.1.0

- Initial public release: display profiles, monitor discovery, per-display resolutions, global shortcuts, tray operation and rollback confirmation.
