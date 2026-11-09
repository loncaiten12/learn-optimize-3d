using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace OptimizationLab.Editor
{
    /// <summary>Chạy EditMode tests của lab và ghi kết quả vào Results/tests_*.txt (dùng được từ menu và automation).</summary>
    public static class LabTestRunnerMenu
    {
        private const string TEST_ASSEMBLY = "OptimizationLab.Tests.Editor";

        [MenuItem("Tools/Optimization Lab/Run EditMode Tests", priority = 2)]
        public static void RunEditModeTests()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { TEST_ASSEMBLY } }));
        }

        private sealed class Callbacks : ICallbacks
        {
            private readonly StringBuilder _sb = new StringBuilder();

            public void RunStarted(ITestAdaptor testsToRun) => _sb.Clear();
            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren) _sb.AppendLine($"{result.TestStatus,-8} {result.Name} {result.Message}");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _sb.AppendLine($"DONE pass={result.PassCount} fail={result.FailCount} skip={result.SkipCount}");
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Results"));
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"tests_editmode_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(path, _sb.ToString());
                Debug.Log($"[OptimizationLab] EditMode tests: pass={result.PassCount} fail={result.FailCount} → {path}");
            }
        }
    }
}
