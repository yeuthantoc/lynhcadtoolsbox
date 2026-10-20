# LCT_RELOAD mở hộp thoại của bản cũ

- **Ngày:** 2026-10-20
- **Vùng code:** Plugin / WPF (`LynhCad.Manhole.Plugin.csproj`, `UI/ManholeWindow.xaml`)
- **Trạng thái:** đã sửa (commit `a7ad510`)
- **Môi trường:** AutoCAD, nạp lại plugin bằng `LCT_RELOAD` trong cùng phiên

## Triệu chứng

Sau khi build bản mới và `LCT_RELOAD`, phần vẽ đã là code mới nhưng hộp thoại vẫn là giao diện cũ.

## Nguyên nhân

WPF tìm và cache XAML theo "tên assembly;phiên bản". Bản mới trùng phiên bản với bản cũ nên WPF trả về XAML đã cache.

## Cách sửa

Mỗi lần build plugin có một `AssemblyVersion` khác (`0.1.<BuildDay>.<BuildRev>` trong csproj).
CI kiểm tra URI trong `ManholeWindow.g.cs` có chứa phiên bản.

## Quy tắc

- Không bỏ / không cố định `AssemblyVersion` của plugin.
- Không xoá bước kiểm tra URI XAML trong `.github/workflows/build.yml`.
