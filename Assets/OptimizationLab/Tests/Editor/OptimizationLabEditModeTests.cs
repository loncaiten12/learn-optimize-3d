using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace OptimizationLab.Tests
{
    public sealed class OptimizationLabEditModeTests
    {
        [Test]
        public void SameSeed_GivesIdenticalTransforms()
        {
            const int n = 1000;
            var p1 = new Vector3[n]; var y1 = new float[n];
            var p2 = new Vector3[n]; var y2 = new float[n];
            DeterministicSpawner.FillGrid(n, 1.2f, 0.15f, 1337, p1, y1);
            DeterministicSpawner.FillGrid(n, 1.2f, 0.15f, 1337, p2, y2);
            for (int i = 0; i < n; i++)
            {
                Assert.AreEqual(p1[i], p2[i]);
                Assert.AreEqual(y1[i], y2[i]);
            }
        }

        [Test]
        public void DifferentSeed_GivesDifferentJitter()
        {
            const int n = 100;
            var p1 = new Vector3[n]; var y1 = new float[n];
            var p2 = new Vector3[n]; var y2 = new float[n];
            DeterministicSpawner.FillGrid(n, 1.2f, 0.15f, 1, p1, y1);
            DeterministicSpawner.FillGrid(n, 1.2f, 0.15f, 2, p2, y2);
            Assert.AreNotEqual(p1[5], p2[5]);
        }

        [Test]
        public void GridBounds_ContainAllPositions()
        {
            const int n = 3000;
            var p = new Vector3[n]; var y = new float[n];
            DeterministicSpawner.FillGrid(n, 1.2f, 0.15f, 1337, p, y);
            Bounds b = DeterministicSpawner.GridBounds(n, 1.2f, 0.15f, 0.6f);
            for (int i = 0; i < n; i++) Assert.IsTrue(b.Contains(p[i]), $"pos {i} ngoài bounds");
        }

        [Test]
        public void Percentiles_NearestRank()
        {
            var sorted = new float[100];
            for (int i = 0; i < 100; i++) sorted[i] = i + 1;
            Assert.AreEqual(95.0, BenchmarkStats.Percentile(sorted, 100, 0.95));
            Assert.AreEqual(99.0, BenchmarkStats.Percentile(sorted, 100, 0.99));
            Assert.AreEqual(50.5, BenchmarkStats.Median(sorted, 100));
            Assert.IsTrue(double.IsNaN(BenchmarkStats.Median(sorted, 0)));
        }

        [Test]
        public void CsvHeader_StartsWithSpecColumns()
        {
            const string spec = "run_id,utc_time,scene,variant,seed,unity_version,render_pipeline,urp_version,device," +
                                "android_version,graphics_api,width,height,render_scale,object_count,visible_renderers," +
                                "frame_ms_median,frame_ms_p95,frame_ms_p99,cpu_main_ms,cpu_render_ms,gpu_ms,batches," +
                                "setpass_calls,triangles,vertices,allocated_mb,status,invalid_reason";
            StringAssert.StartsWith(spec, ResultsExporter.CsvHeader);
        }

        [Test]
        public void UnsupportedMetric_IsNeverWrittenAsZero()
        {
            var run = MakeRun(BenchmarkVariant.A, 10.0);
            run.metrics.gpuMs = double.NaN;
            string csv = ResultsExporter.BuildCsv(new[] { run }, false);
            string[] cells = csv.Trim().Split(',');
            int gpuIndex = System.Array.IndexOf(ResultsExporter.CsvColumns, "gpu_ms");
            Assert.AreEqual("unsupported", cells[gpuIndex]);
        }

        [Test]
        public void Comparison_SmallDifference_IsInconclusive()
        {
            var runs = new List<RunRecord>
            {
                MakeRun(BenchmarkVariant.A, 10.0), MakeRun(BenchmarkVariant.B, 10.1),
                MakeRun(BenchmarkVariant.B, 9.6), MakeRun(BenchmarkVariant.A, 10.5),
            };
            ComparisonResult c = ComparisonResult.Compare(runs, BenchmarkVariant.A, BenchmarkVariant.B);
            Assert.AreEqual("inconclusive", c.verdict);
        }

        [Test]
        public void Comparison_InvalidRun_BlocksVerdict()
        {
            var runs = new List<RunRecord>
            {
                MakeRun(BenchmarkVariant.A, 20.0), MakeRun(BenchmarkVariant.B, 10.0),
                MakeRun(BenchmarkVariant.B, 10.0), MakeRun(BenchmarkVariant.A, 20.0),
            };
            runs[1].status = BenchmarkStatus.Invalid;
            Assert.AreEqual("invalid", ComparisonResult.Compare(runs, BenchmarkVariant.A, BenchmarkVariant.B).verdict);
        }

        [Test]
        public void Comparison_ClearDifference_ReportsImprovement()
        {
            var runs = new List<RunRecord>
            {
                MakeRun(BenchmarkVariant.A, 20.0), MakeRun(BenchmarkVariant.B, 10.0),
                MakeRun(BenchmarkVariant.B, 10.2), MakeRun(BenchmarkVariant.A, 20.2),
            };
            ComparisonResult c = ComparisonResult.Compare(runs, BenchmarkVariant.A, BenchmarkVariant.B);
            Assert.AreEqual("B faster", c.verdict);
            Assert.AreEqual(49.75, c.improvementPercent, 0.01);
        }

        private static RunRecord MakeRun(BenchmarkVariant v, double median)
        {
            return new RunRecord
            {
                runId = "20260101T000000_00",
                variant = v,
                metrics = new MetricsSummary { frameMsMedian = median, sampleCount = 100 },
                environment = new EnvironmentInfo { targetFrameRate = -1, unityVersion = "test" },
            };
        }
    }
}
