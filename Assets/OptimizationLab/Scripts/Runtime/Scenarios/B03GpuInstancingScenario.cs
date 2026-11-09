using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OptimizationLab.Scenarios
{
    /// <summary>
    /// B03 — GPU Instancing (automatic, qua MeshRenderer). Biến độc lập: <c>Material.enableInstancing</c>.
    /// Cùng mesh, cùng MỘT màu, cùng shader URP/Lit; A = material tắt instancing, B = material bật instancing.
    /// C (giáo dục) = bật instancing nhưng mỗi vật một bản sao material (cùng màu) → không vật nào vẽ chung được.
    /// SRP Batcher và dynamic batching tắt ở mọi variant: khi SRP Batcher bật, URP ưu tiên SRP Batcher và
    /// bỏ qua instancing của MeshRenderer, nên không còn so được instancing.
    /// </summary>
    public sealed class B03GpuInstancingScenario : GridScenarioBase
    {
        #region Fields

        [Tooltip("Material URP/Lit giống hệt 'materials[0]' nhưng bật Enable GPU Instancing (variant B, C).")]
        [SerializeField] private Material instancedMaterial;

        private static readonly int[] COUNTS = { 300, 1000, 3000 };
        private static readonly LiveStat[] STATS = { LiveStat.DrawCalls, LiveStat.Batches, LiveStat.CpuRenderMs };

        private readonly List<Material> _clones = new List<Material>();
        private bool _saved, _originalSrp, _originalAssetSrp, _originalDynamic;
        private UniversalRenderPipelineAsset _urp;

        #endregion

        #region Public

        public override string Id => "B03";
        public override string Title => "GPU Instancing On / Off";
        public override string ShortTitle => "Bài 4 · Vẽ nhiều bản sao";
        public override string IndependentVariable => "Material.enableInstancing";
        public override string Hypothesis => "Nhiều vật cùng mesh + cùng material: instancing gộp chúng vào ít lệnh vẽ hơn, CPU chuẩn bị nhẹ hơn.";
        public override int[] ObjectCountOptions => COUNTS;
        public override int DefaultObjectCount => 1000;
        public override LiveStat[] LiveStats => STATS;
        public override BenchmarkVariant[] SupportedVariants => new[] { BenchmarkVariant.A, BenchmarkVariant.B, BenchmarkVariant.C };
        public override bool IsEducationalVariant(BenchmarkVariant v) => v == BenchmarkVariant.C;
        public override string PreviewWarning => CurrentVariant == BenchmarkVariant.C ? "GIÁO DỤC · mỗi khối một material, không dùng để so tốc độ" : "";

        public override string[] QuickTips => new[]
        {
            "Instancing: cùng mesh + cùng material → vẽ nhiều bản sao trong một lệnh.",
            "Khác mesh hoặc khác material thì không vẽ chung. SRP Batcher bật thì URP bỏ qua instancing của MeshRenderer.",
            "Bật thật: chọn material ▸ Inspector ▸ tick Enable GPU Instancing.",
        };

        private static readonly TutorialStep[] STEPS =
        {
            new TutorialStep("Bài 4: vẽ nhiều bản sao giống nhau.\nCùng khối, cùng màu.", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("Bấm Thường: mỗi khối một lệnh vẽ.", TutorialTarget.PreviewA, TutorialWait.Preview, 0),
            new TutorialStep("{draws} lần vẽ · {batches} batches.\nCPU vẽ {cpurender} ms.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Instancing: material bật\nEnable GPU Instancing.", TutorialTarget.PreviewB, TutorialWait.Preview, 1),
            new TutorialStep("Còn {batches} batches. Cảnh y hệt!\nMột lệnh vẽ nhiều bản sao.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Muốn soi tận nơi: thoát hướng dẫn,\nmở Tools ▸ Optimization Lab ▸ Tutorial ▸ B03.", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("Bấm Khác material: mỗi khối một\nbản sao material, cùng màu.", TutorialTarget.PreviewC, TutorialWait.Preview, 2),
            new TutorialStep("Batches vọt lên {batches}: khác material\nthì không vẽ chung được.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Instancing để về cảnh chuẩn.", TutorialTarget.PreviewB, TutorialWait.Preview, 1),
            new TutorialStep("Áp dụng thật? Thoát Play →\nTutorial ▸ B03 ▸ Áp dụng vào project thật.", TutorialTarget.None, TutorialWait.Next),
        };

        public override TutorialStep[] TutorialSteps => STEPS;
        public override string TutorialCompleteText => "Instancing: cùng mesh + cùng material.";

        public override string ShortVariantName(BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? "Thường" : v == BenchmarkVariant.B ? "Instancing" : "Khác material";

        public override string VariantKey(BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? "InstancingOff" : v == BenchmarkVariant.B ? "InstancingOn" : "InstancingUniqueMaterials";

        public override string DescribeVariant(BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? "MeshRenderer, 1 material, Enable GPU Instancing tắt."
            : v == BenchmarkVariant.B ? "MeshRenderer, 1 material, Enable GPU Instancing bật (instancing tự động)."
            : "Như B nhưng mỗi vật một bản sao material (cùng màu): giáo dục, không so hiệu năng.";

        public override string ExplainText =>
            "GPU instancing gửi một mesh + một material kèm danh sách vị trí, GPU vẽ nhiều bản sao trong một lệnh. " +
            "Điều kiện: cùng mesh, cùng material, shader hỗ trợ instancing, material tick Enable GPU Instancing. " +
            "Trong URP, SRP Batcher được ưu tiên hơn instancing cho MeshRenderer, nên lab tắt SRP Batcher và dynamic batching ở mọi variant để nhìn riêng instancing; thoát Play sẽ trả lại. " +
            "Cách tự vẽ bằng code (Graphics.RenderMeshInstanced) không có collider, chọn vật hay culling từng vật như MeshRenderer. " +
            "Variant C cho mỗi vật một bản sao material để thấy instancing không gộp được vật khác material.";

        #endregion

        #region Hooks

        protected override Material MaterialFor(int slot, BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? base.MaterialFor(slot, v) : instancedMaterial;

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
            if (_urp != null) { _urp.supportsDynamicBatching = false; _urp.useSRPBatcher = false; }
        }

        /// <summary>C: mỗi object một bản sao material instancing (tạo một lần mỗi lượt setup, huỷ ở Teardown).</summary>
        protected override Material MaterialForObject(int index, int slot, BenchmarkVariant v)
        {
            if (v != BenchmarkVariant.C || instancedMaterial == null) return MaterialFor(slot, v);
            while (_clones.Count <= index)
                _clones.Add(new Material(instancedMaterial) { name = instancedMaterial.name + "_Copy" + _clones.Count });
            return _clones[index];
        }

        protected override BenchmarkStatus ValidateVariant(out string reason)
        {
            reason = "";
            Material plain = base.MaterialFor(0, BenchmarkVariant.A);
            if (instancedMaterial == null || plain == null) reason = "Thiếu material thường hoặc material instancing.";
            else if (plain.enableInstancing) reason = "Material thường (A) phải tắt instancing.";
            else if (!instancedMaterial.enableInstancing) reason = "Material instancing (B/C) phải bật Enable GPU Instancing.";
            else if (plain.shader != instancedMaterial.shader) reason = "Hai material phải cùng shader.";
            else if (!SystemInfo.supportsInstancing) reason = "Thiết bị không hỗ trợ GPU instancing.";
            else if (GraphicsSettings.useScriptableRenderPipelineBatching || (_urp != null && _urp.supportsDynamicBatching))
                reason = "SRP/dynamic batching phải tắt ở mọi variant.";
            else if (CurrentVariant == BenchmarkVariant.C && _clones.Count < Mathf.Min(ObjectCount, maxObjectCount))
                reason = "Variant C phải có một material riêng cho mỗi khối.";
            return reason.Length == 0 ? BenchmarkStatus.Valid : BenchmarkStatus.Invalid;
        }

        protected override void AddVariantSettings(BenchmarkVariant v, IDictionary<string, string> into)
        {
            into["gpu_instancing"] = v == BenchmarkVariant.A ? "false" : "true";
            into["instancing_path"] = v == BenchmarkVariant.A ? "none" : "automatic (MeshRenderer + Material.enableInstancing)";
            into["srp_batcher"] = "false";
            into["dynamic_batching"] = "false";
            into["material_mode"] = v == BenchmarkVariant.C ? "one material copy per object (same color, instancing on)" : "one shared material (sharedMaterial)";
        }

        public override void Teardown()
        {
            base.Teardown();
            foreach (Material m in _clones) if (m != null) Destroy(m);
            _clones.Clear();
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
