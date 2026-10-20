<#
.SYNOPSIS
  Build plugin và (tuỳ chọn) cài bundle tự nạp cho AutoCAD 2021-2024.

.EXAMPLE
  .\build.ps1                       # build Release, tạo bundle trong .\dist
  .\build.ps1 -Install              # build + copy bundle vào %APPDATA%\Autodesk\ApplicationPlugins
  .\build.ps1 -AcadDir "C:\Program Files\Autodesk\AutoCAD 2024"   # build không cần NuGet AutoCAD.NET
#>
param(
    [switch]$Install,
    [string]$AcadDir = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$proj = Join-Path $root "src\LynhCad.Manhole.Plugin\LynhCad.Manhole.Plugin.csproj"
$props = @("-c", "Release")
if ($AcadDir -ne "") { $props += "-p:AcadDir=$AcadDir" }

Write-Host "== Build plugin ==" -ForegroundColor Cyan
dotnet build $proj @props
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$out = Join-Path $root "src\LynhCad.Manhole.Plugin\bin\Release\net48"
$bundle = Join-Path $root "dist\LynhCadToolsBox.bundle"
$contents = Join-Path $bundle "Contents"

Write-Host "== Assemble bundle ==" -ForegroundColor Cyan
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory -Force -Path $contents | Out-Null
Copy-Item (Join-Path $root "deploy\LynhCadToolsBox.bundle\PackageContents.xml") $bundle
Copy-Item (Join-Path $out "LynhCad.Manhole.dll") $contents
Copy-Item (Join-Path $out "LynhCad.Manhole.Core.dll") $contents
Get-ChildItem $out -Filter *.pdb | Copy-Item -Destination $contents

if ($Install) {
    $target = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\LynhCadToolsBox.bundle"
    Write-Host "== Install to $target ==" -ForegroundColor Cyan
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Copy-Item $bundle $target -Recurse
    Write-Host "Done. Restart AutoCAD, then type LCT_MANHOLE." -ForegroundColor Green
} else {
    Write-Host "Bundle: $bundle  (or NETLOAD $contents\LynhCad.Manhole.dll)" -ForegroundColor Green
}
