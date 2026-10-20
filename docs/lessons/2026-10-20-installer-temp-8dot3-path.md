# Trình cài đặt lỗi vì %TEMP% là đường dẫn 8.3

- **Ngày:** 2026-10-20
- **Vùng code:** Trình cài đặt (`install-acad2023.ps1`)
- **Trạng thái:** đã sửa (commit `3a9c6e4`)
- **Môi trường:** Windows PowerShell 5.1 trên máy người dùng

## Triệu chứng

Cài đặt hỏng với lỗi đường dẫn khó hiểu khi giải nén / chép file trong thư mục tạm. Lỗi thật bị khối `finally` che mất
(nguyên văn thông báo không được lưu lại).

## Nguyên nhân

Trên máy người dùng `%TEMP%` là đường dẫn dạng 8.3 (`C:\Users\HUYNH~1.LYN\...`). PowerShell 5.1 xử lý sai đường dẫn này,
nhất là với tham số `-Path` (ký tự `~` và wildcard).

## Cách sửa

- Làm việc trong `%LOCALAPPDATA%\LynhCadToolsBox\install-<guid>` thay vì `%TEMP%`.
- Dùng `-LiteralPath` cho mọi cmdlet nhận đường dẫn.
- Thêm `catch` in `$_.Exception.Message`; dọn thư mục tạm trong `try { } catch { }` để không che lỗi thật.

## Quy tắc

- Không dùng `%TEMP%` / `$env:TEMP` trong script cài đặt; dùng `%LOCALAPPDATA%`.
- Luôn dùng `-LiteralPath` thay cho `-Path` với đường dẫn file.
- Luôn in lỗi thật ra màn hình; phần dọn dẹp không được ném lỗi.
