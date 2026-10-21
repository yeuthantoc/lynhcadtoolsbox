# Leader ghi chú bị xiên nhẹ thay vì nằm ngang

- **Ngày:** 2026-10-21
- **Vùng code:** Plugin (`AcadRenderer.AddMLeader`)
- **Trạng thái:** đã sửa
- **Môi trường:** AutoCAD 2023, bản vẽ do `LCT_MANHOLE` sinh ra (tỉ lệ 1:20), ghi chú vẽ bằng MLeader

## Triệu chứng

Trên mặt cắt A-A (và mặt bằng), các leader màu vàng của ghi chú (`W1 N12-200 EF VERT`, `P3 DN375 OUTLET`,
`T3/T4/T5 2-N16 TRIMMERS`...) hơi xiên so với phương ngang: đầu mũi tên đúng cao độ điểm đặt nhưng chữ nằm
thấp hơn vài mm giấy, đoạn landing nối vào chữ nghiêng vài độ thay vì 0°. Ghi chú hai dòng lệch nhiều hơn một dòng.
Trong SVG preview thì leader vẫn ngang (Core tính đúng), chỉ bản AutoCAD bị lệch.

## Nguyên nhân

`Annot.LayoutColumn` đặt khuỷu leader (đỉnh cuối của `PLeader.Points`) đúng cao độ tâm chữ `TextPosition.Y`.
`AcadRenderer.AddMLeader` gán `ml.TextLocation = TextPosition` và tưởng MText được neo giữa-trái như `ToMText` đặt
(`Attachment = MiddleLeft`). Nhưng **MLeader bỏ qua Attachment của MText**: `TextLocation` luôn là góc **trên**-trái
(chữ bên phải leader) hoặc trên-phải (chữ bên trái leader). Chữ vì thế tụt xuống nửa khối chữ; điểm bám `AttachmentMiddle`
nằm thấp hơn khuỷu, AutoCAD kéo đỉnh cuối của leader xuống theo chữ và đoạn landing thành xiên.

## Cách sửa

`AcadRenderer.AddMLeader`: đặt `TextLocation.Y = TextPosition.Y + H/2`, với `H` = `MText.ActualHeight`
(chiều cao khối chữ AutoCAD tính); không lấy được thì ước lượng `h + (số dòng - 1) * h * PText.LineSpacing`
(hàm `TextBlockHeight`). Tâm chữ trùng cao độ khuỷu nên AutoCAD không dời đỉnh cuối, landing nằm ngang 0°.

## Quy tắc

- Với MLeader, `TextLocation` là **góc trên** của khối chữ, không phải điểm neo của MText; muốn neo giữa thì phải tự cộng
  nửa chiều cao khối chữ. Không gán thẳng `TextPosition` của `PLeader` vào `TextLocation`.
- Mọi đỉnh của `PLeader.Points` phía chữ phải cùng cao độ với điểm bám của chữ; nếu AutoCAD "tự" dời đỉnh cuối là do
  điểm bám sai, sửa điểm bám chứ không thêm đỉnh để bù.
- Khi đổi cách neo chữ trong renderer, kiểm tra trên AutoCAD cả ghi chú một dòng lẫn hai dòng ở hai phía (trái/phải).
