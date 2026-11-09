using System.Collections.Generic;
using UnityEngine;

namespace OptimizationLab.Scenarios
{
    /// <summary>
    /// B00 — Baseline / object count scaling. Biến độc lập: số MeshRenderer được vẽ.
    /// A = object spawn nhưng renderer tắt (chi phí "scene rỗng" + transform), B = hiển thị, C = hiển thị + xoay (giáo dục).
    /// Cùng mesh, cùng một shared material, shadow tắt → không lẫn chi phí shader/lighting.
    /// </summary>
    public sealed class B00BaselineScenario : GridScenarioBase
    {
        #region Fields

        private static readonly int[] COUNTS = { 100, 300, 1000, 3000 };

        #endregion

        #region Public

        public override string Id => "B00";
        public override string Title => "Baseline / Object count scaling";
        public override string IndependentVariable => "Số MeshRenderer được vẽ (object count)";
        public override string Hypothesis => "Frame time tăng theo số renderer; đường cong cho biết ngân sách object của thiết bị.";
        public override string ShortTitle => "Bài 1 · Nhiều vật = chậm";

        public override string[] QuickTips => new[]
        {
            "Mỗi khối tốn ít nhất một lần vẽ (draw call), có khi 2 lượt.",
            "Càng nhiều lần vẽ, CPU càng bận → mỗi frame lâu hơn (ms tăng).",
            "Khối bị ẩn thì không vẽ, không tốn. Cái tốn là VẼ.",
        };

        private static readonly TutorialStep[] STEPS =
        {
            new TutorialStep("Bài 1: vì sao nhiều vật\nthì game chậm?", TutorialTarget.None, TutorialWait.Next),
            new TutorialStep("{count} khối → {draws} lần vẽ.\nMỗi khối tốn ít nhất một lần vẽ.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm 3K để có 3000 khối.", TutorialTarget.Count3, TutorialWait.Count, 3),
            new TutorialStep("Lần vẽ tăng vọt: {draws}!\nGấp 10 lần — máy làm nhiều hơn hẳn.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Ẩn: khối vẫn còn,\nnhưng không được vẽ.", TutorialTarget.PreviewA, TutorialWait.Preview, 0),
            new TutorialStep("Còn {draws} lần vẽ.\nVậy cái tốn là VẼ, không phải có vật.", TutorialTarget.Stats, TutorialWait.Next),
            new TutorialStep("Bấm Hiện để vẽ lại.", TutorialTarget.PreviewB, TutorialWait.Preview, 1),
            new TutorialStep("Nhớ nhé: ít lần vẽ = game mượt.\nBài sau: gộp nhiều khối vào một lần vẽ.", TutorialTarget.None, TutorialWait.Next),
        };

        public override TutorialStep[] TutorialSteps => STEPS;
        public override string TutorialCompleteText => "Số lần vẽ quyết định game nhanh hay chậm.";

        public override BenchmarkVariant[] SupportedVariants => new[] { BenchmarkVariant.A, BenchmarkVariant.B, BenchmarkVariant.C };
        public override BenchmarkVariant? SweepVariant => BenchmarkVariant.B;
        public override int[] ObjectCountOptions => COUNTS;

        public override bool IsEducationalVariant(BenchmarkVariant variant) => variant == BenchmarkVariant.C;

        public override string ShortVariantName(BenchmarkVariant variant)
        {
            switch (variant)
            {
                case BenchmarkVariant.A: return "Ẩn";
                case BenchmarkVariant.B: return "Hiện";
                case BenchmarkVariant.C: return "Xoay";
                default: return "-";
            }
        }

        public override string VariantKey(BenchmarkVariant variant)
        {
            switch (variant)
            {
                case BenchmarkVariant.A: return "ObjectsDisabled";
                case BenchmarkVariant.B: return "ObjectsVisible";
                case BenchmarkVariant.C: return "ObjectsVisibleAnimated";
                default: return variant.ToString();
            }
        }

        public override string DescribeVariant(BenchmarkVariant variant)
        {
            switch (variant)
            {
                case BenchmarkVariant.A: return "Spawn đủ object nhưng MeshRenderer.enabled = false (không vẽ).";
                case BenchmarkVariant.B: return "Tất cả object hiển thị, đứng yên.";
                case BenchmarkVariant.C: return "Hiển thị + xoay mỗi frame (thêm chi phí transform — test riêng, không phải A/B batching).";
                default: return "";
            }
        }

        public override string ExplainText =>
            "B00 — BASELINE: OBJECT COUNT SCALING\n\n" +
            "Kỹ thuật: chưa tối ưu gì. Đây là đường chuẩn để mọi test sau so sánh.\n\n" +
            "Giả thuyết: frame time tăng theo số renderer. Đo để biết thiết bị chịu được bao nhiêu object trước khi vượt 16,6 ms.\n\n" +
            "A/B: A = object có nhưng renderer TẮT (chỉ còn chi phí scene/transform). B = renderer BẬT. " +
            "Chênh A→B = chi phí vẽ N renderer. C = B + xoay (giáo dục, đổi thêm chi phí script/transform).\n\n" +
            "Giữ cố định: cùng cube mesh, MỘT shared material URP/Lit, shadow off, light probe off, camera orthographic " +
            "khung theo lưới 3000 object cho mọi count, seed 1337.\n\n" +
            "Count sweep: chạy variant B ở 100 → 300 → 1000 → 3000 (lượt sau đảo chiều) để vẽ đường cong.\n\n" +
            "Sai số thường gặp:\n" +
            "• FPS bị cap 60: hai variant cùng 16,6 ms → nhìn CPU/GPU ms hoặc tắt cap.\n" +
            "• Đo trong Editor: số liệu gồm chi phí Editor; chỉ dùng để kiểm cơ chế.\n" +
            "• Scene view đang mở: Stats/Batches cộng cả Scene view.\n" +
            "• SRP Batcher đang bật: batches có thể giữ nguyên nhưng CPU vẫn tăng — đừng suy FPS từ batches.\n\n" +
            "Kiểm chứng: Window ▸ Analysis ▸ Frame Debugger → xem số draw trong 'DrawOpaqueObjects'. " +
            "Profiler (CPU + Rendering) để thấy main/render thread.\n\n" +
            "Đọc kết quả: so median + P95 frametime. Chưa profiling thì KHÔNG kết luận nguyên nhân.";

        #endregion

        #region Hooks

        protected override bool RenderersEnabled(BenchmarkVariant variant) => variant != BenchmarkVariant.A;
        protected override bool Animated(BenchmarkVariant variant) => variant == BenchmarkVariant.C;
        /// <summary>B00: MỘT shared material cho mọi object.</summary>
        protected override int SlotCount => 1;

        #endregion

    }
}
