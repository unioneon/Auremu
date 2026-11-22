$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references=@('System.dll','System.Core.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xml.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object {'/reference:'+(Join-Path $framework $_)}
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /main:Auremu.RuntimeCheck ("/out:$root\dist\Auremu.Check.exe") @references (Join-Path $root 'src\Aemu.cs') (Join-Path $root 'src\AuremuRuntime.cs') (Join-Path $root 'src\AuremuShell.cs') (Join-Path $root 'tests\RuntimeCheck.cs') (Join-Path $root 'src\AuremuApprovedLayout.cs') (Join-Path $root 'src\FirstRunSetup.cs')
if($LASTEXITCODE -ne 0){throw 'Runtime check build failed'}
