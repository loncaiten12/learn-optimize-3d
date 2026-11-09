# Unity 3D Rendering Optimization — Benchmark Lab Specification

> **Mục đích:** Tài liệu triển khai dành cho AI coding agent. Tạo một Unity project/lab có các scene độc lập để **đo thực tế** tác động của từng kỹ thuật tối ưu 3D, đặc biệt cho **mobile 3D puzzle**, và tạo báo cáo so sánh A/B có thể lặp lại.
>
> **Target:** Unity **2022.3 LTS**, **URP 14.x**, C#, Android (ưu tiên) và Editor để debug. **Built-in** chạy trong **project riêng** cùng Unity version, không cố chuyển Render Pipeline trong lúc runtime. Nếu repo có sẵn, giữ nguyên cấu trúc/gameplay hiện hữu và tạo lab trong thư mục riêng.
>
> **Ngôn ngữ:** Comments, UI, báo cáo bằng tiếng Việt; code identifiers bằng tiếng Anh.

---

## 0. System prompt / chỉ dẫn bắt buộc cho AI Agent

Bạn là Unity Rendering/Performance Engineer. Hãy **triển khai code, scene, prefab, asset, shader, UI và automation đo** theo tài liệu này. Không chỉ tóm tắt ý tưởng. Làm theo thứ tự: framework → smoke test → các scene → Android build → kiểm thử kết quả. Nếu không có Unity Editor/headless Unity, tạo đầy đủ scripts, assets, editor scene generator và unit tests khả thi; **không tuyên bố đã đo FPS nếu chưa chạy thiết bị thật**.

Các nguyên tắc bất biến:

1. **Một biến độc lập cho mỗi test**. Cấu hình A/B phải giống camera, mesh, object count, transform distribution, texture, resolution, ánh sáng, shader quality, graphics API, thermal state và cùng hành vi camera; ngoại trừ chính yếu tố đang thử.
2. **Không tự coi `Batches` giảm là FPS sẽ tăng.** Thu thập CPU main thread, render thread, GPU frame time (nếu được hỗ trợ), frame time, memory, SetPass, batches, triangle/vertex stats và nhiệt độ/thermal warning nếu lấy được. Không chế số đo.
3. **Không nhầm SRP Batcher với GPU Instancing.** SRP Batcher giảm chi phí CPU chuẩn bị draw, không nhất thiết giảm draw calls; renderer tương thích SRP Batcher có thể không tự động đi vào đường automatic GPU instancing.
4. **Không dùng `renderer.material` trong hot path.** Dùng `sharedMaterial` khi phù hợp; báo rõ nếu test cần tạo materials riêng. `MaterialPropertyBlock` trên Renderer làm mất tương thích SRP Batcher, phải gắn nhãn là tradeoff, không ghi là tối ưu URP mặc định.
5. **Không giả định Dynamic Batching luôn hiệu quả.** Có CPU transformation overhead và những giới hạn về vertex attributes/size.
6. **Không tự tiện bật tất cả lựa chọn batching cùng lúc trong scene so sánh.** Nếu điều kiện của cơ chế batching bị vi phạm, phải đánh dấu kết quả `INVALID` và giải thích.
7. **Chỉ so sánh Built-in/URP khi tương đương chức năng và chất lượng ảnh.** Shader/Lights/Post-processing khác nhau khiến phép so sánh không còn thuần pipeline; phân biệt “practical equivalent” và “pixel-identical” (thường không đạt).
8. **Phân biệt thiết bị/Editor**: Editor dùng để smoke test, Frame Debugger và xác minh cơ chế; hiệu năng chính thức đo bằng **non-Development Player build** trên thiết bị (hoặc Development Build có ghi rõ overhead), với Profiler attach trong phiên riêng khi cần chẩn đoán.
9. Không thêm package bên thứ ba không cần thiết. Dùng primitive meshes/materials tự tạo cho lab có thể chạy độc lập; nếu cần DOTween, cung cấp fallback Coroutine/AnimationCurve.
10. Các benchmark kết quả phải luôn có **Unity version, URP package version, device model, Android version, resolution, targetFPS, graphics API, quality preset, test configuration, sample count**.

### Deliverables bắt buộc

- `Assets/OptimizationLab/` có scripts, shaders, materials, configs, editor utilities, scenes.
- Mỗi benchmark scene có 2+ preset **Baseline / Optimized** và một bảng điều khiển thống nhất.
- `OptimizationLab/BenchmarkMenu.unity` để chạy mọi test; nút **Run A**, **Run B**, **Run A/B Sequence**, **Export CSV/JSON**.
- Editor menu `Tools/Optimization Lab/Generate All Scenes` để tạo lại các scene bằng code, deterministic seed.
- `BenchmarkRunner` thực hiện warm-up, đo, reset, nghỉ, hoán đổi ABBA/B A A B để giảm drift.
- `README.md` cách chạy Android và xác minh số đo.
- `Results/` chứa file CSV + JSON thực tế sau mỗi test và `REPORT_TEMPLATE.md` để phân tích, không chứa dữ liệu tưởng tượng.
- Một `Validation` suite kiểm tra camera, quality, count, materials, render pipeline, shader variants, draw events ở nơi có thể truy xuất.

---

## 1. Phạm vi và kiến trúc

### 1.1 Các scene cần tạo

| ID | Scene | Biến độc lập | Giả thuyết cần kiểm tra |
|---|---|---|---|
| B00 | `B00_Baseline` | Số object | Frame time tăng như thế nào theo object count? |
| B01 | `B01_SRPBatcher` | SRP Batcher On/Off | CPU render time có giảm khi dùng URP shader tương thích? |
| B02 | `B02_StaticBatching` | Static batching On/Off | Render-thread cost giảm, memory tăng bao nhiêu? |
| B03 | `B03_GPUInstancing` | Explicit instancing vs non-instanced rendering | Nhiều cùng Mesh/Material có lợi khi nào? |
| B04 | `B04_DynamicBatching` | Dynamic batching On/Off | Mesh nhỏ có tiết kiệm CPU không, trên thiết bị mục tiêu? |
| B05 | `B05_MaterialsAndMPB` | Shared materials vs unique material vs MPB | SetPass/SRP compatibility thay đổi thế nào? |
| B06 | `B06_MeshComplexityLOD` | Low/High poly, LOD | Vertex-bound scenarios và LOD thực tế khác nhau ra sao? |
| B07 | `B07_OverdrawTransparency` | Opaque vs transparent layers | Khi nào GPU fragment/fill rate trở thành bottleneck? |
| B08 | `B08_ShadowLighting` | Shadows/lighting options | Shadow pass và additional lights tốn bao nhiêu? |
| B09 | `B09_RenderScalePostFX` | Render scale, HDR, MSAA, post FX | GPU pixel/bandwidth cost thay đổi thế nào? |
| B10 | `B10_Culling` | Frustum/occlusion/mesh combining | Khi nào culling thắng batching và ngược lại? |
| B11 | `B11_PuzzleStress` | Tổ hợp kỹ thuật thực tế | Tối ưu ảnh hưởng FPS và trải nghiệm gameplay ra sao? |
| X01 | `X01_PipelineComparison` | Built-in vs URP bằng 2 project | Chi phí pipeline trong cấu hình tương đương là bao nhiêu? |

### 1.2 Cấu trúc thư mục

```text
Assets/OptimizationLab/
├── Scenes/
│   ├── BenchmarkMenu.unity
│   ├── B00_Baseline.unity
│   ├── B01_SRPBatcher.unity
│   └── ...
├── Scripts/
│   ├── Runtime/
│   │   ├── BenchmarkBootstrap.cs
│   │   ├── BenchmarkRunner.cs
│   │   ├── BenchmarkScenario.cs
│   │   ├── BenchmarkConfig.cs
│   │   ├── BenchmarkHUD.cs
│   │   ├── MetricsCollector.cs
│   │   ├── ResultsExporter.cs
│   │   ├── DeterministicSpawner.cs
│   │   ├── BenchmarkCameraController.cs
│   │   └── Scenarios/*.cs
│   └── Editor/
│       ├── OptimizationLabSceneGenerator.cs
│       ├── BuildOptimizationLab.cs
│       └── ValidationMenu.cs
├── Materials/
├── Meshes/
├── Shaders/
│   ├── SimpleURPLit.shader (hoặc URP/Lit nếu đủ)
│   ├── VertexHeavy.shader
│   ├── FragmentHeavy.shader
│   └── InstancedColor.shader
├── Configs/ (ScriptableObjects)
└── Prefabs/
Results/ (gitignored hoặc có README riêng)
Documentation/
├── README.md
├── REPORT_TEMPLATE.md
└── VALIDATION.md
```

### 1.3 Contract C# (tham khảo; có thể điều chỉnh nhưng giữ ngữ nghĩa)

```csharp
public enum BenchmarkStatus { Valid, Invalid, Unsupported }
public enum BenchmarkVariant { A, B, C }

public interface IBenchmarkScenario
{
    string Id { get; }
    void Setup(BenchmarkVariant variant, int seed);
    void WarmupTick(float deltaTime);
    void MeasurementTick(float deltaTime);
    void Teardown();
    BenchmarkStatus Validate(out string reason);
}

[System.Serializable]
public class BenchmarkConfig
{
    public int seed = 1337;
    public int objectCount = 300;
    public float warmupSeconds = 15f;
    public float measureSeconds = 60f;
    public float cooldownSeconds = 15f;
    public int repetitions = 3;
    public int targetFrameRate = 60;
    public bool animateObjects;
}
```

Không serialise API profiler phức tạp bằng cách `new` mỗi frame. Cache `ProfilerRecorder`/buffers, giải phóng đúng lúc. Một số counter không hỗ trợ trên mọi Unity/player/backend; ghi `unsupported`, tuyệt đối không điền `0` thay giá trị không đọc được.

---

## 2. Quy trình benchmark chuẩn

### 2.1 Trình tự chạy

```text
Launch app → Select scene/preset
   → Validate fixed conditions
   → Setup deterministic objects/mesh/material
   → Warm up 15–30 s
   → Measure 60–120 s, exclude first 2 s after switch
   → Export metrics + metadata
   → Cleanup and cooldown
   → Switch variant
   → Repeat in ABBA order (tối thiểu 3 bộ hoặc 3 runs/variant)
   → Calculate median, P95, P99, stdev
   → Report comparisons + invalid/unsupported flags
```

- Sử dụng `Time.realtimeSinceStartupAsDouble` / Stopwatch cho elapsed wall time; frame time từ frame delta hoặc FrameTimingManager (kiểm tra hỗ trợ); tránh dùng `Time.deltaTime` làm số đo GPU.
- Trong Android nên thử 60 FPS và một lần **uncapped/vsync-off** nếu platform cho phép để lộ chênh lệch. Nếu FPS bị khóa ở 60 thì nhìn **frametime** và P95 thay vì kết luận “giống nhau”.
- Không benchmark khi còn đang import shader, compile, loading scene hoặc instantiate batch mới trong khoảng measurement, trừ test Spawn/Load chuyên biệt.
- Ghi `Application.targetFrameRate`, `QualitySettings.vSyncCount`, MSAA, resolution và `Screen.currentResolution` (và render scale).
- Kiểm tra thermal throttling: thiết bị nguội, battery đủ, không vừa sạc vừa chạy nếu có thể; nếu các run sau chậm dần, chạy thứ tự ABBA và ghi nhiệt trạng.
- Đảm bảo camera nhìn cùng vùng, số object đang **visible** được xác minh; object count spawn không đồng nghĩa số object được render.

### 2.2 Metrics tối thiểu

| Tên trường | Đơn vị | Ý nghĩa / nguồn |
|---|---|---|
| `frame_ms_median`, `frame_ms_p95`, `frame_ms_p99` | ms | Phân phối frametime đo trong player |
| `fps_median` | FPS | Dùng để dễ đọc, **không** thay frame time |
| `cpu_main_ms` | ms | CPU main thread timing, khi hỗ trợ |
| `cpu_render_ms` | ms | Render thread timing, khi hỗ trợ |
| `gpu_ms` | ms/null | GPU timing, phụ thuộc device/API |
| `draw_calls` / `batches` | count | Stats/counters hỗ trợ, có thể khác định nghĩa |
| `setpass_calls` | count | Số lần setup shader pass (nếu lấy được) |
| `triangles`, `vertices` | count | Statistics mang tính pipeline-specific |
| `total_allocated_mb`, `mono_used_mb`, `graphics_memory_mb` | MB/null | Memory/graphics metric khi hỗ trợ |
| `visible_renderers` | count | Tự đếm theo thiết kế test hoặc culling proxy |
| `validation_status`, `invalid_reason` | enum/string | Báo test không đủ điều kiện |

**Quan trọng:** Unity `ProfilerRecorder` counter names thay đổi theo version/platform và không phải counter nào cũng có sẵn trong non-development player. Implementer phải xác minh counter bằng Unity 2022.3 runtime và cung cấp fallback. Dùng Unity Profiler attach hoặc Android GPU Inspector/RenderDoc cho phân tích sâu nếu cần; số liệu từ công cụ khác phải ghi rõ nguồn.

### 2.3 Định dạng kết quả

CSV một hàng cho mỗi run:

```csv
run_id,utc_time,scene,variant,seed,unity_version,render_pipeline,urp_version,device,android_version,graphics_api,width,height,render_scale,object_count,visible_renderers,frame_ms_median,frame_ms_p95,frame_ms_p99,cpu_main_ms,cpu_render_ms,gpu_ms,batches,setpass_calls,triangles,vertices,allocated_mb,status,invalid_reason
```

JSON gồm `metadata`, `config`, `validation`, `samples` hoặc aggregate, `metrics` với giá trị nullable, `comparison`, `notes`. Không bắt buộc ghi mỗi-frame nếu file quá lớn, nhưng nên tùy chọn export raw samples để phân tích spike. Tạo file tại `Application.persistentDataPath/OptimizationLabResults/` rồi hiển thị đường dẫn trong UI; Editor có thêm nút mở thư mục.

### 2.4 Cách tính kết luận

- Tỷ lệ cải thiện thời gian: `improvement_% = (median_A_ms - median_B_ms) / median_A_ms * 100`.
- Nếu CPU frame time giảm nhưng GPU frame time không đổi và frame capped, kết luận **CPU rendering improvement**, không tự kết luận FPS cải thiện.
- Nếu chênh lệch nhỏ hơn run-to-run noise, ghi **inconclusive**.
- So sánh P95/P99 để phát hiện stutter; ghi thêm peak memory nếu thay đổi static batching.
- Với mỗi cặp A/B: có screenshot cùng camera và một danh sách khác biệt cấu hình được tạo tự động.

---

## 3. Scene specifications

### B00 — Baseline / Object count scaling

**Mục tiêu:** đo đường cong tải theo số lượng Renderer, không bị nhầm với shader/lighting.

- Dùng một scene sàn và camera cố định orthographic hoặc perspective, UI Overlay.
- Spawn mesh primitives theo lưới bằng seed cố định, tăng từ 100 → 300 → 1.000 → 3.000, tùy khả năng thiết bị.
- Các preset: `ObjectsDisabled`, `ObjectsVisible`, `ObjectsVisibleAnimated` (animation là test riêng, không dùng như A/B batching).
- Số object thực sự nằm trong frustum phải biết chính xác; không để occlusion đáng kể trừ khi có nhãn.
- Expected: baseline CSV, đồ thị frame time theo object count, không ghi kết luận nguyên nhân nếu chưa profiling.

### B01 — SRP Batcher On / Off (URP only)

**Mục tiêu:** kiểm chứng SRP Batcher giảm CPU overhead khi nhiều renderers dùng shader tương thích.

**Fixture:** 1.000 renderers cùng geometric complexity, nhiều **shared material instances** khác màu, cùng **URP shader variant**; không sử dụng `MaterialPropertyBlock`. Có một phiên bản tăng shader variants để thấy SRP batch bị chia nhỏ.

- Variant A: `GraphicsSettings.useScriptableRenderPipelineBatching = false` (chỉ khi API hỗ trợ trên Unity 2022.3 và trước khi test renderer bắt đầu; nếu dùng cấu hình URP Asset thì tạo riêng asset/preset).
- Variant B: SRP Batcher On.
- Variant C (educational): On, nhiều shader keywords/variants (ghi rõ test này đổi thêm yếu tố).
- Giữ nguyên materials, meshes, camera, lights.
- Validate shader SRP compatible qua Inspector/Editor utility; không dùng MPB.
- Đo CPU render thread, batches, SetPass, GPU time. Kỳ vọng có thể **giữ nguyên draw call count** nhưng giảm CPU render time.
- Frame Debugger: chụp/tài liệu hóa SRP batch grouping trong Editor; không coi đó là benchmark trên Android.

### B02 — Static Batching On / Off

**Mục tiêu:** đánh giá tradeoff CPU overhead vs memory trên mesh tĩnh.

- Fixture: 600–1.500 objects tĩnh, 3–5 shared materials, không animator/transform move; layout và camera cố định.
- A: static batching Off.
- B: static batching On (bake với `StaticBatchingUtility.Combine` trong setup hoặc build-time static flags; phải đảm bảo **đã thực sự combine** trước measurement).
- Thêm scenario `StaticObjectsMoved`: hiển thị cảnh báo **invalid** nếu muốn so sánh mà vật thể lại động.
- Ghi batches, CPU render time, memory usage, mesh memory nếu có. So sánh screenshot/visible count.
- Không khẳng định static batching tạo đúng một draw call; submesh/material/render passes vẫn có thể chia draw.

### B03 — GPU Instancing: automatic vs explicit

**Mục tiêu:** so sánh nhiều bản sao cùng mesh + cùng material.

- Fixture: 300, 1.000, 3.000 bản sao cùng một Mesh/Material, transform khác nhau, nằm trong view. Shader instancing-compatible.
- A: Renderers vẽ theo SRP Batcher hoặc cách thông thường; ghi rõ code path.
- B: explicit `Graphics.DrawMeshInstanced` (batches ≤1023 instances per call trong API này; chunk matrices); hoặc API instancing phù hợp Unity version.
- C: different meshes/materials để **chứng minh không hợp lệ** cho chung một instanced draw, không dùng làm so sánh hiệu năng chính.
- **Cực kỳ quan trọng:** explicit instancing không tự render cùng semantics per-object collider/culling/selection như MeshRenderer; lab phải tách gameplay colliders khỏi geometry visualization và nói rõ cost model khác nhau.
- Ensure instancing enabled trên material, shader có instancing; giữ hiệu ứng shader tương đương.
- Báo draw calls, CPU render time, GPU time, memory và culling overhead; nếu sử dụng custom per-instance color, so sánh cùng cách truyền data hoặc ghi rõ khác biệt.

### B04 — Dynamic Batching

**Mục tiêu:** kiểm chứng Dynamic Batching có/không có lợi cho geometry đơn giản.

- Fixture: 500–1.500 object mesh nhỏ, cùng material, biến thể vertex count thấp/vừa.
- A: Dynamic Batching Off.
- B: Dynamic Batching On qua URP Asset hoặc pipeline/quality config; xác minh setting có tác dụng trên Unity 2022.3.
- SRP Batcher được giữ Off trong test chính (hoặc shader không compatible) để tránh kết quả mất ý nghĩa do ưu tiên code path; có thêm test thứ cấp SRP On.
- Đo CPU main/render thread, draw calls, GPU time; **không** kết luận “dynamic luôn tối ưu”.

### B05 — Shared Material / Unique Material / MaterialPropertyBlock

**Mục tiêu:** hiểu tác động material state và khả năng SRP batching.

- Fixture: 500 objects một shader variant, cùng mesh, chia 5 màu.
- A: 5 shared Materials, assign `renderer.sharedMaterial`.
- B: clone 1 Material **mỗi object** ngay Setup (không clone trong measurement).
- C: một shared material + MPB đổi màu từng object; **SRP Batcher compatibility bị mất** (kiểm tra thực tế).
- D (optional): instanced colors với dedicated instancing shader.
- Ghi số lượng material instances, batches/SetPass, CPU render time, memory; không giả định case nào nhanh nhất.
- Dùng cùng output màu và camera; cách tiếp cận material/instancing phải được ghi vào metadata.

### B06 — Mesh Complexity / LOD / Vertex Splitting

**Mục tiêu:** hiểu Vertex Count, Triangle Count, normals/UV seams và lợi ích LOD.

- Tạo procedural meshes có cùng silhouette gần tương đương: low (≈100 tri), medium (≈1k tri), high (≈10k tri).
- A/B: low-poly vs high-poly trên cùng số object; dùng **simple unlit/cheap lit** tránh fragment bottleneck để kiểm chứng chi phí geometry.
- C: LODGroup (high/medium/low theo khoảng cách) với camera path cố định.
- D: mesh có nhiều UV seams/hard edges vs smooth normals với **cùng triangle count** để so vertex records.
- Ghi vertices/triangles per mesh, rendered stats, CPU/GPU frame time và visual difference.
- Lưu ý same silhouette ở close-up không có nghĩa chất lượng hình ảnh tương đương; báo rõ visual tradeoff.

### B07 — Overdraw / Transparency / Fragment Cost

**Mục tiêu:** tạo tình huống GPU fragment-bound để chứng minh batching không xử lý mọi vấn đề.

- Dùng 20–100 lớp quads chồng lên cùng vùng màn hình, cố định diện tích fill; shader Unlit Opaque/Transparent tương đương màu ở nơi có thể.
- A: ít lớp opaque, sắp xếp thích hợp.
- B: nhiều lớp alpha blended transparent, depth write settings minh bạch.
- C: cùng geometry nhưng fragment shader tăng ALU/texture samples, có preset rõ cost.
- D: giữ scene như B rồi giảm Render Scale từ 1.0 xuống 0.75.
- So GPU time (nếu có), frame time, resolution scaling; chụp Overdraw visualization trong Editor để minh họa, nhưng không coi các chế độ debug là FPS benchmark.
- Phân biệt overdraw và quá nhiều vertex. Cảnh báo transparency có khác biệt blending/order và không thể luôn đạt pixel-identical.

### B08 — Shadow & Lighting

**Mục tiêu:** đo shadow maps và chi phí additional lights trên scene puzzle.

- Fixture: 250 objects, một sàn, một main directional light, camera cố định; dùng URP/Lit.
- A: Main Light Shadows Off.
- B: Main Light Shadows On, hard shadows low resolution.
- C: soft shadows và resolution cao hơn.
- D: add 2–4 additional lights, thử per-vertex vs per-pixel theo support.
- Chỉ đổi **một thông số một lần** trong A/B cặp; các preset bổ sung dùng phân tích nhiều yếu tố, không gộp kết luận.
- Ghi shadow caster count, shadow resolution/cascade/distance, render passes, CPU/GPU time, screenshots.
- Không coi shadow Off có chất lượng hình ảnh tương đương On.

### B09 — Render Scale / MSAA / HDR / Post FX

**Mục tiêu:** giảm pixel, render target bandwidth và các full-screen passes.

- Fixture: sử dụng scene B08 standard với GPU load vừa phải.
- Các cặp test độc lập:
  1. `RenderScale=1.0` vs `0.75`.
  2. `MSAA Off` vs `MSAA 4x` (nếu pipeline/device hỗ trợ).
  3. `HDR Off` vs `HDR On`.
  4. `Post FX Off` vs `Bloom On` (cấu hình cố định).
  5. `Opaque/Depth Texture Off` vs On **khi những texture đó không bắt buộc cho effect**.
- Mỗi cặp có screenshot và metric. Ghi internal render resolution; không lấy screen resolution thay render resolution.
- Cần tạo URP Assets/RendererData riêng nếu không thể đổi runtime; **không** phụ thuộc vào chỉnh trực tiếp asset chia sẻ trong lúc measure.

### B10 — Culling vs Combined Mesh

**Mục tiêu:** giải thích vì sao gộp mesh không phải lúc nào cũng tốt.

- Layout một mê cung/kệ có nhiều khu vực, camera chỉ nhìn một phần ở mỗi thời điểm.
- A: nhiều MeshRenderers riêng, Unity frustum culling bình thường.
- B: combine thành một mesh lớn, không thể cull từng object ban đầu.
- C: chunk combine theo các khu vực nhỏ, kết hợp culling.
- D (optional): occlusion culling bake, trên geometry phù hợp; ghi chi phí bake và CPU overhead.
- Camera chạy waypoint cố định; đo visible renderers và GPU frame time theo vị trí.
- Báo khi A giảm GPU load tốt hơn B dù draw calls nhiều hơn.

### B11 — Mobile 3D Puzzle Stress Test

**Mục tiêu:** benchmark end-to-end có tính ứng dụng; không chỉ stress test một feature nhỏ.

- Spawn 300 item, 20 mesh types, 5–8 colors/material groups, một kệ nhiều ô, 1 light; camera góc nhìn 3D puzzle cố định.
- Có logic deterministic: 5 lượt chọn item, item bay vào tray bằng animation, match/remove, refill rồi reset; dùng seed cố định để replay đúng timeline.
- `Baseline`: renderer/lighting/material cấu hình thông thường, document chính xác từng setting.
- `Optimized`: shared materials, SRP Batcher, culling, mesh LOD khi cần, giản lược shadow và render scale **chỉ nếu báo riêng tradeoff về chất lượng**.
- Ngoài run tổng hợp, chạy ablation tests: bật từng tối ưu một, nhằm xác định đóng góp.
- Ghi FPS/frametime P95/P99, input latency (nếu đo được), peak memory, spike khi animation, visual screenshot.
- **Không** tuyên bố kỹ thuật nào là nguyên nhân cải thiện chỉ từ `Baseline` vs `Optimized` thay nhiều yếu tố.

### X01 — Built-in vs URP comparison (hai project)

**Bắt buộc thiết kế là 2 project:** `OptimizationLab_URP` và `OptimizationLab_BuiltIn`, dùng chung source C# và geometry fixtures. Có thể cung cấp script copy scene fixtures, materials mapping và build configs, tránh sửa asset pipeline hiện tại của user.

- Cùng Unity 2022.3 patch, cùng device, graphics API, resolution, object/mesh/transform count, animation, camera FOV, shadows/lights gần tương đương.
- **Built-in:** dùng shader tương đương `Standard`/Unlit và static/dynamic batching/instancing.
- **URP:** dùng URP/Lit/Unlit tương đương, SRP Batcher; render scale 1.0 để bắt đầu.
- Chạy ít nhất 3 fixtures: (1) Unlit opaque 1k object; (2) Lit + shadows 300 items; (3) 1k identical mesh/material instances.
- Chỉ so theo **practical visual equivalence** và ghi những khác biệt về shader/lighting/passes; không gọi là thí nghiệm kiểm soát tuyệt đối nếu shader khác nhau.
- Bảng report: `Built-in A`, `URP B`, percent diff, CPU main/render, GPU, draw calls, SetPass, memory, image quality notes.

---

## 4. Kiểm soát biến nhiễu & Validation Gates

Trước mỗi run, ghi log và đánh dấu **INVALID** nếu xảy ra một trong các điều kiện:

- Scene/preset không đúng, URP Asset/Quality tier khác mong đợi; graphics API đổi giữa A và B.
- Build trong Editor được dùng như kết luận về Android.
- Độ phân giải hoặc render scale khác **ngoài** test B09.
- Visible object counts hoặc transform distribution khác A/B.
- Shader variant, material grouping, shadows khác ngoài biến cần test.
- Benchmark measuring phase bao gồm async asset import/loading/compilation/instantiation ngoài kế hoạch.
- FPS cap/vsync dẫn tới số đo không phân biệt được sự khác nhau, nhưng bị kết luận B nhanh hơn bằng FPS.
- Thermal throttling/sự kiện hệ thống nghiêm trọng xuất hiện mà không được ghi nhận.
- GPU metric không hỗ trợ nhưng được ghi `0ms`.
- SRP Batcher testcase sử dụng MPB mà vẫn nhận là compatible.
- Static batching case chưa combine hoặc objects bị di chuyển trong phần đo.
- GPU instancing case thực tế vẽ khác mesh/material hoặc custom draw không được xác minh.

Validation report phải liệt kê **scene, variant, expected, actual, pass/fail, explanation**.

---

## 5. UI/UX của Benchmark Lab

Thiết kế tối giản, dark technical, dễ xem trên màn hình điện thoại:

```text
┌──────────────────────────────────────────────────┐
│ 3D Rendering Benchmark Lab      Unity 2022.3     │
│ Scene: B03 GPU Instancing                         │
│ [A Normal] [B Instanced] [Run ABBA]               │
│ Count: [300] [1000] [3000]   Seed: 1337           │
│ Warm-up: 15s  Measurement: 60s  Repeats: 3        │
│ Status: VALID | Measuring 34/60s                 │
│ FPS: 54   Frame P95: 22.4 ms  GPU: Unsupported   │
│ Batches: ...   SetPass: ...  Vertices: ...        │
│ [Export CSV] [Export JSON] [Next Scene]           │
│                                      3D VIEW     │
└──────────────────────────────────────────────────┘
```

Các số trong UI wireframe chỉ **minh họa bố cục**, không phải kết quả thực tế. HUD đo nên tách riêng render/update cost, refresh khoảng 4 lần/giây thay vì rebuild UI mỗi frame; cần hỗ trợ tắt HUD cho phép đo cuối cùng và thu thập số bằng code.

Đối với mỗi scene, có nút **Explain:** mô tả kỹ thuật, giả thuyết, thay đổi A/B, sai số thường gặp, nơi kiểm chứng trong Frame Debugger, và cách đọc kết quả.

---

## 6. Build & Run Automation

### Editor tools

- `Tools/Optimization Lab/Generate All Scenes`
- `Tools/Optimization Lab/Validate Scenes`
- `Tools/Optimization Lab/Build Android Benchmark APK`
- `Tools/Optimization Lab/Export Config Manifest`
- `Tools/Optimization Lab/Create Built-in Comparison Fixture`

Tạo scenes bằng Editor API với seed. Asset generators phải idempotent: chạy lại không nhân đôi objects/materials hoặc làm mất reference. Scene list được tự động thêm vào Build Settings theo thứ tự Menu, B00–B11.

### Android runbook

1. Chọn Android build target, kiểm tra Graphics API (Vulkan hoặc OpenGLES3), ASTC nếu phù hợp, và Quality preset.
2. Build player; lưu git commit/version build vào metadata.
3. Đóng background apps, khóa orientation, giữ device cùng chế độ tiết kiệm pin và độ sáng tương đương.
4. Chạy Menu → chọn scene → A/B Sequence. Chạy tối thiểu 3 lần/variant hoặc ABBA đủ số repetitions.
5. Export data; lấy file từ `Application.persistentDataPath` hoặc dùng Android share nếu đã implement.
6. Tách biệt *performance production-like* và *diagnostic development build*.
7. Lặp lại trên thiết bị mạnh/yếu nếu có, theo cùng checklist.

### Command line (nếu Unity có sẵn)

Các Editor static methods cần hỗ trợ chạy bằng `-executeMethod` (method không tham số) để dùng CI:

```text
Unity -batchmode -nographics -quit \
 -projectPath <PROJECT_PATH> \
 -executeMethod OptimizationLab.Editor.OptimizationLabSceneGenerator.GenerateAll \
 -logFile <LOG_FILE>
```

Không dùng `-nographics` để benchmark GPU rendering; nó chỉ phục vụ tạo scene/build headless.

---

## 7. Automated Tests & Acceptance Criteria

### Test phải có

- **EditMode**: generator idempotent; scenes tồn tại; config serialization; matching seed creates identical transforms; no duplicated materials.
- **PlayMode**: scene loads; toggle A/B works; visible renderer count expected; no exceptions; metrics collector returns samples or `Unsupported`; CSV schema stable.
- **Render validation** (Editor): shader SRP compatibility, material count, expected instancing settings, static combine, shadow options.
- **Device smoke test**: tất cả scenes chạy được, không crash, có export file, không GC spikes do log/HUD trong measuring phase ở mức đáng kể.

### Definition of Done

- [ ] Có Menu + B00–B11, mỗi scene có A/B thực sự hoạt động.
- [ ] Mỗi test ghi rõ **một biến độc lập**; các test multi-factor được đánh dấu educational/composite.
- [ ] Có built-in fixture project hoặc hướng dẫn tạo project + scripts chung.
- [ ] Có scene generation Editor menu, idempotent.
- [ ] Có deterministic seed, warmup, measurement, cooldown, ABBA, multiple repeats.
- [ ] Có metrics CSV/JSON với metadata và nullable unsupported counters.
- [ ] Có validation gates và báo INVALID nếu điều kiện sai.
- [ ] Có screenshots/capture flow và thông tin render path/quality.
- [ ] Có README: build Android, đọc số, cách so sánh và limitations.
- [ ] Có bảng report A/B cho ít nhất B01, B03, B05, B07, B09, B11 trên **thiết bị thật** nếu thiết bị sẵn có; nếu chưa có, ghi `Not measured` trung thực.

---

## 8. Thứ tự triển khai cho AI Agent (milestones)

1. **Milestone 1 — Framework:** Menu, base scenario API, renderer/mesh/material fixtures, MetricsCollector, exporter, ABBA scheduler, validation, Editor generator. Tạo B00 và chạy smoke test.
2. **Milestone 2 — Batching:** B01–B05. Xác minh qua Frame Debugger; giữ đúng compatibility rules; kiểm tra switching không để data từ run trước.
3. **Milestone 3 — GPU/Geometry:** B06–B10. Ghi rõ supported/unsupported counters, scene screenshots và shader cost.
4. **Milestone 4 — Real use-case:** B11 Puzzle Stress, deterministic interaction timeline, ablation matrix.
5. **Milestone 5 — Pipeline Comparison:** Built-in fixture/project + automated build manifests.
6. **Milestone 6 — Device QA:** Android builds, real run export, validation logs, report template được điền bằng số đo thực tế.

**Mỗi milestone cần bàn giao:** danh sách file tạo/sửa, test đã chạy, test chưa chạy, hạn chế/nút thắt, cách tự kiểm chứng. Không khẳng định “optimization works” nếu chưa có số liệu.

---

## 9. Template báo cáo A/B (AI phải tạo `REPORT_TEMPLATE.md`)

```markdown
# Optimization Lab Report — [Device / Build]

## Environment
- Unity/URP version:
- Android & device:
- Graphics API:
- Resolution / render scale:
- Build type / target FPS / VSync:
- Git commit:
- Room temperature / battery / device state (if known):

## Benchmark [ID]
- Hypothesis:
- Independent variable:
- Controlled variables:
- Validation status:
- Repetitions and order:

| Metric | A median | B median | Difference | Notes |
|---|---:|---:|---:|---|
| Frame (ms) | N/A | N/A | N/A | |
| P95 (ms) | N/A | N/A | N/A | |
| Main thread (ms) | N/A | N/A | N/A | |
| Render thread (ms) | N/A | N/A | N/A | |
| GPU time (ms) | N/A | N/A | N/A | |
| Batches | N/A | N/A | N/A | |
| SetPass | N/A | N/A | N/A | |
| Memory (MB) | N/A | N/A | N/A | |

## Conclusion
- Supported / Inconclusive / Invalid:
- Measured benefit:
- Visual or memory tradeoff:
- Recommended for 3D puzzle? Under which conditions?
```

---

## 10. Nguồn kỹ thuật chính thức để đối chiếu implementation

- Unity 2022.3 Manual — Optimizing Draw Calls: https://docs.unity3d.com/2022.3/Documentation/Manual/optimizing-draw-calls.html
- Unity 2022.3 Manual — GPU Instancing: https://docs.unity3d.com/2022.3/Documentation/Manual/GPUInstancing.html
- URP 14 — Optimize for better performance: https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/optimize-for-better-performance.html
- URP 14 — SRP Batcher: https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/urp-shaders/urp-shaderlab-pass-tags.html (tham chiếu thêm trang SRP Batcher đúng minor version)
- Unity — ProfilerRecorder API: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html
- Unity — Frame Debugger: https://docs.unity3d.com/2022.3/Documentation/Manual/FrameDebugger.html

> **Tiêu chí thành công:** AI tạo được một phòng lab benchmark có thể chạy lại, kiểm soát biến nhiễu và xuất số liệu thực. Lab này **không được chứng minh URP luôn tốt hơn Built-in**; nó phải chỉ ra *trong hoàn cảnh nào* kỹ thuật nào có lợi, không có lợi hoặc chưa đủ dữ liệu kết luận.
