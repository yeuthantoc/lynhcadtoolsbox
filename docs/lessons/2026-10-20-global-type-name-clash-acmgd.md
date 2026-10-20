# Lớp của Core trùng tên lớp toàn cục trong AcMgd.dll

- **Ngày:** 2026-10-20
- **Vùng code:** Core (`Drawing/SymbolLibrary.cs`, trước đó đặt tên `BlockLibrary`), Plugin
- **Trạng thái:** đã sửa (phát hiện khi type-check plugin trên Linux, trước khi lên CI)
- **Môi trường:** biên dịch plugin với AutoCAD.NET 24.0.0

## Triệu chứng

Core biên dịch bình thường, nhưng khi biên dịch plugin (tham chiếu `AcMgd.dll`) thì báo
`error CS0117: 'BlockLibrary' does not contain a definition for 'Expand'` ngay trong code Core
(`SvgExporter.cs`, `AcadRenderer.cs`, `PreviewRenderer.cs`).

## Nguyên nhân

`AcMgd.dll` có lớp `BlockLibrary` nằm ở **namespace gốc** (không có namespace). Khi tra tên, C# ưu tiên kiểu ở namespace gốc
trước các `using` ở đầu file, nên `BlockLibrary` của AutoCAD che mất `LynhCad.Manhole.Core.Drawing.BlockLibrary`.

## Cách sửa

Đổi tên lớp thành `SymbolLibrary`.

## Quy tắc

- Không đặt tên lớp trong Core / Plugin trùng với kiểu ở namespace gốc của `AcMgd.dll` / `AcDbMgd.dll` (vd `BlockLibrary`).
  Khi nghi ngờ, tra bằng reflection hoặc chọn tên có tiền tố riêng.
- Trước khi commit thay đổi Core có thêm lớp mới, type-check plugin như CLAUDE.md hướng dẫn (project net48 tạm tham chiếu AutoCAD.NET).
