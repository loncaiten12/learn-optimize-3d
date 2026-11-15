using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OptimizationLab.Scenarios
{
    /// <summary>
    /// B06 — Mesh Complexity / LOD / Vertex Splitting. Biến độc lập: mesh của mỗi khối (cùng silhouette cầu).
    /// A = low (~120 tri), B = high (~9.8k tri, normal mượt), C = LODGroup (high/medium/low theo dải khoảng cách),
    /// D = high với normal phẳng: CÙNG số tam giác với B nhưng đỉnh bị tách (nhiều vertex hơn).
    /// Shader URP/Lit đơn giản, một đèn, không shadow: để chi phí hình học nổi lên. SRP Batcher bật, dynamic batching tắt ở mọi variant.
    /// Camera orthographic cố định không có "khoảng cách", nên C gán mức LOD theo dải bán kính từ tâm lưới (ForceLOD) — mô phỏng, có ghi nhãn.
    /// </summary>
    public sealed class B06MeshComplexityScenario : GridScenarioBase
    {
        #region Fields

        private const int LOW_SEGMENTS = 12, LOW_RINGS = 6;
        private const int MED_SEGMENTS = 32, MED_RINGS = 16;
        private const int HIGH_SEGMENTS = 100, HIGH_RINGS = 50;
        private const float BAND_NEAR = 0.35f, BAND_MID = 0.7f;
        private const int BYTES_PER_VERTEX = 32; // position + normal + uv (float32)

        private static readonly int[] COUNTS = { 100, 300, 1000 };
        private static readonly LiveStat[] STATS = { LiveStat.GpuMs, LiveStat.CpuRenderMs, LiveStat.MeshMemoryMb };
        private static readonly int LOW_TRIS = ProceduralSphereMesh.TriangleCount(LOW_SEGMENTS, LOW_RINGS);
        private static readonly int HIGH_TRIS = ProceduralSphereMesh.TriangleCount(HIGH_SEGMENTS, HIGH_RINGS);

        private Mesh _low, _medium, _high, _highHard;
        private bool _saved, _originalSrp, _originalAssetSrp, _originalDynamic;
        private UniversalRenderPipelineAsset _urp;
        private readonly int[] _lodCounts = new int[3];
        private int _lodGroups;
        private double _meshMb;

        #endregion

        #region Public

        public override string Id => "B06";
        public override string Title => "Mesh Complexity / LOD / Vertex Splitting";
        public override string ShortTitle => "Bài 7 · Độ chi tiết mesh";
        public override string IndependentVariable => "Mesh của mỗi khối: low / high / LODGroup / high với đỉnh bị tách";
        public override string Hypothesis => "Cùng hình cầu, số tam giác và số đỉnh quyết định chi phí hình học; LOD giảm tải cho vật xa nhưng đổi chất lượng. Chỉ kết luận sau khi đo trên máy thật.";
        public override int[] ObjectCountOptions => COUNTS;
        public override int DefaultObjectCount => 300;
        public override LiveStat[] LiveStats => STATS;
        public override double MeshMemoryMb => _meshMb;
        public override BenchmarkVariant[] SupportedVariants => new[] { BenchmarkVariant.A, BenchmarkVariant.B, BenchmarkVariant.C, BenchmarkVariant.D };
        public override BenchmarkVariant PrimaryB => BenchmarkVariant.B;
        public override bool IsEducationalVariant(BenchmarkVariant v) => v == BenchmarkVariant.C;
        public override string PreviewWarning => CurrentVariant == BenchmarkVariant.C ? "GIÁO DỤC · LOD theo dải bán kính (mô phỏng khoảng cách)" : "";
        public int[] LodRendererCounts => _lodCounts;

        public override string[] QuickTips => new[]
        {
            $"Cùng hình cầu: Thấp ≈{LOW_TRIS} tam giác, Cao ≈{HIGH_TRIS}. Số tam giác khác nhau trăm lần.",
            "Normal phẳng (cạnh cứng) cần tách đỉnh: cùng số tam giác nhưng nhiều đỉnh hơn.",
            "LODGroup: vật xa dùng mesh thấp. Camera gần vẫn thấy mesh cao nên chất lượng phải xét riêng.",
        };

        private static readonly TutorialStep[] STEPS =
        {
            new TutorialStep("Bài 7: cùng hình cầu, mesh khác nhau.\nXem GPU và CPU vẽ.", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("Bấm Thấp: mỗi khối ~120 tam giác.", TutorialTarget.PreviewA, TutorialWait.Preview, 0),
            new TutorialStep("GPU {gpu} ms · CPU vẽ {cpurender} ms.\nMesh {meshmb} MB.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Cao: mỗi khối ~9.8k tam giác.", TutorialTarget.PreviewB, TutorialWait.Preview, 1),
            new TutorialStep("Hình gần như y hệt, nhưng GPU {gpu} ms\nvà mesh {meshmb} MB: so với Thấp.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm LOD: giữa lưới dùng mesh cao,\nrìa dùng mesh thấp.", TutorialTarget.PreviewC, TutorialWait.Preview, 2),
            new TutorialStep("Mesh {meshmb} MB (đủ 3 mức). GPU {gpu} ms:\ngiảm tải vật xa, đổi thêm bộ nhớ.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Cạnh cứng: cùng số tam giác như Cao,\nnhưng đỉnh bị tách.", TutorialTarget.None, TutorialWait.Preview, 3),
            new TutorialStep("Mesh to hơn rõ rệt: cạnh cứng/UV seam\nnhân đỉnh. Đo trên máy thật.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Cao để về cảnh chuẩn.", TutorialTarget.PreviewB, TutorialWait.Preview, 1),
        };

        public override TutorialStep[] TutorialSteps => STEPS;
        public override string TutorialCompleteText => "Tam giác, đỉnh và LOD: đo rồi mới chọn.";

        public override string ShortVariantName(BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? "Thấp" : v == BenchmarkVariant.B ? "Cao" : v == BenchmarkVariant.C ? "LOD" : "Cạnh cứng";

        public override string VariantKey(BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? "LowPoly" : v == BenchmarkVariant.B ? "HighPoly" : v == BenchmarkVariant.C ? "LODGroup" : "HighPolyHardEdges";

        public override string DescribeVariant(BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? $"Cầu UV {LOW_SEGMENTS}x{LOW_RINGS}: {LOW_TRIS} tam giác, normal mượt."
            : v == BenchmarkVariant.B ? $"Cầu UV {HIGH_SEGMENTS}x{HIGH_RINGS}: {HIGH_TRIS} tam giác, normal mượt."
            : v == BenchmarkVariant.C ? "LODGroup 3 mức (cao/trung/thấp); mức chọn theo dải bán kính từ tâm lưới vì camera ortho không có khoảng cách. Giáo dục."
            : $"Cầu UV {HIGH_SEGMENTS}x{HIGH_RINGS} với normal phẳng: cùng {HIGH_TRIS} tam giác như B nhưng đỉnh bị tách (3 đỉnh/tam giác).";

        public override string ExplainText =>
            "Chi phí hình học tăng theo số đỉnh (vertex shader, bộ nhớ, băng thông) và số tam giác (rasterizer). " +
            "Cùng một hình cầu có thể dựng bằng ~120, ~1k hoặc ~9.8k tam giác; ở khoảng cách xa trông gần như giống nhau, nhưng gần thì khác rõ. " +
            "LODGroup thay mesh theo kích thước trên màn hình: vật xa dùng mesh thấp, đổi lại phải lưu cả 3 mức và có thể thấy hiện tượng 'nhảy' mức. " +
            "Cạnh cứng (normal phẳng) và UV seam buộc nhân đôi đỉnh: cùng số tam giác nhưng nhiều vertex record hơn. " +
            "Lab dùng shader Lit đơn giản, một đèn, không shadow để nhìn riêng hình học; SRP Batcher bật, dynamic batching tắt, thoát Play sẽ trả lại. " +
            "Variant LOD chọn mức theo dải bán kính (mô phỏng khoảng cách) vì camera orthographic cố định không có khoảng cách thật. Không kết luận nguyên nhân khi chưa profiling.";

        #endregion

        #region Hooks

        public override void Setup(BenchmarkVariant variant, int seed)
        {
            EnsureMeshes();
            mesh = variant == BenchmarkVariant.A ? _low : variant == BenchmarkVariant.D ? _highHard : _high;
            System.Array.Clear(_lodCounts, 0, _lodCounts.Length);
            _lodGroups = 0;
            base.Setup(variant, seed);
            _meshMb = MeshMegabytes(variant);
        }

        protected override int SlotCount => 1;

        protected override void ApplyVariantGlobals(BenchmarkVariant v)
        {
            if (!_saved)
            {
                _originalSrp = GraphicsSettings.useScriptableRenderPipelineBatching;
                _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (_urp != null) { _originalDynamic = _urp.supportsDynamicBatching; _originalAssetSrp = _urp.useSRPBatcher; }
                _saved = true;
            }
            GraphicsSettings.useScriptableRenderPipelineBatching = true;
            if (_urp != null) { _urp.useSRPBatcher = true; _urp.supportsDynamicBatching = false; }
        }

        protected override void AfterSpawn(GameObject root, MeshRenderer[] renderers)
        {
            if (CurrentVariant != BenchmarkVariant.C) return;
            int n = Mathf.Clamp(ObjectCount, 0, maxObjectCount);
            if (n == 0) return;
            Vector3 center = Vector3.zero;
            for (int i = 0; i < n; i++) center += renderers[i].transform.localPosition;
            center /= n;
            float radius = 0.0001f;
            for (int i = 0; i < n; i++) radius = Mathf.Max(radius, Flat(renderers[i].transform.localPosition - center).magnitude);

            for (int i = 0; i < n; i++)
            {
                MeshRenderer top = renderers[i];
                float d = Flat(top.transform.localPosition - center).magnitude / radius;
                int level = d < BAND_NEAR ? 0 : d < BAND_MID ? 1 : 2;
                MeshRenderer mid = MakeLodChild(top, "LOD1", _medium);
                MeshRenderer low = MakeLodChild(top, "LOD2", _low);
                var group = top.gameObject.AddComponent<LODGroup>();
                group.SetLODs(new[]
                {
                    new LOD(0.5f, new Renderer[] { top }),
                    new LOD(0.2f, new Renderer[] { mid }),
                    new LOD(0.02f, new Renderer[] { low }),
                });
                group.RecalculateBounds();
                group.ForceLOD(level);
                _lodCounts[level]++;
                _lodGroups++;
            }
        }

        protected override BenchmarkStatus ValidateVariant(out string reason)
        {
            reason = "";
            Material mat = base.MaterialFor(0, CurrentVariant);
            int lowTris = TrisOf(_low), medTris = TrisOf(_medium), highTris = TrisOf(_high), hardTris = TrisOf(_highHard);
            if (_urp == null) reason = "Không có URP asset.";
            else if (!GraphicsSettings.useScriptableRenderPipelineBatching || !_urp.useSRPBatcher) reason = "SRP Batcher phải bật ở mọi variant.";
            else if (_urp.supportsDynamicBatching) reason = "Dynamic batching phải tắt (mesh thấp có thể bị ghép, làm lệch so sánh).";
            else if (mat == null || mat.enableInstancing) reason = "Cần 1 material, instancing tắt.";
            else if (lowTris < 80 || lowTris > 150) reason = $"Mesh thấp ≈100 tam giác, đang {lowTris}.";
            else if (medTris < 700 || medTris > 1300) reason = $"Mesh trung ≈1k tam giác, đang {medTris}.";
            else if (highTris < 8000 || highTris > 12000) reason = $"Mesh cao ≈10k tam giác, đang {highTris}.";
            else if (hardTris != highTris) reason = "Cạnh cứng phải cùng số tam giác với mesh cao.";
            else if (_highHard.vertexCount <= _high.vertexCount) reason = "Cạnh cứng phải có nhiều đỉnh hơn mesh mượt.";
            else if (CurrentVariant == BenchmarkVariant.C && _lodGroups != Mathf.Min(ObjectCount, maxObjectCount)) reason = "Mỗi khối phải có một LODGroup.";
            else if (CurrentVariant != BenchmarkVariant.C && _lodGroups != 0) reason = "Chỉ variant LOD được có LODGroup.";
            return reason.Length == 0 ? BenchmarkStatus.Valid : BenchmarkStatus.Invalid;
        }

        protected override void AddVariantSettings(BenchmarkVariant v, IDictionary<string, string> into)
        {
            EnsureMeshes();
            Mesh used = v == BenchmarkVariant.A ? _low : v == BenchmarkVariant.D ? _highHard : _high;
            into["mesh"] = $"{used.name} ({used.vertexCount} verts, {TrisOf(used)} tris)";
            into["mesh_triangles"] = TrisOf(used).ToString();
            into["mesh_vertices"] = used.vertexCount.ToString();
            into["normals"] = v == BenchmarkVariant.D ? "flat (hard edges, split vertices)" : "smooth (shared vertices)";
            into["lod"] = v == BenchmarkVariant.C
                ? $"LODGroup 3 levels ({TrisOf(_high)}/{TrisOf(_medium)}/{TrisOf(_low)} tris); level by radius band {BAND_NEAR}/{BAND_MID} (ForceLOD, simulated distance)"
                : "none";
            into["srp_batcher"] = "true";
            into["dynamic_batching"] = "false";
            into["gpu_instancing"] = "false";
            into["shader_cost"] = "URP/Lit, 1 directional light, no shadows";
        }

        public override void Teardown()
        {
            base.Teardown();
            _lodGroups = 0;
            _meshMb = 0;
            System.Array.Clear(_lodCounts, 0, _lodCounts.Length);
        }

        protected override void RestoreGlobals()
        {
            if (!_saved) return;
            GraphicsSettings.useScriptableRenderPipelineBatching = _originalSrp;
            if (_urp != null) { _urp.supportsDynamicBatching = _originalDynamic; _urp.useSRPBatcher = _originalAssetSrp; }
            _saved = false;
        }

        private void OnDestroy()
        {
            if (IsSetUp) Teardown();
            else RestoreGlobals();
            foreach (Mesh m in new[] { _low, _medium, _high, _highHard }) if (m != null) Destroy(m);
            _low = _medium = _high = _highHard = null;
        }

        #endregion

        #region Private

        private void EnsureMeshes()
        {
            if (_high != null) return;
            _low = ProceduralSphereMesh.Create("Sphere_Low", LOW_SEGMENTS, LOW_RINGS, false);
            _medium = ProceduralSphereMesh.Create("Sphere_Medium", MED_SEGMENTS, MED_RINGS, false);
            _high = ProceduralSphereMesh.Create("Sphere_High", HIGH_SEGMENTS, HIGH_RINGS, false);
            _highHard = ProceduralSphereMesh.Create("Sphere_High_HardEdges", HIGH_SEGMENTS, HIGH_RINGS, true);
        }

        private static int TrisOf(Mesh m) => m != null ? (int)(m.GetIndexCount(0) / 3) : 0;

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static MeshRenderer MakeLodChild(MeshRenderer parent, string name, Mesh lodMesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = lodMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = parent.sharedMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        /// <summary>Bộ nhớ mesh duy nhất của variant (xấp xỉ: 32 B/đỉnh + chỉ số). LOD tính đủ 3 mức.</summary>
        private double MeshMegabytes(BenchmarkVariant v)
        {
            double bytes = 0;
            if (v == BenchmarkVariant.C) bytes = Bytes(_high) + Bytes(_medium) + Bytes(_low);
            else bytes = Bytes(v == BenchmarkVariant.A ? _low : v == BenchmarkVariant.D ? _highHard : _high);
            return bytes / (1024.0 * 1024.0);
        }

        private static double Bytes(Mesh m) =>
            m.vertexCount * (double)BYTES_PER_VERTEX + m.GetIndexCount(0) * (m.indexFormat == IndexFormat.UInt32 ? 4.0 : 2.0);

        #endregion
    }
}
