using System;

namespace OptimizationLab
{
    /// <summary>Trạng thái hợp lệ của một lần đo.</summary>
    public enum BenchmarkStatus { Valid, Invalid, Unsupported }

    /// <summary>Biến thể A/B/C/D của một benchmark.</summary>
    public enum BenchmarkVariant { A, B, C, D }

    /// <summary>Số liệu live hiển thị trên HUD cho bài học.</summary>
    public enum LiveStat { ObjectCount, DrawCalls, SetPassCalls, Batches, CpuMainMs, CpuRenderMs, GpuMs, MeshMemoryMb }

    /// <summary>Pha hiện tại của runner.</summary>
    public enum RunnerPhase { Idle, Settling, Warmup, Capturing, Measuring, Cooldown }

    /// <summary>Contract chung cho mọi scenario (theo spec mục 1.3).</summary>
    public interface IBenchmarkScenario
    {
        string Id { get; }
        void Setup(BenchmarkVariant variant, int seed);
        void WarmupTick(float deltaTime);
        void MeasurementTick(float deltaTime);
        void Teardown();
        BenchmarkStatus Validate(out string reason);
    }

    /// <summary>Thông số một phiên đo. Giá trị mặc định = cấu hình đo chính thức trên thiết bị.</summary>
    [Serializable]
    public class BenchmarkConfig
    {
        public int seed = 1337;
        public int objectCount = 300;
        /// <summary>Bỏ qua N giây đầu ngay sau khi đổi variant (spec: exclude first 2 s).</summary>
        public float settleSeconds = 2f;
        public float warmupSeconds = 15f;
        public float measureSeconds = 60f;
        public float cooldownSeconds = 15f;
        /// <summary>Số lần chạy mỗi variant; thứ tự ABBA AB...</summary>
        public int repetitions = 3;
        /// <summary>-1 = uncapped (vSync off).</summary>
        public int targetFrameRate = 60;
        public bool captureScreenshots = true;
        public bool exportRawSamples;
        public bool hideHudWhileMeasuring = true;

        public BenchmarkConfig Clone() => (BenchmarkConfig)MemberwiseClone();

        /// <summary>Cấu hình smoke test nhanh trong Editor — KHÔNG dùng để kết luận hiệu năng.</summary>
        public static BenchmarkConfig CreateQuick()
        {
            return new BenchmarkConfig
            {
                settleSeconds = 1f,
                warmupSeconds = 3f,
                measureSeconds = 8f,
                cooldownSeconds = 1f,
                repetitions = 2,
            };
        }
    }
}
