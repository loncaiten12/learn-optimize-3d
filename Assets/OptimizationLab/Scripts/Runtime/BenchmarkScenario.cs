using System.Collections.Generic;
using UnityEngine;

namespace OptimizationLab
{
    /// <summary>
    /// Base class cho mọi scene benchmark. Mỗi scenario chỉ thay đổi MỘT biến độc lập giữa các variant
    /// và tự mô tả cấu hình của từng variant để exporter sinh danh sách khác biệt tự động.
    /// </summary>
    public abstract class BenchmarkScenario : MonoBehaviour, IBenchmarkScenario
    {
        #region Fields

        [SerializeField] protected Camera benchmarkCamera;

        private static readonly int[] DEFAULT_COUNTS = { 100, 300, 1000, 3000 };
        private static readonly LiveStat[] DEFAULT_STATS = { LiveStat.ObjectCount, LiveStat.DrawCalls };

        #endregion

        #region Public

        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string IndependentVariable { get; }
        public abstract string Hypothesis { get; }

        /// <summary>Nội dung nút "Explain" trên HUD.</summary>
        public abstract string ExplainText { get; }

        /// <summary>Tên ngắn hiển thị trên HUD (2–3 từ).</summary>
        public abstract string ShortTitle { get; }

        /// <summary>Tối đa 3 mẹo ngắn (mỗi mẹo 1 dòng) cho nút "?" trên HUD.</summary>
        public abstract string[] QuickTips { get; }

        /// <summary>Các bước tutorial trong game (như nhiệm vụ). Rỗng = không có tutorial.</summary>
        public virtual TutorialStep[] TutorialSteps => System.Array.Empty<TutorialStep>();

        /// <summary>Một câu chúc mừng khi hoàn thành tutorial.</summary>
        public virtual string TutorialCompleteText => "Hoàn thành!";

        public abstract BenchmarkVariant[] SupportedVariants { get; }

        /// <summary>Hai variant dùng cho "Run A/B" (ABBA).</summary>
        public virtual BenchmarkVariant PrimaryA => BenchmarkVariant.A;
        public virtual BenchmarkVariant PrimaryB => BenchmarkVariant.B;

        /// <summary>Variant dùng cho count sweep; null nếu scenario không hỗ trợ sweep.</summary>
        public virtual BenchmarkVariant? SweepVariant => null;

        public virtual int[] ObjectCountOptions => DEFAULT_COUNTS;

        /// <summary>Số object lúc mở scene.</summary>
        public virtual int DefaultObjectCount => 300;

        public virtual string PreviewWarning => "";

        public virtual double MeshMemoryMb => double.NaN;

        /// <summary>Tối đa 3 ô số liệu live trên HUD.</summary>
        public virtual LiveStat[] LiveStats => DEFAULT_STATS;

        /// <summary>Variant giáo dục/composite (đổi nhiều hơn 1 yếu tố) — ghi nhãn trong kết quả.</summary>
        public virtual bool IsEducationalVariant(BenchmarkVariant variant) => false;

        /// <summary>Tên variant ngắn cho UI (tiếng Việt, 1 từ).</summary>
        public abstract string ShortVariantName(BenchmarkVariant variant);

        /// <summary>Khoá variant ổn định cho CSV/JSON (tiếng Anh, không đổi theo UI).</summary>
        public virtual string VariantKey(BenchmarkVariant variant) => variant.ToString();
        public abstract string DescribeVariant(BenchmarkVariant variant);

        /// <summary>Ghi cấu hình render của variant (key → value) để so sánh A/B tự động.</summary>
        public abstract void GetVariantSettings(BenchmarkVariant variant, IDictionary<string, string> into);

        public Camera BenchmarkCamera => benchmarkCamera;
        public int ObjectCount { get; set; } = 300;
        public BenchmarkVariant CurrentVariant { get; protected set; }
        public bool IsSetUp { get; protected set; }

        /// <summary>Số renderer phải nằm trong frustum theo thiết kế test.</summary>
        public abstract int ExpectedVisibleRenderers { get; }

        /// <summary>Đếm renderer đang bật và nằm trong frustum camera (không phải số object spawn).</summary>
        public abstract int CountVisibleRenderers();

        public abstract void Setup(BenchmarkVariant variant, int seed);
        public virtual void WarmupTick(float deltaTime) { }
        public virtual void MeasurementTick(float deltaTime) { }
        public abstract void Teardown();
        public abstract BenchmarkStatus Validate(out string reason);

        #endregion
    }
}
