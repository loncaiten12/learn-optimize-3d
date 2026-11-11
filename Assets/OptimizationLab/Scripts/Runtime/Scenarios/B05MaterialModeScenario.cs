using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OptimizationLab.Scenarios
{
    /// <summary>
    /// B05 — Shared Material / Unique Material / MaterialPropertyBlock. Biến độc lập: cách gán màu cho từng khối.
    /// Cùng mesh, cùng shader variant URP/Lit, 5 màu chia theo seed; hình ảnh giống hệt nhau ở mọi variant.
    /// A = 5 material dùng chung (<c>sharedMaterial</c>); B = mỗi khối một bản sao material tạo ở Setup;
    /// C = một material chung + MaterialPropertyBlock đổi màu từng khối (không còn tương thích SRP Batcher).
    /// SRP Batcher BẬT ở mọi variant (đó là chỗ B05 thử); dynamic batching và instancing tắt.
    /// </summary>
    public sealed class B05MaterialModeScenario : GridScenarioBase
    {
        #region Fields

        private static readonly int[] COUNTS = { 300, 500, 1000 };
        private static readonly LiveStat[] STATS = { LiveStat.DrawCalls, LiveStat.SetPassCalls, LiveStat.CpuRenderMs };
        private static readonly int BASE_COLOR = Shader.PropertyToID("_BaseColor");

        private readonly List<Material> _clones = new List<Material>();
        private MaterialPropertyBlock _block;
        private bool _saved, _originalSrp, _originalAssetSrp, _originalDynamic;
        private UniversalRenderPipelineAsset _urp;
        private int _materialInstances;

        #endregion

        #region Public

        public override string Id => "B05";
        public override string Title => "Shared / Unique Material / MaterialPropertyBlock";
        public override string ShortTitle => "Bài 6 · Cách gán màu";
        public override string IndependentVariable => "Cách gán màu: sharedMaterial / material riêng / MaterialPropertyBlock";
        public override string Hypothesis => "Cùng hình, cùng 5 màu: gán màu bằng MaterialPropertyBlock làm mất SRP Batcher; material riêng giữ SRP Batcher nhưng tốn bộ nhớ. Không giả định cách nào nhanh nhất.";
        public override int[] ObjectCountOptions => COUNTS;
        public override int DefaultObjectCount => 500;
        public override LiveStat[] LiveStats => STATS;
        public int MaterialInstances => _materialInstances;
        public override BenchmarkVariant[] SupportedVariants => new[] { BenchmarkVariant.A, BenchmarkVariant.B, BenchmarkVariant.C };
        public override string[] QuickTips => new[]
        {
            "Ba cách cho 500 khối 5 màu: 5 material chung, mỗi khối một material, hoặc một material + MaterialPropertyBlock.",
            "MaterialPropertyBlock đổi màu rất tiện nhưng làm vật đó không đi SRP Batcher.",
            "renderer.material (không phải sharedMaterial) âm thầm tạo bản sao material cho từng vật.",
        };

        private static readonly TutorialStep[] STEPS =
        {
            new TutorialStep("Bài 6: cùng 5 màu, ba cách gán.\nHình luôn giống hệt nhau.", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("Bấm Chung: 5 material dùng chung.", TutorialTarget.PreviewA, TutorialWait.Preview, 0),
            new TutorialStep("{draws} lần vẽ · SetPass {setpass}.\nCPU vẽ {cpurender} ms.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Riêng: mỗi khối một bản sao\nmaterial (500 material).", TutorialTarget.PreviewB, TutorialWait.Preview, 1),
            new TutorialStep("Hình y hệt, số lần vẽ gần như cũ.\nNhưng bộ nhớ material nhiều gấp trăm lần.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Block: một material, màu đặt\nbằng MaterialPropertyBlock.", TutorialTarget.PreviewC, TutorialWait.Preview, 2),
            new TutorialStep("Lần vẽ vọt lên {draws}, SetPass {setpass}:\nvật có block không đi SRP Batcher nữa.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Chung để về cảnh chuẩn.", TutorialTarget.PreviewA, TutorialWait.Preview, 0),
            new TutorialStep("Nhớ: đổi màu nhiều vật thì cẩn thận.\nĐo trên máy thật trước khi chọn.", TutorialTarget.None, TutorialWait.Next),
        };

        public override TutorialStep[] TutorialSteps => STEPS;
        public override string TutorialCompleteText => "Cách gán màu quyết định SRP Batcher.";

        public override string ShortVariantName(BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? "Chung" : v == BenchmarkVariant.B ? "Riêng" : "Block";

        public override string VariantKey(BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? "SharedMaterials" : v == BenchmarkVariant.B ? "UniqueMaterials" : "MaterialPropertyBlock";

        public override string DescribeVariant(BenchmarkVariant v) =>
            v == BenchmarkVariant.A ? "5 shared material, gán renderer.sharedMaterial."
            : v == BenchmarkVariant.B ? "Mỗi khối một bản sao material (tạo ở Setup), cùng shader variant."
            : "1 shared material + MaterialPropertyBlock đổi màu từng khối: mất tương thích SRP Batcher.";

        public override string ExplainText =>
            "Ba cách cho cùng một hình: (A) vài shared material, (B) mỗi vật một bản sao material, (C) một material cộng MaterialPropertyBlock. " +
            "SRP Batcher gom theo shader variant nên A và B đều có thể đi SRP Batcher; B tốn nhiều bộ nhớ và công cập nhật material hơn. " +
            "MaterialPropertyBlock làm renderer đó không còn tương thích SRP Batcher nên bị vẽ theo đường thường. " +
            "Lab bật SRP Batcher ở mọi variant, tắt dynamic batching và instancing; thoát Play sẽ trả lại. " +
            "Không có cách nào luôn nhanh nhất: chỉ kết luận sau khi đo trên máy thật. Variant instanced-colors chưa làm.";

        #endregion

        #region Hooks

        protected override Material MaterialFor(int slot, BenchmarkVariant v) =>
            v == BenchmarkVariant.C ? base.MaterialFor(0, v) : base.MaterialFor(slot, v);

        protected override Material MaterialForObject(int index, int slot, BenchmarkVariant v)
        {
            if (v != BenchmarkVariant.B) return MaterialFor(slot, v);
            while (_clones.Count <= index)
            {
                Material src = base.MaterialFor(slot, v);
                _clones.Add(new Material(src) { name = src.name + "_Copy" + _clones.Count });
            }
            return _clones[index];
        }

        protected override bool ExpectsPropertyBlock(BenchmarkVariant v) => v == BenchmarkVariant.C;

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
            int n = Mathf.Clamp(ObjectCount, 0, maxObjectCount);
            if (CurrentVariant == BenchmarkVariant.C)
            {
                if (_block == null) _block = new MaterialPropertyBlock();
                for (int i = 0; i < n; i++)
                {
                    _block.Clear();
                    _block.SetColor(BASE_COLOR, ColorOfObject(i));
                    renderers[i].SetPropertyBlock(_block);
                }
            }
            _materialInstances = CurrentVariant == BenchmarkVariant.A ? materials.Length : CurrentVariant == BenchmarkVariant.B ? _clones.Count : 1;
        }

        protected override BenchmarkStatus ValidateVariant(out string reason)
        {
            reason = "";
            if (materials.Length < 2) reason = "Cần ít nhất 2 material (màu).";
            else if (!GraphicsSettings.useScriptableRenderPipelineBatching || _urp == null || !_urp.useSRPBatcher) reason = "SRP Batcher phải bật ở mọi variant.";
            else if (_urp.supportsDynamicBatching) reason = "Dynamic batching phải tắt.";
            else if (!SystemInfo.supportsInstancing) reason = "Thiết bị không hỗ trợ GPU instancing.";
            else
            {
                Shader shader = materials[0].shader;
                foreach (Material m in materials)
                    if (m == null || m.shader != shader || m.enableInstancing) { reason = "Mọi material phải cùng shader và tắt instancing."; break; }
            }
            if (reason.Length == 0 && CurrentVariant == BenchmarkVariant.B && _clones.Count < Mathf.Min(ObjectCount, maxObjectCount))
                reason = "Variant B phải có một material riêng cho mỗi khối.";
            return reason.Length == 0 ? BenchmarkStatus.Valid : BenchmarkStatus.Invalid;
        }

        protected override void AddVariantSettings(BenchmarkVariant v, IDictionary<string, string> into)
        {
            into["srp_batcher"] = "true";
            into["dynamic_batching"] = "false";
            into["gpu_instancing"] = "false";
            into["color_mode"] = VariantKey(v);
            into["material_instances"] = (v == BenchmarkVariant.A ? materials.Length : v == BenchmarkVariant.B ? Mathf.Min(ObjectCount, maxObjectCount) : 1).ToString();
            into["property_block"] = v == BenchmarkVariant.C ? "true (per object, _BaseColor)" : "false";
        }

        public override void Teardown()
        {
            base.Teardown();
            foreach (Material m in _clones) if (m != null) Destroy(m);
            _clones.Clear();
            _materialInstances = 0;
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

        #region Private

        /// <summary>Màu của khối i: giống hệt variant A (slot theo seed), để hình không đổi giữa các variant.</summary>
        private Color ColorOfObject(int index) => materials[SlotOf(index) % materials.Length].GetColor(BASE_COLOR);

        #endregion
    }
}
