param([string]$Version='0.1.1',[string]$ScrcpyDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
& (Join-Path $root 'build.ps1')
$work=Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force -Path $work | Out-Null
if(!$ScrcpyDirectory){
 $zip=Join-Path $work 'scrcpy-v5.0.1.zip'
 if(!(Test-Path -LiteralPath $zip)){Invoke-WebRequest -Uri 'https://github.com/Genymobile/scrcpy/releases/download/v5.0.1/scrcpy-win64-v5.0.1.zip' -OutFile $zip}
 if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne 'B12A2C4EE8BE317422451FC7DCF8EE20A71B5EA7EF9AD73DDD825A25316227A5'){throw 'scrcpy archive checksum mismatch'}
 $vendor=Join-Path $work 'vendor'
 Expand-Archive -LiteralPath $zip -DestinationPath $vendor -Force
 $ScrcpyDirectory=Join-Path $vendor 'scrcpy-win64-v5.0.1'
}
if(!(Test-Path -LiteralPath (Join-Path $ScrcpyDirectory 'scrcpy.exe'))){throw 'scrcpy directory is incomplete'}
$stage=Join-Path $work ('stage-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'bin'),(Join-Path $stage 'runtime\tools') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'dist\Auremu.exe'),(Join-Path $root 'dist\Auremu.exe.config') -Destination (Join-Path $stage 'bin')
Copy-Item -LiteralPath $ScrcpyDirectory -Destination (Join-Path $stage 'runtime\tools\scrcpy-win64-v5.0.1') -Recurse
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $stage -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'READ-ME.txt') -Destination $stage
Set-Content -LiteralPath (Join-Path $stage 'Auremu.cmd') -Encoding Ascii -Value '@start "" "%~dp0bin\Auremu.exe"'
Set-Content -LiteralPath (Join-Path $stage 'portable.flag') -Encoding Ascii -Value $Version
$portable=Join-Path $work "Auremu-$Version-windows-x64-portable.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $portable -Force
Remove-Item -LiteralPath (Join-Path $stage 'portable.flag')
Set-Content -LiteralPath (Join-Path $stage 'installed.flag') -Encoding Ascii -Value $Version
$payload=Join-Path $work 'installer-payload.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $payload -Force
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs=@('System.dll','System.Core.dll','Microsoft.CSharp.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object {'/reference:'+(Join-Path $framework $_)}
$setup=Join-Path $work "Auremu-$Version-windows-x64-setup.exe"
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ ("/win32icon:$root\assets\brand\auremu.ico") ("/out:$setup") ("/win32manifest:$root\src\app.manifest") ("/resource:$payload,Payload.zip") @refs (Join-Path $PSScriptRoot 'Setup.cs')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
$checksums=@($setup,$portable) | ForEach-Object {((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLower())+'  '+(Split-Path $_ -Leaf)}
[IO.File]::WriteAllLines((Join-Path $work 'SHA256SUMS.txt'),$checksums,[Text.UTF8Encoding]::new($false))
Write-Output "Release ready: $work"
