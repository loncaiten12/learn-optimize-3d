using UnityEngine;

namespace OptimizationLab
{
    /// <summary>
    /// Camera cố định cho benchmark. Khung hình chỉ phụ thuộc vào bounds LỚN NHẤT của test và aspect màn hình,
    /// không phụ thuộc variant → A và B nhìn cùng một vùng.
    /// </summary>
    public static class BenchmarkCameraController
    {
        private const float DEFAULT_PADDING = 1.08f;
        private static readonly Vector3[] Corners = new Vector3[8];

        /// <summary>Đặt camera orthographic sao cho toàn bộ <paramref name="bounds"/> nằm trong khung.</summary>
        public static void FitOrthographic(Camera cam, Bounds bounds, float padding = DEFAULT_PADDING)
        {
            if (cam == null) return;
            cam.orthographic = true;
            Transform t = cam.transform;
            float distance = bounds.extents.magnitude * 2f + 10f;
            t.position = bounds.center - t.forward * distance;

            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;
            int k = 0;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Corners[k++] = c + new Vector3(e.x * sx, e.y * sy, e.z * sz);

            float maxX = 0f, maxY = 0f, maxZ = 0f;
            Matrix4x4 toLocal = t.worldToLocalMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = toLocal.MultiplyPoint3x4(Corners[i]);
                maxX = Mathf.Max(maxX, Mathf.Abs(p.x));
                maxY = Mathf.Max(maxY, Mathf.Abs(p.y));
                maxZ = Mathf.Max(maxZ, p.z);
            }

            float aspect = Mathf.Max(0.01f, cam.aspect);
            cam.orthographicSize = Mathf.Max(maxY, maxX / aspect) * padding;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = maxZ + 10f;
        }
    }
}
