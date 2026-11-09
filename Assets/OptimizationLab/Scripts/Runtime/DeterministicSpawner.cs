using UnityEngine;

namespace OptimizationLab
{
    /// <summary>
    /// Sinh vị trí theo lưới với seed cố định. Cùng (count, spacing, jitter, seed) → cùng kết quả
    /// trên mọi máy, nên A và B luôn có cùng phân bố transform.
    /// </summary>
    public static class DeterministicSpawner
    {
        /// <summary>Số cột của lưới vuông chứa <paramref name="count"/> phần tử.</summary>
        public static int GridSide(int count) => Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, count))));

        /// <summary>Điền vị trí + góc yaw (độ) cho <paramref name="count"/> object, lưới căn giữa gốc toạ độ.</summary>
        public static void FillGrid(int count, float spacing, float jitter, int seed, Vector3[] positions, float[] yaws)
        {
            int side = GridSide(count);
            float half = (side - 1) * spacing * 0.5f;
            // System.Random có seed: deterministic, không đụng UnityEngine.Random toàn cục.
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                int x = i % side;
                int z = i / side;
                float jx = (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                float jz = (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                positions[i] = new Vector3(x * spacing - half + jx, 0f, z * spacing - half + jz);
                yaws[i] = (float)(rng.NextDouble() * 360.0);
            }
        }

        /// <summary>Bounds bao lưới lớn nhất (dùng để cố định camera cho mọi object count).</summary>
        public static Bounds GridBounds(int count, float spacing, float jitter, float objectSize)
        {
            int side = GridSide(count);
            float extent = (side - 1) * spacing * 0.5f + jitter + objectSize;
            return new Bounds(Vector3.zero, new Vector3(extent * 2f, objectSize * 2f, extent * 2f));
        }
    }
}
