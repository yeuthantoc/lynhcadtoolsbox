<#
.SYNOPSIS
  Cài LynhCadToolsBox (Manhole Detail) cho AutoCAD 2023.

.DESCRIPTION
  Tải bản build mới nhất từ GitHub Releases (hoặc dùng file .zip có sẵn),
  bỏ chặn file tải từ Internet, rồi chép bundle vào
  %APPDATA%\Autodesk\ApplicationPlugins để AutoCAD tự nạp mỗi lần mở.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\install-acad2023.ps1
  powershell -ExecutionPolicy Bypass -File .\install-acad2023.ps1 -Zip .\LynhCadToolsBox-AutoCAD2023.zip
  powershell -ExecutionPolicy Bypass -File .\install-acad2023.ps1 -Uninstall
#>
param(
    [string]$Zip = "",
    [switch]$Uninstall
)
$ErrorActionPreference = "Stop"

$url = "https://github.com/yeuthantoc/lynhcadtoolsbox/releases/download/latest/LynhCadToolsBox-AutoCAD2023.zip"
$target = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\LynhCadToolsBox.bundle"

if (Get-Process -Name acad -ErrorAction SilentlyContinue) {
    Write-Host "AutoCAD đang mở. Hãy đóng AutoCAD rồi chạy lại (DLL đang được dùng sẽ không ghi đè được)." -ForegroundColor Yellow
    exit 1
}

if ($Uninstall) {
    if (Test-Path $target) { Remove-Item $target -Recurse -Force; Write-Host "Đã gỡ LynhCadToolsBox." -ForegroundColor Green }
    else { Write-Host "Chưa cài LynhCadToolsBox." }
    exit 0
}

if (-not (Test-Path "HKLM:\SOFTWARE\Autodesk\AutoCAD\R24.2")) {
    Write-Host "Cảnh báo: không thấy AutoCAD 2023 (R24.2) trên máy này. Vẫn tiếp tục cài." -ForegroundColor Yellow
}

$work = Join-Path $env:TEMP ("LynhCadToolsBox-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
    if ($Zip -eq "") {
        $Zip = Join-Path $work "bundle.zip"
        Write-Host "Đang tải $url ..." -ForegroundColor Cyan
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $url -OutFile $Zip -UseBasicParsing
    } elseif (-not (Test-Path $Zip)) {
        throw "Không thấy file $Zip"
    }

    $extract = Join-Path $work "x"
    Expand-Archive -Path $Zip -DestinationPath $extract -Force
    $manifest = Get-ChildItem $extract -Recurse -Filter PackageContents.xml | Select-Object -First 1
    if ($null -eq $manifest) { throw "File zip không chứa LynhCadToolsBox.bundle (thiếu PackageContents.xml)." }
    $bundle = $manifest.Directory.FullName

    # file tải từ Internet bị Windows đánh dấu chặn -> AutoCAD không nạp được DLL
    Get-ChildItem $bundle -Recurse -File | Unblock-File

    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item $bundle $target -Recurse
    Write-Host "Đã cài vào $target" -ForegroundColor Green
    Write-Host "Mở AutoCAD 2023, gõ LCT_MANHOLE (vẽ), LCT_MANHOLE_JSON (từ file), LCT_MANHOLE_EDIT (sửa chi tiết đã vẽ)." -ForegroundColor Green
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
