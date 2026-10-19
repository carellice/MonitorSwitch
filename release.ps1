param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'release'))
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$buildDirectory = Join-Path $OutputDirectory '.build'
& (Join-Path $taskRoot 'build.ps1') -OutputDirectory $buildDirectory
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$report = Join-Path $OutputDirectory 'self-test-report.txt'
$program = Join-Path $buildDirectory 'MonitorSwitch.exe'
$process = Start-Process -FilePath $program -ArgumentList @('--self-test', ('"' + $report + '"')) -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw (Get-Content -LiteralPath $report -Raw) }
Get-Content -LiteralPath $report
$package = Join-Path $OutputDirectory '.package\MonitorSwitch'
New-Item -ItemType Directory -Path $package -Force | Out-Null
Copy-Item -LiteralPath $program -Destination $package -Force
foreach ($file in @('LICENSE', 'PORTABLE.txt', 'LEGGIMI.txt')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $file) -Destination $package -Force
}
$archive = Join-Path $OutputDirectory 'MonitorSwitch-portable-win-x64.zip'
Compress-Archive -LiteralPath $package -DestinationPath $archive -Force
Copy-Item -LiteralPath $program -Destination (Join-Path $OutputDirectory 'MonitorSwitch.exe') -Force
$hashes = foreach ($file in @('MonitorSwitch-portable-win-x64.zip', 'MonitorSwitch.exe')) {
    $hash = Get-FileHash -LiteralPath (Join-Path $OutputDirectory $file) -Algorithm SHA256
    $hash.Hash.ToLowerInvariant() + '  ' + $file
}
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'), $hashes, [Text.Encoding]::ASCII)
Get-ChildItem -LiteralPath $OutputDirectory -File | Select-Object Name, Length
