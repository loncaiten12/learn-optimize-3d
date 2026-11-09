using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OptimizationLab.Scenarios
{
    /// <summary>Same geometry/materials in A/B; combine during setup, never inside measurement.</summary>
    public sealed class B02StaticBatchingScenario : GridScenarioBase
    {
        private static readonly int[] COUNTS = { 600, 1000, 1500 };
        private static readonly LiveStat[] STATS = { LiveStat.DrawCalls, LiveStat.CpuRenderMs, LiveStat.MeshMemoryMb };
        private readonly HashSet<Mesh> _ownedMeshes = new HashSet<Mesh>();
        private MeshRenderer[] _fixture;
        private Matrix4x4[] _matrices;
        private int _fixtureCount;
        private int _combined;
        private double _meshMb;
        private bool _saved, _originalSrp, _originalAssetSrp, _originalDynamic;
        private UniversalRenderPipelineAsset _urp;
        public override string Id => "B02";
        public override string Title => "Static Batching On / Off";
        public override string ShortTitle => "Bài 3 · Ghép vật đứng yên";
        public override string IndependentVariable => "StaticBatchingUtility.Combine";
        public override string Hypothesis => "Static batching giảm chi phí CPU, đổi lại lưu thêm dữ liệu mesh.";
        public override int[] ObjectCountOptions => COUNTS;
        public override int DefaultObjectCount => 1000;
        public override LiveStat[] LiveStats => STATS;
        public override double MeshMemoryMb => _meshMb;
        public override BenchmarkVariant[] SupportedVariants => new[] { BenchmarkVariant.A, BenchmarkVariant.B, BenchmarkVariant.C };
        public override bool IsEducationalVariant(BenchmarkVariant v) => v == BenchmarkVariant.C;
        public override string PreviewWarning => CurrentVariant == BenchmarkVariant.C ? "KHÔNG HỢP LỆ · Vật đã di chuyển" : "";
        public int CombinedRendererCount => _combined;
        public override string[] QuickTips => new[] {
            "Ghép dữ liệu mesh của vật đứng yên: CPU có thể nhẹ hơn.",
            "Đổi lại: bộ nhớ mesh tăng. Không hứa chỉ còn một lần vẽ.",
            "Trong Editor: chọn vật → Static ▾ → Batching Static. Không di chuyển từng vật sau khi ghép."
        };
        private static readonly TutorialStep[] STEPS = {
            new TutorialStep("Bài 3: ghép vật đứng yên.\nThử giảm CPU, đổi thêm bộ nhớ.", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("Bấm Rời để xem trước khi ghép.", TutorialTarget.PreviewA, TutorialWait.Preview, 0),
            new TutorialStep("{draws} lần vẽ · CPU {cpurender} ms.\nMesh đang dùng {meshmb} MB.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Kiểm Rời trong Frame Debugger:\nTutorial ▸ B02 ▸ Thực hành.", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("Bấm Ghép: gom dữ liệu mesh.\nCảnh vẫn giống hệt!", TutorialTarget.PreviewB, TutorialWait.Preview, 1),
            new TutorialStep("Xem CPU và số lần vẽ thay đổi.\nMesh giờ dùng {meshmb} MB.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Disable debugger → chọn Ghép →\nEnable lại. So mesh và draw với Rời.", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("Bấm Di chuyển để thử phá luật.\nVật đã ghép phải đứng yên.", TutorialTarget.PreviewC, TutorialWait.Preview, 2),
            new TutorialStep("KHÔNG HỢP LỆ: đã di chuyển vật.\nKhông dùng lượt này để so tốc độ.", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("Bấm Ghép để trở lại cảnh hợp lệ.", TutorialTarget.PreviewB, TutorialWait.Preview, 1),
            new TutorialStep("Áp dụng thật? Thoát Play →\nTutorial ▸ B02 ▸ Ngoài game.", TutorialTarget.None, TutorialWait.Next)
        };
        public override TutorialStep[] TutorialSteps => STEPS;
        public override string TutorialCompleteText => "Vật đứng yên: cân nhắc CPU và bộ nhớ.";
        public override string ShortVariantName(BenchmarkVariant v) => v == BenchmarkVariant.A ? "Rời" : v == BenchmarkVariant.B ? "Ghép" : "Di chuyển";
        public override string VariantKey(BenchmarkVariant v) => v == BenchmarkVariant.A ? "StaticBatchingOff" : v == BenchmarkVariant.B ? "StaticBatchingOn" : "StaticObjectsMoved";
        public override string DescribeVariant(BenchmarkVariant v) => v == BenchmarkVariant.A ? "1000 khối, 4 shared material, không combine." : v == BenchmarkVariant.B ? "Combine trong setup; giữ nguyên cảnh và material." : "Đã combine rồi đổi transform một vật: INVALID.";
        public override string ExplainText => "Ghép dữ liệu mesh tĩnh giúp giảm overhead CPU, nhưng sao chép geometry tốn bộ nhớ. " +
            "Số draw phụ thuộc material/pass/culling. SRP Batcher và dynamic batching tắt ở cả A/B để cô lập static batching. " +
            "Mesh MB là tổng bộ nhớ runtime của các mesh duy nhất thuộc fixture, kể cả mesh nguồn; không phải tổng RAM. " +
            "Variant C luôn INVALID. Ngoài Editor: Batching Static + Player Settings > Static Batching, kiểm trong Frame Debugger.";

        protected override void ApplyVariantGlobals(BenchmarkVariant v)
        {
            if (!_saved) {
                _originalSrp = GraphicsSettings.useScriptableRenderPipelineBatching;
                _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (_urp != null) { _originalDynamic = _urp.supportsDynamicBatching; _originalAssetSrp = _urp.useSRPBatcher; }
                _saved = true;
            }
            GraphicsSettings.useScriptableRenderPipelineBatching = false;
            if (_urp != null) { _urp.supportsDynamicBatching = false; _urp.useSRPBatcher = false; }
        }
        protected override void AfterSpawn(GameObject root, MeshRenderer[] renderers)
        {
            _fixture = renderers;
            _fixtureCount = Mathf.Clamp(ObjectCount, 0, maxObjectCount);
            _matrices = new Matrix4x4[_fixtureCount];
            for (int i = 0; i < _fixtureCount; i++) _matrices[i] = renderers[i].transform.localToWorldMatrix;
            if (CurrentVariant != BenchmarkVariant.A) StaticBatchingUtility.Combine(root);
            _combined = 0;
            var unique = new HashSet<Mesh> { mesh };
            for (int i = 0; i < _fixtureCount; i++) {
                if (renderers[i].isPartOfStaticBatch) _combined++;
                Mesh m = renderers[i].GetComponent<MeshFilter>().sharedMesh;
                if (m == null) continue;
                unique.Add(m);
                if (m != mesh) _ownedMeshes.Add(m);
            }
            long bytes = 0;
            foreach (Mesh m in unique) if (m != null) bytes += Profiler.GetRuntimeMemorySizeLong(m);
            _meshMb = bytes / (1024.0 * 1024.0);
            if (CurrentVariant == BenchmarkVariant.C && _fixtureCount > 0)
                renderers[0].transform.localPosition += Vector3.up * 2f;
        }
        protected override BenchmarkStatus ValidateVariant(out string reason)
        {
            reason = "";
            if (CurrentVariant == BenchmarkVariant.C) reason = "StaticObjectsMoved: đổi transform sau combine — INVALID.";
            else if (GraphicsSettings.useScriptableRenderPipelineBatching || (_urp != null && _urp.supportsDynamicBatching)) reason = "SRP/dynamic batching phải tắt ở cả A/B.";
            else if (CurrentVariant == BenchmarkVariant.B && _combined != _fixtureCount) reason = $"Chưa combine đủ: {_combined}/{_fixtureCount}.";
            else if (CurrentVariant == BenchmarkVariant.A && _combined != 0) reason = "A không được static batch.";
            else for (int i = 0; i < _fixtureCount; i++) {
                if (_fixture[i].transform.localToWorldMatrix != _matrices[i]) { reason = "Transform đã đổi sau setup."; break; }
                if (_fixture[i].sharedMaterial.enableInstancing) { reason = "GPU instancing phải tắt."; break; }
            }
            return reason.Length == 0 ? BenchmarkStatus.Valid : BenchmarkStatus.Invalid;
        }
        protected override void AddVariantSettings(BenchmarkVariant v, IDictionary<string, string> into)
        {
            into["static_batching"] = v == BenchmarkVariant.A ? "false" : "true";
            into["srp_batcher"] = "false";
            into["dynamic_batching"] = "false";
            into["fixture_motion"] = v == BenchmarkVariant.C ? "moved_after_combine_INVALID" : "none";
        }
        public override void Teardown()
        {
            base.Teardown();
            foreach (Mesh m in _ownedMeshes) if (m != null) Destroy(m);
            _ownedMeshes.Clear();
            _fixture = null;
            _matrices = null;
        }
        private void OnDestroy()
        {
            if (IsSetUp) Teardown();
            else RestoreGlobals();
        }

        protected override void RestoreGlobals()
        {
            if (!_saved) return;
            GraphicsSettings.useScriptableRenderPipelineBatching = _originalSrp;
            if (_urp != null) { _urp.supportsDynamicBatching = _originalDynamic; _urp.useSRPBatcher = _originalAssetSrp; }
            _saved = false;
        }
    }
}
