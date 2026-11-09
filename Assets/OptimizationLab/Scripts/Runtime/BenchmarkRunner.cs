using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OptimizationLab
{
    /// <summary>
    /// Điều phối phiên đo: setup → settle → warm-up → (screenshot) → measure → teardown → cooldown,
    /// lặp theo thứ tự ABBA để giảm drift nhiệt. Xuất CSV + JSON sau mỗi phiên.
    /// </summary>
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        #region Fields

        private const int MIN_VALID_SAMPLES = 30;
        private const float CAPTURE_SETTLE_SECONDS = 0.5f;
        /// <summary>Quá tỉ lệ frame app ở phía sau này → INVALID (vài frame lẻ chỉ ghi chú).</summary>
        private const float MAX_INACTIVE_FRAME_SHARE = 0.02f;

        [SerializeField] private BenchmarkScenario scenario;
        [Tooltip("Cấu hình đo chính thức (thiết bị).")]
        [SerializeField] private BenchmarkConfig fullConfig = new BenchmarkConfig();
        [Tooltip("Cấu hình smoke test nhanh (Editor). Không dùng để kết luận.")]
        [SerializeField] private BenchmarkConfig quickConfig = BenchmarkConfig.CreateQuick();
        [Tooltip("Variant hiển thị khi runner rảnh (preview).")]
        [SerializeField] private BenchmarkVariant previewVariant = BenchmarkVariant.B;

        private readonly MetricsCollector _collector = new MetricsCollector();
        private readonly struct PlanItem
        {
            public readonly BenchmarkVariant Variant;
            public readonly int Count;
            public readonly int Repetition;

            public PlanItem(BenchmarkVariant variant, int count, int repetition)
            {
                Variant = variant;
                Count = count;
                Repetition = repetition;
            }
        }

        private Coroutine _routine;
        private bool _cancelRequested;
        private float _liveFrameMs;
        private int _lastScreenW, _lastScreenH;
        private bool _lostFocusDuringMeasure;
        private int _inactiveFrames;
        private bool _hudVisibleDuringMeasure;

        #endregion

        #region Initialize

        private void Awake()
        {
            // Mặc định Editor = quick (smoke test), Player = full. Đổi bằng nút trên HUD.
            UseQuickConfig = Application.isEditor;
            if (scenario != null)
            {
                fullConfig.objectCount = quickConfig.objectCount = scenario.DefaultObjectCount;
                scenario.ObjectCount = ActiveConfig.objectCount;
            }
        }

        private void Start()
        {
            ApplyFrameRate();
            SetupPreview();
        }

        private void Update()
        {
            float dtMs = Time.unscaledDeltaTime * 1000f;
            _liveFrameMs = _liveFrameMs <= 0f ? dtMs : Mathf.Lerp(_liveFrameMs, dtMs, 0.1f);

            // Khi rảnh: giữ camera khớp aspect mới (xoay màn hình / đổi Game view).
            if (!IsBusy && (Screen.width != _lastScreenW || Screen.height != _lastScreenH)) SetupPreview();
            if (!IsBusy && scenario != null && scenario.IsSetUp) scenario.WarmupTick(Time.unscaledDeltaTime);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && Phase == RunnerPhase.Measuring) _inactiveFrames++;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && Phase == RunnerPhase.Measuring) _lostFocusDuringMeasure = true;
        }

        private void OnDestroy()
        {
            _collector.Dispose();
        }

        #endregion

        #region Public

        public event Action SessionStarted;
        public event Action<BenchmarkSession> SessionCompleted;

        public BenchmarkScenario Scenario => scenario;
        public BenchmarkVariant PreviewVariant => previewVariant;
        public bool UseQuickConfig { get; set; }
        public BenchmarkConfig ActiveConfig => UseQuickConfig ? quickConfig : fullConfig;
        public bool IsBusy => _routine != null;
        public RunnerPhase Phase { get; private set; } = RunnerPhase.Idle;
        public float PhaseElapsed { get; private set; }
        public float PhaseDuration { get; private set; }
        public int PlanIndex { get; private set; }
        public int PlanCount { get; private set; }
        public BenchmarkVariant? RunningVariant { get; private set; }
        public float LiveFrameMs => _liveFrameMs;
        public BenchmarkSession LastSession { get; private set; }
        public IReadOnlyList<string> LastExportPaths { get; private set; } = Array.Empty<string>();
        public string LastSummaryText { get; private set; } = "";

        public void SetObjectCount(int count)
        {
            if (IsBusy) return;
            fullConfig.objectCount = count;
            quickConfig.objectCount = count;
            SetupPreview();
        }

        public void SetPreviewVariant(BenchmarkVariant variant)
        {
            if (IsBusy) return;
            previewVariant = variant;
            SetupPreview();
        }

        /// <summary>Chuyển cap 60 FPS ↔ uncapped (vSync off).</summary>
        public void ToggleFrameCap()
        {
            if (IsBusy) return;
            int target = fullConfig.targetFrameRate > 0 ? -1 : 60;
            fullConfig.targetFrameRate = target;
            quickConfig.targetFrameRate = target;
            ApplyFrameRate();
        }

        /// <summary>Chạy một variant × repetitions.</summary>
        public void RunSingle(BenchmarkVariant variant)
        {
            if (IsBusy || scenario == null) return;
            var plan = new List<PlanItem>();
            for (int i = 0; i < ActiveConfig.repetitions; i++) plan.Add(new PlanItem(variant, ActiveConfig.objectCount, i));
            StartPlan(plan, "single" + variant, null, null);
        }

        /// <summary>Chạy A/B theo thứ tự ABBA AB... (mỗi variant đủ repetitions lần).</summary>
        public void RunAbba()
        {
            if (IsBusy || scenario == null) return;
            BenchmarkVariant a = scenario.PrimaryA, b = scenario.PrimaryB;
            var plan = new List<PlanItem>();
            int reps = Mathf.Max(1, ActiveConfig.repetitions);
            for (int i = 0; i < reps; i++)
            {
                bool forward = (i & 1) == 0;
                plan.Add(new PlanItem(forward ? a : b, ActiveConfig.objectCount, i));
                plan.Add(new PlanItem(forward ? b : a, ActiveConfig.objectCount, i));
            }
            StartPlan(plan, "abba", a, b);
        }

        /// <summary>Đường cong frame time theo object count; thứ tự tăng/giảm xen kẽ mỗi lượt.</summary>
        public void RunCountSweep()
        {
            if (IsBusy || scenario == null || scenario.SweepVariant == null) return;
            int[] counts = scenario.ObjectCountOptions;
            var plan = new List<PlanItem>();
            int reps = Mathf.Max(1, ActiveConfig.repetitions);
            for (int rep = 0; rep < reps; rep++)
            {
                bool ascending = (rep & 1) == 0;
                for (int k = 0; k < counts.Length; k++)
                {
                    int count = counts[ascending ? k : counts.Length - 1 - k];
                    plan.Add(new PlanItem(scenario.SweepVariant.Value, count, rep));
                }
            }
            StartPlan(plan, "sweep", null, null);
        }

        /// <summary>HUD báo đang hiển thị trong cửa sổ đo → run được ghi chú (UI cũng tốn frame time).</summary>
        public void NoteHudVisibleDuringMeasure()
        {
            if (Phase == RunnerPhase.Measuring) _hudVisibleDuringMeasure = true;
        }

        public void Cancel()
        {
            if (IsBusy) _cancelRequested = true;
        }

        /// <summary>Xuất lại phiên cuối (vd. sau khi thư mục bị xoá).</summary>
        public void ExportLast()
        {
            if (LastSession == null) return;
            LastExportPaths = ResultsExporter.Export(LastSession);
        }

        #endregion

        #region Private

        private void ApplyFrameRate()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fullConfig.targetFrameRate;
        }

        private void SetupPreview()
        {
            if (scenario == null) return;
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;
            scenario.Teardown();
            scenario.ObjectCount = ActiveConfig.objectCount;
            scenario.Setup(previewVariant, ActiveConfig.seed);
        }

        private void StartPlan(List<PlanItem> plan, string kind, BenchmarkVariant? a, BenchmarkVariant? b)
        {
            _cancelRequested = false;
            _routine = StartCoroutine(RunPlan(plan, kind, a, b));
            SessionStarted?.Invoke();
        }

        private IEnumerator RunPlan(List<PlanItem> plan, string kind, BenchmarkVariant? a, BenchmarkVariant? b)
        {
            BenchmarkConfig cfg = ActiveConfig.Clone();
            ApplyFrameRate();

            var session = new BenchmarkSession
            {
                sessionId = DateTime.UtcNow.ToString("yyyyMMddTHHmmss"),
                kind = kind,
                scene = SceneManager.GetActiveScene().name,
                scenarioId = scenario.Id,
                config = cfg,
                environment = EnvironmentInfo.Capture(),
            };
            if (UseQuickConfig) session.notes.Add("QUICK config: smoke test, không dùng để kết luận hiệu năng.");
            if (Application.isEditor) session.notes.Add("Chạy trong Editor: chỉ để xác minh cơ chế, không phải số đo thiết bị.");

            PlanCount = plan.Count;
            var screenshotted = new HashSet<string>();

            for (int i = 0; i < plan.Count && !_cancelRequested; i++)
            {
                PlanIndex = i;
                PlanItem item = plan[i];
                RunRecord record = null;
                yield return RunOne(item, i, cfg, session, screenshotted, r => record = r);
                if (record != null) session.runs.Add(record);

                bool last = i == plan.Count - 1;
                if (!last && !_cancelRequested)
                    yield return WaitPhase(RunnerPhase.Cooldown, cfg.cooldownSeconds, null);
            }

            if (_cancelRequested) session.notes.Add("Phiên bị huỷ giữa chừng — dữ liệu không đầy đủ.");
            if (a.HasValue && b.HasValue) session.comparison = ComparisonResult.Compare(session.runs, a.Value, b.Value);

            LastSession = session;
            LastExportPaths = session.runs.Count > 0 ? ResultsExporter.Export(session) : Array.Empty<string>();
            LastSummaryText = BuildSummary(session);
            Debug.Log($"[OptimizationLab] {session.scenarioId} {kind} xong.\n{LastSummaryText}\n" +
                      string.Join("\n", LastExportPaths));

            RunningVariant = null;
            Phase = RunnerPhase.Idle;
            _routine = null;
            SetupPreview();
            SessionCompleted?.Invoke(session);
        }

        private IEnumerator RunOne(PlanItem item, int orderIndex, BenchmarkConfig cfg, BenchmarkSession session,
            HashSet<string> screenshotted, Action<RunRecord> done)
        {
            RunningVariant = item.Variant;
            scenario.Teardown();
            scenario.ObjectCount = item.Count;
            scenario.Setup(item.Variant, cfg.seed);

            // Bỏ qua giai đoạn ngay sau khi đổi variant (spec: exclude first 2 s).
            yield return WaitPhase(RunnerPhase.Settling, cfg.settleSeconds, scenario.WarmupTick);
            if (_cancelRequested) yield break;
            yield return WaitPhase(RunnerPhase.Warmup, cfg.warmupSeconds, scenario.WarmupTick);
            if (_cancelRequested) yield break;

            var record = new RunRecord
            {
                runId = $"{session.sessionId}_{orderIndex:00}",
                scene = session.scene,
                scenarioId = scenario.Id,
                variant = item.Variant,
                variantName = scenario.VariantKey(item.Variant),
                educational = scenario.IsEducationalVariant(item.Variant),
                orderIndex = orderIndex,
                repetition = item.Repetition,
                seed = cfg.seed,
                objectCount = item.Count,
                expectedVisibleRenderers = scenario.ExpectedVisibleRenderers,
            };
            scenario.GetVariantSettings(item.Variant, record.variantSettings);
            if (!double.IsNaN(scenario.MeshMemoryMb)) record.notes.Add("fixture_mesh_memory_mb=" + scenario.MeshMemoryMb.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
            record.variantSettings["object_count"] = item.Count.ToString();

            // Screenshot TRƯỚC measurement (frame chụp bị stall, không được nằm trong cửa sổ đo).
            string shotKey = item.Variant + "_" + item.Count;
            if (cfg.captureScreenshots && screenshotted.Add(shotKey))
            {
                record.screenshotPath = CaptureScreenshot(session, shotKey);
                yield return WaitPhase(RunnerPhase.Capturing, CAPTURE_SETTLE_SECONDS, scenario.WarmupTick);
            }

            // Môi trường tham chiếu = lúc bắt đầu đo run đầu tiên (resize trước đó không phải lỗi của phép so A/B).
            if (!session.referenceCaptured)
            {
                session.environment = EnvironmentInfo.Capture();
                session.referenceCaptured = true;
            }
            ValidateGates(record, session, preMeasure: true);

            // ---- Measurement window: không alloc, không instantiate, không log ----
            _lostFocusDuringMeasure = false;
            _inactiveFrames = 0;
            _hudVisibleDuringMeasure = !ActiveConfig.hideHudWhileMeasuring;
            _collector.Begin(cfg.measureSeconds);
            Phase = RunnerPhase.Measuring;
            PhaseDuration = cfg.measureSeconds;
            PhaseElapsed = 0f;
            double start = Time.realtimeSinceStartupAsDouble;
            while (!_cancelRequested)
            {
                yield return null;
                scenario.MeasurementTick(Time.unscaledDeltaTime);
                _collector.Sample();
                if (!AppIsActive()) _inactiveFrames++;
                PhaseElapsed = (float)(Time.realtimeSinceStartupAsDouble - start);
                if (PhaseElapsed >= cfg.measureSeconds) break;
            }
            record.metrics = _collector.End(cfg.exportRawSamples);
            // ---- hết measurement ----

            record.utcTime = DateTime.UtcNow.ToString("o");
            record.environment = EnvironmentInfo.Capture();
            ValidateGates(record, session, preMeasure: false);
            if (_cancelRequested) Invalidate(record, "Run bị huỷ trước khi đo xong.");
            // Editor/app ở phía sau → Unity hạ tốc độ frame (Editor throttle / app pause): số đo không còn đại diện.
            int samples = Mathf.Max(1, record.metrics.sampleCount);
            float inactiveShare = _inactiveFrames / (float)samples;
            if (_lostFocusDuringMeasure || inactiveShare > MAX_INACTIVE_FRAME_SHARE)
                Invalidate(record, $"Cửa sổ mất focus/bị pause trong lúc đo ({_inactiveFrames}/{samples} frame).");
            else if (_inactiveFrames > 0)
                record.notes.Add($"{_inactiveFrames} frame app không active (dưới ngưỡng {MAX_INACTIVE_FRAME_SHARE:P0}).");
            if (_hudVisibleDuringMeasure) record.notes.Add("HUD hiển thị trong lúc đo — frame time gồm cả chi phí UI.");

            scenario.Teardown();
            done(record);
        }

        private IEnumerator WaitPhase(RunnerPhase phase, float seconds, Action<float> tick)
        {
            Phase = phase;
            PhaseDuration = seconds;
            PhaseElapsed = 0f;
            double start = Time.realtimeSinceStartupAsDouble;
            while (PhaseElapsed < seconds && !_cancelRequested)
            {
                yield return null;
                tick?.Invoke(Time.unscaledDeltaTime);
                PhaseElapsed = (float)(Time.realtimeSinceStartupAsDouble - start);
            }
        }

        /// <summary>Validation gates (spec mục 4). Lỗi → INVALID kèm lý do; cảnh báo → notes.</summary>
        private void ValidateGates(RunRecord record, BenchmarkSession session, bool preMeasure)
        {
            if (preMeasure)
            {
                if (scenario.Validate(out string reason) != BenchmarkStatus.Valid) Invalidate(record, reason);
                record.visibleRenderers = scenario.CountVisibleRenderers();
                if (record.visibleRenderers != record.expectedVisibleRenderers)
                    Invalidate(record, $"visible_renderers {record.visibleRenderers} ≠ expected {record.expectedVisibleRenderers}");
                return;
            }

            EnvironmentInfo start = session.environment;
            EnvironmentInfo now = record.environment;
            if (now.screenWidth != start.screenWidth || now.screenHeight != start.screenHeight)
                Invalidate(record, $"Resolution đổi giữa phiên ({start.screenWidth}x{start.screenHeight} → {now.screenWidth}x{now.screenHeight}).");
            if (now.pipelineAsset != start.pipelineAsset || now.qualityPreset != start.qualityPreset)
                Invalidate(record, "Pipeline asset / quality preset đổi giữa phiên.");
            if (!float.IsNaN(start.renderScale) && !Mathf.Approximately(now.renderScale, start.renderScale))
                Invalidate(record, "Render scale đổi giữa phiên.");
            if (now.graphicsApi != start.graphicsApi) Invalidate(record, "Graphics API đổi giữa phiên.");

            MetricsSummary m = record.metrics;
            if (m.sampleCount < MIN_VALID_SAMPLES) Invalidate(record, $"Quá ít sample ({m.sampleCount}).");
            if (m.bufferOverflow) record.notes.Add("Buffer sample đầy — một phần frame cuối không được ghi.");
            if (scenario.Validate(out string post) != BenchmarkStatus.Valid) Invalidate(record, "Sau đo: " + post);

            if (Application.isEditor) record.notes.Add("EDITOR run");
            if (GraphicsSettings.currentRenderPipeline == null) record.notes.Add("Không có SRP active.");
            if (double.IsNaN(m.gpuMs)) record.notes.Add("GPU time unsupported trên thiết bị/API này.");
            int target = now.targetFrameRate;
            if (target > 0 && m.frameMsMedian <= 1000.0 / target * 1.05)
                record.notes.Add($"Frame chạm cap {target} FPS — đọc CPU/GPU ms và P95 thay vì FPS.");
            if (record.educational) record.notes.Add("Variant giáo dục/composite: đổi nhiều hơn một yếu tố.");
        }

        /// <summary>
        /// App đang ở phía trước? Trong Editor dùng trạng thái cửa sổ Editor (Application.isFocused không đáng tin
        /// khi runInBackground bật) — Editor ở phía sau bị hạ tốc độ frame nên số đo vô nghĩa.
        /// </summary>
        private static bool AppIsActive()
        {
#if UNITY_EDITOR
            return UnityEditorInternal.InternalEditorUtility.isApplicationActive;
#else
            return Application.isFocused;
#endif
        }

        private static void Invalidate(RunRecord record, string reason)
        {
            record.status = BenchmarkStatus.Invalid;
            record.invalidReason = string.IsNullOrEmpty(record.invalidReason) ? reason : record.invalidReason + "; " + reason;
        }

        private static string CaptureScreenshot(BenchmarkSession session, string key)
        {
            try
            {
                string folder = Path.Combine(ResultsExporter.ResultsFolder, "screenshots");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"{session.scenarioId}_{session.sessionId}_{key}.png");
                ScreenCapture.CaptureScreenshot(path);
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OptimizationLab] Không chụp được screenshot: {ex.Message}");
                return "";
            }
        }

        private static string BuildSummary(BenchmarkSession s)
        {
            var sb = new StringBuilder(512);
            foreach (RunRecord r in s.runs)
            {
                sb.Append('#').Append(r.orderIndex).Append(' ').Append(r.variant).Append(" n=").Append(r.objectCount)
                    .Append("  median ").Append(Fmt(r.metrics.frameMsMedian)).Append(" ms")
                    .Append("  P95 ").Append(Fmt(r.metrics.frameMsP95))
                    .Append("  CPU ").Append(Fmt(r.metrics.cpuMainMs))
                    .Append("  GPU ").Append(Fmt(r.metrics.gpuMs))
                    .Append("  ").Append(r.status.ToString().ToUpperInvariant());
                if (r.status != BenchmarkStatus.Valid) sb.Append(" (").Append(r.invalidReason).Append(')');
                sb.AppendLine();
            }
            ComparisonResult c = s.comparison;
            if (c != null)
            {
                sb.Append("A/B: ").Append(Fmt(c.frameMedianA)).Append(" → ").Append(Fmt(c.frameMedianB))
                    .Append(" ms (").Append(Fmt(c.improvementPercent)).Append("%), noise ±").Append(Fmt(c.noiseMs))
                    .Append(" ms → ").Append(c.verdict.ToUpperInvariant()).AppendLine();
                foreach (string n in c.notes) sb.Append("• ").AppendLine(n);
            }
            return sb.ToString();
        }

        private static string Fmt(double v) => double.IsNaN(v) ? "n/a" : v.ToString("0.00");

        #endregion
    }
}
