# Validation

## Editor validation (`Tools ▸ Optimization Lab ▸ Validate Scenes`)
Kết quả ghi vào `Results/validation_<time>.md`, dạng bảng **scene | check | expected | actual | PASS/FAIL | giải thích**.

| Scene | Check |
|---|---|
| Project | Render pipeline = URP; Frame Timing Stats bật |
| B00 | Scene tồn tại và có trong Build Settings; có Scenario, Runner, HUD, `InputSystemUIInputModule`; đúng 1 camera đã gán; mesh cube; material URP/Lit, không bật instancing; light không shadow; folder Materials có đúng 1 material (generator idempotent) |

## Runtime gates (mỗi run)
Xem `README.md` → "Validation gates". Run INVALID vẫn được ghi vào CSV kèm `invalid_reason` và bị loại khỏi phép so sánh.

## EditMode tests (`Window ▸ General ▸ Test Runner ▸ EditMode`)
- Cùng seed thì transform giống nhau; khác seed thì jitter khác nhau; bounds chứa mọi vị trí.
- Percentile nearest-rank và median.
- Header CSV bắt đầu đúng bằng các cột của spec mục 2.3.
- Metric unsupported được ghi `unsupported`, không ghi `0`.
- Verdict: trong noise → inconclusive; có INVALID → invalid; chênh rõ → B faster kèm % đúng.


## B02
Editor kiểm scene, Build Settings, scenario/HUD, 4 shared materials, instancing tắt, mesh nguồn readable, max count 1500.
Runtime kiểm combine đầy đủ, transform không đổi và SRP/dynamic batching tắt ở A/B. C (`StaticObjectsMoved`) luôn INVALID. Đã kiểm cả 3 count: 600, 1000, 1500.

## B03
Editor kiểm scene, Build Settings, scenario/HUD, material A tắt instancing, material B/C bật instancing, hai material cùng shader và cùng màu, max count 3000, đúng 2 material trong `Materials/B03`.
Runtime kiểm: material A/B đúng trạng thái instancing, cùng shader, thiết bị hỗ trợ instancing, SRP/dynamic batching tắt; C có một material riêng cho mỗi khối. Đã kiểm 300 và 1000 khối trong Editor (A/B/C đều Valid, visible = số khối).
