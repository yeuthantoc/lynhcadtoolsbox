# Ghi chú (callout) đè lên kích thước và nhãn cao độ

- **Ngày:** 2026-10-20
- **Vùng code:** Core (`Drawing/Annot.cs`, `Drawing/ViewBuilder.cs`, `Drawing/SectionBuilder.cs`)
- **Trạng thái:** đã sửa
- **Môi trường:** AutoCAD 2023, bản vẽ do `LCT_MANHOLE` sinh ra (tỉ lệ 1:20)

## Triệu chứng

Người dùng báo "ghi chú đè kích thước". Trên bản vẽ thấy:

- Mặt bằng: chữ ghi chú cột trái (`W1 N12-200 EF VERT`, `W2/W3 ...`, `P1 DN300 ...`) nằm ngay trên chuỗi kích thước đứng bên trái.
- Mặt cắt: đường leader của `R1/R2/R3 ... BOT EW` và `B1/B2 ... TOP/BOT EW` cắt ngang số kích thước `200` / `250`.
- Mặt cắt: nhãn cao độ `TOB RL` / `USB RL` bên phải đè lên đoạn ống ra thò ra và lên leader của `T3/T4/T5 ... TRIMMERS`.

## Nguyên nhân

- Hoành độ cột ghi chú được tính từ biên hình (`ExtentX/ExtentY`) mà không biết chuỗi kích thước nằm ở đâu;
  khi ống thò ra theo phương Y dài hơn phương X thì cột chữ rơi đúng vào vùng kích thước.
- `Annot.CalloutColumn` chỉ giãn các ghi chú để chữ không đè nhau, không biết số kích thước / nhãn cao độ là vật cản.
- Nhãn cao độ đặt sát bản đáy/bản nắp (`max(roofS.Max, baseS.Max) + 4 mm`) trong khi ống ra luôn thò ra 600 mm về bên phải.

## Cách sửa

- `Annot.DimTextRect` / `Annot.TextRect`: hộp chữ của kích thước (đúng chỗ renderer đặt chữ) và của PText.
- `ViewBuilder.PlaceCalloutColumns` lấy mọi hộp chữ kích thước + PText đã có trong hình làm vật cản:
  đẩy cột chữ ra ngoài vật cản cùng phía, và truyền vật cản cho `CalloutColumn` để leader lách xuống (`GeomUtil.SegmentIntersectsRect`).
- `SectionBuilder.BuildLevels` đặt cao độ ngoài `RightExtent` (kể cả đoạn ống thò ra).
- `check` có mục `Overlap`: với mọi mẫu, chữ của leader không đè số kích thước / chữ khác, leader không cắt qua số kích thước.

## Quy tắc

- Trong builder, luôn vẽ kích thước và cao độ **trước** rồi mới gọi `PlaceCalloutColumns`; không gọi thẳng `Annot.CalloutColumn` mà bỏ qua vật cản.
- Không tính vị trí cột ghi chú bằng hằng số cộng vào biên hình nếu phía đó có kích thước; để `PlaceCalloutColumns` đẩy ra.
- Chữ rời mới (PText) phải nằm trong model trước khi xếp ghi chú để được coi là vật cản.
- Thêm hình chiếu / chuỗi kích thước mới thì chạy `check` (mục `Overlap`) và xem SVG trước khi commit; không được bỏ mục `Overlap`.
