# Builds Disks.exe with the C# compiler that ships with Windows (.NET Framework 4.x) - no SDK needed.
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$root = Join-Path $PSScriptRoot 'src'
function Files($dir) { Get-ChildItem (Join-Path $root $dir) -Filter *.cs | ForEach-Object { $_.FullName } }
$icon = Join-Path $root 'app.ico'
$iconArg = if (Test-Path $icon) { "/win32icon:$icon" } else { $null }

$out = Join-Path $PSScriptRoot 'Disks.exe'
& $csc /nologo /optimize+ /platform:x64 /target:winexe /codepage:65001 "/out:$out" `
    "/win32manifest:$(Join-Path $root 'gui\app.manifest')" $iconArg `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll (Files 'core') (Files 'gui')
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Write-Host "built $out"
