<#
.SYNOPSIS
  Cài LynhCadToolsBox (Manhole Detail) cho AutoCAD 2023.

.DESCRIPTION
  Lấy bản build mới nhất (release "latest" trên GitHub), bỏ chặn file tải từ Internet,
  rồi chép bundle vào %APPDATA%\Autodesk\ApplicationPlugins để AutoCAD tự nạp mỗi lần mở.

  Repo là private nên script lấy file zip theo thứ tự:
    1. -Zip <đường dẫn>
    2. LynhCadToolsBox-AutoCAD2023.zip nằm cạnh script hoặc trong thư mục Downloads
       (tải bằng trình duyệt đã đăng nhập GitHub ở trang release)
    3. Tải qua GitHub API bằng token: -Token, biến môi trường GITHUB_TOKEN, hoặc "gh auth token"

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\install-acad2023.ps1
  powershell -ExecutionPolicy Bypass -File .\install-acad2023.ps1 -Zip .\LynhCadToolsBox-AutoCAD2023.zip
  powershell -ExecutionPolicy Bypass -File .\install-acad2023.ps1 -Token ghp_xxx
  powershell -ExecutionPolicy Bypass -File .\install-acad2023.ps1 -Uninstall
#>
param(
    [string]$Zip = "",
    [string]$Token = "",
    [switch]$Uninstall
)
$ErrorActionPreference = "Stop"

$repo = "yeuthantoc/lynhcadtoolsbox"
$asset = "LynhCadToolsBox-AutoCAD2023.zip"
$releasePage = "https://github.com/$repo/releases/tag/latest"
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
    if ($Zip -ne "" -and -not (Test-Path $Zip)) { throw "Không thấy file $Zip" }

    if ($Zip -eq "") {
        # file zip đã tải sẵn bằng trình duyệt (lấy file mới nhất)
        $dirs = @($PSScriptRoot, (Join-Path $env:USERPROFILE "Downloads")) | Where-Object { $_ -and (Test-Path $_) }
        $found = Get-ChildItem -Path $dirs -Filter "LynhCadToolsBox-AutoCAD2023*.zip" -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($found) { $Zip = $found.FullName; Write-Host "Dùng file $Zip" -ForegroundColor Cyan }
    }

    if ($Zip -eq "") {
        if ($Token -eq "") { $Token = $env:GITHUB_TOKEN }
        if (-not $Token -and (Get-Command gh -ErrorAction SilentlyContinue)) { $Token = (gh auth token 2>$null) }
        if (-not $Token) {
            Write-Host "Không tìm thấy $asset." -ForegroundColor Yellow
            Write-Host "Mở trang release (đăng nhập GitHub), tải $asset về thư mục Downloads rồi chạy lại script." -ForegroundColor Yellow
            Start-Process $releasePage
            exit 1
        }
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $headers = @{ Authorization = "Bearer $Token"; "User-Agent" = "LynhCadToolsBox-installer" }
        Write-Host "Đang tải $asset từ GitHub ..." -ForegroundColor Cyan
        $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/tags/latest" -Headers $headers
        $a = $rel.assets | Where-Object { $_.name -eq $asset } | Select-Object -First 1
        if ($null -eq $a) { throw "Release latest chưa có $asset." }
        $Zip = Join-Path $work $asset
        $headers["Accept"] = "application/octet-stream"
        Invoke-WebRequest -Uri $a.url -Headers $headers -OutFile $Zip -UseBasicParsing
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
