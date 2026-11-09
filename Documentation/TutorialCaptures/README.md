# Ảnh hướng dẫn thực tế

Chụp qua native UI của Unity 6000.3.16f1 / URP 17.3 / PC Metal. B01: 300 khối; B02: 600 khối.

`build_media.py` cắt ảnh gốc, khoanh vùng tương tác màu vàng và ghép GIF. GIF là chuỗi ảnh trạng thái thực, có giữ hình 1,6 giây để người mới theo kịp; một số thao tác chọn nút dùng khung nhấp nháy trên ảnh thật. Không phải video quay liên tục; không tạo lại giao diện Unity bằng AI.

Chạy: `python3 Documentation/TutorialCaptures/build_media.py` từ bất kỳ thư mục nào.

Đầu ra: `Assets/OptimizationLab/TutorialMedia/Editor/{b01,b02}`. Mỗi clip có các frame PNG và GIF cùng tên; `media_manifest.json` ghi số frame. Unity phát PNG flipbook; nút Phóng to cho xem ảnh lớn và mở GIF riêng. Ảnh chỉ nằm trong Editor, không đưa vào player.

B02: nhóm opaque có 600 Draw Mesh khi Rời và 6 Static Batch khi Ghép ở ảnh mẫu. B01: nhiều Draw Mesh khi Tắt, nhóm SRP Batch khi Bật; số dòng event không phải tổng draw call trong batch. Số liệu là minh hoạ, không phải kết quả benchmark thiết bị.

## B02 · Áp dụng vào project thật

Ảnh `b02-out-*.png` chụp bằng `GUIView.GrabPixels` của chính Unity (2x, cửa sổ thật; `screencapture` của macOS không có quyền ghi màn hình). Ảnh lấy từ GrabPixels bị lật dọc, đã lật lại trước khi lưu.

| Clip | Nguồn | Nội dung |
|---|---|---|
| `out-static` | `b02-out-static-off/on.png` | Inspector của Cube: ô Static trống → "—" sau khi bật Batching Static |
| `out-player` | `b02-out-player.png` | Project Settings ▸ Player: ô tìm "static" → tab Android → dòng Static Batching |
| `out-profiler` | `b02-out-profiler-a/b.png` | Profiler ▸ Rendering, 600 khối: Rời (Static 0) → Ghép (Static Batching 1.2k draw / 12 batch) |

Thẻ Frame Debugger dùng lại clip `opaque-b`. Menu Static ▾ là menu native nên không chụp được; thẻ chỉ chỗ bấm bằng khung vàng và chữ.

## B03 · GPU Instancing

Toàn bộ ảnh B03 chụp bằng `GUIView.GrabPixels` (2x, 300 khối, PC/Metal), không có viền cửa sổ macOS, nên vùng cắt cửa sổ Frame Debugger cao hơn b01/b02 56 px; khung vàng vẫn theo toạ độ vùng cắt. Ảnh `debugger-off.png` / `debugger-disabled.png` dùng chung.

| Ảnh | Nội dung |
|---|---|
| `b03-main-a/b.png` | Game view, HUD: Thường 609 lần vẽ → Instancing 13 |
| `b03-a-tree/open/draws.png` | Frame Debugger, Thường: `DrawOpaqueObjects` 300 → `RenderLoop.Draw` → `Draw Mesh Obj_*` |
| `b03-b-tree/open/draws.png` | Frame Debugger, Instancing: 2 dòng `Draw Mesh (instanced)` |
| `b03-b-detail.png` | Chi tiết một dòng instanced (Mesh: Cube). Không dùng trong clip; Unity 6 không hiện số instance ở đây |
| `b03-out-material-closed/open/on.png` | Inspector material: Advanced Options gập → mở (ô trống) → bản Instanced đã tick |
| `b03-out-profiler-a/b.png` | Profiler ▸ Rendering: (Instancing) 0 → Batched Draw Calls 600, Batches 4 |

Lúc chụp material, trạng thái gập/mở mục của URP/Lit (`EditorPrefs` `CoreRP:Material:UI_State:Universal Render Pipeline/Lit`) được đổi tạm rồi trả lại giá trị cũ.
