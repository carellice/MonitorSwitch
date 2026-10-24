# Contributing

Bug reports and pull requests are welcome. Check existing issues before opening a new one, and describe the monitor topology, Windows version and display connection when reporting a problem.

## Development

Use Windows 10/11 x64 with the .NET Framework C# compiler bundled with Windows. No NuGet dependencies are needed.

```powershell
.\build.ps1
.\release.ps1
.\tools\test-updater.ps1
```

`release.ps1` builds the application, runs the self-tests without changing display settings, and creates the portable package and SHA-256 checksums in `release/`.

`tools/test-updater.ps1` exercises the real temporary updater using isolated stub executables. It checks process handoff, replacement, startup receipt, tray restart and retention of the previous version, then removes its own test folders. It does not run the normal application or change monitor settings. The live `--test-update-download` command additionally checks the public GitHub download and digest without executing it.

Display-changing behavior should be checked on physical hardware with rollback confirmation enabled. Automated tests and `SDC_VALIDATE` do not prove that every driver will display a requested mode correctly. Do not include local profiles, monitor diagnostics or generated binaries in commits.

Keep the application lightweight and preserve compatibility with existing saved profiles. Contributions are licensed under the project's MIT license.

Before a release, update the embedded version, changelog, portable guides and
`docs/releases/<tag>.md`. The tag workflow reads that file as its release notes.
Use `--render-preview <png>` and `--render-dialogs <folder>` for offscreen UI
review; these modes use sample data and do not change displays or user profiles.
The history importer uses registered Windows monitor interfaces, including
non-present devices; do not infer historical connection dates from installation
records. Resolution protection tests inject the correction action and must never
apply real display changes. Do not describe reactive protection as a universal
exclusive-fullscreen or internal rendering lock.
