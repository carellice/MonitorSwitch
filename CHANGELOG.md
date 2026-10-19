# Changelog

## 2.2.0

- Added in-app update checking against the latest stable GitHub release.
- Added optional startup checks, limited to once per day after a successful check.
- Added download progress, cancellation, SHA-256 and executable version validation.
- Added a temporary updater that replaces the portable executable on restart, retains the previous EXE, and preserves profiles and tray state.
- Added update parsing/replacement/rollback tests and a GitHub download validation command.

## 2.1.0

- Initial public release: display profiles, monitor discovery, per-display resolutions, global shortcuts, tray operation and rollback confirmation.
