using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OptimizationLab.Scenarios
{
    /// <summary>
    /// B01 — SRP Batcher On/Off (URP). Biến độc lập: <c>GraphicsSettings.useScriptableRenderPipelineBatching</c>.
    /// Cùng 1000 khối, 8 shared material khác màu, CÙNG một shader variant URP/Lit, không MaterialPropertyBlock.
    /// A = SRP Batcher tắt, B = bật, C (giáo dục) = bật nhưng 8 material mang 8 tổ hợp keyword khác nhau
    /// → 8 shader variant → batch bị chia nhỏ (đổi thêm yếu tố shader).
    /// </summary>
    public sealed class B01SrpBatcherScenario : GridScenarioBase
    {
        #region Fields

        [Tooltip("8 material cùng màu với 'materials' nhưng mỗi cái bật một tổ hợp keyword khác (variant C).")]
        [SerializeField] private Material[] keywordMaterials = System.Array.Empty<Material>();

        private static readonly int[] COUNTS = { 300, 1000, 3000 };
        private static readonly LiveStat[] STATS = { LiveStat.DrawCalls, LiveStat.SetPassCalls, LiveStat.CpuRenderMs };

        private bool _savedGlobal;
        private bool _originalSrpBatching;

        #endregion

        #region Public

        public override string Id => "B01";
        public override string Title => "SRP Batcher On / Off";
        public override string IndependentVariable => "GraphicsSettings.useScriptableRenderPipelineBatching";
        public override string Hypothesis => "SRP Batcher giữ nguyên số draw call nhưng giảm chi phí CPU chuẩn bị mỗi lần vẽ.";
        public override string ShortTitle => "Bài 2 · SRP Batcher";
        public override int[] ObjectCountOptions => COUNTS;
        public override int DefaultObjectCount => 1000;
        public override LiveStat[] LiveStats => STATS;
        public override BenchmarkVariant[] SupportedVariants => new[] { BenchmarkVariant.A, BenchmarkVariant.B, BenchmarkVariant.C };
        public override bool IsEducationalVariant(BenchmarkVariant variant) => variant == BenchmarkVariant.C;

        public override string[] QuickTips => new[]
        {
            "SRP Batcher không giảm số lần vẽ — nó làm MỖI lần vẽ rẻ hơn cho CPU.",
            "Điều kiện: cùng shader variant, shader hỗ trợ SRP Batcher, không MaterialPropertyBlock.",
            "Bật thật: URP Asset ▸ Rendering ▸ ⋮ Show Additional Properties ▸ tick SRP Batcher.",
        };

        private static readonly TutorialStep[] STEPS =
        {
            new TutorialStep("Bài 2: vẽ nhiều mà CPU\nvẫn nhẹ — SRP Batcher.", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("1000 khối, 8 màu.\nĐang có {draws} lần vẽ.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Tắt để tắt SRP Batcher.", TutorialTarget.PreviewA, TutorialWait.Preview, 0),
            new TutorialStep("Lần vẽ vẫn {draws}, nhưng CPU vẽ\nvọt lên {cpurender} ms! Game giật.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Bật lại.", TutorialTarget.PreviewB, TutorialWait.Preview, 1),
            new TutorialStep("CPU vẽ còn {cpurender} ms.\nCùng số lần vẽ, mỗi lần rẻ hơn nhiều.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Lộn xộn: mỗi màu một\nshader variant khác.", TutorialTarget.PreviewC, TutorialWait.Preview, 2),
            new TutorialStep("SetPass tăng lên {setpass}: khác variant\n= batch bị cắt. Càng nhiều variant càng tốn.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Nhớ nhé: giữ ít shader variant,\nđừng dùng MaterialPropertyBlock.", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("Bật cho game thật? Thoát Play, mở\nTools ▸ Optimization Lab ▸ Tutorial ▸ B01.", TutorialTarget.None, TutorialWait.Next),
        };

        public override TutorialStep[] TutorialSteps => STEPS;
        public override string TutorialCompleteText => "SRP Batcher: cùng số lần vẽ, CPU nhẹ hơn.";

        public override string ShortVariantName(BenchmarkVariant variant)
        {
            switch (variant)
            {
                case BenchmarkVariant.A: return "Tắt";
                case BenchmarkVariant.B: return "Bật";
                case BenchmarkVariant.C: return "Lộn xộn";
                default: return "-";
            }
        }

        public override string VariantKey(BenchmarkVariant variant)
        {
            switch (variant)
            {
                case BenchmarkVariant.A: return "SrpBatcherOff";
                case BenchmarkVariant.B: return "SrpBatcherOn";
                case BenchmarkVariant.C: return "SrpBatcherOnManyVariants";
                default: return variant.ToString();
            }
        }

        public override string DescribeVariant(BenchmarkVariant variant)
        {
            switch (variant)
            {
                case BenchmarkVariant.A: return "SRP Batcher OFF, 8 material cùng shader variant.";
                case BenchmarkVariant.B: return "SRP Batcher ON, 8 material cùng shader variant.";
                case BenchmarkVariant.C: return "SRP Batcher ON, 8 material mỗi cái một shader variant (đổi thêm yếu tố shader — giáo dục).";
                default: return "";
            }
        }

        public override string ExplainText =>
            "B01 — SRP BATCHER ON / OFF\n\n" +
            "SRP Batcher giữ dữ liệu material trên GPU và chỉ cập nhật dữ liệu per-object, nên CPU chuẩn bị mỗi draw rẻ hơn. " +
            "Số draw call/batches có thể không đổi — đừng suy FPS từ batches.\n\n" +
            "Điều kiện: shader SRP-compatible, cùng shader variant trong một SRP batch, không MaterialPropertyBlock.\n\n" +
            "Không nhầm với GPU Instancing (B03): instancing gộp nhiều bản sao cùng mesh+material vào một draw.\n\n" +
            "Kiểm chứng: Frame Debugger → 'SRP Batch' và lý do tách batch.";

        #endregion

        #region Hooks

        protected override Material MaterialFor(int slot, BenchmarkVariant variant)
        {
            Material[] set = variant == BenchmarkVariant.C && keywordMaterials.Length > 0 ? keywordMaterials : materials;
            return set.Length > 0 ? set[slot % set.Length] : null;
        }

        protected override int SlotCount => Mathf.Max(1, materials.Length);

        protected override void ApplyVariantGlobals(BenchmarkVariant variant)
        {
            if (!_savedGlobal)
            {
                _originalSrpBatching = GraphicsSettings.useScriptableRenderPipelineBatching;
                _savedGlobal = true;
            }
            GraphicsSettings.useScriptableRenderPipelineBatching = variant != BenchmarkVariant.A;
        }

        protected override void RestoreGlobals()
        {
            if (!_savedGlobal) return;
            GraphicsSettings.useScriptableRenderPipelineBatching = _originalSrpBatching;
            _savedGlobal = false;
        }

        protected override BenchmarkStatus ValidateVariant(out string reason)
        {
            reason = "";
            bool expected = CurrentVariant != BenchmarkVariant.A;
            if (GraphicsSettings.useScriptableRenderPipelineBatching != expected)
            {
                reason = $"SRP Batcher = {GraphicsSettings.useScriptableRenderPipelineBatching}, mong đợi {expected}.";
                return BenchmarkStatus.Invalid;
            }
            if (keywordMaterials.Length != materials.Length)
            {
                reason = "keywordMaterials phải cùng số lượng với materials.";
                return BenchmarkStatus.Invalid;
            }
            return BenchmarkStatus.Valid;
        }

        protected override void AddVariantSettings(BenchmarkVariant variant, IDictionary<string, string> into)
        {
            into["srp_batcher"] = variant != BenchmarkVariant.A ? "true" : "false";
        }

        #endregion
    }
}
