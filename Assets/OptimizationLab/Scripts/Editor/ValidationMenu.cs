using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OptimizationLab.Scenarios;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OptimizationLab.Editor
{
    /// <summary>
    /// Validation suite trong Editor: kiểm scene, build settings, pipeline, camera, material, shader.
    /// Báo cáo dạng bảng scene | check | expected | actual | pass/fail | giải thích → Results/validation_*.md.
    /// </summary>
    public static class ValidationMenu
    {
        public sealed class Check
        {
            public string scene, name, expected, actual, explanation;
            public bool pass;
        }

        [MenuItem("Tools/Optimization Lab/Validate Scenes", priority = 1)]
        public static void ValidateScenesMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            List<Check> checks = Run();
            string report = WriteReport(checks);
            int fails = checks.Count(c => !c.pass);
            string msg = fails == 0 ? $"Tất cả {checks.Count} check PASS." : $"{fails}/{checks.Count} check FAIL.";
            Debug.Log($"[OptimizationLab] Validation: {msg}\n{report}");
            EditorUtility.DisplayDialog("Optimization Lab — Validation", msg + "\n\nBáo cáo: " + report, "OK");
        }

        /// <summary>Chạy toàn bộ check. Mở scene lần lượt rồi trả lại scene đang mở.</summary>
        public static List<Check> Run()
        {
            var checks = new List<Check>();
            string previous = SceneManager.GetActiveScene().path;

            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            Add(checks, "Project", "Render pipeline", "URP", rp is UniversalRenderPipelineAsset ? "URP" : rp == null ? "Built-in" : rp.GetType().Name,
                "Lab B00–B11 chạy trên URP; Built-in so ở project riêng (X01).");
            Add(checks, "Project", "Frame Timing Stats", "true", PlayerSettings.enableFrameTimingStats.ToString().ToLower(),
                "Cần để FrameTimingManager trả CPU main/render + GPU time trong player.");

            ValidateB00(checks);
            ValidateB01(checks);
            ValidateB02(checks);
            ValidateB03(checks);

            if (!string.IsNullOrEmpty(previous) && File.Exists(previous)) EditorSceneManager.OpenScene(previous);
            return checks;
        }

        private static void ValidateB00(List<Check> checks)
        {
            const string s = "B00";
            string path = OptimizationLabSceneGenerator.B00_SCENE;
            bool exists = File.Exists(path);
            Add(checks, s, "Scene tồn tại", path, exists ? path : "missing", "Chạy Tools ▸ Optimization Lab ▸ Generate All Scenes.");
            if (!exists) return;

            bool inBuild = EditorBuildSettings.scenes.Any(e => e.path == path && e.enabled);
            Add(checks, s, "Trong Build Settings", "enabled", inBuild ? "enabled" : "missing", "Scene phải có trong build để chạy trên Android.");

            EditorSceneManager.OpenScene(path);
            var scenario = Object.FindFirstObjectByType<B00BaselineScenario>();
            var runner = Object.FindFirstObjectByType<BenchmarkRunner>();
            var hud = Object.FindFirstObjectByType<BenchmarkHUD>();
            Add(checks, s, "Scenario component", "1", scenario != null ? "1" : "0", "B00BaselineScenario trên node OptimizationLab.");
            Add(checks, s, "Runner component", "1", runner != null ? "1" : "0", "BenchmarkRunner điều phối warm-up/đo/ABBA.");
            Add(checks, s, "HUD", "1", hud != null ? "1" : "0", "HUD dựng sẵn trong scene.");
            var uiModule = Object.FindFirstObjectByType<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            Add(checks, s, "UI input module", "InputSystemUIInputModule", uiModule != null ? "InputSystemUIInputModule" : "missing",
                "Project chỉ bật Input System mới; thiếu module này thì nút HUD không bấm được.");
            if (scenario == null) return;

            var so = new SerializedObject(scenario);
            var cam = so.FindProperty("benchmarkCamera").objectReferenceValue as Camera;
            SerializedProperty mats = so.FindProperty("materials");
            var mat = mats != null && mats.arraySize > 0 ? mats.GetArrayElementAtIndex(0).objectReferenceValue as Material : null;
            var mesh = so.FindProperty("mesh").objectReferenceValue as Mesh;
            int cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length;

            Add(checks, s, "Camera", "1 camera, assigned", $"{cameras} camera, {(cam != null ? "assigned" : "unassigned")}",
                "Một camera duy nhất → A/B nhìn cùng vùng; nhiều camera = nhiều lần render.");
            Add(checks, s, "Mesh", "Cube (24 verts)", mesh != null ? $"{mesh.name} ({mesh.vertexCount} verts)" : "none",
                "Cùng mesh cho mọi object.");
            Add(checks, s, "Material shader", "Universal Render Pipeline/Lit", mat != null ? mat.shader.name : "none",
                "Một shared URP/Lit material; không clone per-object.");
            Add(checks, s, "Material instancing", "false", mat != null ? mat.enableInstancing.ToString().ToLower() : "n/a",
                "B00 đo đường render thông thường; instancing thuộc B03.");

            var light = Object.FindFirstObjectByType<Light>();
            Add(checks, s, "Directional light shadows", "None", light != null ? light.shadows.ToString() : "no light",
                "B00 tách khỏi chi phí shadow (B08).");
            int materialsInFolder = AssetDatabase.FindAssets("t:Material", new[] { OptimizationLabSceneGenerator.B00_MATERIALS }).Length;
            Add(checks, s, "Material assets (idempotent)", "1", materialsInFolder.ToString(),
                "Generate chạy lại không được nhân đôi material.");
        }

        private static void ValidateB02(List<Check> checks)
        {
            const string id = "B02";
            string path = OptimizationLabSceneGenerator.B02_SCENE;
            Add(checks, id, "Scene", "present", File.Exists(path) ? "present" : "missing", "Generate B02.");
            if (!File.Exists(path)) return;
            Add(checks, id, "Build Settings", "enabled", EditorBuildSettings.scenes.Any(x => x.path == path && x.enabled) ? "enabled" : "missing", "Scene bài học.");
            EditorSceneManager.OpenScene(path);
            var scenario = Object.FindFirstObjectByType<B02StaticBatchingScenario>();
            Add(checks, id, "Scenario", "present", scenario != null ? "present" : "missing", "Runtime combine trước đo.");
            Add(checks, id, "HUD", "present", Object.FindFirstObjectByType<BenchmarkHUD>() != null ? "present" : "missing", "Ba ô live.");
            if (scenario == null) return;
            var so = new SerializedObject(scenario);
            Material[] mats = Materials(so.FindProperty("materials"));
            Add(checks, id, "Shared materials", "4", mats.Length.ToString(), "3–5 material theo spec.");
            Add(checks, id, "Instancing", "off", mats.All(m => m != null && !m.enableInstancing) ? "off" : "on/missing", "Cô lập static batching.");
            var mesh = so.FindProperty("mesh").objectReferenceValue as Mesh;
            Add(checks, id, "Mesh readable", "true", mesh != null && mesh.isReadable ? "true" : "false", "Runtime combine cần mesh readable.");
            Add(checks, id, "Max count", "1500", so.FindProperty("maxObjectCount").intValue.ToString(), "Camera khung cố định.");
        }

        private static void ValidateB03(List<Check> checks)
        {
            const string id = "B03";
            string path = OptimizationLabSceneGenerator.B03_SCENE;
            Add(checks, id, "Scene", "present", File.Exists(path) ? "present" : "missing", "Generate B03.");
            if (!File.Exists(path)) return;
            Add(checks, id, "Build Settings", "enabled", EditorBuildSettings.scenes.Any(x => x.path == path && x.enabled) ? "enabled" : "missing", "Scene bài học.");
            EditorSceneManager.OpenScene(path);
            var scenario = Object.FindFirstObjectByType<B03GpuInstancingScenario>();
            Add(checks, id, "Scenario", "present", scenario != null ? "present" : "missing", "B03GpuInstancingScenario trên node OptimizationLab.");
            Add(checks, id, "HUD", "present", Object.FindFirstObjectByType<BenchmarkHUD>() != null ? "present" : "missing", "Ba ô live.");
            if (scenario == null) return;
            var so = new SerializedObject(scenario);
            Material[] mats = Materials(so.FindProperty("materials"));
            var instanced = so.FindProperty("instancedMaterial").objectReferenceValue as Material;
            Add(checks, id, "Material A", "1, instancing off", mats.Length == 1 && mats[0] != null && !mats[0].enableInstancing ? "1, instancing off" : "sai",
                "A = đường vẽ thường.");
            Add(checks, id, "Material B/C", "instancing on", instanced != null && instanced.enableInstancing ? "instancing on" : "off/missing",
                "B = cùng material nhưng tick Enable GPU Instancing.");
            bool same = mats.Length == 1 && mats[0] != null && instanced != null && mats[0].shader == instanced.shader
                && mats[0].GetColor("_BaseColor") == instanced.GetColor("_BaseColor");
            Add(checks, id, "A/B chỉ khác instancing", "true", same ? "true" : "false", "Cùng shader, cùng màu.");
            Add(checks, id, "Max count", "3000", so.FindProperty("maxObjectCount").intValue.ToString(), "Camera khung cố định theo 3000 khối.");
            int folderMats = AssetDatabase.FindAssets("t:Material", new[] { OptimizationLabSceneGenerator.B03_MATERIALS }).Length;
            Add(checks, id, "Material assets (idempotent)", "2", folderMats.ToString(), "Generate chạy lại không nhân đôi material.");
        }

        private static void ValidateB01(List<Check> checks)
        {
            const string s = "B01";
            string path = OptimizationLabSceneGenerator.B01_SCENE;
            bool exists = File.Exists(path);
            Add(checks, s, "Scene tồn tại", path, exists ? path : "missing", "Chạy Tools ▸ Optimization Lab ▸ Generate All Scenes.");
            if (!exists) return;
            bool inBuild = EditorBuildSettings.scenes.Any(e => e.path == path && e.enabled);
            Add(checks, s, "Trong Build Settings", "enabled", inBuild ? "enabled" : "missing", "Scene phải có trong build.");

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Add(checks, s, "URP asset bật SRP Batcher", "true", urp != null ? urp.useSRPBatcher.ToString().ToLower() : "n/a",
                "Variant B/C cần SRP Batcher bật sẵn; runner chỉ tắt tạm cho variant A rồi trả lại.");

            EditorSceneManager.OpenScene(path);
            var scenario = Object.FindFirstObjectByType<B01SrpBatcherScenario>();
            Add(checks, s, "Scenario component", "1", scenario != null ? "1" : "0", "B01SrpBatcherScenario trên node OptimizationLab.");
            if (scenario == null) return;
            var so = new SerializedObject(scenario);
            Material[] colors = Materials(so.FindProperty("materials"));
            Material[] variants = Materials(so.FindProperty("keywordMaterials"));
            int n = OptimizationLabSceneGenerator.B01_MATERIAL_COUNT;
            Add(checks, s, "Material màu", n.ToString(), colors.Length.ToString(), "8 material khác màu, dùng chung.");
            Add(checks, s, "Material biến thể", n.ToString(), variants.Length.ToString(), "8 material mang 8 tổ hợp keyword (variant C).");

            var colorShaders = new HashSet<string>(colors.Where(m => m != null).Select(m => m.shader.name + "|" + string.Join(" ", m.shaderKeywords.OrderBy(k => k))));
            Add(checks, s, "A/B cùng một shader variant", "1", colorShaders.Count.ToString(),
                "SRP Batcher chỉ gộp các draw cùng shader variant; 8 màu phải chung variant.");
            var variantSet = new HashSet<string>(variants.Where(m => m != null).Select(m => string.Join(" ", m.shaderKeywords.OrderBy(k => k))));
            Add(checks, s, "C có 8 shader variant", n.ToString(), variantSet.Count.ToString(), "Mỗi material C một tổ hợp keyword khác nhau.");
            Add(checks, s, "Shader", "Universal Render Pipeline/Lit", colors.Length > 0 && colors[0] != null ? colors[0].shader.name : "none",
                "URP/Lit là shader SRP-compatible.");
            int folderMats = AssetDatabase.FindAssets("t:Material", new[] { OptimizationLabSceneGenerator.B01_MATERIALS }).Length;
            Add(checks, s, "Material assets (idempotent)", (n * 2).ToString(), folderMats.ToString(), "Generate chạy lại không nhân đôi material.");
        }

        private static Material[] Materials(SerializedProperty array)
        {
            if (array == null) return System.Array.Empty<Material>();
            var list = new List<Material>();
            for (int i = 0; i < array.arraySize; i++) list.Add(array.GetArrayElementAtIndex(i).objectReferenceValue as Material);
            return list.ToArray();
        }

        private static void Add(List<Check> list, string scene, string name, string expected, string actual, string explanation)
        {
            list.Add(new Check
            {
                scene = scene, name = name, expected = expected, actual = actual, explanation = explanation,
                pass = expected == actual,
            });
        }

        private static string WriteReport(List<Check> checks)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Optimization Lab — Validation ({System.DateTime.Now:yyyy-MM-dd HH:mm})");
            sb.AppendLine();
            sb.AppendLine($"Unity {Application.unityVersion}");
            sb.AppendLine();
            sb.AppendLine("| Scene | Check | Expected | Actual | Result | Giải thích |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (Check c in checks)
                sb.AppendLine($"| {c.scene} | {c.name} | {c.expected} | {c.actual} | {(c.pass ? "PASS" : "**FAIL**")} | {c.explanation} |");

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Results"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"validation_{System.DateTime.Now:yyyyMMdd_HHmmss}.md");
            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }
}
