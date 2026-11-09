using System.Collections.Generic;
using System.IO;
using System.Linq;
using OptimizationLab.Scenarios;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OptimizationLab.Editor
{
    /// <summary>
    /// Dựng lại các scene benchmark bằng code (deterministic, idempotent): chạy lại không nhân đôi material,
    /// ghi đè scene, giữ GUID asset. Hỗ trợ <c>-executeMethod OptimizationLab.Editor.OptimizationLabSceneGenerator.GenerateAll</c>.
    /// </summary>
    public static class OptimizationLabSceneGenerator
    {
        #region Fields

        public const string ROOT = "Assets/OptimizationLab";
        public const string SCENES = ROOT + "/Scenes";
        public const string MATERIALS = ROOT + "/Materials";
        public const string RESOURCES = ROOT + "/Resources";
        public const string B00_SCENE = SCENES + "/B00_Baseline.unity";
        public const string B02_SCENE = SCENES + "/B02_StaticBatching.unity";
        public const string B02_MATERIALS = MATERIALS + "/B02";
        public const string B03_SCENE = SCENES + "/B03_GPUInstancing.unity";
        public const string B03_MATERIALS = MATERIALS + "/B03";
        public const string B03_PLAIN_MATERIAL = B03_MATERIALS + "/OL_B03_Plain.mat";
        public const string B03_INSTANCED_MATERIAL = B03_MATERIALS + "/OL_B03_Instanced.mat";
        public const string B01_SCENE = SCENES + "/B01_SRPBatcher.unity";
        public const string B00_MATERIALS = MATERIALS + "/B00";
        public const string B01_MATERIALS = MATERIALS + "/B01";
        public const string BASELINE_MATERIAL = B00_MATERIALS + "/OL_Baseline_Lit.mat";
        private const string LEGACY_BASELINE_MATERIAL = MATERIALS + "/OL_Baseline_Lit.mat";
        public const int B01_MATERIAL_COUNT = 8;
        private const string BUILD_INFO = RESOURCES + "/OptimizationLabBuildInfo.json";
        private const string URP_LIT = "Universal Render Pipeline/Lit";

        /// <summary>8 màu phân biệt rõ cho B01 (cùng shader variant).</summary>
        private static readonly Color[] B01_COLORS =
        {
            new Color(0.90f, 0.36f, 0.33f), new Color(0.95f, 0.62f, 0.25f), new Color(0.95f, 0.84f, 0.30f),
            new Color(0.45f, 0.78f, 0.38f), new Color(0.30f, 0.72f, 0.72f), new Color(0.32f, 0.55f, 0.90f),
            new Color(0.58f, 0.42f, 0.88f), new Color(0.88f, 0.45f, 0.70f),
        };

        /// <summary>Keyword URP/Lit dùng để tạo 8 shader variant khác nhau ở B01-C (bit i bật keyword i), kèm property tương ứng.</summary>
        private static readonly (string keyword, string property)[] B01_VARIANT_KEYWORDS =
        {
            ("_SPECULARHIGHLIGHTS_OFF", "_SpecularHighlights"),
            ("_ENVIRONMENTREFLECTIONS_OFF", "_EnvironmentReflections"),
            ("_RECEIVE_SHADOWS_OFF", "_ReceiveShadows"),
        };

        /// <summary>Thứ tự scene lab trong Build Settings (Menu, B00–B11). Scene chưa có thì bỏ qua.</summary>
        public static readonly string[] SceneOrder =
        {
            SCENES + "/BenchmarkMenu.unity", B00_SCENE, B01_SCENE, B02_SCENE, B03_SCENE,
        };

        private static readonly Color BACKGROUND = new Color(0.08f, 0.09f, 0.11f);
        private static readonly Color PANEL = new Color(0.06f, 0.07f, 0.10f, 0.92f);
        private static readonly Color CARD = new Color(0.11f, 0.13f, 0.17f, 1f);
        private static readonly Color BUTTON = new Color(0.17f, 0.20f, 0.27f, 1f);
        private static readonly Color TOOL = new Color(0.12f, 0.14f, 0.19f, 1f);
        private static readonly Color ACCENT = new Color(0.16f, 0.55f, 0.78f, 1f);
        private static readonly Color GOOD = new Color(0.20f, 0.66f, 0.38f, 1f);
        private static readonly Color DIM = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color BUBBLE = new Color(0.99f, 0.97f, 0.91f, 1f);
        private static readonly Color BUBBLE_SOFT = new Color(0.90f, 0.87f, 0.80f, 1f);
        private static readonly Color BUBBLE_TEXT = new Color(0.13f, 0.14f, 0.18f, 1f);
        private static readonly Color HIGHLIGHT = new Color(1f, 0.82f, 0.22f, 1f);
        private static readonly Color STAR = new Color(1f, 0.80f, 0.20f, 1f);
        private static readonly Color TEXT = new Color(0.93f, 0.95f, 0.98f);
        private static readonly Color TEXT_DIM = new Color(0.66f, 0.72f, 0.80f);

        #endregion

        #region Public

        [MenuItem("Tools/Optimization Lab/Generate All Scenes", priority = 0)]
        public static void GenerateAll()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            GenerateB00Internal();
            GenerateB01Internal();
            GenerateB02Internal();
            GenerateB03Internal();
            UpdateBuildSettings();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(B00_SCENE);
            Debug.Log("[OptimizationLab] Generate All Scenes xong.");
        }

        [MenuItem("Tools/Optimization Lab/Scenes/Generate B00 Baseline", priority = 20)]
        public static void GenerateB00()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            GenerateB00Internal();
            UpdateBuildSettings();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Optimization Lab/Scenes/Generate B01 SRP Batcher", priority = 21)]
        public static void GenerateB01()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            GenerateB01Internal();
            UpdateBuildSettings();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Đưa scene lab lên đầu Build Settings theo <see cref="SceneOrder"/>, giữ các scene khác phía sau.</summary>
        public static void UpdateBuildSettings()
        {
            var lab = SceneOrder.Where(p => File.Exists(p)).Select(p => new EditorBuildSettingsScene(p, true)).ToList();
            var labPaths = new HashSet<string>(lab.Select(s => s.path));
            var others = EditorBuildSettings.scenes.Where(s => !labPaths.Contains(s.path));
            EditorBuildSettings.scenes = lab.Concat(others).ToArray();
        }

        #endregion

        #region Private — B00

        private static void GenerateB00Internal()
        {
            PrepareProject();
            Material mat = EnsureLitMaterial(BASELINE_MATERIAL, new Color(0.62f, 0.66f, 0.72f));
            BuildLessonScene<B00BaselineScenario>(B00_SCENE, variantCount: 3, countOptions: 4,
                scenario => SetArray(scenario, "materials", new Object[] { mat }));
        }

        [MenuItem("Tools/Optimization Lab/Scenes/Generate B02 Static Batching", priority = 22)]
        public static void GenerateB02()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            GenerateB02Internal();
            UpdateBuildSettings();
            AssetDatabase.SaveAssets();
        }

        private static void GenerateB02Internal()
        {
            PrepareProject();
            var mats = new Material[4];
            for (int i = 0; i < mats.Length; i++) mats[i] = EnsureLitMaterial($"{B02_MATERIALS}/OL_B02_{i}.mat", B01_COLORS[i * 2]);
            BuildLessonScene<B02StaticBatchingScenario>(B02_SCENE, 3, 3, scenario => {
                SetArray(scenario, "materials", mats);
                var so = new SerializedObject(scenario);
                so.FindProperty("maxObjectCount").intValue = 1500;
                so.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        [MenuItem("Tools/Optimization Lab/Scenes/Generate B03 GPU Instancing", priority = 23)]
        public static void GenerateB03()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            GenerateB03Internal();
            UpdateBuildSettings();
            AssetDatabase.SaveAssets();
        }

        /// <summary>B03: một màu, hai material giống hệt nhau, chỉ khác Enable GPU Instancing.</summary>
        private static void GenerateB03Internal()
        {
            PrepareProject();
            Color color = B01_COLORS[5];
            Material plain = EnsureLitMaterial(B03_PLAIN_MATERIAL, color);
            Material instanced = EnsureLitMaterial(B03_INSTANCED_MATERIAL, color);
            instanced.enableInstancing = true;
            EditorUtility.SetDirty(instanced);
            BuildLessonScene<B03GpuInstancingScenario>(B03_SCENE, 3, 3, scenario =>
            {
                SetArray(scenario, "materials", new Object[] { plain });
                Set(scenario, "instancedMaterial", instanced);
            });
        }

        private static void GenerateB01Internal()
        {
            PrepareProject();
            var colors = new Material[B01_MATERIAL_COUNT];
            var variants = new Material[B01_MATERIAL_COUNT];
            for (int i = 0; i < B01_MATERIAL_COUNT; i++)
            {
                colors[i] = EnsureLitMaterial($"{B01_MATERIALS}/OL_B01_Color_{i}.mat", B01_COLORS[i]);
                variants[i] = EnsureLitMaterial($"{B01_MATERIALS}/OL_B01_Variant_{i}.mat", B01_COLORS[i]);
                ApplyVariantKeywords(variants[i], i);
            }
            BuildLessonScene<B01SrpBatcherScenario>(B01_SCENE, variantCount: 3, countOptions: 3, scenario =>
            {
                SetArray(scenario, "materials", colors);
                SetArray(scenario, "keywordMaterials", variants);
            });
        }

        /// <summary>Material i bật tổ hợp keyword theo bit của i → 8 shader variant khác nhau (i = 0 trùng variant gốc).</summary>
        private static void ApplyVariantKeywords(Material mat, int index)
        {
            for (int bit = 0; bit < B01_VARIANT_KEYWORDS.Length; bit++)
            {
                (string keyword, string property) = B01_VARIANT_KEYWORDS[bit];
                bool on = (index & (1 << bit)) != 0;
                // Property "_X" = 1 nghĩa là tính năng bật → keyword "_X_OFF" phải tắt; giữ hai thứ khớp nhau để URP không tự sửa lại.
                mat.SetFloat(property, on ? 0f : 1f);
                if (on) mat.EnableKeyword(keyword);
                else mat.DisableKeyword(keyword);
            }
            EditorUtility.SetDirty(mat);
        }

        private static void PrepareProject()
        {
            EnsureFolders();
            WriteBuildInfo();
            // Cần cho FrameTimingManager (CPU main/render + GPU time) trong player.
            PlayerSettings.enableFrameTimingStats = true;
        }

        /// <summary>Dựng scene bài học chung: camera, light, scenario + runner, HUD, EventSystem.</summary>
        private static void BuildLessonScene<T>(string path, int variantCount, int countOptions, System.Action<T> wireScenario)
            where T : BenchmarkScenario
        {
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureLighting();

            Camera cam = CreateCamera();
            CreateDirectionalLight();

            var lab = new GameObject("OptimizationLab");
            var scenario = lab.AddComponent<T>();
            Set(scenario, "benchmarkCamera", cam);
            Set(scenario, "mesh", cube);
            wireScenario(scenario);
            var runner = lab.AddComponent<BenchmarkRunner>();
            Set(runner, "scenario", scenario);

            BuildHud(runner, variantCount, countOptions);
            CreateEventSystem();

            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[OptimizationLab] Đã tạo {path}");
        }

        private static Camera CreateCamera()
        {
            var go = new GameObject("BenchmarkCamera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BACKGROUND;
            cam.orthographic = true;
            cam.orthographicSize = 20f;
            // Góc nhìn nghiêng kiểu puzzle 3D; vị trí/size thực tế do scenario fit lúc chạy.
            go.transform.rotation = Quaternion.Euler(55f, 0f, 0f); // yaw 0: lưới vuông vừa màn dọc hơn góc 45°
            go.transform.position = -go.transform.forward * 80f;
            go.AddComponent<AudioListener>();
            return cam;
        }

        private static void CreateDirectionalLight()
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.None; // B00 tách khỏi chi phí shadow (xem B08)
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void ConfigureLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.37f, 0.42f);
            RenderSettings.fog = false;
        }

        private static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            // Project dùng Input System mới (activeInputHandler = 1).
            go.AddComponent<InputSystemUIInputModule>();
        }

        #endregion

        #region Private — HUD (dựng sẵn trong scene, script chỉ bơm dữ liệu)

        private static void BuildHud(BenchmarkRunner runner, int variantCount, int countOptions)
        {
            var canvasGo = new GameObject("BenchmarkHUD", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var hud = canvasGo.AddComponent<BenchmarkHUD>();
            var guide = canvasGo.AddComponent<TutorialGuide>();
            Transform root = canvasGo.transform;
            var targets = new RectTransform[System.Enum.GetValues(typeof(TutorialTarget)).Length];

            // ── Thanh trên: tên bài + chip tốc độ ──
            RectTransform top = Rect("TopBar", root, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 150));
            Img(top, PANEL);
            Text title = Label("Title", top, "B00", 46, TEXT, FontStyle.Bold, TextAnchor.UpperLeft);
            Place(title, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -20), new Vector2(420, 64));
            Text subtitle = Label("Subtitle", top, "", 30, TEXT_DIM, FontStyle.Normal, TextAnchor.UpperLeft);
            Place(subtitle, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -86), new Vector2(520, 44));

            RectTransform chip = Rect("FpsChip", top, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -19), new Vector2(240, 112));
            Image chipImg = Img(chip, GOOD);
            Text fpsValue = Label("Value", chip, "60", 52, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(fpsValue, new Vector2(0, 0.38f), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Text fpsMs = Label("Ms", chip, "16.6 ms", 24, Color.white, FontStyle.Normal, TextAnchor.MiddleCenter);
            Place(fpsMs, new Vector2(0, 0), new Vector2(1, 0.4f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            targets[(int)TutorialTarget.FpsChip] = chip;

            // ── Viên "Đang đo" (bấm để hiện lại UI) ──
            RectTransform pill = Rect("MeasuringPill", root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -172), new Vector2(620, 100));
            Image pillImg = Img(pill, CARD);
            AddButton(pill, pillImg, hud.ToggleUi);
            Text pillText = Label("Text", pill, "Đang đo", 32, TEXT, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(pillText, new Vector2(0, 0.2f), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            RectTransform barBg = Rect("Bar", pill, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(-48, 10));
            Img(barBg, BUTTON);
            RectTransform fill = Rect("Fill", barBg, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            Img(fill, ACCENT);
            pill.gameObject.SetActive(false);

            // ── Thanh dưới ──
            RectTransform bottom = Rect("BottomPanel", root, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, Vector2.zero);
            Img(bottom, PANEL);
            var vlg = bottom.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(24, 24, 24, 32);
            vlg.spacing = 16;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            bottom.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Thẻ kết quả (ẩn tới khi có kết quả)
            RectTransform card = Rect("ResultCard", bottom, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Img(card, CARD);
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 150;
            RectTransform accent = Rect("Accent", card, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(14, 0));
            Image accentImg = Img(accent, GOOD);
            Text resultTitle = Label("Title", card, "", 34, TEXT, FontStyle.Bold, TextAnchor.LowerLeft);
            Place(resultTitle, new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-80, 0), new Vector2(-220, 0));
            Text resultLine = Label("Line", card, "", 26, TEXT_DIM, FontStyle.Normal, TextAnchor.UpperLeft);
            Place(resultLine, new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-80, 0), new Vector2(-220, 0));
            RectTransform detailBtn = Rect("Btn_Detail", card, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(150, 84));
            MakeButton(detailBtn, "Chi tiết", BUTTON, 24, hud.ToggleDetail);
            card.gameObject.SetActive(false);
            targets[(int)TutorialTarget.ResultCard] = card;

            // Xem A / B / C
            RectTransform rowPreview = Row("Row_Preview", bottom, 100);
            var previewImgs = new Image[variantCount];
            var previewTexts = new Text[variantCount];
            for (int i = 0; i < variantCount; i++)
            {
                int index = i;
                RectTransform b = Rect($"Btn_Preview{(char)('A' + i)}", rowPreview, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                previewTexts[i] = MakeButton(b, ((char)('A' + i)).ToString(), BUTTON, 32, out previewImgs[i],
                    btn => UnityEventTools.AddIntPersistentListener(btn.onClick, hud.PreviewVariant, index));
                targets[(int)TutorialTarget.PreviewA + i] = b;
            }

            // Số khối
            RectTransform rowCounts = Row("Row_Counts", bottom, 84);
            var countImgs = new Image[countOptions];
            var countTexts = new Text[countOptions];
            for (int i = 0; i < countOptions; i++)
            {
                int index = i;
                RectTransform b = Rect($"Btn_Count{i}", rowCounts, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                countTexts[i] = MakeButton(b, "-", BUTTON, 30, out countImgs[i],
                    btn => UnityEventTools.AddIntPersistentListener(btn.onClick, hud.SetCountIndex, index));
                targets[(int)TutorialTarget.Count0 + i] = b;
            }

            // Số liệu live của bài học: số khối + số lần vẽ
            RectTransform rowStats = Row("Row_Stats", bottom, 150);
            var statValues = new Text[3];
            var statCaptions = new Text[3];
            for (int i = 0; i < statValues.Length; i++)
                statValues[i] = StatCell($"Stat_{i}", rowStats, "-", out statCaptions[i]);
            targets[(int)TutorialTarget.Stats] = rowStats;

            // Công cụ nhỏ
            RectTransform rowTools = Row("Row_Tools", bottom, 76);
            MakeButton(Rect("Btn_Measure", rowTools, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero), "Đo", TOOL, 26, hud.RunSweep);
            MakeButton(Rect("Btn_Help", rowTools, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero), "?", TOOL, 32, hud.ToggleHelp);
            MakeButton(Rect("Btn_Tutorial", rowTools, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero), "Hướng dẫn", TOOL, 26, hud.StartTutorial);
            MakeButton(Rect("Btn_HideUi", rowTools, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero), "Ẩn UI", TOOL, 26, hud.ToggleUi);

            RectTransform showUi = Rect("Btn_ShowUi", root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(170, 84));
            MakeButton(showUi, "Hiện UI", BUTTON, 26, hud.ToggleUi);
            showUi.gameObject.SetActive(false);

            // ── Overlay "?" ──
            RectTransform help = Overlay("HelpPanel", root, out RectTransform helpCard);
            Text helpTitle = Label("Title", helpCard, "", 40, TEXT, FontStyle.Bold, TextAnchor.MiddleCenter);
            helpTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 70;
            var tips = new Text[3];
            for (int i = 0; i < tips.Length; i++)
            {
                RectTransform tip = Rect($"Tip{i}", helpCard, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                Img(tip, BUTTON);
                tip.gameObject.AddComponent<LayoutElement>().preferredHeight = 120;
                tips[i] = Label("Text", tip, "", 30, TEXT, FontStyle.Normal, TextAnchor.MiddleLeft);
                Place(tips[i], Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-48, -16));
            }
            RectTransform helpClose = Rect("Btn_Close", helpCard, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            helpClose.gameObject.AddComponent<LayoutElement>().preferredHeight = 100;
            MakeButton(helpClose, "Hiểu rồi", ACCENT, 34, hud.ToggleHelp);
            help.gameObject.SetActive(false);

            // ── Overlay chi tiết ──
            RectTransform detail = Overlay("DetailPanel", root, out RectTransform detailCard);
            Text detailText = Label("Text", detailCard, "", 24, TEXT, FontStyle.Normal, TextAnchor.UpperLeft);
            detailText.gameObject.AddComponent<LayoutElement>().preferredHeight = 900;
            RectTransform detailClose = Rect("Btn_Close", detailCard, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            detailClose.gameObject.AddComponent<LayoutElement>().preferredHeight = 100;
            MakeButton(detailClose, "Đóng", ACCENT, 34, hud.ToggleDetail);
            detail.gameObject.SetActive(false);

            // ── Tutorial: khung nhấp nháy + bong bóng + màn hoàn thành ──
            RectTransform layer = Rect("TutorialLayer", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            RectTransform highlight = Rect("Highlight", layer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200, 100));
            BorderBars(highlight, HIGHLIGHT, 8f);
            highlight.gameObject.SetActive(false);

            RectTransform bubble = Rect("Bubble", layer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860, 0));
            Img(bubble, BUBBLE);
            var bv = bubble.gameObject.AddComponent<VerticalLayoutGroup>();
            bv.padding = new RectOffset(36, 36, 30, 26);
            bv.spacing = 18;
            bv.childControlWidth = bv.childControlHeight = true;
            bv.childForceExpandWidth = true;
            bv.childForceExpandHeight = false;
            bubble.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Text bubbleText = Label("Text", bubble, "", 36, BUBBLE_TEXT, FontStyle.Bold, TextAnchor.MiddleCenter);
            RectTransform bubbleRow = Row("Row", bubble, 76);
            MakeButton(Rect("Btn_Skip", bubbleRow, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero), "Bỏ qua", BUBBLE_SOFT, 24, guide.Skip, BUBBLE_TEXT);
            Text dots = Label("Dots", bubbleRow, "", 26, BUBBLE_TEXT, FontStyle.Normal, TextAnchor.MiddleCenter);
            RectTransform next = Rect("Btn_Next", bubbleRow, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            MakeButton(next, "Tiếp  ›", ACCENT, 30, guide.Next);
            bubble.gameObject.SetActive(false);

            RectTransform complete = Overlay("CompletePanel", layer, out RectTransform completeCard);
            Text stars = Label("Stars", completeCard, "★ ★ ★", 96, STAR, FontStyle.Bold, TextAnchor.MiddleCenter);
            stars.gameObject.AddComponent<LayoutElement>().preferredHeight = 140;
            Text done = Label("Title", completeCard, "Hoàn thành!", 46, TEXT, FontStyle.Bold, TextAnchor.MiddleCenter);
            done.gameObject.AddComponent<LayoutElement>().preferredHeight = 70;
            Text completeText = Label("Text", completeCard, "", 30, TEXT_DIM, FontStyle.Normal, TextAnchor.MiddleCenter);
            completeText.gameObject.AddComponent<LayoutElement>().preferredHeight = 100;
            RectTransform completeRow = Row("Row", completeCard, 100);
            MakeButton(Rect("Btn_Replay", completeRow, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero), "Chơi lại", BUTTON, 30, guide.Replay);
            MakeButton(Rect("Btn_Close", completeRow, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero), "Đóng", ACCENT, 30, guide.CloseComplete);
            complete.gameObject.SetActive(false);

            // ── Gán tham chiếu ──
            Set(hud, "runner", runner);
            Set(hud, "titleText", title);
            Set(hud, "subtitleText", subtitle);
            Set(hud, "fpsChip", chipImg);
            Set(hud, "fpsValueText", fpsValue);
            Set(hud, "fpsMsText", fpsMs);
            Set(hud, "measuringPill", pill.gameObject);
            Set(hud, "measuringText", pillText);
            Set(hud, "measuringFill", fill);
            Set(hud, "bottomPanel", bottom.gameObject);
            SetArray(hud, "previewButtons", previewImgs);
            SetArray(hud, "previewLabels", previewTexts);
            SetArray(hud, "countButtons", countImgs);
            SetArray(hud, "countLabels", countTexts);
            SetArray(hud, "statValueTexts", statValues);
            SetArray(hud, "statCaptionTexts", statCaptions);
            Set(hud, "showUiButton", showUi.gameObject);
            Set(hud, "resultCard", card.gameObject);
            Set(hud, "resultAccent", accentImg);
            Set(hud, "resultTitle", resultTitle);
            Set(hud, "resultLine", resultLine);
            Set(hud, "helpPanel", help.gameObject);
            Set(hud, "helpTitle", helpTitle);
            SetArray(hud, "helpTips", tips);
            Set(hud, "detailPanel", detail.gameObject);
            Set(hud, "detailText", detailText);

            Set(guide, "hud", hud);
            Set(guide, "canvasRect", (RectTransform)root);
            Set(guide, "bubble", bubble.gameObject);
            Set(guide, "bubbleRect", bubble);
            Set(guide, "bubbleText", bubbleText);
            Set(guide, "dotsText", dots);
            Set(guide, "nextButton", next.gameObject);
            Set(guide, "highlight", highlight);
            Set(guide, "completePanel", complete.gameObject);
            Set(guide, "completeText", completeText);
            SetArray(guide, "targets", targets);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        private static void Place(Graphic g, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rt = g.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static Image Img(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>Ô số liệu: số lớn ở trên, nhãn nhỏ ở dưới.</summary>
        private static Text StatCell(string name, Transform parent, string caption, out Text captionText)
        {
            RectTransform cell = Rect(name, parent, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Img(cell, CARD);
            Text value = Label("Value", cell, "-", 60, TEXT, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(value, new Vector2(0, 0.32f), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            captionText = Label("Caption", cell, caption, 26, TEXT_DIM, FontStyle.Normal, TextAnchor.MiddleCenter);
            Place(captionText, new Vector2(0, 0), new Vector2(1, 0.36f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return value;
        }

        private static RectTransform Row(string name, Transform parent, float height)
        {
            RectTransform row = Rect(name, parent, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return row;
        }

        /// <summary>Overlay toàn màn hình (nền tối, chặn bấm xuyên) + thẻ giữa có VerticalLayout.</summary>
        private static RectTransform Overlay(string name, Transform parent, out RectTransform card)
        {
            RectTransform overlay = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Img(overlay, DIM);
            card = Rect("Card", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920, 0));
            Img(card, CARD);
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(36, 36, 36, 36);
            v.spacing = 18;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return overlay;
        }

        /// <summary>Khung viền rỗng từ 4 thanh (không che nút bên dưới).</summary>
        private static void BorderBars(RectTransform frame, Color color, float thickness)
        {
            (string, Vector2, Vector2, Vector2)[] bars =
            {
                ("Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, thickness)),
                ("Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, thickness)),
                ("Left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(thickness, 0)),
                ("Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(thickness, 0)),
            };
            foreach (var (n, min, max, size) in bars)
            {
                RectTransform bar = Rect(n, frame, min, max, new Vector2(0.5f, 0.5f), Vector2.zero, size);
                Image img = Img(bar, color);
                img.raycastTarget = false;
            }
        }

        private static Text Label(string name, Transform parent, string text, int size, Color color, FontStyle style, TextAnchor align)
        {
            RectTransform rt = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static Text MakeButton(RectTransform rt, string label, Color color, int fontSize, UnityAction onClick, Color? textColor = null)
        {
            return MakeButton(rt, label, color, fontSize, out _, b => UnityEventTools.AddPersistentListener(b.onClick, onClick), textColor);
        }

        private static Image AddButton(RectTransform rt, Image img, UnityAction onClick)
        {
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            UnityEventTools.AddPersistentListener(btn.onClick, onClick);
            return img;
        }

        private static Text MakeButton(RectTransform rt, string label, Color color, int fontSize, out Image image,
            System.Action<Button> wire, Color? textColor = null)
        {
            image = Img(rt, color);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = image;
            wire(btn);
            Text t = Label("Label", rt, label, fontSize, textColor ?? TEXT, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(t, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-12, -8));
            return t;
        }

        #endregion

        #region Private — assets

        private static void EnsureFolders()
        {
            foreach (string path in new[] { ROOT, SCENES, MATERIALS, B00_MATERIALS, B01_MATERIALS, B02_MATERIALS, B03_MATERIALS, RESOURCES })
            {
                if (AssetDatabase.IsValidFolder(path)) continue;
                string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
            // Chuyển material B00 cũ vào thư mục riêng (giữ GUID).
            if (AssetDatabase.LoadAssetAtPath<Material>(LEGACY_BASELINE_MATERIAL) != null
                && AssetDatabase.LoadAssetAtPath<Material>(BASELINE_MATERIAL) == null)
                AssetDatabase.MoveAsset(LEGACY_BASELINE_MATERIAL, BASELINE_MATERIAL);
        }

        /// <summary>Tạo hoặc cập nhật material URP/Lit tại chỗ (giữ GUID, không nhân đôi).</summary>
        public static Material EnsureLitMaterial(string path, Color color)
        {
            Shader shader = Shader.Find(URP_LIT);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.3f);
            mat.enableInstancing = false; // B00: đường render thông thường, không instancing
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void WriteBuildInfo()
        {
            var urp = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.render-pipelines.universal");
            var info = new LabBuildInfo
            {
                urpVersion = urp != null ? urp.version : "not installed",
                gitCommit = ReadGitCommit(),
                generatedUtc = System.DateTime.UtcNow.ToString("o"),
            };
            string json = JsonUtility.ToJson(info, true);
            if (File.Exists(BUILD_INFO) && File.ReadAllText(BUILD_INFO).Contains($"\"urpVersion\": \"{info.urpVersion}\"")
                && File.ReadAllText(BUILD_INFO).Contains(info.gitCommit))
                return; // không ghi lại nếu chỉ đổi timestamp → giữ git diff sạch
            File.WriteAllText(BUILD_INFO, json);
            AssetDatabase.ImportAsset(BUILD_INFO);
        }

        private static string ReadGitCommit()
        {
            try
            {
                string head = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".git", "HEAD"));
                if (!File.Exists(head)) return "no-git";
                string text = File.ReadAllText(head).Trim();
                if (!text.StartsWith("ref: ")) return text;
                string refPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".git", text.Substring(5)));
                return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : text;
            }
            catch (IOException)
            {
                return "unknown";
            }
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[OptimizationLab] Không thấy field '{field}' trên {target.GetType().Name}"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[OptimizationLab] Không thấy field '{field}' trên {target.GetType().Name}"); return; }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        #endregion
    }
}
