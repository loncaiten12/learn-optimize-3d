# 3D Rendering Optimization — Benchmark Lab

Lab đo tác động thật của từng kỹ thuật tối ưu render 3D cho mobile, theo `Docs/Unity_3D_Render_Optimization_Benchmark_Spec.md`.

> **Trạng thái:** đã có framework, **B00 Baseline**, **B01 SRP Batcher**, **B02 Static Batching** và **B03 GPU Instancing**. Các scene B04–B11 và X01 chưa làm.
> **Chưa có số đo thiết bị.** Mọi bảng kết quả hiện ghi `Not measured`.

## Môi trường

| Mục | Giá trị |
|---|---|
| Unity | 6000.3.16f1 (spec viết cho 2022.3 LTS — ghi chênh lệch khi so số) |
| URP | 17.3.0 (spec: 14.x) |
| Input | Input System mới (EventSystem dùng `InputSystemUIInputModule`) |
| Quality | `Mobile` (Android) / `PC` (Editor) — runner ghi preset thực tế vào mỗi run |

## Bắt đầu nhanh (Editor)

1. `Tools ▸ Optimization Lab ▸ Tutorial`: cửa sổ nhiệm vụ; chọn bài B00, B01 hoặc B02 ở thanh trên.
2. `Tools ▸ Optimization Lab ▸ Generate All Scenes`: dựng `Assets/OptimizationLab/Scenes/B00_Baseline.unity` và thêm vào Build Settings.
3. `Tools ▸ Optimization Lab ▸ Validate Scenes`: ghi bảng kiểm tra vào `Results/validation_*.md`.
4. Mở B00, đặt Game view về độ phân giải dọc cố định rồi vào Play. Lần đầu sẽ có **tutorial trong game**: bong bóng thoại và khung vàng chỉ vào nút cần bấm, có 8 bước. Muốn xem lại thì bấm **Hướng dẫn**.

**B00 dạy gì:** mỗi vật trên màn hình tốn ít nhất một lần vẽ (draw call). Càng nhiều lần vẽ thì CPU càng bận. Vật bị ẩn thì không tốn gì: cái tốn là **vẽ**, không phải việc vật tồn tại.

HUD có các phần:

| Vị trí | Nội dung |
|---|---|
| Thanh trên | Tên bài và chip tốc độ: FPS + ms, màu xanh/vàng/đỏ |
| Hàng 1 | Ẩn / Hiện / Xoay |
| Hàng 2 | Số khối: 100 / 300 / 1K / 3K |
| Hàng 3 | Số liệu live: **khối** và **lần vẽ** (draw calls) |
| Hàng cuối | **Đo** (đo số khối 100→3K, ghi CSV), `?` (3 mẹo), Hướng dẫn, Ẩn UI |

Đo xong sẽ hiện thẻ kết quả; bấm **Chi tiết** để xem số đầy đủ. Phép so A/B kiểu ABBA (`RunAbba`) vẫn còn trong `BenchmarkRunner`, nhưng không có nút trên HUD.

Số đo trong Editor (renderer PC): 300 khối ≈ 611 lần vẽ, 3000 khối ≈ 6011, khi ẩn còn ≈ 11. Mỗi khối tốn 2 lần vẽ vì renderer có thêm một lượt vẽ độ sâu. Đây là số tham khảo để hiểu bài, chưa phải số đo trên thiết bị.

Khi bấm ĐO, HUD **tự ẩn trong lúc đo** và chỉ còn viên "Đang đo" (variant, run x/y, thanh tiến độ), để chi phí vẽ UI không lẫn vào frame time. HUD hiện lại khi phiên đo xong. Bấm vào viên "Đang đo" để hiện HUD giữa chừng; run đó sẽ được ghi chú. Muốn tắt hẳn cơ chế này, bỏ tick `Hide Hud While Measuring` trong Full Config / Quick Config của `BenchmarkRunner`.

Trong Editor runner mặc định ở chế độ **QUICK** (settle 1 s, warm-up 3 s, đo 8 s, 2 lần/variant). Chế độ này chỉ để xác minh cơ chế. Số dùng để kết luận phải đo ở **FULL** (settle 2 s, warm-up 15 s, đo 60 s, cooldown 15 s, 3 lần/variant) trên thiết bị thật.

Chạy headless (chỉ dựng scene, **không** dùng để benchmark GPU):

```
Unity -batchmode -nographics -quit -projectPath <PROJECT_PATH> \
  -executeMethod OptimizationLab.Editor.OptimizationLabSceneGenerator.GenerateAll -logFile <LOG_FILE>
```

## B00 — Baseline / object count scaling

| Variant | Nội dung |
|---|---|
| A `ObjectsDisabled` | Spawn N object, `MeshRenderer.enabled = false` |
| B `ObjectsVisible` | N object hiển thị, đứng yên |
| C `VisibleAnimated` | B + xoay mỗi frame. Đây là variant **giáo dục** vì đổi thêm chi phí script/transform |

Giữ cố định giữa các variant: cube mesh, **một** shared material URP/Lit (không instancing), shadow cast/receive off, light probe off. Camera orthographic được khung theo lưới 3000 object cho mọi count nên kích thước pixel của mỗi object không đổi. Seed mặc định là 1337.

- **Run A/B (ABBA):** thứ tự A B B A A B…, mỗi variant chạy đủ `repetitions` lần.
- **Count Sweep:** variant B ở 100 → 300 → 1000 → 3000; lượt sau đảo chiều để giảm drift nhiệt.

## B01 — SRP Batcher

**B01 dạy gì:** SRP Batcher **không giảm số lần vẽ**, nó làm **mỗi lần vẽ rẻ hơn** cho CPU, vì dữ liệu material được giữ sẵn trên GPU. Điều kiện để được gộp:

- cùng shader variant;
- shader hỗ trợ SRP Batcher;
- không dùng MaterialPropertyBlock.

| Variant | Nội dung |
|---|---|
| A `Tắt` | `GraphicsSettings.useScriptableRenderPipelineBatching = false`, 8 material màu chung một shader variant |
| B `Bật` | SRP Batcher bật, cùng 8 material |
| C `Lộn xộn` | SRP Batcher bật, 8 material mỗi cái một tổ hợp keyword URP/Lit nên ra 8 shader variant. Đây là variant **giáo dục** vì đổi thêm yếu tố shader |

### Bật SRP Batcher cho project thật (ngoài game)

Cửa sổ `Tools ▸ Optimization Lab ▸ Tutorial` → tab **B01** có mục "Ngoài game", gồm 5 nhiệm vụ có nút:

1. **Mở URP Asset đang dùng.** Project có 2 asset: `PC_RPAsset` (Editor/PC) và `Mobile_RPAsset` (Android).
2. **Hiện ô SRP Batcher.** Trong Inspector, bấm dấu **⋮** bên phải mục **Rendering** → **Show Additional Properties**. Ô **SRP Batcher** hiện ra ngay dưới.
3. **Tick SRP Batcher ở mọi URP Asset.** Mỗi quality level dùng asset riêng, nên bật một cái là chưa đủ. Nút "Bật hết" bật tất cả, có Undo.
4. **Kiểm shader.** Chọn shader `Universal Render Pipeline/Lit`; Inspector phải ghi "SRP Batcher: compatible". Shader tự viết cần khai báo property trong `CBUFFER_START(UnityPerMaterial)` thì mới compatible.
5. **Kiểm khi chạy.** Mở Frame Debugger trong Play mode, vào `DrawOpaqueObjects`, phải thấy các dòng **SRP Batch**.

Bật bằng code: `GraphicsSettings.useScriptableRenderPipelineBatching = true;`

Fixture: 1000 khối (có thể chọn 300 hoặc 3K), material được gán ngẫu nhiên theo seed, shadow tắt. Runner chỉ tắt SRP Batcher tạm cho variant A, rồi trả lại giá trị cũ khi teardown hoặc thoát Play.

Số live trong Editor (Mac, 1000 khối, renderer PC):

| | Lần vẽ | SetPass | CPU vẽ (render thread) | Frame |
|---|---:|---:|---:|---:|
| A Tắt | 2.009 | 30 | ~23 ms | ~40 ms |
| B Bật | 2.009 | 16 | ~3 ms | ~16,7 ms (cap 60) |
| C Lộn xộn | 2.009 | 23 | ~3 ms | ~16,7 ms |

Đây là số tham khảo để hiểu bài, chưa phải số đo trên thiết bị. Ở B01, nút **Đo** chạy phép so **Tắt vs Bật** theo thứ tự ABBA, rồi hiện thẻ kết quả.

## Quy trình mỗi run

```
Teardown → Setup(variant, seed) → Settle (bỏ qua) → Warm-up → Screenshot (ngoài cửa sổ đo)
→ Validate → Measure (không alloc / instantiate / log) → Validate → Teardown → Cooldown
```

## Metrics và nguồn

| Trường | Nguồn | Khi không đọc được |
|---|---|---|
| `frame_ms_*` | delta `Time.realtimeSinceStartupAsDouble` mỗi frame | — |
| `cpu_main_ms`, `cpu_render_ms`, `gpu_ms` | `FrameTimingManager` (Player Settings ▸ Frame Timing Stats, generator tự bật) | `unsupported` |
| `cpu_main_ms` (fallback) | `ProfilerRecorder` "Main Thread" | `unsupported` |
| `batches`, `setpass_calls`, `draw_calls`, `triangles`, `vertices` | `ProfilerRecorder` category Render | `unsupported`. Có thể thiếu trong non-Development player |
| `allocated_mb`, `peak_allocated_mb`, `mono_used_mb` | `Profiler.GetTotalAllocatedMemoryLong` / `GetMonoUsedSizeLong` | — |
| `graphics_memory_mb` | `ProfilerRecorder` "Gfx Used Memory" | `unsupported` |
| `visible_renderers` | Tự đếm: renderer đang bật và có AABB nằm trong frustum camera | — |
| `thermal_state` | Chưa đọc (Android thermal API chưa nối) | `unsupported` |

Giá trị không đọc được ghi `unsupported` (CSV) hoặc `null` (JSON), **không bao giờ ghi 0**. Cột `*_source` cho biết số lấy từ đâu.

## Kết quả

- `Application.persistentDataPath/OptimizationLabResults/`: `{scenario}_{kind}_{session}.csv` + `.json`, `all_runs.csv`, `screenshots/`.
- Trong Editor ghi thêm vào `Results/` ở gốc project.
- Android: `adb pull /sdcard/Android/data/<package>/files/OptimizationLabResults .`

Cách đọc kết luận A/B: `improvement_% = (median_A − median_B) / median_A × 100`. Noise = 2 × stdev các median theo run.

- Chênh lệch ≤ noise → `inconclusive`.
- Có run INVALID → `invalid`, không kết luận.
- Cả A và B chạm cap FPS → so CPU/GPU ms và P95, hoặc chạy lại với **Cap OFF**.

## Validation gates (đánh dấu INVALID)

Một run bị đánh dấu INVALID khi:

- visible renderers khác số kỳ vọng;
- resolution, render scale, pipeline asset, quality preset hoặc graphics API đổi giữa phiên;
- có renderer không dùng shared material hoặc sai trạng thái enabled;
- ít hơn 30 sample;
- app bị pause, hoặc hơn 2 % số frame app không active trong lúc đo (Editor tự hạ tốc độ frame khi không focus);
- phiên bị huỷ.

Các điều sau chỉ thêm ghi chú: đang chạy trong Editor, GPU time unsupported, frame chạm cap, variant giáo dục.

## Android runbook

1. Build Profile Android, cố định Graphics API (Vulkan **hoặc** GLES3), quality `Mobile`.
2. Build **non-Development** để có số chính thức. Nếu dùng Development build thì ghi rõ trong báo cáo.
3. Máy nguội, pin > 50 %, không sạc, đóng app nền, khoá xoay màn hình, độ sáng cố định.
4. Trên HUD chọn Mode **FULL**, bấm Run A/B và Count Sweep. Chạy thêm một lượt với Cap OFF.
5. Lấy file kết quả, điền `Documentation/REPORT_TEMPLATE.md`.
6. Phiên chẩn đoán (Profiler attach, Development build) phải tách riêng khỏi phiên đo.

## Hạn chế hiện tại

- Chưa có `BenchmarkMenu.unity`, B04–B11, X01, PlayMode tests, menu build APK.
- Render counters trong Editor có thể gồm cả Scene view. Hãy đóng Scene view khi đo.
- Thermal state chưa đọc được, luôn ghi `unsupported`.
- Trong Editor, giữ cửa sổ Unity ở trạng thái focus suốt phiên đo; nếu mất focus, run sẽ bị đánh INVALID.


## B02 — Ghép vật đứng yên (Static Batching)

**Bài học:** ghép geometry của vật đứng yên có thể giảm chi phí CPU, nhưng tạo thêm dữ liệu mesh. Không hứa chỉ còn một draw call: material, pass và culling vẫn chia lần vẽ.

Mở `Assets/OptimizationLab/Scenes/B02_StaticBatching.unity` hoặc chọn B02 trong `Tools ▸ Optimization Lab ▸ Tutorial`.

- **Rời:** không combine.
- **Ghép:** `StaticBatchingUtility.Combine(root)` sau spawn, trước warmup/đo. Kiểm mọi renderer có `isPartOfStaticBatch`.
- **Di chuyển:** combine rồi đổi transform một vật. Luôn INVALID, có cảnh báo đỏ, không dùng để so hiệu năng.

Cùng 600 / 1000 / 1500 khối, 4 shared materials, cùng seed, camera cố định. SRP Batcher và dynamic batching tắt ở cả A/B để cô lập phép thử; GPU instancing tắt trên material. Setting được trả lại khi teardown/thoát Play.

HUD: lần vẽ · CPU vẽ (ms) · mesh (MB). Tutorial 9 bước dùng số live, nút Đo chạy ABBA Rời/Ghép. Mesh MB cộng bộ nhớ runtime của các mesh duy nhất trong fixture và mesh nguồn (Profiler.GetRuntimeMemorySizeLong); không phải tổng RAM hoặc phép đo GPU VRAM. Giá trị chụp sau setup được ghi vào notes CSV/JSON dưới khoá `fixture_mesh_memory_mb`; tổng allocated và graphics memory vẫn theo collector chung.

Kiểm cơ chế trong Editor: 1000/1000 renderer đã combine, visible count giữ 1000; mesh Rời ~0,004 MB, Ghép ~2,79 MB. Các số này không đại diện hiệu năng thiết bị. Memory/CPU phụ thuộc platform, renderer và phiên Unity.

**Ngoài game:** cửa sổ Tutorial → B02 → **Áp dụng vào project thật**. 4 thẻ, mỗi thẻ một thao tác, có ảnh/GIF chụp từ Unity thật và chỉ cho qua khi trạng thái thật đúng:

1. Chọn vật không di chuyển, Inspector → Static ▾ → **Batching Static** (chỉ đánh dấu vật thật sự tĩnh). Nút "Đã tick xong" chỉ bật khi vật đang chọn có flag `BatchingStatic`.
2. Project Settings → Player → gõ "static" → tab Android → Other Settings → **Static Batching**. Thẻ đọc `PlayerSettings.GetStaticBatchingForPlatform` cho Android và nền tảng đang build.
3. Play → Frame Debugger → Enable → `DrawOpaqueObjects`: thấy **Static Batch**. Nút xác nhận chỉ bật khi đang Play và debugger đang Enable. Với scene sinh bằng code như lab, dùng `StaticBatchingUtility.Combine(root)`; mesh nguồn cần Read/Write.
4. Tắt Frame Debugger → Profiler → module **Rendering** → dòng **(Static Batching)**: Batched Draw Calls > 0. Ảnh mẫu (600 khối, PC/Metal): Rời có Batches 1209, Static 0; Ghép có Batches 21, trong đó Static Batching gộp 1.2k draw thành 12 batch. Chỉ là minh hoạ cơ chế. Không di chuyển từng con sau combine (có thể di chuyển root chung).

Tài liệu Unity: https://docs.unity.cn/6000.1/Documentation/ScriptReference/StaticBatchingUtility.Combine.html


## B03 — Vẽ nhiều bản sao (GPU Instancing)

**B03 dạy gì:** nhiều vật **cùng mesh + cùng material** có thể vẽ trong một lệnh: gửi một mesh kèm danh sách vị trí, GPU vẽ nhiều bản sao. Khác mesh hoặc khác material thì không vẽ chung được.

Mở `Assets/OptimizationLab/Scenes/B03_GPUInstancing.unity` hoặc chọn B03 trong `Tools ▸ Optimization Lab ▸ Tutorial`.

| Variant | Nội dung |
|---|---|
| A `Thường` (`InstancingOff`) | MeshRenderer, material `OL_B03_Plain`, Enable GPU Instancing **tắt** |
| B `Instancing` (`InstancingOn`) | Cùng cảnh, material `OL_B03_Instanced`: giống hệt A, chỉ khác Enable GPU Instancing **bật** (instancing tự động qua MeshRenderer) |
| C `Khác material` (`InstancingUniqueMaterials`) | Như B nhưng mỗi khối một bản sao material (cùng màu, vẫn bật instancing). Variant **giáo dục**: chứng minh khác material thì không gộp được, không dùng để so tốc độ |

Cùng 300 / 1000 / 3000 khối, cùng seed, một màu, camera khung theo 3000 khối. **SRP Batcher và dynamic batching tắt ở mọi variant**: trong URP, khi SRP Batcher bật, MeshRenderer dùng shader tương thích (như URP/Lit) đi đường SRP Batcher và instancing bị bỏ qua. Setting được trả lại khi teardown/thoát Play.

HUD: lần vẽ · batches · CPU vẽ (ms). Nút **Đo** chạy ABBA Thường/Instancing.

Kiểm cơ chế trong Editor (Mac, PC/Metal, chỉ để hiểu bài, **không** phải số đo thiết bị):

| | Khối | Batches | Instanced batches (gộp bao nhiêu draw) | CPU vẽ |
|---|---:|---:|---:|---:|
| A Thường | 300 | 609 | 0 | ~2,9 ms |
| B Instancing | 300 | 13 | 4 (600 draw) | ~0,9 ms |
| C Khác material | 300 | 609 | 600 (mỗi lệnh 1 draw) | — |
| A Thường | 1000 | 2010 | 0 | ~15 ms |
| B Instancing | 1000 | 17 | 8 (2000 draw) | ~1,2 ms |

Mỗi khối tốn 2 draw vì renderer có thêm lượt DrawDepthNormalPrepass. Một lệnh instanced có giới hạn số bản sao, nên 300 khối vẫn chia thành 2 lệnh mỗi lượt.

Frame Debugger (ảnh mẫu 300 khối): Thường có 300 dòng `Draw Mesh Obj_*` trong `RenderLoop.Draw`; Instancing còn 2 dòng `Draw Mesh (instanced)`.

**Ngoài game:** cửa sổ Tutorial → B03 → **Áp dụng vào project thật**, 4 thẻ có ảnh/GIF thật:

1. Chọn material → Inspector → **Advanced Options** → tick **Enable GPU Instancing**. Nút chỉ bật khi material đang chọn đã bật instancing (không nhận `OL_B03_Plain`, bản đối chứng A của lab).
2. Kiểm SRP Batcher của từng URP Asset: nếu đang bật, MeshRenderer với URP/Lit sẽ đi SRP Batcher thay vì instancing. Instancing có tác dụng khi SRP Batcher tắt, shader không tương thích SRP Batcher, hoặc vẽ bằng code.
3. Play → Frame Debugger → Enable → `DrawOpaqueObjects`: thấy `Draw Mesh (instanced)`.
4. Tắt Frame Debugger → Profiler → Rendering → dòng **(Instancing)**: Batched Draw Calls > 0.

Vẽ bằng code: `Graphics.RenderMeshInstanced(rparams, mesh, 0, matrices)`. Cách này không có collider, chọn vật hay culling từng vật như MeshRenderer; lab chưa có variant cho nó.

### Học bằng hình — B01, B02 và B03

Mở **Tools → Optimization Lab → Tutorial**, chọn B01, B02 hoặc B03 rồi bấm **Bắt đầu chơi**.

- 12 thẻ, mỗi thẻ một thao tác; ảnh Unity thật được khoanh vàng.
- Nút **Ảnh / GIF** đổi giữa ảnh tĩnh và chuỗi hình hướng dẫn. **Phóng to** mở cửa sổ lớn; có thể mở file GIF riêng.
- Bắt đầu tự mở đúng scene, Play và số khối mẫu (B01: 300, B02: 600, B03: 300).
- Làm lần lượt: chọn A → mở debugger → Enable → tìm nhóm khối → xem draw → Disable → chọn B → Enable lại → xem batch → trả lời câu hỏi → hoàn thành.
- Bước Enable/Disable kiểm trạng thái debugger thật. Không tự hoàn thành bước quan sát thay người học.
- Không bắt đếm draw hoặc nhập biểu mẫu. Câu hỏi cuối kiểm hiểu đúng ý: B02 ghép dữ liệu, không xoá vật; B01 giảm CPU setup, không phải chỉ còn 2 draw call.
- Giải thích thuật ngữ nằm trong **Vì sao?**; cài đặt project và công cụ nâng cao được thu gọn.

Ảnh mẫu được chụp trong Unity 6000.3.16f1, URP 17.3, PC/Metal. Tên nhóm/số liệu có thể khác trên thiết bị. Đặc biệt số dòng SRP Batch không phải tổng draw call bên trong.

Media thật: `Assets/OptimizationLab/TutorialMedia/Editor/{b01,b02,b03}/` (PNG + GIF). Bản gốc và script tạo media: `Documentation/TutorialCaptures/`. Unity hiển thị PNG flipbook tương ứng GIF; media chỉ dành cho Editor, không đưa vào player.
