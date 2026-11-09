using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OptimizationLab.Editor
{
    /// <summary>
    /// Bảng nhiệm vụ trong Editor: mỗi nhiệm vụ một dòng + một nút, tự tick khi xong.
    /// Phần "học" nằm trong tutorial trong game (TutorialGuide) — cửa sổ này chỉ dẫn tới đó.
    /// </summary>
    public sealed class OptimizationLabTutorialWindow : EditorWindow
    {
        #region Fields

        private const string SHOWN_KEY = "OptimizationLab.Tutorial.Shown";
        private const string QUEST_KEY = "OptimizationLab.Quest.";
        private const string INGAME_DONE_PREFIX = "OptimizationLab.Tutorial.Done.";
        private const string LESSON_KEY = "OptimizationLab.Lesson";

        private readonly struct Lesson
        {
            public readonly string Id, Title, Scene;
            public Lesson(string id, string title, string scene) { Id = id; Title = title; Scene = scene; }
        }

        private static readonly Lesson[] LESSONS =
        {
            new Lesson("B00", "Bài 1 · Nhiều vật = chậm", OptimizationLabSceneGenerator.B00_SCENE),
            new Lesson("B01", "Bài 2 · SRP Batcher", OptimizationLabSceneGenerator.B01_SCENE),
            new Lesson("B02", "Bài 3 · Ghép vật đứng yên", OptimizationLabSceneGenerator.B02_SCENE),
            new Lesson("B03", "Bài 4 · Vẽ nhiều bản sao (GPU Instancing)", OptimizationLabSceneGenerator.B03_SCENE),
        };
        private const int QUEST_COUNT = 5;

        private static readonly Color DONE_COLOR = new Color(0.30f, 0.75f, 0.45f);
        private static readonly Color ACTIVE_COLOR = new Color(0.25f, 0.62f, 0.90f);

        [System.NonSerialized] private GUIStyle _title, _quest, _questDone, _hint, _big, _cardTitle, _cardBody;
        [SerializeField] private int _outsideStep = -1;
        [SerializeField] private bool _outsideAnimate = true;
        private bool _outsideWhy;
        private bool _playerOpened;
        private double _outsideStart;
        private bool _showTools;
        [SerializeField] private bool _showSetup;
        [SerializeField] private bool _showOutside;
        private Vector2 _scroll;
        [SerializeField] private DebuggerComparisonGuide _debuggerGuide = new DebuggerComparisonGuide();

        #endregion

        #region Initialize

        [MenuItem("Tools/Optimization Lab/Tutorial", priority = -10)]
        public static void Open()
        {
            var w = GetWindow<OptimizationLabTutorialWindow>("Optimization Lab");
            w.minSize = new Vector2(360, 380);
            w.Show();
        }

        [InitializeOnLoadMethod]
        private static void OpenOnce()
        {
            if (Application.isBatchMode || SessionState.GetBool(SHOWN_KEY, false)) return;
            SessionState.SetBool(SHOWN_KEY, true);
            if (EditorPrefs.GetBool(SHOWN_KEY, false)) return;
            EditorPrefs.SetBool(SHOWN_KEY, true);
            EditorApplication.delayCall += Open;
        }

        private void OnInspectorUpdate() => Repaint();

        #endregion

        #region GUI

        private void OnGUI()
        {
            InitStyles();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Space(8);
            int lessonIndex = Mathf.Clamp(EditorPrefs.GetInt(LESSON_KEY, 0), 0, LESSONS.Length - 1);
            var ids = new string[LESSONS.Length];
            for (int i = 0; i < LESSONS.Length; i++) ids[i] = LESSONS[i].Id;
            int picked = GUILayout.Toolbar(lessonIndex, ids, GUILayout.Height(26));
            if (picked != lessonIndex) { EditorPrefs.SetInt(LESSON_KEY, picked); ShowOutsideStep(-1); }
            GUILayout.Space(6);
            GUILayout.Label(Current.Title, _title);

            if (Current.Id == "B01" || Current.Id == "B02" || Current.Id == "B03")
            {
                _debuggerGuide.Draw(Current.Id);
                GUILayout.Space(12);
                _showOutside = EditorGUILayout.Foldout(_showOutside, "Áp dụng vào project thật", true);
                if (_showOutside) { if (Current.Id == "B01") DrawSrpBatcherGuide(); else if (Current.Id == "B02") DrawStaticBatchingGuide(); else DrawInstancingGuide(); }
                _showTools = EditorGUILayout.Foldout(_showTools, "Công cụ nâng cao", true);
                if (_showTools) DrawTools();
                _showSetup = EditorGUILayout.Foldout(_showSetup, "Thiết lập scene", true);
                if (_showSetup)
                {
                    if (GUILayout.Button("Tạo các scene bài học")) OptimizationLabSceneGenerator.GenerateAll();
                    if (GUILayout.Button("Mở scene bài này")) OpenScene();
                }
                EditorGUILayout.EndScrollView();
                return;
            }

            bool[] done = { IsSceneGenerated(), IsSceneOpen(), InGameTutorialDone(), HasResults(), Quest(4) };
            int doneCount = 0;
            foreach (bool d in done) if (d) doneCount++;
            Rect bar = GUILayoutUtility.GetRect(18, 22, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(bar, doneCount / (float)QUEST_COUNT, $"{doneCount} / {QUEST_COUNT} nhiệm vụ");
            GUILayout.Space(10);

            int active = System.Array.IndexOf(done, false);

            QuestRow(0, done, active, "Tạo scene", "Tạo", OptimizationLabSceneGenerator.GenerateAll);
            QuestRow(1, done, active, "Mở scene", "Mở", OpenScene);
            QuestRow(2, done, active, "Chơi tutorial trong game", EditorApplication.isPlaying ? "Đang chơi…" : "▶ Chơi",
                () => { if (!EditorApplication.isPlaying) { OpenScene(); EditorApplication.isPlaying = true; } });
            QuestRow(3, done, active, "Bấm Đo trong game, xem file", "Mở thư mục", () => Reveal(ProjectResults()));
            QuestRow(4, done, active, "Đo trên điện thoại", "Cách làm", () => { OpenDoc("README.md"); SetQuest(4); });

            if (active == 2 && EditorApplication.isPlaying)
                GUILayout.Label("Làm theo bong bóng trong Game view.", _hint);
            if (active == 4)
                GUILayout.Label("Build Android, bấm ĐO trên máy thật.", _hint);
            if (doneCount == QUEST_COUNT)
                GUILayout.Label($"★ ★ ★   Xong {Current.Id}!", _big);

            if (Current.Id == "B01" || Current.Id == "B02") _debuggerGuide.Draw(Current.Id);

            if (Current.Id == "B01") DrawSrpBatcherGuide();
            if (Current.Id == "B02") DrawStaticBatchingGuide();

            GUILayout.Space(12);
            _showTools = EditorGUILayout.Foldout(_showTools, "Công cụ", true);
            if (_showTools) DrawTools();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// "Ngoài game" B02: 4 thẻ, mỗi thẻ một thao tác + ảnh/GIF chụp từ Unity thật.
        /// Chỉ cho qua khi trạng thái thật đúng (flag Batching Static, Player Settings, debugger/Profiler đang chạy).
        /// </summary>
        private void DrawStaticBatchingGuide()
        {
            GUILayout.Space(14);
            GUILayout.Label("Ngoài game: ghép vật đứng yên", _title);
            bool[] done = { Quest(10), Quest(11), Quest(12), Quest(13) };
            int active = System.Array.IndexOf(done, false);
            string[] labels =
            {
                "Đánh dấu vật đứng yên: Batching Static",
                "Player Settings: bật Static Batching",
                "Frame Debugger: thấy Static Batch",
                "Profiler: đọc dòng Static Batching",
            };
            for (int i = 0; i < labels.Length; i++)
                QuestRow(i, done, active, labels[i], i == active ? "Đang làm" : done[i] ? "Xem lại" : "—", () => ShowOutsideStep(i));

            int shown = _outsideStep >= 0 ? _outsideStep : active;
            if (shown < 0)
            {
                GUILayout.Label("★ ★ ★   Đã áp dụng được vào project!", _big);
                GUILayout.Label("Vật sinh bằng code: gọi StaticBatchingUtility.Combine(root) sau khi spawn (lab dùng cách này). Mesh nguồn cần Read/Write.", _hint);
                return;
            }
            GUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                switch (shown)
                {
                    case 0: StaticStepMark(); break;
                    case 1: StaticStepPlayer(); break;
                    case 2: StaticStepDebugger(); break;
                    default: StaticStepProfiler(); break;
                }
                _outsideWhy = EditorGUILayout.Foldout(_outsideWhy, "Vì sao?", true);
                if (_outsideWhy) GUILayout.Label(StaticWhy(shown), _hint);
            }
        }

        private void ShowOutsideStep(int i)
        {
            _outsideStep = i;
            _outsideWhy = false;
            _outsideStart = EditorApplication.timeSinceStartup;
        }

        /// <summary>Xong một thẻ → về thẻ đang làm tiếp theo.</summary>
        private void CompleteOutsideStep(int quest)
        {
            SetQuest(quest);
            ShowOutsideStep(-1);
        }

        private void StaticCard(string title, string caption, string clip, string note) => OutsideCard("b02", title, caption, clip, note);

        private void OutsideCard(string lesson, string title, string caption, string clip, string note)
        {
            // Giới hạn bề rộng để chữ dài xuống dòng thay vì bị cắt "…" trong helpBox.
            float max = EditorGUIUtility.currentViewWidth - 40;
            GUILayout.Label(title, _cardTitle, GUILayout.MaxWidth(max));
            GUILayout.Label(caption, _cardBody, GUILayout.MaxWidth(max));
            GUILayout.Space(4);
            if (clip != null) _outsideAnimate = TutorialMedia.Draw(lesson, clip, _outsideAnimate, _outsideStart, note);
            GUILayout.Space(6);
        }

        private void StaticStepMark()
        {
            StaticCard("Tick Batching Static cho vật không bao giờ di chuyển",
                "Chọn vật trong Hierarchy → Inspector → bấm Static ▾ (góc phải) → chọn Batching Static.",
                "out-static", "Ảnh mẫu: Unity 6000.3.16f1 · ô Static hiện \"—\" khi chỉ bật một loại static.");
            GameObject go = Selection.activeGameObject;
            bool marked = go != null && GameObjectUtility.AreStaticEditorFlagsSet(go, StaticEditorFlags.BatchingStatic);
            string state = go == null ? "Chưa chọn vật nào. Bấm một vật trong Hierarchy."
                : marked ? $"✔ \"{go.name}\" đã có Batching Static."
                : $"\"{go.name}\" chưa có Batching Static.";
            EditorGUILayout.HelpBox(state, marked ? MessageType.Info : MessageType.None);
            using (new EditorGUI.DisabledScope(!marked))
                if (GUILayout.Button("Đã tick xong →", GUILayout.Height(36))) CompleteOutsideStep(10);
        }

        private void StaticStepPlayer()
        {
            StaticCard("Bật Static Batching cho nền tảng build",
                "Gõ \"static\" vào ô tìm → chọn tab Android → tick Static Batching.",
                "out-player", "Ảnh mẫu: Project Settings ▸ Player ▸ Android ▸ Other Settings.");
            BuildTarget current = EditorUserBuildSettings.activeBuildTarget;
            bool android = PlayerSettings.GetStaticBatchingForPlatform(BuildTarget.Android);
            bool active = PlayerSettings.GetStaticBatchingForPlatform(current);
            GUILayout.Label($"{(android ? "✔" : "✘")} Android     {(active ? "✔" : "✘")} {current} (đang build)", _cardBody);
            if (GUILayout.Button("Mở Player Settings", GUILayout.Height(30)))
            {
                SettingsService.OpenProjectSettings("Project/Player");
                _playerOpened = true;
            }
            using (new EditorGUI.DisabledScope(!_playerOpened || !android || !active))
                if (GUILayout.Button("Đã thấy ô được tick →", GUILayout.Height(36))) CompleteOutsideStep(11);
            if (_playerOpened && (!android || !active))
                GUILayout.Label("Ô còn trống ở nền tảng có dấu ✘. Tick ô đó trong tab tương ứng.", _hint);
        }

        private void StaticStepDebugger()
        {
            StaticCard("Kiểm: nhóm vẽ có dòng Static Batch",
                "Vào Play → Frame Debugger → Enable → mở DrawOpaqueObjects.",
                "opaque-b", "Ảnh mẫu: B02 · Ghép · 600 khối · PC/Metal.");
            OutsideDebuggerActions("Tôi thấy Static Batch →");
        }

        /// <summary>Nút xác nhận chỉ bật khi đang Play và Frame Debugger đang Enable thật.</summary>
        private void OutsideDebuggerActions(string confirm)
        {
            bool enabled = TutorialDebugger.Enabled;
            if (GUILayout.Button(enabled ? "Mở lại Frame Debugger" : "Mở Frame Debugger", GUILayout.Height(30))) TutorialDebugger.Open();
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || !enabled))
                if (GUILayout.Button(confirm, GUILayout.Height(36))) CompleteOutsideStep(12);
            if (!EditorApplication.isPlaying) GUILayout.Label("Cần vào Play trước.", _hint);
            else if (!enabled) GUILayout.Label("Chờ bạn bấm Enable trong Frame Debugger.", _hint);
        }

        private void StaticStepProfiler()
        {
            StaticCard("Đọc số trong Profiler",
                "Tắt Frame Debugger → mở Profiler → bấm Rendering → xem dòng (Static Batching).",
                "out-profiler", "Ảnh mẫu: B02 · 600 khối · Rời rồi Ghép · PC/Metal. Số trên máy bạn có thể khác.");
            OutsideProfilerActions();
        }

        /// <summary>Profiler chỉ đọc khi Frame Debugger đã tắt; xác nhận khi đang Play và Profiler đang mở.</summary>
        private void OutsideProfilerActions()
        {
            bool debugger = TutorialDebugger.Enabled;
            bool open = EditorWindow.HasOpenInstances<ProfilerWindow>();
            if (debugger)
            {
                EditorGUILayout.HelpBox("Frame Debugger còn bật: số Profiler lúc này không đúng. Tắt trước.", MessageType.Info);
                if (GUILayout.Button("Tắt debugger", GUILayout.Height(30))) TutorialDebugger.Disable();
                return;
            }
            if (GUILayout.Button(open ? "Mở lại Profiler" : "Mở Profiler", GUILayout.Height(30)))
                EditorApplication.ExecuteMenuItem("Window/Analysis/Profiler");
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || !open))
                if (GUILayout.Button("Tôi thấy Batched Draw Calls > 0 →", GUILayout.Height(36))) CompleteOutsideStep(13);
            if (!EditorApplication.isPlaying) GUILayout.Label("Cần vào Play để Profiler ghi frame.", _hint);
        }

        private static string StaticWhy(int step)
        {
            switch (step)
            {
                case 0: return "Static = đứng yên. Unity chỉ ghép vật có Batching Static. Đừng tick vật sẽ di chuyển: sau khi ghép, dời từng vật sẽ làm hình sai. Dời cả nhóm cha chung thì được.";
                case 1: return "Đây là công tắc của cả project cho từng nền tảng. Tắt ở đây thì tick Batching Static trên vật cũng không có tác dụng khi build.";
                case 2: return "Frame Debugger cho thấy cách vẽ, không đo tốc độ. Static Batch là một lệnh vẽ dùng bộ đệm mesh đã ghép. Material, pass và culling vẫn có thể chia thành nhiều batch.";
                default: return "Batched Draw Calls = số lần vẽ đã được gộp vào static batch. Đổi lại mesh được sao chép nên tốn thêm bộ nhớ (xem Memory ▸ Mesh Memory). Chỉ kết luận nhanh/chậm khi đo trên máy thật.";
            }
        }

        /// <summary>
        /// "Ngoài game" B03: tick Enable GPU Instancing trên material, hiểu SRP Batcher được ưu tiên,
        /// kiểm Frame Debugger và Profiler. Thẻ chỉ cho qua khi trạng thái thật đúng.
        /// </summary>
        private void DrawInstancingGuide()
        {
            GUILayout.Space(14);
            GUILayout.Label("Ngoài game: bật GPU Instancing", _title);
            bool[] done = { Quest(10), Quest(11), Quest(12), Quest(13) };
            int active = System.Array.IndexOf(done, false);
            string[] labels =
            {
                "Material: tick Enable GPU Instancing",
                "Kiểm SRP Batcher của project",
                "Frame Debugger: thấy Draw Mesh (instanced)",
                "Profiler: đọc dòng Instancing",
            };
            for (int i = 0; i < labels.Length; i++)
                QuestRow(i, done, active, labels[i], i == active ? "Đang làm" : done[i] ? "Xem lại" : "—", () => ShowOutsideStep(i));

            int shown = _outsideStep >= 0 ? _outsideStep : active;
            if (shown < 0)
            {
                GUILayout.Label("★ ★ ★   Đã áp dụng được vào project!", _big);
                GUILayout.Label("Vẽ bằng code (không cần GameObject): Graphics.RenderMeshInstanced(rparams, mesh, 0, matrices). Không có collider, chọn vật hay culling từng vật.", _hint);
                return;
            }
            GUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                switch (shown)
                {
                    case 0: InstancingStepMaterial(); break;
                    case 1: InstancingStepSrp(); break;
                    case 2:
                        OutsideCard("b03", "Kiểm: nhóm vẽ có dòng (instanced)",
                            "Vào Play → Frame Debugger → Enable → mở DrawOpaqueObjects.",
                            "opaque-b", "Ảnh mẫu: B03 · Instancing · 300 khối · PC/Metal.");
                        OutsideDebuggerActions("Tôi thấy Draw Mesh (instanced) →");
                        break;
                    default:
                        OutsideCard("b03", "Đọc số trong Profiler",
                            "Tắt Frame Debugger → mở Profiler → bấm Rendering → xem dòng (Instancing).",
                            "out-profiler", "Ảnh mẫu: B03 · 300 khối · Thường rồi Instancing · PC/Metal. Số trên máy bạn có thể khác.");
                        OutsideProfilerActions();
                        break;
                }
                _outsideWhy = EditorGUILayout.Foldout(_outsideWhy, "Vì sao?", true);
                if (_outsideWhy) GUILayout.Label(InstancingWhy(shown), _hint);
            }
        }

        private void InstancingStepMaterial()
        {
            OutsideCard("b03", "Tick Enable GPU Instancing trên material",
                "Chọn material trong Project → Inspector → kéo xuống cuối → tick Enable GPU Instancing.",
                "out-material", "Ảnh mẫu: material URP/Lit · Unity 6000.3.16f1.");
            if (GUILayout.Button("Chọn material mẫu của lab (đã tick)", GUILayout.Height(28)))
            {
                var sample = AssetDatabase.LoadAssetAtPath<Material>(OptimizationLabSceneGenerator.B03_INSTANCED_MATERIAL);
                if (sample != null) { Selection.activeObject = sample; EditorGUIUtility.PingObject(sample); }
            }
            var mat = Selection.activeObject as Material;
            bool on = mat != null && mat.enableInstancing;
            bool plain = mat != null && AssetDatabase.GetAssetPath(mat) == OptimizationLabSceneGenerator.B03_PLAIN_MATERIAL;
            string state = mat == null ? "Chưa chọn material nào. Bấm một material trong cửa sổ Project."
                : plain ? $"\"{mat.name}\" là bản đối chứng A của lab: để tắt. Chọn material khác."
                : on ? $"✔ \"{mat.name}\" đã bật GPU Instancing."
                : $"\"{mat.name}\" chưa bật GPU Instancing.";
            EditorGUILayout.HelpBox(state, on && !plain ? MessageType.Info : MessageType.None);
            using (new EditorGUI.DisabledScope(!on || plain))
                if (GUILayout.Button("Đã tick xong →", GUILayout.Height(36))) CompleteOutsideStep(10);
        }

        private void InstancingStepSrp()
        {
            OutsideCard("b03", "Kiểm SRP Batcher: nó được ưu tiên hơn instancing",
                "URP bật SRP Batcher thì MeshRenderer dùng shader tương thích (như URP/Lit) đi SRP Batcher, không đi instancing.",
                null, null);
            foreach (UniversalRenderPipelineAsset a in ProjectUrpAssets())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"{a.name}: SRP Batcher {(a.useSRPBatcher ? "BẬT" : "tắt")}", _cardBody, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("Chọn", EditorStyles.miniButton, GUILayout.Width(60)))
                    { Selection.activeObject = a; EditorGUIUtility.PingObject(a); }
                }
            }
            GUILayout.Label("Instancing có tác dụng khi: SRP Batcher tắt, shader không tương thích SRP Batcher, hoặc vẽ bằng code (Graphics.RenderMeshInstanced). Không cần đổi gì: chỉ cần biết project đang theo đường nào.", _hint);
            if (GUILayout.Button("Đã hiểu: project mình đi đường nào →", GUILayout.Height(36))) CompleteOutsideStep(11);
        }

        private static string InstancingWhy(int step)
        {
            switch (step)
            {
                case 0: return "Instancing = gửi một mesh + một material kèm danh sách vị trí; GPU vẽ nhiều bản sao trong một lệnh. Chỉ vật cùng mesh và cùng material mới vẽ chung được.";
                case 1: return "Lab tắt SRP Batcher ở mọi chế độ của B03 để nhìn riêng instancing. Project thật nên giữ SRP Batcher nếu đang có lợi; đổi gì cũng phải đo trên máy thật.";
                case 2: return "Frame Debugger cho thấy cách vẽ, không đo tốc độ. Một dòng (instanced) vẽ nhiều khối cùng lúc. Muốn biết mỗi dòng gộp bao nhiêu lần vẽ, xem Profiler ở bước sau.";
                default: return "Batched Draw Calls của dòng (Instancing) = số lần vẽ đã gộp vào lệnh instanced. Chỉ kết luận nhanh/chậm khi đo trên máy thật.";
            }
        }

        private void DrawSrpBatcherGuide()
        {
            GUILayout.Space(14);
            GUILayout.Label("Ngoài game: bật SRP Batcher cho project", _title);

            UniversalRenderPipelineAsset[] assets = ProjectUrpAssets();
            bool allOn = assets.Length > 0;
            foreach (UniversalRenderPipelineAsset a in assets) allOn &= a.useSRPBatcher;

            bool[] done = { Quest(10), Quest(11), allOn, Quest(13), Quest(14) };
            int active = System.Array.IndexOf(done, false);

            QuestRow(0, done, active, "Mở URP Asset đang dùng", "Mở", () =>
            {
                var current = GraphicsSettings.currentRenderPipeline;
                if (current != null) { Selection.activeObject = current; EditorGUIUtility.PingObject(current); }
                SetQuest(10);
            });
            QuestRow(1, done, active, "Inspector ▸ Rendering ▸ ⋮ ▸ Show Additional Properties", "Đã thấy ô", () => SetQuest(11));
            if (active == 1)
                GUILayout.Label("Bấm dấu ⋮ bên phải tiêu đề \"Rendering\" → bật \"Show Additional Properties\". Ô \"SRP Batcher\" hiện ngay bên dưới.", _hint);

            QuestRow(2, done, active, "Tick ô SRP Batcher ở mọi URP Asset", "Bật hết", () =>
            {
                foreach (UniversalRenderPipelineAsset a in assets)
                {
                    if (a.useSRPBatcher) continue;
                    Undo.RecordObject(a, "Enable SRP Batcher");
                    a.useSRPBatcher = true;
                    EditorUtility.SetDirty(a);
                }
                AssetDatabase.SaveAssets();
            });
            foreach (UniversalRenderPipelineAsset a in assets)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(40);
                    GUILayout.Label((a.useSRPBatcher ? "✔ " : "✘ ") + a.name, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("Chọn", EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        Selection.activeObject = a;
                        EditorGUIUtility.PingObject(a);
                    }
                }
            }

            QuestRow(3, done, active, "Kiểm shader: Inspector ghi \"SRP Batcher: compatible\"", "Chọn shader", () =>
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null) { Selection.activeObject = lit; EditorGUIUtility.PingObject(lit); }
                SetQuest(13);
            });
            QuestRow(4, done, active, "Frame Debugger: thấy dòng \"SRP Batch\"", "Mở", () =>
            {
                EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger");
                SetQuest(14);
            });
            if (active == 4)
                GUILayout.Label("Vào Play, bấm Enable trong Frame Debugger, mở DrawOpaqueObjects.", _hint);
            if (active < 0)
                GUILayout.Label("Code: GraphicsSettings.useScriptableRenderPipelineBatching = true", _hint);
        }

        /// <summary>URP Asset mặc định + của từng quality level (không trùng).</summary>
        private static UniversalRenderPipelineAsset[] ProjectUrpAssets()
        {
            var set = new System.Collections.Generic.List<UniversalRenderPipelineAsset>();
            void Add(RenderPipelineAsset rp)
            {
                if (rp is UniversalRenderPipelineAsset u && !set.Contains(u)) set.Add(u);
            }
            Add(GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.names.Length; i++) Add(QualitySettings.GetRenderPipelineAssetAt(i));
            return set.ToArray();
        }

        private void QuestRow(int index, bool[] done, int active, string label, string button, System.Action action)
        {
            bool isDone = done[index];
            bool isActive = index == active;
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(34)))
            {
                Color prev = GUI.color;
                GUI.color = isDone ? DONE_COLOR : isActive ? ACTIVE_COLOR : Color.gray;
                GUILayout.Label(isDone ? "✔" : (index + 1).ToString(), _big, GUILayout.Width(30));
                GUI.color = prev;
                GUILayout.Label(label, isDone ? _questDone : _quest, GUILayout.ExpandWidth(true));
                using (new EditorGUI.DisabledScope(!isActive && !isDone))
                {
                    Color bg = GUI.backgroundColor;
                    if (isActive) GUI.backgroundColor = ACTIVE_COLOR;
                    if (GUILayout.Button(button, GUILayout.Width(110), GUILayout.Height(28))) action();
                    GUI.backgroundColor = bg;
                }
            }
        }

        private void DrawTools()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate")) ValidationMenu.ValidateScenesMenu();
                if (GUILayout.Button("Tests")) LabTestRunnerMenu.RunEditModeTests();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Frame Debugger")) EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger");
                if (GUILayout.Button("Profiler")) EditorApplication.ExecuteMenuItem("Window/Analysis/Profiler");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Chơi lại tutorial"))
                {
                    PlayerPrefs.DeleteKey(INGAME_DONE_PREFIX + Current.Id);
                    PlayerPrefs.Save();
                }
                if (GUILayout.Button("Reset nhiệm vụ"))
                {
                    for (int i = 0; i < 20; i++) EditorPrefs.DeleteKey(QUEST_KEY + Current.Id + i);
                    PlayerPrefs.DeleteKey(INGAME_DONE_PREFIX + Current.Id);
                    PlayerPrefs.Save();
                }
            }
            if (GUILayout.Button("Báo cáo mẫu")) OpenDoc("REPORT_TEMPLATE.md");
        }

        private void InitStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
            _quest = new GUIStyle(EditorStyles.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            _questDone = new GUIStyle(_quest) { normal = { textColor = Color.gray } };
            _hint = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            _big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            _cardTitle = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 16, fontStyle = FontStyle.Bold };
            _cardBody = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 13 };
        }

        #endregion

        #region Private

        private static Lesson Current => LESSONS[Mathf.Clamp(EditorPrefs.GetInt(LESSON_KEY, 0), 0, LESSONS.Length - 1)];

        private static bool IsSceneGenerated() => File.Exists(Current.Scene);

        private static bool IsSceneOpen() => SceneManager.GetActiveScene().path == Current.Scene;

        private static bool InGameTutorialDone() => PlayerPrefs.HasKey(INGAME_DONE_PREFIX + Current.Id);

        private static bool HasResults()
        {
            string folder = ProjectResults();
            return Directory.Exists(folder) && Directory.GetFiles(folder, Current.Id + "_*.csv").Length > 0;
        }

        private static bool Quest(int i) => EditorPrefs.GetBool(QUEST_KEY + Current.Id + i, false);

        private static void SetQuest(int i) => EditorPrefs.SetBool(QUEST_KEY + Current.Id + i, true);

        private static void OpenScene()
        {
            if (!IsSceneGenerated()) OptimizationLabSceneGenerator.GenerateAll();
            if (IsSceneOpen()) return;
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(Current.Scene);
        }

        private static string ProjectResults() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Results"));

        private static void Reveal(string folder)
        {
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }

        private static void OpenDoc(string file)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Documentation", file));
            if (File.Exists(path)) EditorUtility.OpenWithDefaultApp(path);
            else Debug.LogWarning($"[OptimizationLab] Không thấy {path}");
        }

        #endregion
    }
}
