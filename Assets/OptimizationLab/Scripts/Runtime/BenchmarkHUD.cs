using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

namespace OptimizationLab
{
    /// <summary>Hành động người dùng trên HUD — TutorialGuide nghe để biết bước đã xong.</summary>
    public enum HudAction { Preview, Count, RunAbba, RunSweep, Help, Tutorial }

    /// <summary>
    /// HUD gọn kiểu game: thanh trên (tên + chip FPS), thanh dưới (xem A/B/C, số khối, nút ĐO lớn, công cụ nhỏ),
    /// thẻ kết quả 2 dòng. Node UI nằm sẵn trong scene (generator dựng); script chỉ bơm chữ/màu.
    /// Trong lúc đo chỉ còn một viên "Đang đo" để UI không cộng vào frame time.
    /// </summary>
    public sealed class BenchmarkHUD : MonoBehaviour
    {
        #region Fields

        private const float REFRESH_INTERVAL = 0.25f;
        private const float GOOD_MS = 16.7f;
        private const float OK_MS = 33.4f;

        public static readonly Color ColorButton = new Color(0.17f, 0.20f, 0.27f, 1f);
        public static readonly Color ColorSelected = new Color(0.16f, 0.55f, 0.78f, 1f);
        public static readonly Color ColorGood = new Color(0.20f, 0.66f, 0.38f, 1f);
        public static readonly Color ColorOk = new Color(0.88f, 0.64f, 0.16f, 1f);
        public static readonly Color ColorBad = new Color(0.84f, 0.29f, 0.27f, 1f);
        public static readonly Color ColorNeutral = new Color(0.45f, 0.50f, 0.58f, 1f);

        [SerializeField] private BenchmarkRunner runner;

        [Header("Thanh trên")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text subtitleText;
        [SerializeField] private Image fpsChip;
        [SerializeField] private Text fpsValueText;
        [SerializeField] private Text fpsMsText;

        [Header("Đang đo")]
        [SerializeField] private GameObject measuringPill;
        [SerializeField] private Text measuringText;
        [SerializeField] private RectTransform measuringFill;

        [Header("Thanh dưới")]
        [SerializeField] private GameObject bottomPanel;
        [SerializeField] private Image[] previewButtons;
        [SerializeField] private Text[] previewLabels;
        [SerializeField] private Image[] countButtons;
        [SerializeField] private Text[] countLabels;
        [Tooltip("Ô số liệu live (số lớn) — nội dung do scenario.LiveStats quyết định.")]
        [SerializeField] private Text[] statValueTexts;
        [SerializeField] private Text[] statCaptionTexts;
        [SerializeField] private GameObject showUiButton;

        [Header("Kết quả")]
        [SerializeField] private GameObject resultCard;
        [SerializeField] private Image resultAccent;
        [SerializeField] private Text resultTitle;
        [SerializeField] private Text resultLine;

        [Header("Overlay")]
        [SerializeField] private GameObject helpPanel;
        [SerializeField] private Text helpTitle;
        [SerializeField] private Text[] helpTips;
        [SerializeField] private GameObject detailPanel;
        [SerializeField] private Text detailText;

        private readonly StringBuilder _sb = new StringBuilder(256);
        private float _nextRefresh;
        private bool _uiHidden;
        private bool _forceShowWhileMeasuring;
        private int _lastPillSecond = -1;
        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _setPass;
        private ProfilerRecorder _batches;
        private ProfilerRecorder _mainThread;
        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private double _sumCpuMain, _sumCpuRender, _sumGpu;
        private int _nCpuMain, _nCpuRender, _nGpu;
        private double _avgCpuMain = double.NaN, _avgCpuRender = double.NaN, _avgGpu = double.NaN;

        #endregion

        #region Initialize

        private void Start()
        {
            if (runner == null) return;
            runner.SessionCompleted += OnSessionCompleted;
            BenchmarkScenario s = runner.Scenario;
            if (s != null)
            {
                if (titleText != null) titleText.text = s.Id;
                if (subtitleText != null) subtitleText.text = s.ShortTitle;
                if (helpTitle != null) helpTitle.text = s.Id + " · " + s.ShortTitle;
                FillHelp(s.QuickTips);
            }
            SetActive(helpPanel, false);
            SetActive(detailPanel, false);
            SetActive(resultCard, false);
            FillPreviewLabels();
            FillCountLabels();
            FillStatCaptions();
            Refresh();
        }

        private void OnEnable()
        {
            // Số lần vẽ live cho bài học. Có thể không có trong bản build Release → hiện "—".
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 4);
        }

        private void OnDisable()
        {
            _drawCalls.Dispose();
            _setPass.Dispose();
            _batches.Dispose();
            _mainThread.Dispose();
        }

        private void OnDestroy()
        {
            if (runner != null) runner.SessionCompleted -= OnSessionCompleted;
        }

        private void Update()
        {
            if (runner == null) return;
            bool measuring = runner.Phase == RunnerPhase.Measuring;
            if (!runner.IsBusy) _forceShowWhileMeasuring = false;

            bool autoHidden = measuring && runner.ActiveConfig.hideHudWhileMeasuring && !_forceShowWhileMeasuring;
            SetActive(bottomPanel, !_uiHidden && !autoHidden);
            SetActive(showUiButton, _uiHidden && !runner.IsBusy);
            SetActive(measuringPill, runner.IsBusy);
            if (runner.IsBusy) UpdatePill();
            if (measuring && _forceShowWhileMeasuring) runner.NoteHudVisibleDuringMeasure();

            if (!autoHidden) SampleTimings();
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + REFRESH_INTERVAL;
            if (!autoHidden) AverageTimings();
            // Trong lúc đo không cập nhật chip FPS (tránh rebuild text) — trừ khi người dùng bật HUD.
            if (!autoHidden) Refresh();
        }

        #endregion

        #region Public — nút bấm (wired trong scene)

        public event Action<HudAction, int> ActionPerformed;

        public BenchmarkRunner Runner => runner;

        public string LiveDrawCallsText => LiveValue(LiveStat.DrawCalls);
        public string LiveMsText => runner != null ? runner.LiveFrameMs.ToString("0.0") : "-";
        public string LiveCountText => LiveValue(LiveStat.ObjectCount);

        /// <summary>Giá trị live dạng chữ; "—" nếu counter không có trên bản build/thiết bị này.</summary>
        public string LiveValue(LiveStat stat)
        {
            switch (stat)
            {
                case LiveStat.MeshMemoryMb: return runner != null && !double.IsNaN(runner.Scenario.MeshMemoryMb) ? runner.Scenario.MeshMemoryMb.ToString("0.00") : "—";
                case LiveStat.ObjectCount: return runner != null ? runner.ActiveConfig.objectCount.ToString("N0") : "—";
                case LiveStat.DrawCalls: return Count(_drawCalls);
                case LiveStat.SetPassCalls: return Count(_setPass);
                case LiveStat.Batches: return Count(_batches);
                case LiveStat.CpuMainMs:
                    if (!double.IsNaN(_avgCpuMain)) return _avgCpuMain.ToString("0.00");
                    return _mainThread.Valid && _mainThread.Count > 0 ? (_mainThread.LastValue * 1e-6).ToString("0.00") : "—";
                case LiveStat.CpuRenderMs: return double.IsNaN(_avgCpuRender) ? "—" : _avgCpuRender.ToString("0.00");
                case LiveStat.GpuMs: return double.IsNaN(_avgGpu) ? "—" : _avgGpu.ToString("0.00");
                default: return "—";
            }
        }

        public static string Caption(LiveStat stat)
        {
            switch (stat)
            {
                case LiveStat.MeshMemoryMb: return "mesh (MB)";
                case LiveStat.ObjectCount: return "khối";
                case LiveStat.DrawCalls: return "lần vẽ";
                case LiveStat.SetPassCalls: return "SetPass";
                case LiveStat.Batches: return "batches";
                case LiveStat.CpuMainMs: return "CPU (ms)";
                case LiveStat.CpuRenderMs: return "CPU vẽ (ms)";
                case LiveStat.GpuMs: return "GPU (ms)";
                default: return "";
            }
        }

        public void PreviewVariant(int index)
        {
            if (runner == null || runner.IsBusy) return;
            runner.SetPreviewVariant((BenchmarkVariant)index);
            Refresh();
            ActionPerformed?.Invoke(HudAction.Preview, index);
        }

        public void SetCountIndex(int index)
        {
            if (runner == null || runner.IsBusy || runner.Scenario == null) return;
            int[] counts = runner.Scenario.ObjectCountOptions;
            if (index < 0 || index >= counts.Length) return;
            runner.SetObjectCount(counts[index]);
            Refresh();
            ActionPerformed?.Invoke(HudAction.Count, index);
        }

        public void RunAbba()
        {
            if (runner == null || runner.IsBusy) return;
            SetActive(resultCard, false);
            runner.RunAbba();
            ActionPerformed?.Invoke(HudAction.RunAbba, 0);
        }

        public void RunSweep()
        {
            if (runner == null || runner.IsBusy) return;
            SetActive(resultCard, false);
            // Nút "Đo": bài có đường cong (B00) → đo theo số khối; bài so sánh (B01) → đo A vs B theo ABBA.
            if (runner.Scenario != null && runner.Scenario.SweepVariant == null) runner.RunAbba();
            else runner.RunCountSweep();
            ActionPerformed?.Invoke(HudAction.RunSweep, 0);
        }

        /// <summary>Chạy một variant (dùng từ Editor window / automation).</summary>
        public void RunVariant(int index)
        {
            if (runner != null) runner.RunSingle((BenchmarkVariant)index);
        }

        public void Cancel() { if (runner != null) runner.Cancel(); }

        public void ToggleQuick()
        {
            if (runner == null || runner.IsBusy) return;
            runner.UseQuickConfig = !runner.UseQuickConfig;
            Refresh();
        }

        public void ToggleCap()
        {
            if (runner == null) return;
            runner.ToggleFrameCap();
            Refresh();
        }

        public void ToggleHelp()
        {
            Toggle(helpPanel);
            if (helpPanel != null && helpPanel.activeSelf) ActionPerformed?.Invoke(HudAction.Help, 0);
        }

        public void ToggleDetail()
        {
            if (detailText != null && runner != null)
            {
                string paths = runner.LastExportPaths.Count > 0 ? "\n\nFile: " + runner.LastExportPaths[0] : "";
                detailText.text = string.IsNullOrEmpty(runner.LastSummaryText) ? "Chưa có kết quả." : runner.LastSummaryText + paths;
            }
            Toggle(detailPanel);
        }

        public void StartTutorial() => ActionPerformed?.Invoke(HudAction.Tutorial, 0);

        /// <summary>Ẩn/hiện UI. Trong lúc đo: hiện lại HUD (run được ghi chú vì UI cũng tốn frame time).</summary>
        public void ToggleUi()
        {
            if (runner != null && runner.Phase == RunnerPhase.Measuring && runner.ActiveConfig.hideHudWhileMeasuring)
            {
                _forceShowWhileMeasuring = !_forceShowWhileMeasuring;
                _uiHidden = false;
                return;
            }
            _uiHidden = !_uiHidden;
        }

        public void OpenResultsFolder() => Application.OpenURL("file://" + ResultsExporter.ResultsFolder);

        public void ExportAgain() { if (runner != null) runner.ExportLast(); }

        public static string CountLabel(int count) =>
            count >= 1000 && count % 1000 == 0 ? (count / 1000) + "K" : count.ToString();

        #endregion

        #region Private

        private void OnSessionCompleted(BenchmarkSession session) => ShowResult(session);

        private void ShowResult(BenchmarkSession s)
        {
            if (resultCard == null) return;
            Color accent;
            string title, line;
            ComparisonResult c = s.comparison;

            if (c != null)
            {
                double diff = c.frameMedianB - c.frameMedianA;
                line = $"A {Ms(c.frameMedianA)}  →  B {Ms(c.frameMedianB)}";
                switch (c.verdict)
                {
                    case "B faster":
                        title = $"B nhanh hơn {Math.Abs(diff):0.0} ms ({Math.Abs(c.improvementPercent):0}%)";
                        accent = ColorGood;
                        break;
                    case "B slower":
                        title = $"B chậm hơn {Math.Abs(diff):0.0} ms ({Math.Abs(c.improvementPercent):0}%)";
                        accent = ColorOk;
                        break;
                    case "invalid":
                        title = "Lần đo không hợp lệ";
                        line = ShortInvalidReason(s);
                        accent = ColorBad;
                        break;
                    default:
                        title = "Chưa thấy khác biệt rõ";
                        accent = ColorNeutral;
                        break;
                }
            }
            else if (s.kind == "sweep")
            {
                title = "Đường cong theo số khối";
                line = SweepLine(s);
                accent = ColorSelected;
            }
            else
            {
                RunRecord last = s.runs.Count > 0 ? s.runs[s.runs.Count - 1] : null;
                title = last != null ? $"{last.variant}: {Ms(last.metrics.frameMsMedian)}" : "Không có run";
                line = last != null ? $"P95 {Ms(last.metrics.frameMsP95)}" : "";
                accent = last != null && last.status == BenchmarkStatus.Valid ? ColorSelected : ColorBad;
            }

            if (resultTitle != null) resultTitle.text = title;
            if (resultLine != null) resultLine.text = line;
            if (resultAccent != null) resultAccent.color = accent;
            SetActive(resultCard, true);
        }

        private static string ShortInvalidReason(BenchmarkSession s)
        {
            foreach (RunRecord r in s.runs)
            {
                if (r.status == BenchmarkStatus.Valid) continue;
                if (r.invalidReason.Contains("focus")) return "Giữ cửa sổ luôn ở phía trước khi đo";
                if (r.invalidReason.Contains("Resolution")) return "Đừng đổi kích thước màn hình khi đo";
                if (r.invalidReason.Contains("huỷ")) return "Đã huỷ giữa chừng";
                return r.invalidReason;
            }
            return "";
        }

        private static string SweepLine(BenchmarkSession s)
        {
            var byCount = new SortedDictionary<int, List<double>>();
            foreach (RunRecord r in s.runs)
            {
                if (r.status != BenchmarkStatus.Valid) continue;
                if (!byCount.TryGetValue(r.objectCount, out var list)) byCount[r.objectCount] = list = new List<double>();
                list.Add(r.metrics.frameMsMedian);
            }
            if (byCount.Count == 0) return "Không có run hợp lệ";
            var sb = new StringBuilder();
            foreach (var kv in byCount)
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.Append(CountLabel(kv.Key)).Append(": ").Append(BenchmarkStats.Median(kv.Value.ToArray()).ToString("0.0"));
            }
            return sb.Append(" ms").ToString();
        }

        private void UpdatePill()
        {
            int second = Mathf.FloorToInt(runner.PhaseElapsed);
            if (second == _lastPillSecond) return;
            _lastPillSecond = second;

            float total = Mathf.Max(1, runner.PlanCount);
            float phase01 = runner.PhaseDuration > 0 ? Mathf.Clamp01(runner.PhaseElapsed / runner.PhaseDuration) : 0f;
            float progress = (runner.PlanIndex + (runner.Phase == RunnerPhase.Measuring ? phase01 : 0f)) / total;
            if (measuringFill != null) measuringFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);

            if (measuringText == null) return;
            _sb.Clear();
            _sb.Append(PhaseLabel(runner.Phase)).Append("  ").Append(runner.RunningVariant).Append("   ")
                .Append(runner.PlanIndex + 1).Append('/').Append(runner.PlanCount);
            measuringText.text = _sb.ToString();
        }

        private static string PhaseLabel(RunnerPhase p)
        {
            switch (p)
            {
                case RunnerPhase.Measuring: return "Đang đo";
                case RunnerPhase.Cooldown: return "Nghỉ";
                case RunnerPhase.Capturing: return "Chụp ảnh";
                default: return "Khởi động";
            }
        }

        private void Refresh()
        {
            if (runner != null && runner.Scenario != null && subtitleText != null) {
                string warning = runner.Scenario.PreviewWarning;
                subtitleText.text = warning.Length > 0 ? warning : runner.Scenario.ShortTitle;
                subtitleText.color = warning.Length > 0 ? new Color(1f, 0.4f, 0.3f) : new Color(0.66f, 0.72f, 0.80f);
            }
            BenchmarkConfig cfg = runner.ActiveConfig;
            float ms = runner.LiveFrameMs;

            if (fpsValueText != null) fpsValueText.text = ms > 0 ? (1000f / ms).ToString("0") : "-";
            if (fpsMsText != null) fpsMsText.text = ms.ToString("0.0") + " ms";
            if (fpsChip != null) fpsChip.color = ms <= GOOD_MS ? ColorGood : ms <= OK_MS ? ColorOk : ColorBad;

            LiveStat[] stats = runner.Scenario != null ? runner.Scenario.LiveStats : Array.Empty<LiveStat>();
            if (statValueTexts != null)
                for (int i = 0; i < statValueTexts.Length; i++)
                    if (statValueTexts[i] != null && i < stats.Length) statValueTexts[i].text = LiveValue(stats[i]);

            BenchmarkScenario s = runner.Scenario;
            if (s == null) return;
            if (previewButtons != null)
                for (int i = 0; i < previewButtons.Length; i++)
                    if (previewButtons[i] != null)
                        previewButtons[i].color = (int)runner.PreviewVariant == i ? ColorSelected : ColorButton;

            int[] counts = s.ObjectCountOptions;
            if (countButtons != null)
                for (int i = 0; i < countButtons.Length; i++)
                    if (countButtons[i] != null)
                        countButtons[i].color = i < counts.Length && counts[i] == cfg.objectCount ? ColorSelected : ColorButton;
        }

        /// <summary>Gom CPU/GPU time mỗi frame (FrameTimingManager) để hiện trung bình 4 lần/giây.</summary>
        private void SampleTimings()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timing) == 0) return;
            FrameTiming t = _timing[0];
            if (t.cpuMainThreadFrameTime > 0) { _sumCpuMain += t.cpuMainThreadFrameTime; _nCpuMain++; }
            if (t.cpuRenderThreadFrameTime > 0) { _sumCpuRender += t.cpuRenderThreadFrameTime; _nCpuRender++; }
            if (t.gpuFrameTime > 0) { _sumGpu += t.gpuFrameTime; _nGpu++; }
        }

        private void AverageTimings()
        {
            if (_nCpuMain > 0) _avgCpuMain = _sumCpuMain / _nCpuMain;
            if (_nCpuRender > 0) _avgCpuRender = _sumCpuRender / _nCpuRender;
            if (_nGpu > 0) _avgGpu = _sumGpu / _nGpu;
            _sumCpuMain = _sumCpuRender = _sumGpu = 0;
            _nCpuMain = _nCpuRender = _nGpu = 0;
        }

        private static string Count(ProfilerRecorder r) => r.Valid && r.Count > 0 ? r.LastValue.ToString("N0") : "—";

        private void FillStatCaptions()
        {
            if (statCaptionTexts == null || runner.Scenario == null) return;
            LiveStat[] stats = runner.Scenario.LiveStats;
            for (int i = 0; i < statCaptionTexts.Length; i++)
            {
                Text t = statCaptionTexts[i];
                if (t == null) continue;
                bool has = i < stats.Length;
                t.transform.parent.gameObject.SetActive(has);
                if (has) t.text = Caption(stats[i]);
            }
        }

        private void FillPreviewLabels()
        {
            BenchmarkScenario s = runner.Scenario;
            if (s == null || previewLabels == null) return;
            for (int i = 0; i < previewLabels.Length; i++)
            {
                Text t = previewLabels[i];
                if (t == null) continue;
                var v = (BenchmarkVariant)i;
                bool supported = Array.IndexOf(s.SupportedVariants, v) >= 0;
                t.transform.parent.gameObject.SetActive(supported);
                if (supported) t.text = v + "  " + s.ShortVariantName(v);
            }
        }

        private void FillCountLabels()
        {
            BenchmarkScenario s = runner.Scenario;
            if (s == null || countLabels == null) return;
            int[] counts = s.ObjectCountOptions;
            for (int i = 0; i < countLabels.Length; i++)
            {
                Text t = countLabels[i];
                if (t == null) continue;
                bool has = i < counts.Length;
                t.transform.parent.gameObject.SetActive(has);
                if (has) t.text = CountLabel(counts[i]);
            }
        }

        private void FillHelp(string[] tips)
        {
            if (helpTips == null) return;
            for (int i = 0; i < helpTips.Length; i++)
            {
                Text t = helpTips[i];
                if (t == null) continue;
                bool has = tips != null && i < tips.Length;
                t.transform.parent.gameObject.SetActive(has);
                if (has) t.text = tips[i];
            }
        }

        private static string Ms(double v) => double.IsNaN(v) ? "n/a" : v.ToString("0.0") + " ms";

        private static void Toggle(GameObject go) { if (go != null) go.SetActive(!go.activeSelf); }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        #endregion
    }
}
