param([string]$Version='0.1.0')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$artifacts=Join-Path $root 'artifacts'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$output=Join-Path $PSScriptRoot 'output'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$refs=@('System.dll','System.Core.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object {'/reference:'+(Join-Path $framework $_)}
$app=Join-Path $root 'dist\Auremu.exe'
foreach($test in @('ReleaseCheck','ApprovedDesignCheck','ScreenCornerMaskCheck')){
 $exe=Join-Path $output ($test+'.exe')
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 ("/out:$exe") ("/reference:$app") @refs (Join-Path $PSScriptRoot ($test+'.cs'))
 if($LASTEXITCODE -ne 0){throw "$test failed to compile"}
 Copy-Item -LiteralPath $app -Destination (Join-Path $output 'Auremu.exe') -Force
 $argument=if($test -eq 'ApprovedDesignCheck'){Join-Path $root 'assets\design\approved-layout.json'}else{Join-Path $output ('release-'+[Guid]::NewGuid().ToString('N'))}
 & $exe $argument
 if($LASTEXITCODE -ne 0){throw "$test failed"}
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$portable=Join-Path $artifacts "Auremu-$Version-windows-x64-portable.zip"
$archive=[IO.Compression.ZipFile]::OpenRead($portable)
try{
 foreach($entry in $archive.Entries){
  if($entry.FullName -match '(^|[/\\])(data|avd|snapshots|android-user)([/\\]|$)|settings\.json|adbkey|\.log$|\.qcow2$|\.img$|\.apk$'){throw "Private or unexpected entry: $($entry.FullName)"}
 }
 if(!($archive.Entries | Where-Object FullName -eq 'portable.flag')){throw 'Portable marker missing'}
 if(!($archive.Entries | Where-Object FullName -eq 'bin/Auremu.exe')){throw 'Application missing'}
 $target=Join-Path $output ('installer-'+[Guid]::NewGuid().ToString('N'))
 $setup=Join-Path $artifacts "Auremu-$Version-windows-x64-setup.exe"
 $process=Start-Process -FilePath $setup -ArgumentList @('--extract',('"'+$target+'"')) -WindowStyle Hidden -Wait -PassThru
 if($process.ExitCode -ne 0){throw 'Installer extraction failed'}
 if(!(Test-Path -LiteralPath (Join-Path $target 'installed.flag')) -or (Test-Path -LiteralPath (Join-Path $target 'portable.flag'))){throw 'Installed marker isolation failed'}
 foreach($entry in $archive.Entries){
  if($entry.Name -eq '' -or $entry.FullName -eq 'portable.flag'){continue}
  $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create();try{$expected=[Convert]::ToBase64String($sha.ComputeHash($stream))}finally{$stream.Dispose();$sha.Dispose()}
  $file=Join-Path $target $entry.FullName
  $actual=[Convert]::ToBase64String([Convert]::FromHexString((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash))
  if($actual -ne $expected){throw "Installer differs from portable: $($entry.FullName)"}
 }
 Write-Output 'PASS: release contains no VM/session/settings/ADB keys; installer and portable payloads match; distinct data markers.'
}finally{$archive.Dispose()}
