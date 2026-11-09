using System;
using UnityEngine;
using UnityEngine.UI;

namespace OptimizationLab
{
    /// <summary>Phần tử HUD mà một bước tutorial chỉ vào.</summary>
    public enum TutorialTarget { None, FpsChip, PreviewA, PreviewB, PreviewC, Count0, Count1, Count2, Count3, Stats, ResultCard }

    /// <summary>Điều kiện để qua bước.</summary>
    public enum TutorialWait { Next, Preview, Count, RunStarted, RunFinished }

    /// <summary>
    /// Một bước tutorial kiểu nhiệm vụ: một câu ngắn + chỉ vào nút + chờ người chơi làm.
    /// Text có thể chứa số live: {draws} {setpass} {batches} {cpu} {cpurender} {gpu} {count} {ms}.
    /// </summary>
    [Serializable]
    public struct TutorialStep
    {
        public string text;
        public TutorialTarget target;
        public TutorialWait wait;
        /// <summary>Tham số của điều kiện (index variant / index count).</summary>
        public int arg;

        public TutorialStep(string text, TutorialTarget target, TutorialWait wait, int arg = 0)
        {
            this.text = text;
            this.target = target;
            this.wait = wait;
            this.arg = arg;
        }
    }

    /// <summary>
    /// Tutorial trong game: bong bóng thoại + khung nhấp nháy quanh nút cần bấm, chấm tiến độ, màn hoàn thành.
    /// Tự bật lần đầu (PlayerPrefs). Node UI nằm sẵn trong scene; script chỉ di chuyển bong bóng/khung theo nút đích.
    /// </summary>
    public sealed class TutorialGuide : MonoBehaviour
    {
        #region Fields

        private const string DONE_KEY_PREFIX = "OptimizationLab.Tutorial.Done.";
        private const float PULSE_SPEED = 5f;
        private const float PULSE_SCALE = 0.06f;
        private const float HIGHLIGHT_PADDING = 18f;
        private const float BUBBLE_GAP = 36f;
        private const float NO_TARGET_Y = 0.25f;
        private const float LIVE_TEXT_INTERVAL = 0.25f;

        [SerializeField] private BenchmarkHUD hud;
        [SerializeField] private RectTransform canvasRect;
        [SerializeField] private GameObject bubble;
        [SerializeField] private RectTransform bubbleRect;
        [SerializeField] private Text bubbleText;
        [SerializeField] private Text dotsText;
        [SerializeField] private GameObject nextButton;
        [SerializeField] private RectTransform highlight;
        [SerializeField] private GameObject completePanel;
        [SerializeField] private Text completeText;
        [Tooltip("Index theo enum TutorialTarget.")]
        [SerializeField] private RectTransform[] targets;

        private TutorialStep[] _steps = Array.Empty<TutorialStep>();
        private int _index = -1;
        private readonly Vector3[] _corners = new Vector3[4];
        private float _nextLiveText;

        #endregion

        #region Initialize

        private void Start()
        {
            if (hud == null) return;
            hud.ActionPerformed += OnHudAction;
            BenchmarkRunner runner = hud.Runner;
            if (runner != null)
            {
                runner.SessionStarted += OnRunStarted;
                runner.SessionCompleted += OnRunFinished;
            }
            HideAll();
            if (!PlayerPrefs.HasKey(DoneKey)) Begin();
        }

        private void OnDestroy()
        {
            if (hud == null) return;
            hud.ActionPerformed -= OnHudAction;
            if (hud.Runner != null)
            {
                hud.Runner.SessionStarted -= OnRunStarted;
                hud.Runner.SessionCompleted -= OnRunFinished;
            }
        }

        private void LateUpdate()
        {
            if (!IsRunning) return;
            TutorialStep step = _steps[_index];
            // Lúc đo: giấu bong bóng để UI không cộng vào frame time.
            bool measuring = hud.Runner != null && hud.Runner.Phase == RunnerPhase.Measuring;
            SetActive(bubble, !measuring);
            RectTransform target = TargetRect(step.target);
            bool showHighlight = !measuring && target != null && target.gameObject.activeInHierarchy;
            SetActive(highlight != null ? highlight.gameObject : null, showHighlight);
            if (measuring) return;
            if (step.text.IndexOf('{') >= 0 && Time.unscaledTime >= _nextLiveText)
            {
                _nextLiveText = Time.unscaledTime + LIVE_TEXT_INTERVAL;
                if (bubbleText != null) bubbleText.text = FillLive(step.text);
            }
            if (showHighlight) PlaceHighlight(target);
            PlaceBubble(showHighlight ? target : null);
        }

        #endregion

        #region Public — nút bấm

        public bool IsRunning => _index >= 0 && _index < _steps.Length;

        public void Begin()
        {
            BenchmarkScenario s = hud != null && hud.Runner != null ? hud.Runner.Scenario : null;
            _steps = s != null ? s.TutorialSteps : Array.Empty<TutorialStep>();
            if (_steps.Length == 0) return;
            SetActive(completePanel, false);
            ShowStep(0);
        }

        public void Next()
        {
            if (IsRunning && _steps[_index].wait == TutorialWait.Next) Advance();
        }

        /// <summary>Hide in-game coach while the Editor picture lesson is running; do not mark it completed.</summary>
        public void HideForEditorLesson()
        {
            _index = -1;
            HideAll();
        }

        public void Skip()
        {
            PlayerPrefs.SetInt(DoneKey, 1);
            _index = -1;
            HideAll();
        }

        public void CloseComplete() => SetActive(completePanel, false);

        public void Replay()
        {
            SetActive(completePanel, false);
            Begin();
        }

        #endregion

        #region Private

        private string DoneKey
        {
            get
            {
                BenchmarkScenario s = hud != null && hud.Runner != null ? hud.Runner.Scenario : null;
                return DONE_KEY_PREFIX + (s != null ? s.Id : "none");
            }
        }

        private void OnHudAction(HudAction action, int arg)
        {
            if (action == HudAction.Tutorial) { Begin(); return; }
            if (!IsRunning) return;
            TutorialStep step = _steps[_index];
            bool done = (step.wait == TutorialWait.Preview && action == HudAction.Preview && arg == step.arg)
                        || (step.wait == TutorialWait.Count && action == HudAction.Count && arg == step.arg);
            if (done) Advance();
        }

        private void OnRunStarted()
        {
            if (IsRunning && _steps[_index].wait == TutorialWait.RunStarted) Advance();
        }

        private void OnRunFinished(BenchmarkSession session)
        {
            if (IsRunning && _steps[_index].wait == TutorialWait.RunFinished) Advance();
        }

        private void Advance()
        {
            if (_index + 1 >= _steps.Length) { Complete(); return; }
            ShowStep(_index + 1);
        }

        private void ShowStep(int index)
        {
            _index = index;
            TutorialStep step = _steps[index];
            if (bubbleText != null) bubbleText.text = FillLive(step.text);
            if (dotsText != null) dotsText.text = Dots(index, _steps.Length);
            SetActive(nextButton, step.wait == TutorialWait.Next);
            SetActive(bubble, true);
        }

        private void Complete()
        {
            _index = -1;
            HideAll();
            PlayerPrefs.SetInt(DoneKey, 1);
            BenchmarkScenario s = hud.Runner != null ? hud.Runner.Scenario : null;
            if (completeText != null && s != null) completeText.text = s.TutorialCompleteText;
            SetActive(completePanel, true);
        }

        private void HideAll()
        {
            SetActive(bubble, false);
            SetActive(highlight != null ? highlight.gameObject : null, false);
            SetActive(completePanel, false);
        }

        private RectTransform TargetRect(TutorialTarget t)
        {
            int i = (int)t;
            return t == TutorialTarget.None || targets == null || i >= targets.Length ? null : targets[i];
        }

        private void PlaceHighlight(RectTransform target)
        {
            target.GetWorldCorners(_corners);
            Vector3 center = (_corners[0] + _corners[2]) * 0.5f;
            highlight.position = center;
            float scale = canvasRect.lossyScale.x;
            Vector2 size = new Vector2(_corners[2].x - _corners[0].x, _corners[2].y - _corners[0].y) / scale;
            highlight.sizeDelta = size + Vector2.one * HIGHLIGHT_PADDING * 2f;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * PULSE_SPEED) * PULSE_SCALE;
            highlight.localScale = new Vector3(pulse, pulse, 1f);
        }

        /// <summary>Đặt bong bóng phía trên nút đích nếu nút ở nửa dưới màn hình, ngược lại phía dưới; không có đích → giữa.</summary>
        private void PlaceBubble(RectTransform target)
        {
            if (bubbleRect == null || canvasRect == null) return;
            Vector2 canvasSize = canvasRect.rect.size;
            float halfH = bubbleRect.rect.height * 0.5f;
            // Không có đích: đặt ở 1/4 trên màn hình để không che khối 3D ở giữa.
            float y = canvasSize.y * NO_TARGET_Y;
            if (target != null)
            {
                target.GetWorldCorners(_corners);
                float top = canvasRect.InverseTransformPoint(_corners[1]).y;
                float bottom = canvasRect.InverseTransformPoint(_corners[0]).y;
                bool targetLow = (top + bottom) * 0.5f < 0f;
                y = targetLow ? top + HIGHLIGHT_PADDING + BUBBLE_GAP + halfH : bottom - HIGHLIGHT_PADDING - BUBBLE_GAP - halfH;
            }
            float limit = canvasSize.y * 0.5f - halfH - 20f;
            bubbleRect.anchoredPosition = new Vector2(0f, Mathf.Clamp(y, -limit, limit));
        }

        private string FillLive(string text)
        {
            if (text.IndexOf('{') < 0) return text;
            return text.Replace("{meshmb}", hud.LiveValue(LiveStat.MeshMemoryMb)).Replace("{draws}", hud.LiveValue(LiveStat.DrawCalls))
                .Replace("{setpass}", hud.LiveValue(LiveStat.SetPassCalls))
                .Replace("{batches}", hud.LiveValue(LiveStat.Batches))
                .Replace("{cpurender}", hud.LiveValue(LiveStat.CpuRenderMs))
                .Replace("{cpu}", hud.LiveValue(LiveStat.CpuMainMs))
                .Replace("{gpu}", hud.LiveValue(LiveStat.GpuMs))
                .Replace("{count}", hud.LiveCountText)
                .Replace("{ms}", hud.LiveMsText);
        }

        private static string Dots(int index, int count)
        {
            var chars = new char[count * 2 - 1];
            for (int i = 0; i < count; i++)
            {
                chars[i * 2] = i <= index ? '●' : '○';
                if (i < count - 1) chars[i * 2 + 1] = ' ';
            }
            return new string(chars);
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        #endregion
    }
}
