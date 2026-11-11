using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OptimizationLab.Scenarios
{
    /// <summary>
    /// B04 — Dynamic Batching On/Off (URP). Biến độc lập: <c>UniversalRenderPipelineAsset.supportsDynamicBatching</c>.
    /// Cùng một shared material URP/Lit (instancing tắt), mesh nhỏ (Cube, 24 đỉnh &lt; 300).
    /// A = tắt, B = bật. C (giáo dục) = bật nhưng mesh Sphere 515 đỉnh vượt giới hạn 300 đỉnh → không ghép được.
    /// SRP Batcher tắt ở mọi variant: khi SRP Batcher bật, URP đi đường SRP Batcher nên dynamic batching không còn so được.
    /// </summary>
    public sealed class B04DynamicBatchingScenario : GridScenarioBase
    {
        #region Fields

        /// <summary>Giới hạn đỉnh của một mesh để Unity dynamic-batch (tài liệu Unity: 300 đỉnh).</summary>
        public const int DYNAMIC_BATCH_VERTEX_LIMIT = 300;

        [Tooltip("Mesh > 300 đỉnh (Sphere) cho variant C: bật dynamic batching vẫn không ghép được.")]
        [SerializeField] private Mesh heavyMesh;

        private static readonly int[] COUNTS = { 500, 1000, 1500 };
        private static readonly LiveStat[] STATS = { LiveStat.DrawCalls, LiveStat.Batches, LiveStat.CpuRenderMs };

        private bool _saved, _originalSrp, _originalAssetSrp, _originalDynamic;
        private UniversalRenderPipelineAsset _urp;

        #endregion

        #region Public

        public override string Id => "B04";
        public override string Title => "Dynamic Batching On / Off";
        public override string ShortTitle => "Bài 5 · Ghép vật nhỏ";
        public override string IndependentVariable => "UniversalRenderPipelineAsset.supportsDynamicBatching";
        public override string Hypothesis => "Dynamic batching ghép mesh nhỏ cùng material thành ít lệnh vẽ hơn; đổi lại CPU phải tự biến đổi đỉnh mỗi frame, nên chưa chắc nhanh hơn.";
        public override int[] ObjectCountOptions => COUNTS;
        public override int DefaultObjectCount => 1000;
        public override LiveStat[] LiveStats => STATS;
        public override BenchmarkVariant[] SupportedVariants => new[] { BenchmarkVariant.A, BenchmarkVariant.B, BenchmarkVariant.C };
        public override bool IsEducationalVariant(BenchmarkVariant v) => v == BenchmarkVariant.C;
        public override string PreviewWarning => CurrentVariant == BenchmarkVariant.C ? "GIÁO DỤC · mesh quá lớn để ghép, không dùng để so tốc độ" : "";

        public override string[] QuickTips => new[]
        {
            "Dynamic batching ghép mesh NHỎ (dưới 300 đỉnh) cùng material, mỗi frame, bằng CPU.",
            "Ít lệnh vẽ hơn chưa chắc nhanh hơn: CPU phải biến đổi đỉnh. Trên máy mạnh có thể không lợi.",
            "Bật thật: URP Asset ▸ Rendering ▸ Dynamic Batching. SRP Batcher bật thì URP bỏ qua dynamic batching.",
        };

        private static readonly TutorialStep[] STEPS =
        {
            new TutorialStep("Bài 5: ghép các vật nhỏ.\nCùng khối, cùng màu.", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("Bấm Tắt: mỗi khối một lệnh vẽ.", TutorialTarget.PreviewA, TutorialWait.Preview, 0),
            new TutorialStep("{draws} lần vẽ · {batches} batches.\nCPU vẽ {cpurender} ms.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Bật: Unity ghép các khối nhỏ\nthành ít lệnh vẽ.", TutorialTarget.PreviewB, TutorialWait.Preview, 1),
            new TutorialStep("Còn {batches} batches. Cảnh y hệt!\nNhưng CPU vẽ {cpurender} ms: so với lúc Tắt.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Ít lệnh vẽ KHÔNG đảm bảo nhanh hơn.\nCPU tự biến đổi đỉnh mỗi frame.", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("Bấm Mesh lớn: khối cầu 515 đỉnh,\nvượt giới hạn 300 đỉnh.", TutorialTarget.PreviewC, TutorialWait.Preview, 2),
            new TutorialStep("Batches quay lại {batches}: mesh quá\nlớn thì dù bật cũng không ghép.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Bật để về cảnh chuẩn.", TutorialTarget.PreviewB, TutorialWait.Preview, 1),
            new TutorialStep("Muốn dùng thật? Đo trên máy thật\ntrước. SRP Batcher thường đã đủ.", TutorialTarget.None, TutorialWait.Next),
        };

        public override TutorialStep[] TutorialSteps => STEPS;
        public override string TutorialCompleteText => "Dynamic batching: mesh nhỏ, nhưng phải đo.";

        public override string ShortVariantName(BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? "Tắt" : v == BenchmarkVariant.B ? "Bật" : "Mesh lớn";

        public override string VariantKey(BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? "DynamicBatchingOff" : v == BenchmarkVariant.B ? "DynamicBatchingOn" : "DynamicBatchingOnHeavyMesh";

        public override string DescribeVariant(BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? "Dynamic batching OFF, Cube 24 đỉnh, 1 material, SRP Batcher tắt."
            : v == BenchmarkVariant.B ? "Dynamic batching ON, Cube 24 đỉnh, 1 material, SRP Batcher tắt."
            : "Dynamic batching ON nhưng mesh Sphere 515 đỉnh (> 300): không ghép được. Giáo dục, không so hiệu năng.";

        public override string ExplainText =>
            "Dynamic batching gom các mesh nhỏ dùng chung material thành một lệnh vẽ: CPU biến đổi đỉnh của từng vật sang không gian thế giới rồi ghi vào bộ đệm chung mỗi frame. " +
            "Chỉ áp dụng với mesh nhỏ (dưới 300 đỉnh), cùng material, không scale âm hay không đều. " +
            "Số lệnh vẽ giảm nhưng CPU tốn thêm cho việc biến đổi đỉnh, nên có thể không nhanh hơn, nhất là khi mesh nhiều đỉnh hoặc máy khoẻ. Đừng kết luận 'dynamic luôn tối ưu'. " +
            "Trong URP, SRP Batcher được ưu tiên: lab tắt SRP Batcher ở mọi variant để nhìn riêng dynamic batching; thoát Play sẽ trả lại. " +
            "Instancing cũng tắt vì nó được ưu tiên hơn dynamic batching. Variant C dùng Sphere 515 đỉnh để thấy giới hạn 300 đỉnh.";

        #endregion

        #region Hooks

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
            GraphicsSettings.useScriptableRenderPipelineBatching = false;
            if (_urp != null)
            {
                _urp.useSRPBatcher = false;
                _urp.supportsDynamicBatching = v != BenchmarkVariant.A;
            }
        }

        protected override void AfterSpawn(GameObject root, MeshRenderer[] renderers)
        {
            if (CurrentVariant != BenchmarkVariant.C || heavyMesh == null) return;
            int n = Mathf.Clamp(ObjectCount, 0, maxObjectCount);
            for (int i = 0; i < n; i++) renderers[i].GetComponent<MeshFilter>().sharedMesh = heavyMesh;
        }

        protected override BenchmarkStatus ValidateVariant(out string reason)
        {
            reason = "";
            bool wantDynamic = CurrentVariant != BenchmarkVariant.A;
            Mesh light = mesh;
            Material mat = base.MaterialFor(0, CurrentVariant);
            if (_urp == null) reason = "Không có URP asset: không điều khiển được dynamic batching.";
            else if (_urp.supportsDynamicBatching != wantDynamic) reason = $"Dynamic batching = {_urp.supportsDynamicBatching}, mong đợi {wantDynamic}.";
            else if (GraphicsSettings.useScriptableRenderPipelineBatching || _urp.useSRPBatcher) reason = "SRP Batcher phải tắt ở mọi variant.";
            else if (mat == null) reason = "Thiếu material.";
            else if (mat.enableInstancing) reason = "GPU instancing phải tắt (nó được ưu tiên hơn dynamic batching).";
            else if (light == null || light.vertexCount >= DYNAMIC_BATCH_VERTEX_LIMIT) reason = $"Mesh A/B phải dưới {DYNAMIC_BATCH_VERTEX_LIMIT} đỉnh.";
            else if (CurrentVariant == BenchmarkVariant.C && (heavyMesh == null || heavyMesh.vertexCount <= DYNAMIC_BATCH_VERTEX_LIMIT))
                reason = $"Mesh variant C phải trên {DYNAMIC_BATCH_VERTEX_LIMIT} đỉnh.";
            return reason.Length == 0 ? BenchmarkStatus.Valid : BenchmarkStatus.Invalid;
        }

        protected override void AddVariantSettings(BenchmarkVariant v, IDictionary<string, string> into)
        {
            Mesh used = v == BenchmarkVariant.C && heavyMesh != null ? heavyMesh : mesh;
            into["mesh"] = used != null ? $"{used.name} ({used.vertexCount} verts)" : "none";
            into["dynamic_batching"] = v == BenchmarkVariant.A ? "false" : "true";
            into["dynamic_batch_vertex_limit"] = DYNAMIC_BATCH_VERTEX_LIMIT.ToString();
            into["srp_batcher"] = "false";
            into["gpu_instancing"] = "false";
            into["mesh_batchable"] = v == BenchmarkVariant.C ? "false (over vertex limit)" : "true";
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
        }

        #endregion
    }
}
