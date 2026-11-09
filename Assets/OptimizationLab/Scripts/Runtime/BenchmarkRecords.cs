using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OptimizationLab
{
    /// <summary>Thông tin build sinh bởi Editor generator (Resources/OptimizationLabBuildInfo.json).</summary>
    [Serializable]
    public sealed class LabBuildInfo
    {
        public string urpVersion = "unknown";
        public string gitCommit = "unknown";
        public string generatedUtc = "unknown";

        private const string RESOURCE_PATH = "OptimizationLabBuildInfo";
        private static LabBuildInfo _cached;

        public static LabBuildInfo Load()
        {
            if (_cached != null) return _cached;
            var asset = Resources.Load<TextAsset>(RESOURCE_PATH);
            _cached = asset != null ? JsonUtility.FromJson<LabBuildInfo>(asset.text) : new LabBuildInfo();
            return _cached;
        }
    }

    /// <summary>Metadata môi trường bắt buộc (spec mục 0.10, 2.1).</summary>
    [Serializable]
    public sealed class EnvironmentInfo
    {
        public string unityVersion;
        public string renderPipeline;
        public string pipelineAsset;
        public string urpVersion;
        public string gitCommit;
        public string buildType;
        public string platform;
        public string device;
        public string operatingSystem;
        public string graphicsApi;
        public string gpuName;
        public string cpuName;
        public int systemMemoryMb;
        public int screenWidth;
        public int screenHeight;
        public int currentResolutionWidth;
        public int currentResolutionHeight;
        public double refreshRateHz;
        public float renderScale = float.NaN;
        public int internalWidth;
        public int internalHeight;
        public string qualityPreset;
        public int targetFrameRate;
        public int vSyncCount;
        public int msaaSamples;
        public bool hdr;
        public bool srpBatcher;
        public bool dynamicBatching;
        public bool frameTimingStats;
        public float batteryLevel;
        public string batteryStatus;
        public string thermalState = "unsupported";

        public static EnvironmentInfo Capture()
        {
            var e = new EnvironmentInfo();
            LabBuildInfo build = LabBuildInfo.Load();
            e.unityVersion = Application.unityVersion;
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            e.renderPipeline = rp is UniversalRenderPipelineAsset ? "URP" : rp == null ? "Built-in" : rp.GetType().Name;
            e.pipelineAsset = rp != null ? rp.name : "none";
            e.urpVersion = build.urpVersion;
            e.gitCommit = build.gitCommit;
            e.buildType = Application.isEditor ? "Editor" : Debug.isDebugBuild ? "Development" : "Release";
            e.platform = Application.platform.ToString();
            e.device = SystemInfo.deviceModel;
            e.operatingSystem = SystemInfo.operatingSystem;
            e.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            e.gpuName = SystemInfo.graphicsDeviceName;
            e.cpuName = SystemInfo.processorType;
            e.systemMemoryMb = SystemInfo.systemMemorySize;
            e.screenWidth = Screen.width;
            e.screenHeight = Screen.height;
            Resolution res = Screen.currentResolution;
            e.currentResolutionWidth = res.width;
            e.currentResolutionHeight = res.height;
            e.refreshRateHz = res.refreshRateRatio.value;
            e.qualityPreset = QualitySettings.names[QualitySettings.GetQualityLevel()];
            e.targetFrameRate = Application.targetFrameRate;
            e.vSyncCount = QualitySettings.vSyncCount;
            e.frameTimingStats = FrameTimingManager.IsFeatureEnabled();
            e.srpBatcher = GraphicsSettings.useScriptableRenderPipelineBatching;
            if (rp is UniversalRenderPipelineAsset urp)
            {
                e.renderScale = urp.renderScale;
                e.msaaSamples = urp.msaaSampleCount;
                e.hdr = urp.supportsHDR;
                e.dynamicBatching = urp.supportsDynamicBatching;
                // Internal render resolution = screen × render scale (không lấy screen thay cho render).
                e.internalWidth = Mathf.RoundToInt(Screen.width * urp.renderScale);
                e.internalHeight = Mathf.RoundToInt(Screen.height * urp.renderScale);
            }
            else
            {
                e.internalWidth = Screen.width;
                e.internalHeight = Screen.height;
                e.msaaSamples = QualitySettings.antiAliasing;
            }
            e.batteryLevel = SystemInfo.batteryLevel;
            e.batteryStatus = SystemInfo.batteryStatus.ToString();
            return e;
        }
    }

    /// <summary>Một lần chạy (một hàng CSV).</summary>
    [Serializable]
    public sealed class RunRecord
    {
        public string runId;
        public string utcTime;
        public string scene;
        public string scenarioId;
        public BenchmarkVariant variant;
        public string variantName;
        public bool educational;
        public int orderIndex;
        public int repetition;
        public int seed;
        public int objectCount;
        public int visibleRenderers;
        public int expectedVisibleRenderers;
        public BenchmarkStatus status = BenchmarkStatus.Valid;
        public string invalidReason = "";
        public List<string> notes = new List<string>();
        public MetricsSummary metrics;
        public EnvironmentInfo environment;
        public SortedDictionary<string, string> variantSettings = new SortedDictionary<string, string>();
        public string screenshotPath = "";
    }

    /// <summary>So sánh A/B theo spec mục 2.4.</summary>
    public sealed class ComparisonResult
    {
        public BenchmarkVariant variantA;
        public BenchmarkVariant variantB;
        public int validRunsA;
        public int validRunsB;
        public double frameMedianA = double.NaN;
        public double frameMedianB = double.NaN;
        public double frameP95A = double.NaN;
        public double frameP95B = double.NaN;
        public double cpuMainA = double.NaN, cpuMainB = double.NaN;
        public double cpuRenderA = double.NaN, cpuRenderB = double.NaN;
        public double gpuA = double.NaN, gpuB = double.NaN;
        public double batchesA = double.NaN, batchesB = double.NaN;
        public double setPassA = double.NaN, setPassB = double.NaN;
        public double improvementPercent = double.NaN;
        public double noiseMs = double.NaN;
        public string verdict = "inconclusive";
        public List<string> configDifferences = new List<string>();
        public List<string> notes = new List<string>();

        /// <summary>
        /// improvement_% = (median_A − median_B) / median_A × 100. Chênh lệch nhỏ hơn noise (2 × stdev của các
        /// median từng run) → inconclusive. Không kết luận nếu một bên có run INVALID hoặc &lt; 2 run hợp lệ.
        /// </summary>
        public static ComparisonResult Compare(IReadOnlyList<RunRecord> runs, BenchmarkVariant a, BenchmarkVariant b)
        {
            var r = new ComparisonResult { variantA = a, variantB = b };
            var medA = Collect(runs, a, x => x.frameMsMedian, out int invalidA);
            var medB = Collect(runs, b, x => x.frameMsMedian, out int invalidB);
            r.validRunsA = medA.Length;
            r.validRunsB = medB.Length;
            r.frameMedianA = BenchmarkStats.Median(medA);
            r.frameMedianB = BenchmarkStats.Median(medB);
            r.frameP95A = BenchmarkStats.Median(Collect(runs, a, x => x.frameMsP95, out _));
            r.frameP95B = BenchmarkStats.Median(Collect(runs, b, x => x.frameMsP95, out _));
            r.cpuMainA = BenchmarkStats.Median(Collect(runs, a, x => x.cpuMainMs, out _));
            r.cpuMainB = BenchmarkStats.Median(Collect(runs, b, x => x.cpuMainMs, out _));
            r.cpuRenderA = BenchmarkStats.Median(Collect(runs, a, x => x.cpuRenderMs, out _));
            r.cpuRenderB = BenchmarkStats.Median(Collect(runs, b, x => x.cpuRenderMs, out _));
            r.gpuA = BenchmarkStats.Median(Collect(runs, a, x => x.gpuMs, out _));
            r.gpuB = BenchmarkStats.Median(Collect(runs, b, x => x.gpuMs, out _));
            r.batchesA = BenchmarkStats.Median(Collect(runs, a, x => x.batches, out _));
            r.batchesB = BenchmarkStats.Median(Collect(runs, b, x => x.batches, out _));
            r.setPassA = BenchmarkStats.Median(Collect(runs, a, x => x.setPassCalls, out _));
            r.setPassB = BenchmarkStats.Median(Collect(runs, b, x => x.setPassCalls, out _));

            double sdA = BenchmarkStats.Stdev(medA), sdB = BenchmarkStats.Stdev(medB);
            if (!double.IsNaN(sdA) || !double.IsNaN(sdB))
                r.noiseMs = 2.0 * Math.Max(double.IsNaN(sdA) ? 0 : sdA, double.IsNaN(sdB) ? 0 : sdB);

            if (r.frameMedianA > 0 && !double.IsNaN(r.frameMedianB))
                r.improvementPercent = (r.frameMedianA - r.frameMedianB) / r.frameMedianA * 100.0;

            if (invalidA > 0 || invalidB > 0)
            {
                r.verdict = "invalid";
                r.notes.Add($"Có run INVALID (A: {invalidA}, B: {invalidB}) — không kết luận.");
            }
            else if (r.validRunsA < 2 || r.validRunsB < 2)
            {
                r.verdict = "inconclusive";
                r.notes.Add("Cần ≥ 2 run hợp lệ mỗi variant để ước lượng noise.");
            }
            else if (Math.Abs(r.frameMedianA - r.frameMedianB) <= r.noiseMs)
            {
                r.verdict = "inconclusive";
                r.notes.Add($"Chênh lệch {Math.Abs(r.frameMedianA - r.frameMedianB):0.###} ms ≤ noise {r.noiseMs:0.###} ms.");
            }
            else
            {
                r.verdict = r.frameMedianB < r.frameMedianA ? "B faster" : "B slower";
            }

            AddCapNote(runs, r);
            AddConfigDiff(runs, a, b, r);
            return r;
        }

        private static void AddCapNote(IReadOnlyList<RunRecord> runs, ComparisonResult r)
        {
            if (runs.Count == 0) return;
            int target = runs[0].environment.targetFrameRate;
            if (target <= 0) return;
            double capMs = 1000.0 / target;
            if (r.frameMedianA <= capMs * 1.05 && r.frameMedianB <= capMs * 1.05)
                r.notes.Add($"Cả A và B chạm cap {target} FPS: frametime không phân biệt được — xem CPU/GPU ms hoặc chạy uncapped.");
        }

        private static void AddConfigDiff(IReadOnlyList<RunRecord> runs, BenchmarkVariant a, BenchmarkVariant b, ComparisonResult r)
        {
            RunRecord ra = null, rb = null;
            foreach (RunRecord run in runs)
            {
                if (ra == null && run.variant == a) ra = run;
                if (rb == null && run.variant == b) rb = run;
            }
            if (ra == null || rb == null) return;
            foreach (var kv in ra.variantSettings)
            {
                rb.variantSettings.TryGetValue(kv.Key, out string vb);
                if (vb != kv.Value) r.configDifferences.Add($"{kv.Key}: A={kv.Value} | B={vb ?? "(none)"}");
            }
            foreach (var kv in rb.variantSettings)
                if (!ra.variantSettings.ContainsKey(kv.Key)) r.configDifferences.Add($"{kv.Key}: A=(none) | B={kv.Value}");
        }

        private static double[] Collect(IReadOnlyList<RunRecord> runs, BenchmarkVariant v, Func<MetricsSummary, double> pick, out int invalid)
        {
            invalid = 0;
            var list = new List<double>();
            foreach (RunRecord run in runs)
            {
                if (run.variant != v) continue;
                if (run.status != BenchmarkStatus.Valid) { invalid++; continue; }
                list.Add(pick(run.metrics));
            }
            return list.ToArray();
        }
    }
}
