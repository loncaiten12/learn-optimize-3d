# Optimization Lab Report — [Device / Build]

> Chỉ điền số đo THẬT lấy từ CSV/JSON của lab. Ô chưa đo ghi `Not measured`, counter không đọc được ghi `unsupported`.

## Environment
- Unity/URP version:
- Android & device:
- Graphics API:
- Resolution / render scale (internal resolution):
- Build type / target FPS / VSync:
- Quality preset:
- Git commit:
- Room temperature / battery / device state (if known):
- Result files:

## Benchmark B00 — Baseline / object count scaling
- Hypothesis: frame time tăng theo số renderer.
- Independent variable: số MeshRenderer được vẽ.
- Controlled variables: cube mesh, 1 shared URP/Lit material, shadow off, camera ortho fit lưới 3000, seed 1337.
- Validation status:
- Repetitions and order:

| Metric | A median | B median | Difference | Notes |
|---|---:|---:|---:|---|
| Frame (ms) | Not measured | Not measured | N/A | |
| P95 (ms) | Not measured | Not measured | N/A | |
| Main thread (ms) | Not measured | Not measured | N/A | |
| Render thread (ms) | Not measured | Not measured | N/A | |
| GPU time (ms) | Not measured | Not measured | N/A | |
| Batches | Not measured | Not measured | N/A | |
| SetPass | Not measured | Not measured | N/A | |
| Memory (MB) | Not measured | Not measured | N/A | |

### Count sweep (variant B)

| Object count | Frame median (ms) | P95 (ms) | CPU main (ms) | GPU (ms) | Batches | Status |
|---:|---:|---:|---:|---:|---:|---|
| 100 | Not measured | | | | | |
| 300 | Not measured | | | | | |
| 1000 | Not measured | | | | | |
| 3000 | Not measured | | | | | |

## Conclusion
- Supported / Inconclusive / Invalid:
- Measured benefit:
- Visual or memory tradeoff:
- Recommended for 3D puzzle? Under which conditions?

## B02 — Static Batching

- Thiết bị / API / resolution: Not measured
- Số khối: 600 / 1000 / 1500; 4 shared materials
- A: không combine. B: runtime combine trước warmup, xác nhận đủ renderer trong static batch.
- SRP/dynamic batching tắt ở cả A/B, GPU instancing tắt. Không di chuyển transform.
- CPU render median, batches, GPU time, allocated / graphics memory: Not measured
- `fixture_mesh_memory_mb` (notes): Not measured
- Visible count / screenshot A/B: Not measured trên thiết bị
- C StaticObjectsMoved: phải INVALID, loại khỏi so sánh chính
- Kết luận lợi CPU / tăng memory và noise: Not measured
