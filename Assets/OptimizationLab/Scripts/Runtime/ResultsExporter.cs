using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace OptimizationLab
{
    /// <summary>Một phiên chạy = danh sách run + so sánh, xuất ra CSV + JSON.</summary>
    public sealed class BenchmarkSession
    {
        public string sessionId;
        public string kind;
        public string scene;
        public string scenarioId;
        public BenchmarkConfig config;
        public EnvironmentInfo environment;
        /// <summary>true khi <see cref="environment"/> đã được chụp lại lúc bắt đầu đo run đầu tiên.</summary>
        public bool referenceCaptured;
        public readonly List<RunRecord> runs = new List<RunRecord>();
        public ComparisonResult comparison;
        public readonly List<string> notes = new List<string>();
    }

    /// <summary>
    /// Ghi kết quả vào <c>Application.persistentDataPath/OptimizationLabResults/</c>; trong Editor ghi thêm vào
    /// <c>&lt;project&gt;/Results/</c>. Giá trị không đọc được ghi "unsupported" (CSV) / null (JSON), không bao giờ 0.
    /// </summary>
    public static class ResultsExporter
    {
        #region Fields

        public const string FOLDER_NAME = "OptimizationLabResults";
        public const string ALL_RUNS_FILE = "all_runs.csv";
        private const string UNSUPPORTED = "unsupported";

        /// <summary>Cột theo đúng thứ tự spec mục 2.3, cột mở rộng nối phía sau (schema ổn định).</summary>
        public static readonly string[] CsvColumns =
        {
            "run_id", "utc_time", "scene", "variant", "seed", "unity_version", "render_pipeline", "urp_version",
            "device", "android_version", "graphics_api", "width", "height", "render_scale", "object_count",
            "visible_renderers", "frame_ms_median", "frame_ms_p95", "frame_ms_p99", "cpu_main_ms", "cpu_render_ms",
            "gpu_ms", "batches", "setpass_calls", "triangles", "vertices", "allocated_mb", "status", "invalid_reason",
            // mở rộng
            "session_id", "variant_name", "educational", "order_index", "repetition", "expected_visible",
            "fps_median", "frame_ms_mean", "frame_ms_stdev", "frame_ms_max", "sample_count", "draw_calls",
            "peak_allocated_mb", "mono_used_mb", "graphics_memory_mb", "gc_alloc_kb_per_frame",
            "cpu_main_source", "cpu_render_source", "gpu_source", "build_type", "quality_preset", "pipeline_asset",
            "target_fps", "vsync", "msaa", "hdr", "srp_batcher", "dynamic_batching", "internal_width",
            "internal_height", "battery_level", "thermal_state", "notes",
        };

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        #endregion

        #region Public

        public static string ResultsFolder => Path.Combine(Application.persistentDataPath, FOLDER_NAME);

        /// <summary>Thư mục Results/ ở gốc project (chỉ trong Editor), null nếu không phải Editor.</summary>
        public static string ProjectResultsFolder =>
            Application.isEditor ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Results")) : null;

        public static string CsvHeader => string.Join(",", CsvColumns);

        /// <summary>Xuất phiên; trả về danh sách file đã ghi.</summary>
        public static List<string> Export(BenchmarkSession session)
        {
            var written = new List<string>();
            string csv = BuildCsv(session.runs, true);
            string json = BuildJson(session);
            string baseName = $"{session.scenarioId}_{session.kind}_{session.sessionId}";

            foreach (string folder in TargetFolders())
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    string csvPath = Path.Combine(folder, baseName + ".csv");
                    string jsonPath = Path.Combine(folder, baseName + ".json");
                    File.WriteAllText(csvPath, csv, Encoding.UTF8);
                    File.WriteAllText(jsonPath, json, Encoding.UTF8);
                    AppendAllRuns(folder, session.runs);
                    written.Add(csvPath);
                    written.Add(jsonPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OptimizationLab] Export thất bại ở {folder}: {ex.Message}");
                }
            }
            return written;
        }

        public static string BuildCsv(IReadOnlyList<RunRecord> runs, bool includeHeader)
        {
            var sb = new StringBuilder(4096);
            if (includeHeader) sb.AppendLine(CsvHeader);
            foreach (RunRecord r in runs) AppendCsvRow(sb, r);
            return sb.ToString();
        }

        #endregion

        #region Private

        private static IEnumerable<string> TargetFolders()
        {
            yield return ResultsFolder;
            if (ProjectResultsFolder != null) yield return ProjectResultsFolder;
        }

        private static void AppendAllRuns(string folder, IReadOnlyList<RunRecord> runs)
        {
            string path = Path.Combine(folder, ALL_RUNS_FILE);
            bool exists = File.Exists(path);
            if (exists)
            {
                // Schema đổi → giữ file cũ, mở file mới (không trộn cột).
                string firstLine;
                using (var reader = new StreamReader(path)) firstLine = reader.ReadLine();
                if (firstLine != CsvHeader)
                {
                    File.Move(path, Path.Combine(folder, $"all_runs_old_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv"));
                    exists = false;
                }
            }
            File.AppendAllText(path, BuildCsv(runs, !exists), Encoding.UTF8);
        }

        private static void AppendCsvRow(StringBuilder sb, RunRecord r)
        {
            MetricsSummary m = r.metrics;
            EnvironmentInfo e = r.environment;
            string android = e.platform == "Android" ? e.operatingSystem : "n/a";
            var cells = new List<string>(CsvColumns.Length)
            {
                r.runId, r.utcTime, r.scene, r.variant.ToString(), I(r.seed), e.unityVersion, e.renderPipeline,
                e.urpVersion, e.device, android, e.graphicsApi, I(e.screenWidth), I(e.screenHeight), F(e.renderScale),
                I(r.objectCount), I(r.visibleRenderers), D(m.frameMsMedian), D(m.frameMsP95), D(m.frameMsP99),
                D(m.cpuMainMs), D(m.cpuRenderMs), D(m.gpuMs), D(m.batches), D(m.setPassCalls), D(m.triangles),
                D(m.vertices), D(m.allocatedMb), r.status.ToString().ToUpperInvariant(), r.invalidReason,
                r.runId.Split('_')[0], r.variantName, r.educational ? "1" : "0", I(r.orderIndex), I(r.repetition),
                I(r.expectedVisibleRenderers), D(m.fpsMedian), D(m.frameMsMean), D(m.frameMsStdev), D(m.frameMsMax),
                I(m.sampleCount), D(m.drawCalls), D(m.peakAllocatedMb), D(m.monoUsedMb), D(m.graphicsMemoryMb),
                D(m.gcAllocatedInFrameKbMean), m.cpuMainSource, m.cpuRenderSource, m.gpuSource, e.buildType,
                e.qualityPreset, e.pipelineAsset, I(e.targetFrameRate), I(e.vSyncCount), I(e.msaaSamples),
                e.hdr ? "1" : "0", e.srpBatcher ? "1" : "0", e.dynamicBatching ? "1" : "0", I(e.internalWidth),
                I(e.internalHeight), F(e.batteryLevel), e.thermalState, string.Join(" | ", r.notes),
            };
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(cells[i]));
            }
            sb.AppendLine();
        }

        private static string BuildJson(BenchmarkSession s)
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.Key("session_id").Value(s.sessionId);
            w.Key("kind").Value(s.kind);
            w.Key("scene").Value(s.scene);
            w.Key("scenario").Value(s.scenarioId);

            w.Key("metadata");
            WriteEnvironment(w, s.environment);

            w.Key("config").BeginObject();
            BenchmarkConfig c = s.config;
            w.Key("seed").Value(c.seed).Key("settle_seconds").Value(c.settleSeconds)
                .Key("warmup_seconds").Value(c.warmupSeconds).Key("measure_seconds").Value(c.measureSeconds)
                .Key("cooldown_seconds").Value(c.cooldownSeconds).Key("repetitions").Value(c.repetitions)
                .Key("target_frame_rate").Value(c.targetFrameRate);
            w.EndObject();

            w.Key("runs").BeginArray();
            foreach (RunRecord r in s.runs) WriteRun(w, r);
            w.EndArray();

            w.Key("comparison");
            if (s.comparison == null) w.Null();
            else WriteComparison(w, s.comparison);

            w.Key("notes").BeginArray();
            foreach (string n in s.notes) w.Value(n);
            w.EndArray();
            w.EndObject();
            return w.ToString();
        }

        private static void WriteEnvironment(JsonWriter w, EnvironmentInfo e)
        {
            w.BeginObject();
            w.Key("unity_version").Value(e.unityVersion).Key("render_pipeline").Value(e.renderPipeline)
                .Key("pipeline_asset").Value(e.pipelineAsset).Key("urp_version").Value(e.urpVersion)
                .Key("git_commit").Value(e.gitCommit).Key("build_type").Value(e.buildType)
                .Key("platform").Value(e.platform).Key("device").Value(e.device)
                .Key("os").Value(e.operatingSystem).Key("graphics_api").Value(e.graphicsApi)
                .Key("gpu").Value(e.gpuName).Key("cpu").Value(e.cpuName)
                .Key("system_memory_mb").Value(e.systemMemoryMb)
                .Key("screen").Value($"{e.screenWidth}x{e.screenHeight}")
                .Key("current_resolution").Value($"{e.currentResolutionWidth}x{e.currentResolutionHeight}")
                .Key("refresh_rate_hz").Value(e.refreshRateHz)
                .Key("render_scale").Value(e.renderScale)
                .Key("internal_resolution").Value($"{e.internalWidth}x{e.internalHeight}")
                .Key("quality_preset").Value(e.qualityPreset).Key("target_frame_rate").Value(e.targetFrameRate)
                .Key("vsync_count").Value(e.vSyncCount).Key("msaa").Value(e.msaaSamples).Key("hdr").Value(e.hdr)
                .Key("srp_batcher").Value(e.srpBatcher).Key("dynamic_batching").Value(e.dynamicBatching)
                .Key("frame_timing_stats").Value(e.frameTimingStats)
                .Key("battery_level").Value(e.batteryLevel).Key("battery_status").Value(e.batteryStatus)
                .Key("thermal_state").Value(e.thermalState);
            w.EndObject();
        }

        private static void WriteRun(JsonWriter w, RunRecord r)
        {
            MetricsSummary m = r.metrics;
            w.BeginObject();
            w.Key("run_id").Value(r.runId).Key("utc_time").Value(r.utcTime).Key("variant").Value(r.variant.ToString())
                .Key("variant_name").Value(r.variantName).Key("educational").Value(r.educational)
                .Key("order_index").Value(r.orderIndex).Key("repetition").Value(r.repetition)
                .Key("seed").Value(r.seed).Key("object_count").Value(r.objectCount)
                .Key("visible_renderers").Value(r.visibleRenderers)
                .Key("expected_visible_renderers").Value(r.expectedVisibleRenderers)
                .Key("screenshot").Value(r.screenshotPath);

            w.Key("validation").BeginObject();
            w.Key("status").Value(r.status.ToString().ToUpperInvariant()).Key("invalid_reason").Value(r.invalidReason);
            w.Key("notes").BeginArray();
            foreach (string n in r.notes) w.Value(n);
            w.EndArray();
            w.EndObject();

            w.Key("variant_settings").BeginObject();
            foreach (var kv in r.variantSettings) w.Key(kv.Key).Value(kv.Value);
            w.EndObject();

            w.Key("metrics").BeginObject();
            w.Key("sample_count").Value(m.sampleCount).Key("buffer_overflow").Value(m.bufferOverflow)
                .Key("frame_ms_median").Value(m.frameMsMedian).Key("frame_ms_mean").Value(m.frameMsMean)
                .Key("frame_ms_p95").Value(m.frameMsP95).Key("frame_ms_p99").Value(m.frameMsP99)
                .Key("frame_ms_stdev").Value(m.frameMsStdev).Key("frame_ms_max").Value(m.frameMsMax)
                .Key("fps_median").Value(m.fpsMedian)
                .Key("cpu_main_ms").Value(m.cpuMainMs).Key("cpu_main_source").Value(m.cpuMainSource)
                .Key("cpu_render_ms").Value(m.cpuRenderMs).Key("cpu_render_source").Value(m.cpuRenderSource)
                .Key("gpu_ms").Value(m.gpuMs).Key("gpu_source").Value(m.gpuSource)
                .Key("batches").Value(m.batches).Key("setpass_calls").Value(m.setPassCalls)
                .Key("draw_calls").Value(m.drawCalls).Key("triangles").Value(m.triangles)
                .Key("vertices").Value(m.vertices).Key("allocated_mb").Value(m.allocatedMb)
                .Key("peak_allocated_mb").Value(m.peakAllocatedMb).Key("mono_used_mb").Value(m.monoUsedMb)
                .Key("graphics_memory_mb").Value(m.graphicsMemoryMb)
                .Key("gc_alloc_kb_per_frame").Value(m.gcAllocatedInFrameKbMean);
            if (m.rawFrameMs != null)
            {
                w.Key("raw_frame_ms").BeginArray();
                foreach (float f in m.rawFrameMs) w.Value(f);
                w.EndArray();
            }
            w.EndObject();
            w.EndObject();
        }

        private static void WriteComparison(JsonWriter w, ComparisonResult c)
        {
            w.BeginObject();
            w.Key("variant_a").Value(c.variantA.ToString()).Key("variant_b").Value(c.variantB.ToString())
                .Key("valid_runs_a").Value(c.validRunsA).Key("valid_runs_b").Value(c.validRunsB)
                .Key("frame_ms_median_a").Value(c.frameMedianA).Key("frame_ms_median_b").Value(c.frameMedianB)
                .Key("frame_ms_p95_a").Value(c.frameP95A).Key("frame_ms_p95_b").Value(c.frameP95B)
                .Key("cpu_main_ms_a").Value(c.cpuMainA).Key("cpu_main_ms_b").Value(c.cpuMainB)
                .Key("cpu_render_ms_a").Value(c.cpuRenderA).Key("cpu_render_ms_b").Value(c.cpuRenderB)
                .Key("gpu_ms_a").Value(c.gpuA).Key("gpu_ms_b").Value(c.gpuB)
                .Key("batches_a").Value(c.batchesA).Key("batches_b").Value(c.batchesB)
                .Key("setpass_a").Value(c.setPassA).Key("setpass_b").Value(c.setPassB)
                .Key("improvement_percent").Value(c.improvementPercent).Key("noise_ms").Value(c.noiseMs)
                .Key("verdict").Value(c.verdict);
            w.Key("config_differences").BeginArray();
            foreach (string d in c.configDifferences) w.Value(d);
            w.EndArray();
            w.Key("notes").BeginArray();
            foreach (string n in c.notes) w.Value(n);
            w.EndArray();
            w.EndObject();
        }

        private static string I(int v) => v.ToString(Inv);
        private static string F(float v) => float.IsNaN(v) ? UNSUPPORTED : v.ToString("0.###", Inv);
        private static string D(double v) => double.IsNaN(v) ? UNSUPPORTED : v.ToString("0.####", Inv);

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        #endregion

        /// <summary>JSON writer tối giản: hỗ trợ null cho NaN (JsonUtility không làm được).</summary>
        private sealed class JsonWriter
        {
            private readonly StringBuilder _sb = new StringBuilder(8192);
            private readonly Stack<bool> _first = new Stack<bool>();
            private bool _afterKey;

            public JsonWriter BeginObject() { Prefix(); _sb.Append('{'); _first.Push(true); return this; }
            public JsonWriter EndObject() { _first.Pop(); _sb.Append('}'); return this; }
            public JsonWriter BeginArray() { Prefix(); _sb.Append('['); _first.Push(true); return this; }
            public JsonWriter EndArray() { _first.Pop(); _sb.Append(']'); return this; }

            public JsonWriter Key(string key)
            {
                Prefix();
                WriteString(key);
                _sb.Append(':');
                _afterKey = true;
                return this;
            }

            public JsonWriter Value(string v) { Prefix(); if (v == null) _sb.Append("null"); else WriteString(v); return this; }
            public JsonWriter Value(bool v) { Prefix(); _sb.Append(v ? "true" : "false"); return this; }
            public JsonWriter Value(int v) { Prefix(); _sb.Append(v.ToString(Inv)); return this; }
            public JsonWriter Value(float v) => Value((double)v);

            public JsonWriter Value(double v)
            {
                Prefix();
                if (double.IsNaN(v) || double.IsInfinity(v)) _sb.Append("null");
                else _sb.Append(v.ToString("0.#####", Inv));
                return this;
            }

            public JsonWriter Null() { Prefix(); _sb.Append("null"); return this; }

            public override string ToString() => _sb.ToString();

            private void Prefix()
            {
                if (_afterKey) { _afterKey = false; return; }
                if (_first.Count == 0) return;
                if (_first.Peek()) { _first.Pop(); _first.Push(false); }
                else _sb.Append(',');
            }

            private void WriteString(string s)
            {
                _sb.Append('"');
                foreach (char ch in s)
                {
                    switch (ch)
                    {
                        case '"': _sb.Append("\\\""); break;
                        case '\\': _sb.Append("\\\\"); break;
                        case '\n': _sb.Append("\\n"); break;
                        case '\r': _sb.Append("\\r"); break;
                        case '\t': _sb.Append("\\t"); break;
                        default:
                            if (ch < 0x20) _sb.Append("\\u").Append(((int)ch).ToString("x4"));
                            else _sb.Append(ch);
                            break;
                    }
                }
                _sb.Append('"');
            }
        }
    }
}
