# Benching (mass concrete) nhảy ra ngoài bản vẽ sau khi vẽ

- **Ngày:** 2026-10-21
- **Vùng code:** Core (`Drawing/SectionBuilder.cs`, `Drawing/Primitives.cs`), ảnh hưởng tới Plugin khi dời model
- **Trạng thái:** đã sửa
- **Môi trường:** AutoCAD 2023, `LCT_MANHOLE` vẽ hố MH01 (tỉ lệ 1:20)

## Triệu chứng

Sau khi chạy lệnh, hình thang "MASS CONCRETE BENCHING" (hatch + đường bao) không nằm trong lòng hố trên mặt cắt A-A mà
nằm tách hẳn ra ngoài, phía dưới-phải chi tiết, cách xa đúng bằng khoảng từ gốc (0,0) tới điểm người dùng chọn.
Preview SVG (`check` / `svg`) vẫn đúng vì không dời model tới điểm chọn (chỉ dời giữa các hình chiếu, lượng nhỏ, khó thấy).

## Nguyên nhân

`SectionBuilder.BuildBenching` tạo một `PPolyline poly` rồi dùng **cùng một đối tượng** cho vòng biên của `PHatch`
(`ConcreteHatch(poly)`) và thêm `poly` vào model làm đường bao. `DrawingModel.Translate` dời từng primitive:
`PHatch.Translate` dời các vòng biên, rồi `PPolyline.Translate` dời `poly` thêm lần nữa → hatch và đường bao cùng bị dời
gấp đôi khi `Commands.cs` gọi `model.Translate(center)` để đặt chi tiết vào điểm chọn.

## Cách sửa

- `PPolyline.Clone()`; benching dùng `poly.Clone()` (layer hatch) cho vòng biên, `poly` (layer bê tông) cho đường bao.
- `check` có mục `SharedPrimitives`: với mọi mẫu, không có primitive nào xuất hiện hai lần (kể cả trong `PHatch.Loops`),
  dời model thì hộp bao dời đúng một lần, đường bao benching vẫn trùng vòng biên hatch sau khi dời.

## Quy tắc

- Một primitive chỉ được thuộc về **một** chỗ trong model: không đưa polyline của `PHatch.Loops` vào `Items` (và ngược lại),
  không dùng chung một `Prim` giữa hai model / hai hatch. Cần hai bản thì `Clone()` (PPolyline) hoặc tạo mới.
- Primitive mới có thành phần con (như `PHatch.Loops`, `PBlock`) phải tự dời/biến đổi các thành phần con; không được giữ
  tham chiếu tới primitive đã nằm trong `Items`.
- Không bỏ mục `SharedPrimitives` trong `check`.
