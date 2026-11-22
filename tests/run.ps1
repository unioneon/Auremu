$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
& (Join-Path $root 'build.ps1')
$output = Join-Path $PSScriptRoot 'output'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
& (Join-Path $framework 'csc.exe') /nologo /target:exe ("/out:$output\fixture.exe") (Join-Path $PSScriptRoot 'FakeAndroidTool.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed' }
foreach ($mode in @('--self-test','--integration-test')) {
 $destination = if ($mode -eq '--self-test') { Join-Path $output 'results.txt' } else { Join-Path $output ('integration-' + [Guid]::NewGuid().ToString('N')) }
 $process = Start-Process -FilePath (Join-Path $root 'dist\Auremu.exe') -ArgumentList @($mode,('"' + $destination + '"')) -WindowStyle Hidden -Wait -PassThru
 if ($process.ExitCode -ne 0) { Get-Content (Join-Path $root 'dist\error.log') -ErrorAction SilentlyContinue; throw "$mode failed" }
 if ($mode -eq '--self-test') { Get-Content $destination } else { Get-Content (Join-Path $destination 'results.txt') }
}
