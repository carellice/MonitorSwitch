param([string]$Program = (Join-Path $PSScriptRoot '..\release\MonitorSwitch.exe'))
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testId = [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path $workspace ('tests\updater-' + $testId)
$stageRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MonitorSwitch\updates'
$stage = Join-Path $stageRoot $testId
$parent = $null
$helper = $null
try {
    New-Item -ItemType Directory -Path $testRoot,$stage -Force | Out-Null
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $stub = @'
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
[assembly: AssemblyProduct("MonitorSwitch")]
[assembly: AssemblyFileVersion("TEST_VERSION")]
class Probe {
    static int Main(string[] args) {
        if (args.Length == 2 && args[0] == "--parent") {
            Stopwatch clock = Stopwatch.StartNew();
            while (!File.Exists(args[1]) && clock.ElapsedMilliseconds < 15000) Thread.Sleep(25);
            return File.Exists(args[1]) ? 0 : 1;
        }
        if (args.Length >= 2 && args[0] == "--updated") {
            File.WriteAllText(args[1], args.Contains("--tray") ? "tray" : "window");
            Thread.Sleep(2500); return 0;
        }
        return 2;
    }
}
'@
    $source = Join-Path $testRoot 'Probe.cs'
    $target = Join-Path $testRoot 'MonitorSwitch.exe'
    $payload = Join-Path $stage 'MonitorSwitch.exe'
    foreach ($entry in @(@{Version='2.1.0.0'; Path=$target}, @{Version='2.2.0.0'; Path=$payload})) {
        [IO.File]::WriteAllText($source, $stub.Replace('TEST_VERSION', $entry.Version))
        & $compiler /nologo /target:winexe /platform:x64 ('/out:' + $entry.Path) $source
        if ($LASTEXITCODE -ne 0) { throw 'Test executable compilation failed.' }
    }
    $ready = Join-Path $stage 'ready.ok'
    $parent = Start-Process -FilePath $target -ArgumentList @('--parent', ('"' + $ready + '"')) -WindowStyle Hidden -PassThru
    $manifest = Join-Path $stage 'update.xml'
    $writer = [Xml.XmlWriter]::Create($manifest)
    try {
        $writer.WriteStartElement('UpdateManifest')
        $values = [ordered]@{Target=$target; Payload=$payload; Hash=(Get-FileHash -LiteralPath $payload).Hash.ToLowerInvariant(); Version='2.2.0.0'; ParentId=$parent.Id; ParentStarted=$parent.StartTime.ToUniversalTime().Ticks; InTray='true'}
        foreach ($key in $values.Keys) { $writer.WriteElementString($key, [string]$values[$key]) }
        $writer.WriteEndElement()
    } finally { $writer.Dispose() }
    $helperPath = Join-Path $stage 'MonitorSwitch.Updater.exe'
    Copy-Item -LiteralPath $Program -Destination $helperPath
    $helper = Start-Process -FilePath $helperPath -ArgumentList @('--apply-update', ('"' + $manifest + '"')) -WindowStyle Hidden -PassThru
    if (-not $helper.WaitForExit(35000)) { throw 'Updater integration test timed out.' }
    if ($helper.ExitCode -ne 0) { throw 'Updater integration test failed.' }
    if ((Get-Content -LiteralPath (Join-Path $stage 'started.ok') -Raw) -ne 'tray') { throw 'Tray restart was not preserved.' }
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($target).FileVersion -ne '2.2.0.0') { throw 'Target was not updated.' }
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($target + '.previous').FileVersion -ne '2.1.0.0') { throw 'Previous version was not retained.' }
    if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $payload).Hash) { throw 'Installed bytes do not match the candidate.' }
    Write-Output 'PASS: updater handoff, parent exit, atomic replacement, restart receipt, tray flag and previous executable backup. Only isolated test executables were run.'
} finally {
    foreach ($process in @($helper,$parent)) {
        if ($null -ne $process) { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }; $process.Dispose() }
    }
    Start-Sleep -Seconds 3
    foreach ($entry in @(@{Path=$testRoot; Root=(Join-Path $workspace 'tests'); Name=('updater-' + $testId)}, @{Path=$stage; Root=$stageRoot; Name=$testId})) {
        $resolved = [IO.Path]::GetFullPath($entry.Path)
        $root = [IO.Path]::GetFullPath($entry.Root).TrimEnd('\') + '\'
        if (-not $resolved.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -ne $entry.Name) { throw 'Unsafe cleanup path.' }
        if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }
}
