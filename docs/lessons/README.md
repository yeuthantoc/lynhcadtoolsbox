# Kinh nghiệm & lỗi đã gặp

Mỗi lỗi runtime từng xảy ra là **một file markdown** trong thư mục này. Mục đích: không lặp lại lỗi cũ.

## Quy tắc

1. **Trước khi viết hoặc sửa add-in** (`src/`, `install-acad2023.ps1`, `build.ps1`, `deploy/`, `.github/workflows/`):
   đọc hết các file trong thư mục này và làm đúng phần "Quy tắc" của từng bài.
2. **Mỗi khi có sự cố** (trong AutoCAD, trình cài đặt, loader, CI, hay trên máy người dùng):
   chép `_TEMPLATE.md` thành `YYYY-MM-DD-ten-ngan.md`, điền đủ, rồi thêm một dòng vào bảng bên dưới.
   Commit cùng với bản sửa; nếu chưa sửa được thì vẫn commit bài học, ghi "Trạng thái: chưa sửa".
3. Không xoá bài cũ. Nếu quy tắc thay đổi, sửa bài đó và ghi rõ lý do.

## Mục lục

| Ngày | Bài học | Vùng code |
|---|---|---|
| 2026-10-20 | [Lệnh LCT_* không gõ được trong AutoCAD](2026-10-20-loader-commandmethod-localized-name.md) | Loader |
| 2026-10-20 | [Trình cài đặt lỗi vì %TEMP% là đường dẫn 8.3](2026-10-20-installer-temp-8dot3-path.md) | Trình cài đặt |
| 2026-10-20 | [Trình cài đặt lỗi dưới Constrained Language Mode](2026-10-20-installer-constrained-language-mode.md) | Trình cài đặt |
| 2026-10-20 | [LCT_RELOAD mở hộp thoại cũ](2026-10-20-wpf-xaml-cache-on-reload.md) | Plugin / WPF |
| 2026-10-20 | [Không thay được DLL khi AutoCAD đang mở](2026-10-20-dll-locked-while-autocad-open.md) | Loader / cài đặt |
| 2026-10-20 | [Ghi chú đè lên kích thước và nhãn cao độ](2026-10-20-callout-overlaps-dimensions.md) | Core / bản vẽ |
| 2026-10-20 | [Lớp Core trùng tên lớp toàn cục trong AcMgd.dll](2026-10-20-global-type-name-clash-acmgd.md) | Core / Plugin |
