using UnityEngine;

namespace OptimizationLab
{
    /// <summary>
    /// Sinh mesh cầu UV với số tam giác cho trước. Cùng silhouette ở mọi độ phân giải;
    /// <c>hardEdges</c> tách đỉnh từng tam giác (normal phẳng) để so số vertex với cùng số tam giác.
    /// </summary>
    public static class ProceduralSphereMesh
    {
        /// <summary>Số tam giác của cầu UV: 2 · segments · (rings − 1).</summary>
        public static int TriangleCount(int segments, int rings) => 2 * segments * (rings - 1);

        public static Mesh Create(string name, int segments, int rings, bool hardEdges)
        {
            var mesh = new Mesh { name = name };
            if (hardEdges) BuildHard(mesh, segments, rings);
            else BuildSmooth(mesh, segments, rings);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Point(int s, int r, int segments, int rings)
        {
            float theta = Mathf.PI * r / rings;
            float phi = 2f * Mathf.PI * (s % segments) / segments;
            return new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi)) * 0.5f;
        }

        private static void BuildSmooth(Mesh mesh, int segments, int rings)
        {
            int cols = segments + 1; // cột cuối lặp để UV khép kín (seam)
            int vertCount = cols * (rings + 1);
            var verts = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s <= segments; s++)
                {
                    int i = r * cols + s;
                    verts[i] = Point(s, r, segments, rings);
                    normals[i] = verts[i].normalized;
                    uvs[i] = new Vector2((float)s / segments, 1f - (float)r / rings);
                }
            var tris = new System.Collections.Generic.List<int>(TriangleCount(segments, rings) * 3);
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * cols + s, b = a + 1, c = a + cols, d = c + 1;
                    if (r != 0) { tris.Add(a); tris.Add(b); tris.Add(c); }
                    if (r != rings - 1) { tris.Add(b); tris.Add(d); tris.Add(c); }
                }
            Assign(mesh, verts, normals, uvs, tris.ToArray());
        }

        private static void BuildHard(Mesh mesh, int segments, int rings)
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();
            void Tri(Vector3 p0, Vector2 t0, Vector3 p1, Vector2 t1, Vector3 p2, Vector2 t2)
            {
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                int i = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(p2);
                normals.Add(n); normals.Add(n); normals.Add(n);
                uvs.Add(t0); uvs.Add(t1); uvs.Add(t2);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    Vector3 a = Point(s, r, segments, rings), b = Point(s + 1, r, segments, rings);
                    Vector3 c = Point(s, r + 1, segments, rings), d = Point(s + 1, r + 1, segments, rings);
                    Vector2 ta = new Vector2((float)s / segments, 1f - (float)r / rings), tb = new Vector2((float)(s + 1) / segments, ta.y);
                    Vector2 tc = new Vector2(ta.x, 1f - (float)(r + 1) / rings), td = new Vector2(tb.x, tc.y);
                    if (r != 0) Tri(a, ta, b, tb, c, tc);
                    if (r != rings - 1) Tri(b, tb, d, td, c, tc);
                }
            Assign(mesh, verts.ToArray(), normals.ToArray(), uvs.ToArray(), tris.ToArray());
        }

        private static void Assign(Mesh mesh, Vector3[] verts, Vector3[] normals, Vector2[] uvs, int[] tris)
        {
            if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
        }
    }
}
