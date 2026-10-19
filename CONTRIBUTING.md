# Contributing

Bug reports and pull requests are welcome. Check existing issues before opening a new one, and describe the monitor topology, Windows version and display connection when reporting a problem.

## Development

Use Windows 10/11 x64 with the .NET Framework C# compiler bundled with Windows. No NuGet dependencies are needed.

```powershell
.\build.ps1
.\release.ps1
```

`release.ps1` builds the application, runs the self-tests without changing display settings, and creates the portable package and SHA-256 checksums in `release/`.

Display-changing behavior should be checked on physical hardware with rollback confirmation enabled. Automated tests and `SDC_VALIDATE` do not prove that every driver will display a requested mode correctly. Do not include local profiles, monitor diagnostics or generated binaries in commits.

Keep the application lightweight and preserve compatibility with existing saved profiles. Contributions are licensed under the project's MIT license.
