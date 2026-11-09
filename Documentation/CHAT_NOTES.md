# Lưu ý triển khai Optimization Lab

Tổng hợp yêu cầu và quyết định trong cuộc trao đổi. Dùng làm hướng dẫn khi tiếp tục xây dựng bài học.

## 1. Môi trường dự án

- Thư mục dự án: `/Users/gigan/projects/learn-3d/Learn 3D`.
- Unity thực tế: **6000.3.16f1**, URP **17.3**. Spec ban đầu ghi phiên bản khác; code, ảnh và hướng dẫn phải theo bản đang dùng.
- Unity MCP đã kết nối Learn 3D qua port **7890** ở thời điểm kiểm tra. Luôn khám phá và chọn đúng instance trước khi thao tác; không mặc định port sẽ không đổi.
- Tài liệu gốc: `Docs/Unity_3D_Render_Optimization_Benchmark_Spec.md`.

## 2. Đối tượng học và cách trình bày

Người học là **fresher**, có thể chưa biết thuật ngữ render hoặc vị trí các cửa sổ trong Unity.

- Mở đầu mỗi bài bằng một câu trả lời rõ: **Bài này dạy gì?**
- Mỗi bước chỉ yêu cầu **một thao tác** và một câu hướng dẫn ngắn.
- Giải thích thuật ngữ ngay khi xuất hiện: frame = một ảnh game; draw = lệnh vẽ; opaque = vật đặc, không trong suốt.
- Học như chơi game: tiến độ, chỉ dẫn chỗ bấm, xác nhận quan sát và câu hỏi ngắn ở cuối.
- Không bắt người mới đọc một màn hình đầy chữ, đếm hàng trăm draw hoặc điền biểu mẫu dài.
- Giải thích thêm đặt sau **Vì sao?**; thiết lập và công cụ nâng cao mặc định thu gọn.
- Không chỉ mô phỏng trong game: phải hướng dẫn cách áp dụng và kiểm tra thật trong Unity Editor.
- Đã có yêu cầu bỏ nút **Compare riêng** trên HUD. Tránh tự thêm lại giao diện so sánh phức tạp.

## 3. Bắt buộc dùng ảnh và GIF để hướng dẫn

**Các bước thao tác Editor cần có ảnh hoặc GIF minh hoạ rõ vị trí bấm. Ưu tiên GIF khi thao tác tạo thay đổi trạng thái hoặc mở nhiều cấp.**

### Nội dung hình minh hoạ

- Chụp từ Unity thật của dự án; không dùng hình AI dựng lại giao diện thay cho bằng chứng thực tế.
- Cắt vào vùng liên quan, giữ đủ ngữ cảnh để người mới tìm được vị trí trên màn hình của họ.
- Khoanh vàng hoặc dùng con trỏ chỉ đúng nút, tam giác mở nhóm, tên dòng hay thông tin cần xem.
- GIF nên thể hiện **trước → thao tác → sau**, ví dụ:
  - Chọn Rời / Ghép, Tắt / Bật.
  - Mở Frame Debugger và bấm Enable.
  - Mở `DrawOpaqueObjects` rồi mở nhóm con.
  - Chọn `Draw Mesh`, `Static Batch` hoặc `SRP Batch`.
  - Bấm Disable trước khi đổi chế độ, sau đó Enable lại.
- Giữ mỗi khung đủ lâu để người mới theo kịp; bộ mẫu hiện tại dùng khoảng **1,6 giây/khung**.
- Có nút **Ảnh / GIF**, **Phóng to**, xem lại và tạm dừng ảnh động.
- Không dùng chữ nhỏ hoặc số liệu không liên quan làm nội dung chính của hình.
- Ghi rõ đây là ảnh mẫu: phiên bản Unity, renderer/nền tảng và số khối. Số trên máy người học có thể khác.
- Chuỗi ảnh trạng thái và hiệu ứng khoanh nhấp nháy là GIF minh hoạ; không gọi đó là video quay liên tục.
- Kiểm file GIF thực sự có nhiều frame, đường dẫn ảnh đúng và ảnh hiển thị được trong Editor.

### Vị trí media hiện có

- Media dùng trong Editor: `Assets/OptimizationLab/TutorialMedia/Editor/b01/`, `b02/` và `b03/`.
- Bản chụp gốc: `Documentation/TutorialCaptures/`.
- Script tạo ảnh khoanh vùng và GIF: `Documentation/TutorialCaptures/build_media.py`.
- Danh sách số frame: `Assets/OptimizationLab/TutorialMedia/Editor/media_manifest.json`.
- Bộ đã kiểm tra gồm **35 GIF và 80 frame PNG** (B03 thêm 12 clip) (thêm 3 clip `out-*` cho B02 "Áp dụng vào project thật": Inspector Batching Static, Player Settings Static Batching, Profiler Rendering).
- Đặt `Application.runInBackground` trong Play rồi thoát sẽ ghi vào Player Settings ▸ Run In Background; nếu phải bật để Editor chạy khi mất focus, kiểm lại setting sau khi thoát (project để **bật**).
- Khi `screencapture` bị chặn quyền, chụp cửa sổ Unity bằng `GUIView.GrabPixels` qua Unity MCP (ảnh ra bị lật dọc, cần lật lại).
- Unity phát chuỗi PNG tương ứng GIF trong cửa sổ tutorial; có thể mở GIF riêng. Media nằm trong thư mục Editor để không đưa vào player.

## 4. Luồng thực hành Frame Debugger

Áp dụng cho B01, B02 và B03, hướng dẫn từng bước bằng hình/GIF:

1. Mở đúng bài, vào Play, giữ nguyên số khối, camera và chất lượng.
2. Chọn A: **Tắt** ở B01, **Rời** ở B02, **Thường** ở B03.
3. Mở Frame Debugger, chọn target Editor và bấm **Enable**.
4. Tìm nhóm vẽ khối, thường là `DrawOpaqueObjects`.
5. Mở nhóm con, chọn một mục render và xem tên/chi tiết.
6. Bấm **Disable** để thoát frame đang soi.
7. Chọn B: **Bật** ở B01, **Ghép** ở B02, **Instancing** ở B03.
8. Chờ frame mới rồi **Enable lại**.
9. Kiểm cùng nhóm render và đối chiếu thay đổi.
10. Trả lời câu hỏi ngắn để kiểm tra hiểu bài.
11. **Disable debugger trước khi đọc CPU/FPS hoặc bấm Đo.** Nếu Editor còn pause, cần cho game chạy lại.

Không đánh dấu đã quan sát chỉ vì cửa sổ debugger được mở. Các bước Enable/Disable cần kiểm trạng thái thật. Tên nhóm có thể khác theo renderer và phiên bản Unity.

## 5. Nội dung các bài đã triển khai

| Bài | Ý chính | Điều cần quan sát |
|---|---|---|
| B00 — Baseline | Số renderer được vẽ làm tăng công việc render | Thay số khối, ẩn/hiện renderer, xem số lần vẽ |
| B01 — SRP Batcher | Giảm công CPU chuẩn bị mỗi draw; không đồng nghĩa giảm draw call | Draw Mesh khi tắt, SRP Batch khi bật; xem thêm CPU khi đã tắt debugger |
| B02 — Static Batching | Ghép geometry vật đứng yên; có thể giảm chi phí CPU nhưng tăng bộ nhớ mesh | Draw Mesh khi Rời, Static Batch khi Ghép; kiểm mesh MB và cảnh vẫn giữ nguyên |
| B03 — GPU Instancing | Cùng mesh + cùng material vẽ nhiều bản sao trong một lệnh; khác material thì không gộp | 300 `Draw Mesh` khi Thường, 2 `Draw Mesh (instanced)` khi Instancing; Profiler dòng (Instancing) |

### Những điểm không được dạy sai

- **Ẩn renderer không làm vật hoàn toàn miễn phí:** transform, script, physics và bộ nhớ vẫn có thể tốn chi phí.
- **Số dòng event trong Frame Debugger không phải lúc nào cũng là số draw call.** Một SRP Batch có thể chứa nhiều draw.
- SRP Batcher (B01) khác GPU Instancing (B03). Trong URP, SRP Batcher bật thì MeshRenderer dùng shader tương thích đi SRP Batcher, instancing bị bỏ qua; B03 tắt SRP/dynamic batching ở mọi variant để cô lập, trả lại khi thoát.
- B03 dùng instancing **tự động** (MeshRenderer + Enable GPU Instancing), không phải `Graphics.RenderMeshInstanced` như spec gợi ý cho variant B; variant C của B03 là "mỗi khối một material" (giáo dục). Ở 300 khối, "khác mesh" (khối + cầu) cho cùng số instanced batch với một mesh vì mỗi lệnh có giới hạn số bản sao, nên không dùng để dạy.
- Static Batching không đảm bảo chỉ còn một draw call; material, pass và culling vẫn có thể chia batch.
- Không di chuyển từng vật sau khi static combine. B02 có variant `StaticObjectsMoved` luôn **INVALID**, dùng để dạy giới hạn này.
- Chỉ so sánh A/B khi giữ các yếu tố khác tương đương. B02 cô lập static batching bằng cách tắt SRP/dynamic batching ở cả A/B; phải trả setting lại sau khi kết thúc.
- `mesh (MB)` của B02 là bộ nhớ runtime của các mesh duy nhất thuộc fixture và mesh nguồn; không phải tổng RAM hay GPU VRAM.

## 6. Asset URP và cách tìm

Hai asset trong dự án:

- `Assets/Settings/PC_RPAsset.asset`.
- `Assets/Settings/Mobile_RPAsset.asset`.

Cách tìm cho người mới:

1. Mở cửa sổ **Project** và vào `Assets → Settings`.
2. Hoặc tìm bằng `t:UniversalRenderPipelineAsset`.
3. Chọn asset để xem Inspector.
4. Mở **Rendering → ⋮ → Show Additional Properties → SRP Batcher**.
5. Kiểm asset gán cho quality level trong **Project Settings → Quality → Render Pipeline Asset**.

Không mặc định bật một asset là đủ cho mọi nền tảng. Hướng dẫn nên có nút chọn/ping asset và ảnh/GIF chỉ rõ vị trí của setting.

## 7. Lưu ý khi đo và báo cáo

- Giữ nguyên camera, số vật, resolution, chất lượng và seed giữa các variant cần so sánh.
- Giữ Unity ở phía trước khi đo; mất focus, pause hoặc đổi resolution có thể làm run INVALID.
- FPS khoá 60 có thể che khác biệt. Xem thêm CPU/GPU ms, percentile và độ nhiễu.
- Không đánh giá tốc độ trong lúc Frame Debugger đang Enable.
- HUD tự ẩn trong cửa sổ đo để giảm nhiễu; cần có dấu hiệu đang đo và cách hiện lại.
- Compile sạch, validation và tests pass chỉ xác nhận cơ chế; không chứng minh hiệu năng thiết bị.
- Đã có smoke run xuất CSV/JSON nhưng bị INVALID do điều kiện Editor. **Chưa có số đo hiệu năng thiết bị hợp lệ để kết luận.**
- Số mẫu trên ảnh/GIF dùng để giải thích cơ chế, không dùng làm cam kết cải thiện hiệu năng.

## 8. Khi tiếp tục phát triển

- Giữ phong cách một bước/một thao tác cho các bài tiếp theo.
- Bổ sung ảnh/GIF ngay khi thêm thao tác mới, không để phần hướng dẫn chỉ còn văn bản.
- Kiểm cả code, trạng thái thật trong Unity và bố cục tutorial sau khi sửa.
- Các đường dẫn, trạng thái và số liệu trong ghi chú là mốc của cuộc trao đổi; kiểm tra lại dự án trước khi thao tác.
