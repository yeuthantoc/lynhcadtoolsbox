# Không thay được DLL khi AutoCAD đang mở

- **Ngày:** 2026-10-20
- **Vùng code:** Loader (`src/LynhCad.Loader/Loader.cs`), trình cài đặt, `build.ps1`
- **Trạng thái:** đã sửa (commit `a7ad510`)
- **Môi trường:** AutoCAD đang mở trong lúc build / cài bản mới

## Triệu chứng

Build hoặc cài bản mới báo không ghi được DLL; phải đóng AutoCAD mỗi lần sửa code.

## Nguyên nhân

AutoCAD khoá file DLL đã nạp. .NET Framework không gỡ được assembly khỏi bộ nhớ.

## Cách sửa

- AutoCAD chỉ nạp `LynhCad.Loader.dll`. Loader nạp plugin + Core bằng `Assembly.Load(byte[])` nên file không bị khoá,
  và tự nạp lại khi DLL trên đĩa mới hơn (hoặc `LCT_RELOAD`).
- Trình cài đặt chép từng file; chỉ `LynhCad.Loader.dll` có thể bị khoá, khi đó báo người dùng khởi động lại AutoCAD.

## Quy tắc

- Plugin không khai báo `[CommandMethod]` / `ExtensionApplication` và không tham chiếu loader; loader không tham chiếu plugin/Core.
- Giữ loader tối giản: sửa loader thì người dùng phải khởi động lại AutoCAD.
