<#
.SYNOPSIS
  Cài LynhCadToolsBox (Manhole Detail) cho AutoCAD 2023.

.DESCRIPTION
  Tải bản build mới nhất (release "latest" trên GitHub), bỏ chặn file tải từ Internet,
  rồi chép bundle vào %APPDATA%\Autodesk\ApplicationPlugins để AutoCAD tự nạp mỗi lần mở.
  Chạy được cả khi AutoCAD đang mở: cài xong gõ LCT_RELOAD (hoặc chạy lệnh) để dùng bản mới.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\install-acad2023.ps1
  powershell -ExecutionPolicy Bypass -File .\install-acad2023.ps1 -Zip .\LynhCadToolsBox-AutoCAD2023.zip
  powershell -ExecutionPolicy Bypass -File .\install-acad2023.ps1 -Uninstall

  Chạy thẳng từ GitHub, không cần lưu file:
  powershell -ExecutionPolicy Bypass -Command "iwr https://raw.githubusercontent.com/yeuthantoc/lynhcadtoolsbox/main/install-acad2023.ps1 -OutFile $env:TEMP\install-acad2023.ps1 -UseBasicParsing; & $env:TEMP\install-acad2023.ps1"
#>
param(
    [string]$Zip = "",
    [switch]$Uninstall
)
$ErrorActionPreference = "Stop"

$url = "https://github.com/yeuthantoc/lynhcadtoolsbox/releases/download/latest/LynhCadToolsBox-AutoCAD2023.zip"
$target = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\LynhCadToolsBox.bundle"
$acadRunning = [bool](Get-Process -Name acad -ErrorAction SilentlyContinue)

if ($Uninstall) {
    if ($acadRunning) { Write-Host "Hãy đóng AutoCAD trước khi gỡ." -ForegroundColor Yellow; return }
    if (Test-Path $target) { Remove-Item $target -Recurse -Force; Write-Host "Đã gỡ LynhCadToolsBox." -ForegroundColor Green }
    else { Write-Host "Chưa cài LynhCadToolsBox." }
    return
}

if (-not (Test-Path "HKLM:\SOFTWARE\Autodesk\AutoCAD\R24.2")) {
    Write-Host "Cảnh báo: không thấy AutoCAD 2023 (R24.2) trên máy này. Vẫn tiếp tục cài." -ForegroundColor Yellow
}

# không dùng %TEMP%: trên nhiều máy nó là đường dẫn 8.3 (C:\Users\HUYNH~1.LYN\...) mà PowerShell 5.1 xử lý lỗi
$work = Join-Path $env:LOCALAPPDATA ("LynhCadToolsBox\install-" + [guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($work) | Out-Null
try {
    if ($Zip -eq "") {
        $Zip = Join-Path $work "bundle.zip"
        Write-Host "Đang tải $url ..." -ForegroundColor Cyan
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = "SilentlyContinue"   # thanh tiến trình làm Invoke-WebRequest của PowerShell 5.1 rất chậm
        Invoke-WebRequest -Uri $url -OutFile $Zip -UseBasicParsing
    } elseif (-not (Test-Path -LiteralPath $Zip)) {
        throw "Không thấy file $Zip"
    } else {
        $Zip = (Resolve-Path -LiteralPath $Zip).ProviderPath
    }

    $extract = Join-Path $work "x"
    Expand-Archive -LiteralPath $Zip -DestinationPath $extract -Force
    $manifest = Get-ChildItem -LiteralPath $extract -Recurse -Filter PackageContents.xml | Select-Object -First 1
    if ($null -eq $manifest) { throw "File zip không chứa LynhCadToolsBox.bundle (thiếu PackageContents.xml)." }
    $bundle = $manifest.Directory.FullName

    # file tải từ Internet bị Windows đánh dấu chặn -> AutoCAD không nạp được DLL
    Get-ChildItem -LiteralPath $bundle -Recurse -File | Unblock-File

    # chép đè từng file: chạy được cả khi AutoCAD đang mở (chỉ LynhCad.Loader.dll bị khoá)
    $loaderLocked = $false
    foreach ($f in Get-ChildItem -LiteralPath $bundle -Recurse -File) {
        $to = Join-Path $target ($f.FullName.Substring($bundle.Length).TrimStart('\'))
        New-Item -ItemType Directory -Force -Path (Split-Path $to) | Out-Null
        try { Copy-Item -LiteralPath $f.FullName -Destination $to -Force -ErrorAction Stop }
        catch {
            if ($f.Name -eq "LynhCad.Loader.dll") { $loaderLocked = $true }
            else { throw "Không ghi được $to (AutoCAD đang dùng bản cũ chưa có loader?). Hãy đóng AutoCAD rồi chạy lại." }
        }
    }

    Write-Host "Đã cài vào $target" -ForegroundColor Green
    if ($loaderLocked) {
        Write-Host "LynhCad.Loader.dll đang được AutoCAD dùng nên giữ bản cũ (chỉ cần thay khi loader thay đổi: đóng AutoCAD rồi chạy lại)." -ForegroundColor Yellow
    }
    if ($acadRunning) {
        Write-Host "AutoCAD đang mở: gõ LCT_RELOAD (hoặc chạy lệnh) để dùng bản mới, không cần mở lại." -ForegroundColor Green
    } else {
        Write-Host "Mở AutoCAD 2023, gõ LCT_MANHOLE (vẽ), LCT_MANHOLE_JSON (từ file), LCT_MANHOLE_EDIT (sửa), LCT_RELOAD (nạp bản mới)." -ForegroundColor Green
    }
}
catch {
    Write-Host ("Cài đặt lỗi: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
finally {
    # dọn thư mục tạm; lỗi ở đây không được che lỗi thật ở trên
    try { [System.IO.Directory]::Delete($work, $true) } catch { }
}
