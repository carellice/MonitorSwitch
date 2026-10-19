param(
    [string]$OutputName = 'MonitorSwitch.exe',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or $OutputName -notlike '*.exe') { throw 'Nome eseguibile non valido.' }
$taskRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Compilatore .NET Framework 4.x non trovato.' }
$outputFolder = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputFolder -Force | Out-Null
$iconSource = Join-Path $taskRoot 'tools\IconBuilder.cs'
$iconFolder = Join-Path $taskRoot 'assets'
$iconFile = Join-Path $iconFolder 'monitor-switch.ico'
if (-not (Test-Path -LiteralPath $iconFile) -or (Get-Item -LiteralPath $iconSource).LastWriteTimeUtc -gt (Get-Item -LiteralPath $iconFile).LastWriteTimeUtc) {
    $iconBuilder = Join-Path $outputFolder 'IconBuilder.exe'
    & $compiler /nologo /target:exe /r:System.Drawing.dll ('/out:' + $iconBuilder) $iconSource
    if ($LASTEXITCODE -ne 0) { throw 'Compilazione icona non riuscita.' }
    & $iconBuilder $iconFolder
    if ($LASTEXITCODE -ne 0) { throw 'Creazione icona non riuscita.' }
    Remove-Item -LiteralPath $iconBuilder -Force
}
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$compilerOptions = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/warn:4', '/warnaserror+',
    '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Runtime.Serialization.dll',
    ('/win32manifest:' + (Join-Path $taskRoot 'src\app.manifest')),
    ('/win32icon:' + $iconFile),
    ('/resource:' + $iconFile + ',MonitorSwitch.Icon'),
    ('/out:' + (Join-Path $outputFolder $OutputName)))
& $compiler @compilerOptions @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Compilazione non riuscita.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'LEGGIMI.txt') -Destination $outputFolder -Force
Get-Item -LiteralPath (Join-Path $outputFolder $OutputName) | Select-Object FullName, Length
