<#
.SYNOPSIS
  Build plugin và (tuỳ chọn) cài bundle tự nạp cho AutoCAD 2021-2024.

.EXAMPLE
  .\build.ps1                       # build Release, tạo bundle trong .\dist
  .\build.ps1 -Install              # build + copy bundle vào %APPDATA%\Autodesk\ApplicationPlugins
  .\build.ps1 -AcadDir "C:\Program Files\Autodesk\AutoCAD 2024"   # build không cần NuGet AutoCAD.NET
  .\build.ps1 -Acad 2023            # build với API AutoCAD 2023 (AutoCAD.NET 24.2.0)
#>
param(
    [switch]$Install,
    [string]$AcadDir = "",
    [ValidateSet("", "2021", "2022", "2023", "2024")]
    [string]$Acad = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$proj = Join-Path $root "src\LynhCad.Manhole.Plugin\LynhCad.Manhole.Plugin.csproj"
$loaderProj = Join-Path $root "src\LynhCad.Loader\LynhCad.Loader.csproj"
$props = @("-c", "Release")
if ($AcadDir -ne "") { $props += "-p:AcadDir=$AcadDir" }
$acadNet = @{ "2021" = "24.0.0"; "2022" = "24.1.51000"; "2023" = "24.2.0"; "2024" = "24.3.0" }
if ($Acad -ne "") { $props += "-p:AcadNetVersion=$($acadNet[$Acad])" }

Write-Host "== Build plugin ==" -ForegroundColor Cyan
dotnet build $proj @props
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
dotnet build $loaderProj @props
if ($LASTEXITCODE -ne 0) { throw "Build loader failed" }

$out = Join-Path $root "src\LynhCad.Manhole.Plugin\bin\Release\net48"
$loaderOut = Join-Path $root "src\LynhCad.Loader\bin\Release\net48"
$bundle = Join-Path $root "dist\LynhCadToolsBox.bundle"
$contents = Join-Path $bundle "Contents"

Write-Host "== Assemble bundle ==" -ForegroundColor Cyan
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory -Force -Path $contents | Out-Null
Copy-Item (Join-Path $root "deploy\LynhCadToolsBox.bundle\PackageContents.xml") $bundle
Copy-Item (Join-Path $out "LynhCad.Manhole.dll") $contents
Copy-Item (Join-Path $out "LynhCad.Manhole.Core.dll") $contents
Get-ChildItem $out -Filter *.pdb | Copy-Item -Destination $contents
Copy-Item (Join-Path $loaderOut "LynhCad.Loader.dll") $contents

# Chép đè từng file, chạy được cả khi AutoCAD đang mở: plugin không bị khoá (loader đọc vào bộ nhớ),
# chỉ LynhCad.Loader.dll bị khoá → giữ bản cũ nếu không đổi được.
function Install-Bundle([string]$src, [string]$dst) {
    New-Item -ItemType Directory -Force -Path (Join-Path $dst "Contents") | Out-Null
    $loaderLocked = $false
    foreach ($f in Get-ChildItem $src -Recurse -File) {
        $rel = $f.FullName.Substring($src.Length).TrimStart('\')
        $to = Join-Path $dst $rel
        try { Copy-Item $f.FullName $to -Force -ErrorAction Stop }
        catch {
            if ($f.Name -eq "LynhCad.Loader.dll") { $loaderLocked = $true } else { throw }
        }
    }
    if ($loaderLocked) {
        Write-Host "LynhCad.Loader.dll đang được AutoCAD dùng nên giữ bản cũ (chỉ cần thay khi loader thay đổi: đóng AutoCAD rồi cài lại)." -ForegroundColor Yellow
    }
    if (Get-Process -Name acad -ErrorAction SilentlyContinue) {
        Write-Host "Done. AutoCAD đang mở: gõ LCT_RELOAD (hoặc chạy lệnh) để dùng bản mới." -ForegroundColor Green
    } else {
        Write-Host "Done. Mở AutoCAD rồi gõ LCT_MANHOLE." -ForegroundColor Green
    }
}

if ($Install) {
    $target = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\LynhCadToolsBox.bundle"
    Write-Host "== Install to $target ==" -ForegroundColor Cyan
    Install-Bundle $bundle $target
} else {
    Write-Host "Bundle: $bundle  (or NETLOAD $contents\LynhCad.Loader.dll)" -ForegroundColor Green
}
