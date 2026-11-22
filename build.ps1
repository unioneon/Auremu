param([string]$OutputName='Auremu.exe')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$out = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$references = @('System.dll','System.Core.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xml.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /utf8output ("/win32icon:$root\assets\brand\auremu.ico") ("/out:$out\$OutputName") ("/win32manifest:$root\src\app.manifest") @references ("/resource:$root\assets\brand\auremu.ico,Auremu.ico") ("/resource:$root\src\MainWindow.xaml,MainWindow.xaml") ("/resource:$root\src\AuremuWindow.xaml,AuremuWindow.xaml") ("/resource:$root\assets\design\approved-layout.json,ApprovedLayout.json") ("/resource:$root\assets\brand\logo-light.svg,logo-light.svg") ("/resource:$root\assets\brand\logo-dark.svg,logo-dark.svg") ("/resource:$root\assets\android-hero.png,android-hero.png") (Join-Path $root 'src\Aemu.cs') (Join-Path $root 'src\AuremuRuntime.cs') (Join-Path $root 'src\AuremuShell.cs') (Join-Path $root 'src\AuremuApprovedLayout.cs') (Join-Path $root 'src\FirstRunSetup.cs') (Join-Path $root 'src\AssemblyInfo.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Copy-Item -LiteralPath (Join-Path $root 'src\App.config') -Destination (Join-Path $out ($OutputName + '.config')) -Force
Write-Output "Built: $out\$OutputName"
