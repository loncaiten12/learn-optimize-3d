using System;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace OptimizationLab
{
    /// <summary>
    /// Kết quả tổng hợp của một lần đo. Mọi giá trị không đọc được = <see cref="double.NaN"/>
    /// (xuất ra "unsupported"/null) — KHÔNG BAO GIỜ thay bằng 0.
    /// </summary>
    [Serializable]
    public sealed class MetricsSummary
    {
        public int sampleCount;
        public bool bufferOverflow;

        public double frameMsMedian = double.NaN;
        public double frameMsMean = double.NaN;
        public double frameMsP95 = double.NaN;
        public double frameMsP99 = double.NaN;
        public double frameMsStdev = double.NaN;
        public double frameMsMax = double.NaN;
        public double fpsMedian = double.NaN;

        public double cpuMainMs = double.NaN;
        public double cpuRenderMs = double.NaN;
        public double gpuMs = double.NaN;
        public string cpuMainSource = "unsupported";
        public string cpuRenderSource = "unsupported";
        public string gpuSource = "unsupported";

        public double batches = double.NaN;
        public double setPassCalls = double.NaN;
        public double drawCalls = double.NaN;
        public double triangles = double.NaN;
        public double vertices = double.NaN;

        public double allocatedMb = double.NaN;
        public double peakAllocatedMb = double.NaN;
        public double monoUsedMb = double.NaN;
        public double graphicsMemoryMb = double.NaN;
        public double gcAllocatedInFrameKbMean = double.NaN;

        public float[] rawFrameMs;
    }

    /// <summary>Thống kê đơn giản trên mảng đã sort (nearest-rank).</summary>
    public static class BenchmarkStats
    {
        public static double Percentile(float[] sorted, int count, double p)
        {
            if (count <= 0) return double.NaN;
            int idx = (int)Math.Ceiling(p * count) - 1;
            idx = Mathf.Clamp(idx, 0, count - 1);
            return sorted[idx];
        }

        public static double Median(float[] sorted, int count)
        {
            if (count <= 0) return double.NaN;
            int mid = count / 2;
            return (count & 1) == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) * 0.5;
        }

        public static void MeanStdev(float[] values, int count, out double mean, out double stdev)
        {
            mean = double.NaN;
            stdev = double.NaN;
            if (count <= 0) return;
            double sum = 0;
            for (int i = 0; i < count; i++) sum += values[i];
            mean = sum / count;
            if (count < 2) { stdev = 0; return; }
            double sq = 0;
            for (int i = 0; i < count; i++) { double d = values[i] - mean; sq += d * d; }
            stdev = Math.Sqrt(sq / (count - 1));
        }

        /// <summary>Median trên mảng double (copy + sort; dùng ngoài measurement).</summary>
        public static double Median(double[] values)
        {
            int n = 0;
            for (int i = 0; i < values.Length; i++) if (!double.IsNaN(values[i])) n++;
            if (n == 0) return double.NaN;
            var tmp = new double[n];
            int k = 0;
            for (int i = 0; i < values.Length; i++) if (!double.IsNaN(values[i])) tmp[k++] = values[i];
            Array.Sort(tmp);
            int mid = n / 2;
            return (n & 1) == 1 ? tmp[mid] : (tmp[mid - 1] + tmp[mid]) * 0.5;
        }

        public static double Stdev(double[] values)
        {
            int n = 0;
            double sum = 0;
            foreach (double v in values) if (!double.IsNaN(v)) { n++; sum += v; }
            if (n < 2) return double.NaN;
            double mean = sum / n, sq = 0;
            foreach (double v in values) if (!double.IsNaN(v)) sq += (v - mean) * (v - mean);
            return Math.Sqrt(sq / (n - 1));
        }
    }

    /// <summary>
    /// Thu thập số đo mỗi frame. Mọi buffer/recorder cấp phát MỘT lần trong Begin(), không alloc trong measurement.
    /// Nguồn: frame delta (realtimeSinceStartupAsDouble), FrameTimingManager (CPU main/render, GPU),
    /// ProfilerRecorder (render stats, memory). Counter nào không có → NaN.
    /// </summary>
    public sealed class MetricsCollector : IDisposable
    {
        #region Fields

        private const double NS_TO_MS = 1e-6;
        private const double BYTES_TO_MB = 1.0 / (1024.0 * 1024.0);

        private ProfilerRecorder _mainThread;
        private ProfilerRecorder _batches;
        private ProfilerRecorder _setPass;
        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _triangles;
        private ProfilerRecorder _vertices;
        private ProfilerRecorder _gfxMemory;
        private ProfilerRecorder _gcAllocInFrame;

        private readonly FrameTiming[] _timings = new FrameTiming[1];

        private float[] _frameMs = Array.Empty<float>();
        private float[] _cpuMain = Array.Empty<float>();
        private float[] _cpuMainFallback = Array.Empty<float>();
        private float[] _cpuRender = Array.Empty<float>();
        private float[] _gpu = Array.Empty<float>();
        private int _n, _nMain, _nMainFallback, _nRender, _nGpu;
        private bool _overflow;

        private double _sumBatches, _sumSetPass, _sumDraw, _sumTris, _sumVerts, _sumGfx, _sumGc;
        private int _nBatches, _nSetPass, _nDraw, _nTris, _nVerts, _nGfx, _nGc;
        private long _peakAllocated;

        private double _lastTimestamp;
        private bool _running;

        #endregion

        #region Public

        public bool IsRunning => _running;
        public int SampleCount => _n;

        /// <summary>FrameTimingManager có bật không (Player Settings → Frame Timing Stats).</summary>
        public static bool FrameTimingSupported => FrameTimingManager.IsFeatureEnabled();

        /// <summary>Bắt đầu một lần đo. Gọi NGOÀI measurement window (có cấp phát).</summary>
        public void Begin(float measureSeconds)
        {
            int capacity = Mathf.CeilToInt(measureSeconds * 300f) + 64;
            EnsureCapacity(ref _frameMs, capacity);
            EnsureCapacity(ref _cpuMain, capacity);
            EnsureCapacity(ref _cpuMainFallback, capacity);
            EnsureCapacity(ref _cpuRender, capacity);
            EnsureCapacity(ref _gpu, capacity);
            _n = _nMain = _nMainFallback = _nRender = _nGpu = 0;
            _overflow = false;
            _sumBatches = _sumSetPass = _sumDraw = _sumTris = _sumVerts = _sumGfx = _sumGc = 0;
            _nBatches = _nSetPass = _nDraw = _nTris = _nVerts = _nGfx = _nGc = 0;
            _peakAllocated = 0;

            StartRecorders();
            _lastTimestamp = Time.realtimeSinceStartupAsDouble;
            _running = true;
        }

        /// <summary>Gọi đúng một lần mỗi frame trong measurement window.</summary>
        public void Sample()
        {
            if (!_running) return;
            double now = Time.realtimeSinceStartupAsDouble;
            float dtMs = (float)((now - _lastTimestamp) * 1000.0);
            _lastTimestamp = now;

            if (_n >= _frameMs.Length) { _overflow = true; return; }
            _frameMs[_n++] = dtMs;

            // FrameTimingManager: số đo của một frame cũ vài frame (GPU trễ) — vẫn đúng phân phối.
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                FrameTiming t = _timings[0];
                if (t.cpuMainThreadFrameTime > 0) _cpuMain[_nMain++] = (float)t.cpuMainThreadFrameTime;
                if (t.cpuRenderThreadFrameTime > 0) _cpuRender[_nRender++] = (float)t.cpuRenderThreadFrameTime;
                if (t.gpuFrameTime > 0) _gpu[_nGpu++] = (float)t.gpuFrameTime;
            }

            if (_mainThread.Valid && _mainThread.Count > 0)
                _cpuMainFallback[_nMainFallback++] = (float)(_mainThread.LastValue * NS_TO_MS);

            Accumulate(ref _batches, ref _sumBatches, ref _nBatches);
            Accumulate(ref _setPass, ref _sumSetPass, ref _nSetPass);
            Accumulate(ref _drawCalls, ref _sumDraw, ref _nDraw);
            Accumulate(ref _triangles, ref _sumTris, ref _nTris);
            Accumulate(ref _vertices, ref _sumVerts, ref _nVerts);
            Accumulate(ref _gfxMemory, ref _sumGfx, ref _nGfx);
            Accumulate(ref _gcAllocInFrame, ref _sumGc, ref _nGc);

            long allocated = Profiler.GetTotalAllocatedMemoryLong();
            if (allocated > _peakAllocated) _peakAllocated = allocated;
        }

        /// <summary>Kết thúc và tính thống kê (có cấp phát — ngoài measurement).</summary>
        public MetricsSummary End(bool includeRaw)
        {
            _running = false;
            var s = new MetricsSummary { sampleCount = _n, bufferOverflow = _overflow };

            if (includeRaw)
            {
                s.rawFrameMs = new float[_n];
                Array.Copy(_frameMs, s.rawFrameMs, _n);
            }

            BenchmarkStats.MeanStdev(_frameMs, _n, out s.frameMsMean, out s.frameMsStdev);
            Array.Sort(_frameMs, 0, _n);
            s.frameMsMedian = BenchmarkStats.Median(_frameMs, _n);
            s.frameMsP95 = BenchmarkStats.Percentile(_frameMs, _n, 0.95);
            s.frameMsP99 = BenchmarkStats.Percentile(_frameMs, _n, 0.99);
            s.frameMsMax = _n > 0 ? _frameMs[_n - 1] : double.NaN;
            s.fpsMedian = s.frameMsMedian > 0 ? 1000.0 / s.frameMsMedian : double.NaN;

            if (_nMain > 0)
            {
                Array.Sort(_cpuMain, 0, _nMain);
                s.cpuMainMs = BenchmarkStats.Median(_cpuMain, _nMain);
                s.cpuMainSource = "FrameTimingManager";
            }
            else if (_nMainFallback > 0)
            {
                Array.Sort(_cpuMainFallback, 0, _nMainFallback);
                s.cpuMainMs = BenchmarkStats.Median(_cpuMainFallback, _nMainFallback);
                s.cpuMainSource = "ProfilerRecorder:Main Thread";
            }

            if (_nRender > 0)
            {
                Array.Sort(_cpuRender, 0, _nRender);
                s.cpuRenderMs = BenchmarkStats.Median(_cpuRender, _nRender);
                s.cpuRenderSource = "FrameTimingManager";
            }

            if (_nGpu > 0)
            {
                Array.Sort(_gpu, 0, _nGpu);
                s.gpuMs = BenchmarkStats.Median(_gpu, _nGpu);
                s.gpuSource = "FrameTimingManager";
            }

            s.batches = Mean(_sumBatches, _nBatches);
            s.setPassCalls = Mean(_sumSetPass, _nSetPass);
            s.drawCalls = Mean(_sumDraw, _nDraw);
            s.triangles = Mean(_sumTris, _nTris);
            s.vertices = Mean(_sumVerts, _nVerts);
            s.graphicsMemoryMb = _nGfx > 0 ? _sumGfx / _nGfx * BYTES_TO_MB : double.NaN;
            s.gcAllocatedInFrameKbMean = _nGc > 0 ? _sumGc / _nGc / 1024.0 : double.NaN;

            s.allocatedMb = Profiler.GetTotalAllocatedMemoryLong() * BYTES_TO_MB;
            s.peakAllocatedMb = _peakAllocated * BYTES_TO_MB;
            s.monoUsedMb = Profiler.GetMonoUsedSizeLong() * BYTES_TO_MB;

            StopRecorders();
            return s;
        }

        public void Dispose()
        {
            _running = false;
            StopRecorders();
        }

        #endregion

        #region Private

        private void StartRecorders()
        {
            StopRecorders();
            _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 4);
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _vertices = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            _gfxMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            _gcAllocInFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        }

        private void StopRecorders()
        {
            _mainThread.Dispose();
            _batches.Dispose();
            _setPass.Dispose();
            _drawCalls.Dispose();
            _triangles.Dispose();
            _vertices.Dispose();
            _gfxMemory.Dispose();
            _gcAllocInFrame.Dispose();
        }

        private static void Accumulate(ref ProfilerRecorder recorder, ref double sum, ref int count)
        {
            if (!recorder.Valid || recorder.Count == 0) return;
            sum += recorder.LastValue;
            count++;
        }

        private static double Mean(double sum, int count) => count > 0 ? sum / count : double.NaN;

        private static void EnsureCapacity(ref float[] array, int capacity)
        {
            if (array.Length < capacity) array = new float[capacity];
        }

        #endregion
    }
}
