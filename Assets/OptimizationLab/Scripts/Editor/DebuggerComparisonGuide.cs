using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OptimizationLab.Editor
{
    /// <summary>One action per card, using actual Unity screenshots and matching GIF flipbooks.</summary>
    [Serializable]
    public sealed class DebuggerComparisonGuide
    {
        public const string MediaRoot = "Assets/OptimizationLab/TutorialMedia/Editor/";
        private const int StepCount = 12;
        [SerializeField] private int step;
        [SerializeField] private string lessonId;
        [SerializeField] private bool animate = true;
        [SerializeField] private bool why;
        [SerializeField] private bool pendingStart;
        [SerializeField] private string feedback = "";
        [NonSerialized] private Dictionary<string, Texture2D> images;
        [NonSerialized] private GUIStyle titleStyle, bodyStyle;
        private double frameStart;

        public void Draw(string id)
        {
            if (lessonId != id) { lessonId = id; Reset(); }
            step = Mathf.Clamp(step, 0, StepCount - 1);
            string a = id == "B02" ? "Rời" : id == "B03" ? "Thường" : "Tắt";
            string b = id == "B02" ? "Ghép" : id == "B03" ? "Instancing" : "Bật";
            int sampleCount = LessonCount(id);
            var runner = UnityEngine.Object.FindFirstObjectByType<BenchmarkRunner>();
            bool ready = EditorApplication.isPlaying && runner != null && runner.Scenario != null && runner.Scenario.Id == id && !runner.IsBusy;
            if (pendingStart && ready)
            {
                pendingStart = false;
                runner.SetObjectCount(sampleCount);
                var guide = UnityEngine.Object.FindFirstObjectByType<TutorialGuide>();
                if (guide != null) guide.HideForEditorLesson();
                MoveTo(1);
            }
            InitStyles();
            EditorGUI.ProgressBar(GUILayoutUtility.GetRect(20, 22), (step + 1f) / StepCount, $"{step + 1} / {StepCount}");
            GUILayout.Space(10);
            GUILayout.Label(Title(step, a, b, id), titleStyle);
            GUILayout.Label(Caption(step, id), bodyStyle);
            GUILayout.Space(8);
            string clip = Clip(step);
            int frames = Frames(clip);
            DrawMedia(id.ToLowerInvariant(), clip, frames, animate);
            using (new EditorGUILayout.HorizontalScope())
            {
                animate = GUILayout.Toolbar(animate ? 1 : 0, new[] { "Ảnh", "GIF ↻" }, GUILayout.Width(150)) == 1;
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Phóng to", EditorStyles.miniButton)) TutorialMediaPreview.Open(id.ToLowerInvariant(), clip, frames);
            }
            GUILayout.Label($"Ảnh mẫu chụp trong Unity 6 · {sampleCount} khối · PC", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(8);

            if (!ready && step > 0)
            {
                EditorGUILayout.HelpBox($"Bài đang dừng. Bấm để mở lại {id}.", MessageType.Info);
                if (GUILayout.Button("Mở bài & Play", GUILayout.Height(36))) StartLesson(id);
            }
            else DrawAction(id, runner, ready, a, b, sampleCount);

            if (!string.IsNullOrEmpty(feedback)) EditorGUILayout.HelpBox(feedback, MessageType.Info);
            why = EditorGUILayout.Foldout(why, "Vì sao?", true);
            if (why) GUILayout.Label(Explanation(step, id), bodyStyle);
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(step == 0))
                    if (GUILayout.Button("‹ Trước")) MoveTo(step - 1);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Học lại")) Reset();
            }
        }

        private void DrawAction(string id, BenchmarkRunner runner, bool ready, string a, string b, int sampleCount)
        {
            bool batching = id == "B02";
            bool instancing = id == "B03";
            if (step == 0)
            {
                if (GUILayout.Button(pendingStart ? "Đang mở…" : "Bắt đầu chơi →", GUILayout.Height(40)) && !pendingStart) StartLesson(id);
                return;
            }
            if (step == 1 || step == 7)
            {
                if (TutorialDebugger.Enabled)
                {
                    EditorGUILayout.HelpBox("Debugger còn bật. Tắt trước khi đổi chế độ.", MessageType.Info);
                    if (GUILayout.Button("Tắt debugger")) TutorialDebugger.Disable();
                    return;
                }
                BenchmarkVariant variant = step == 1 ? BenchmarkVariant.A : BenchmarkVariant.B;
                if (GUILayout.Button($"Chọn {(step == 1 ? a : b)} →", GUILayout.Height(40)))
                {
                    runner.SetObjectCount(sampleCount);
                    runner.SetPreviewVariant(variant);
                    // Disable can leave the Editor paused. Render a fresh frame before the next capture.
                    EditorApplication.isPaused = false;
                    EditorApplication.QueuePlayerLoopUpdate();
                    MoveTo(step + 1);
                }
                return;
            }
            if (step == 2)
            {
                if (GUILayout.Button("Mở Frame Debugger", GUILayout.Height(40)))
                { TutorialDebugger.Open(); MoveTo(3); }
                return;
            }
            if (step == 3 || step == 8)
            {
                if (GUILayout.Button("Mở cửa sổ để bấm Enable", GUILayout.Height(34)))
                {
                    if (!TutorialDebugger.Enabled) { EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate(); }
                    TutorialDebugger.Open();
                }
                using (new EditorGUI.DisabledScope(!TutorialDebugger.Enabled))
                    if (GUILayout.Button("Đã bấm Enable →", GUILayout.Height(40))) MoveTo(step + 1);
                if (!TutorialDebugger.Enabled) GUILayout.Label("Chờ bạn bấm Enable như hình.", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (step == 4 || step == 5 || step == 9)
            {
                BenchmarkVariant required = step == 9 ? BenchmarkVariant.B : BenchmarkVariant.A;
                if (runner.PreviewVariant != required)
                {
                    EditorGUILayout.HelpBox($"Chế độ đã đổi. Quay lại bước chọn {(required == BenchmarkVariant.A ? a : b)}.", MessageType.Info);
                    if (GUILayout.Button("Quay lại chọn chế độ")) MoveTo(required == BenchmarkVariant.A ? 1 : 7);
                    return;
                }
                if (!TutorialDebugger.Enabled)
                {
                    if (GUILayout.Button("Mở lại debugger và bấm Enable")) TutorialDebugger.Open();
                    return;
                }
                if (GUILayout.Button(step == 5 ? "Tôi thấy các dòng Draw Mesh →" : step == 9 ? (batching ? "Tôi thấy Static Batch →" : instancing ? "Tôi thấy Draw Mesh (instanced) →" : "Tôi thấy SRP Batch →") : "Tôi thấy DrawOpaqueObjects →", GUILayout.Height(40))) MoveTo(step + 1);
                return;
            }
            if (step == 6)
            {
                if (GUILayout.Button("Mở cửa sổ để bấm Disable", GUILayout.Height(34))) TutorialDebugger.Open();
                using (new EditorGUI.DisabledScope(TutorialDebugger.Enabled))
                    if (GUILayout.Button("Đã bấm Disable →", GUILayout.Height(40))) MoveTo(7);
                return;
            }
            if (step == 10)
            {
                GUILayout.Label(batching ? "Ghép đã làm gì?" : instancing ? "Instancing đã làm gì?" : "SRP Batcher đã làm gì?", EditorStyles.boldLabel);
                string right = batching ? "Gộp dữ liệu của các khối đứng yên" : instancing ? "Vẽ nhiều bản sao cùng mesh trong một lệnh" : "Giảm công chuẩn bị từng draw cho CPU";
                string wrong = batching ? "Xoá bớt khối trong game" : instancing ? "Ghép tất cả khối thành một mesh lớn" : "Chỉ còn đúng 2 draw call";
                if (GUILayout.Button(right, GUILayout.Height(36))) MoveTo(11);
                if (GUILayout.Button(wrong, GUILayout.Height(32)))
                    feedback = batching ? "Khối vẫn còn đủ. Chỉ cách gửi dữ liệu để vẽ thay đổi."
                        : instancing ? "Không ghép mesh (đó là Static Batching, B02). Instancing gửi MỘT mesh kèm danh sách vị trí."
                        : "Một dòng SRP Batch chứa nhiều draw. Số dòng không phải tổng draw call.";
                return;
            }
            if (step == 11)
            {
                GUILayout.Label("★ ★ ★  Đã hiểu!", titleStyle);
                if (TutorialDebugger.Enabled)
                {
                    EditorGUILayout.HelpBox("Tắt debugger trước khi đọc thời gian CPU.", MessageType.Info);
                    if (GUILayout.Button("Tắt debugger, xem game", GUILayout.Height(38))) TutorialDebugger.Disable();
                }
                else if (GUILayout.Button("Xem lại từ đầu", GUILayout.Height(38))) Reset();
            }
        }

        private void StartLesson(string id)
        {
            TutorialDebugger.Disable();
            string path = id == "B02" ? OptimizationLabSceneGenerator.B02_SCENE : id == "B03" ? OptimizationLabSceneGenerator.B03_SCENE : OptimizationLabSceneGenerator.B01_SCENE;
            if (EditorApplication.isPlaying && UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != path)
            { feedback = "Thoát Play rồi bấm Bắt đầu để đổi bài."; return; }
            if (!EditorApplication.isPlaying)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                if (!System.IO.File.Exists(path)) { feedback = "Chưa có scene. Mở Thiết lập để tạo bài."; return; }
                EditorSceneManager.OpenScene(path);
                EditorApplication.isPlaying = true;
            }
            pendingStart = true;
            feedback = "";
        }

        private void DrawMedia(string lesson, string clip, int frames, bool play)
        {
            int f = clip == "compare" && !play ? 0 : play ? (int)((EditorApplication.timeSinceStartup - frameStart) / 1.6) % frames : frames - 1;
            if (clip == "compare" && play) f = 1 + (int)((EditorApplication.timeSinceStartup - frameStart) / 2.2) % 2;
            string path = MediaRoot + lesson + "/" + clip + "_" + Mathf.Max(0, f) + ".png";
            if (images == null) images = new Dictionary<string, Texture2D>();
            if (!images.TryGetValue(path, out Texture2D texture) || texture == null)
                images[path] = texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            float width = EditorGUIUtility.currentViewWidth - 48;
            Rect rect = GUILayoutUtility.GetRect(0, Mathf.Clamp(width * 0.6f, 180, 360), GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.09f, 0.11f, 0.14f));
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
            else GUI.Label(rect, "Đang tải ảnh mẫu…", EditorStyles.centeredGreyMiniLabel);
        }
        private void InitStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 21, wordWrap = true };
            bodyStyle = new GUIStyle(EditorStyles.label) { fontSize = 14, wordWrap = true };
        }
        private void MoveTo(int next) { step = next; why = false; feedback = ""; frameStart = EditorApplication.timeSinceStartup; }
        private void Reset() { pendingStart = false; MoveTo(0); }
        /// <summary>Số khối mẫu của ảnh hướng dẫn mỗi bài.</summary>
        internal static int LessonCount(string id) => id == "B02" ? 600 : 300;

        private static string Title(int s, string a, string b, string id)
        {
            bool batching = id == "B02", instancing = id == "B03";
            switch (s) {
                case 0: return "Nhìn máy vẽ một ảnh game";
                case 1: return "1 thao tác: chọn " + a;
                case 2: return "Mở kính lúp của người làm game";
                case 3: return "Bấm Enable";
                case 4: return "Tìm DrawOpaqueObjects";
                case 5: return "Mỗi dòng này đang vẽ một khối";
                case 6: return "Bấm Disable trước khi đổi";
                case 7: return "Bây giờ chọn " + b;
                case 8: return "Bấm Enable thêm lần nữa";
                case 9: return batching ? "Tìm dòng Static Batch" : instancing ? "Tìm dòng Draw Mesh (instanced)" : "Tìm dòng SRP Batch";
                case 10: return "Khác nhau ở đâu?";
                default: return batching ? "Đổi thêm bộ nhớ để vẽ nhẹ hơn" : instancing ? "Một lệnh, nhiều bản sao" : "Cùng draw, CPU chuẩn bị nhẹ hơn";
            }
        }
        private static string Caption(int s, string id)
        {
            bool batching = id == "B02", instancing = id == "B03";
            switch (s) {
                case 0: return "Frame = một ảnh game. Làm từng bước, không cần biết code.";
                case 1: return "Bấm nút được khoanh vàng, hoặc nút lớn bên dưới.";
                case 2: return "Frame Debugger cho xem từng việc máy làm để vẽ ảnh.";
                case 3: return "Nút ở góc trên trái của cửa sổ vừa mở.";
                case 4: return "Bấm tam giác để mở. Đây là nhóm vẽ các khối đặc.";
                case 5: return "Mở tiếp RenderLoop.Draw. Nhìn các dòng Draw Mesh.";
                case 6: return "Tắt kính lúp để game chạy và nhận thay đổi mới.";
                case 7: return instancing ? "Giữ nguyên số khối. Chỉ đổi material sang bản bật instancing." : "Giữ nguyên số khối. Chỉ đổi cách vẽ.";
                case 8: return "Lần này xem ảnh game SAU khi đã đổi cách vẽ.";
                case 9: return batching ? "Mở lại cùng nhóm. Nhiều Draw Mesh nay thành Static Batch."
                    : instancing ? "Mở lại cùng nhóm. Hàng trăm Draw Mesh nay chỉ còn vài dòng (instanced)."
                    : "Mở RenderLoop.DrawSRPBatcher. Bên trong là SRP Batch.";
                case 10: return "Trái: trước. Phải: sau. Khối trong game vẫn còn đủ.";
                default: return batching ? "Ít việc vẽ hơn, nhưng mesh chiếm thêm RAM. Chỉ dùng cho vật đứng yên."
                    : instancing ? "Chỉ gộp được khi cùng mesh + cùng material. Khác mesh là tách nhóm."
                    : "Ít dòng SRP Batch không có nghĩa là chỉ còn từng ấy draw call.";
            }
        }
        private static string Explanation(int s, string id)
        {
            if (s == 3 || s == 6 || s == 8) return "Enable giữ lại một frame để bạn soi. Disable cho game chạy tiếp. Đừng dùng số FPS/CPU khi đang soi để kết luận tốc độ.";
            if (s == 4 || s == 5) return "Draw là lệnh vẽ. Opaque nghĩa là vật đặc, không trong suốt. Chỉ nhìn nhóm khối; bỏ qua UI (giao diện) và depth (độ sâu).";
            if (id == "B02") return "Static Batching gom geometry của vật đứng yên vào bộ đệm chung. Material và pass vẫn chia công việc vẽ. Đừng di chuyển từng vật sau khi ghép.";
            if (id == "B03") return "Instancing gửi một mesh + một material kèm danh sách vị trí, GPU vẽ nhiều bản sao trong một lệnh. Mỗi dòng (instanced) vẽ nhiều khối cùng lúc; bấm một dòng sẽ thấy Mesh là Cube. Lab tắt SRP Batcher vì khi bật, URP ưu tiên SRP Batcher và bỏ qua instancing.";
            return "SRP Batcher giữ dữ liệu material sẵn để CPU chuẩn bị mỗi draw rẻ hơn. Mỗi SRP Batch có thể chứa nhiều draw; hãy so thêm CPU khi debugger đã tắt.";
        }
        private static string Clip(int s)
        {
            string[] clips = { "compare", "choose-a", "open", "enable-a", "opaque-a", "draw-a", "disable", "choose-b", "enable-b", "opaque-b", "compare", "compare" };
            return clips[Mathf.Clamp(s, 0, clips.Length - 1)];
        }
        private static int Frames(string c) => c == "opaque-b" || c == "compare" ? 3 : 2;
    }

    internal static class TutorialDebugger
    {
        private static Type EngineType => typeof(Camera).Assembly.GetType("UnityEngine.FrameDebugger");
        internal static bool Enabled => (bool?)(EngineType?.GetProperty("enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)) ?? false;
        internal static EditorWindow Open()
        {
            Type type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.FrameDebuggerWindow");
            if (type == null) { EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger"); return null; }
            var window = EditorWindow.GetWindow(type);
            window.Show(); window.Focus();
            return window;
        }
        internal static void Disable()
        {
            if (!Enabled) return;
            EditorWindow window = Open();
            window?.GetType().GetMethod("ToggleFrameDebuggerEnabled", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(window, null);
        }
    }

    /// <summary>Vẽ một clip ảnh thật (PNG flipbook) theo media_manifest.json — dùng cho các thẻ "Áp dụng vào project thật".</summary>
    internal static class TutorialMedia
    {
        private const string MANIFEST = DebuggerComparisonGuide.MediaRoot + "media_manifest.json";
        private const double FRAME_SECONDS = 1.6;
        private static Dictionary<string, int> counts;
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        internal static int Frames(string lesson, string clip)
        {
            if (counts == null)
            {
                counts = new Dictionary<string, int>();
                string path = System.IO.Path.GetFullPath(MANIFEST);
                if (System.IO.File.Exists(path))
                {
                    // Manifest phẳng {"b02/clip": n}; đọc tay để khỏi cần thư viện JSON.
                    foreach (string line in System.IO.File.ReadAllLines(path))
                    {
                        string[] kv = line.Trim().TrimEnd(',').Split(':');
                        if (kv.Length == 2 && int.TryParse(kv[1].Trim(), out int n)) counts[kv[0].Trim().Trim('"')] = n;
                    }
                }
            }
            return counts.TryGetValue(lesson + "/" + clip, out int c) ? c : 1;
        }

        /// <summary>Ảnh / GIF + nút Phóng to. <paramref name="play"/> = phát chuỗi trạng thái; tắt thì giữ khung cuối.</summary>
        internal static bool Draw(string lesson, string clip, bool play, double start, string note)
        {
            int frames = Frames(lesson, clip);
            int f = play ? (int)((EditorApplication.timeSinceStartup - start) / FRAME_SECONDS) % frames : frames - 1;
            string path = DebuggerComparisonGuide.MediaRoot + lesson + "/" + clip + "_" + Mathf.Max(0, f) + ".png";
            if (!cache.TryGetValue(path, out Texture2D texture) || texture == null)
                cache[path] = texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            float width = EditorGUIUtility.currentViewWidth - 48;
            Rect rect = GUILayoutUtility.GetRect(0, Mathf.Clamp(width * 0.6f, 160, 340), GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.09f, 0.11f, 0.14f));
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
            else GUI.Label(rect, "Đang tải ảnh mẫu…", EditorStyles.centeredGreyMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                play = GUILayout.Toolbar(play ? 1 : 0, new[] { "Ảnh", "GIF ↻" }, GUILayout.Width(150)) == 1;
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Phóng to", EditorStyles.miniButton)) TutorialMediaPreview.Open(lesson, clip, frames);
            }
            if (!string.IsNullOrEmpty(note)) GUILayout.Label(note, EditorStyles.centeredGreyMiniLabel);
            return play;
        }
    }

    public sealed class TutorialMediaPreview : EditorWindow
    {
        [SerializeField] private string lesson, clip;
        [SerializeField] private int frames;
        [SerializeField] private bool play = true;
        private Texture2D[] textures;
        public static void Open(string id, string name, int count)
        {
            var w = GetWindow<TutorialMediaPreview>("Ảnh / GIF hướng dẫn");
            w.lesson = id; w.clip = name; w.frames = count; w.textures = null;
            w.minSize = new Vector2(640, 420); w.Show();
        }
        private void OnInspectorUpdate() { if (play) Repaint(); }
        private void OnGUI()
        {
            if (string.IsNullOrEmpty(lesson)) return;
            if (textures == null || textures.Length != frames)
            {
                textures = new Texture2D[frames];
                for (int i = 0; i < frames; i++) textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(DebuggerComparisonGuide.MediaRoot + lesson + "/" + clip + "_" + i + ".png");
            }
            play = GUILayout.Toggle(play, "Phát GIF ↻", "Button");
            int f = clip == "compare" ? play ? 1 + (int)(EditorApplication.timeSinceStartup / 2.2) % 2 : 0 : play ? (int)(EditorApplication.timeSinceStartup / 1.6) % frames : frames - 1;
            Rect r = GUILayoutUtility.GetRect(0, position.height - 65, GUILayout.ExpandWidth(true));
            if (textures[f] != null) GUI.DrawTexture(r, textures[f], ScaleMode.ScaleToFit);
            if (GUILayout.Button("Mở file GIF")) EditorUtility.OpenWithDefaultApp(System.IO.Path.GetFullPath(DebuggerComparisonGuide.MediaRoot + lesson + "/" + clip + ".gif"));
        }
    }
}
