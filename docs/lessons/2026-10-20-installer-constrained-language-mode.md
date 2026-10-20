# Trình cài đặt lỗi dưới Constrained Language Mode

- **Ngày:** 2026-10-20
- **Vùng code:** Trình cài đặt (`install-acad2023.ps1`)
- **Trạng thái:** đã sửa (commit `f77e277`)
- **Môi trường:** máy người dùng bật PowerShell Constrained Language Mode

## Triệu chứng

Script dừng ngay ở dòng gọi .NET (vd `[System.IO.Directory]::CreateDirectory(...)`, `[guid]::NewGuid()`)
(thông báo của PowerShell thường là "Cannot invoke method. Method invocation is supported only on core types in this language mode."
— nguyên văn trên máy người dùng không được lưu lại).

## Nguyên nhân

Constrained Language Mode cấm gọi phương thức .NET (`[Type]::Method()`) và `New-Object` với kiểu .NET.

## Cách sửa

Thay bằng cmdlet: `New-Item -ItemType Directory`, `(New-Guid).Guid`, `Remove-Item -LiteralPath ... -Recurse`.
Riêng việc bật TLS 1.2 (không bắt buộc trên Windows 10/11) để trong `try { } catch { }`.

## Quy tắc

- Script cài đặt chỉ dùng cmdlet.
- Gọi .NET chỉ khi là phần phụ, không bắt buộc, và phải bọc trong `try { } catch { }`.
- File lưu UTF-8 có BOM + CRLF để PowerShell 5.1 đọc đúng tiếng Việt.
