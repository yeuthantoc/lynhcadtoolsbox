# Lệnh LCT_* không gõ được trong AutoCAD

- **Ngày:** 2026-10-20
- **Vùng code:** Loader (`src/LynhCad.Loader/Loader.cs`)
- **Trạng thái:** đã sửa (commit `384493b`)
- **Môi trường:** AutoCAD 2023

## Triệu chứng

Loader nạp bình thường (có dòng thông báo "LynhCadToolsBox đã nạp"), nhưng gõ `LCT_MANHOLE` thì AutoCAD báo lệnh không tồn tại.

## Nguyên nhân

Lệnh được khai báo bằng `[CommandMethod(group, globalName, localizedNameId, flags)]`. Dạng này tra tên địa phương
trong resource của DLL; loader không có resource nên lệnh không có tên để gõ.

## Cách sửa

Đổi mọi lệnh sang `[CommandMethod("LCT_MANHOLE", CommandFlags.Modal)]`.

## Quy tắc

- Chỉ dùng `[CommandMethod("TÊN", flags)]`. Không dùng dạng có `localizedNameId`.
- Thêm lệnh mới: `[CommandMethod]` trong `Loader.cs` + case trong `Entry.Run` + mục trong `PackageContents.xml`.
