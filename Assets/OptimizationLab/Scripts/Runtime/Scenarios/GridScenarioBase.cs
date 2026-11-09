using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OptimizationLab.Scenarios
{
    /// <summary>
    /// Base cho các bài dùng lưới khối: spawn deterministic theo seed, camera khung theo lưới lớn nhất,
    /// đếm renderer trong frustum, kiểm material/MPB. Bài con chỉ quyết định variant đổi cái gì.
    /// </summary>
    public abstract class GridScenarioBase : BenchmarkScenario
    {
        #region Fields

        private const float ROTATE_SPEED_MIN = 20f;
        private const float ROTATE_SPEED_MAX = 90f;
        private const int MATERIAL_SEED_SALT = 0x2c1b3c6d;
        private const int SPEED_SEED_SALT = 0x5bd1e995;

        [SerializeField] protected Mesh mesh;
        [Tooltip("Material dùng chung (sharedMaterial). Mỗi object được gán một slot theo seed.")]
        [SerializeField] protected Material[] materials = System.Array.Empty<Material>();
        [SerializeField] protected float spacing = 1.2f;
        [SerializeField] protected float jitter = 0.15f;
        [SerializeField] protected float objectScale = 0.6f;
        [Tooltip("Camera được khung theo lưới LỚN NHẤT để mọi count dùng cùng một camera.")]
        [SerializeField] protected int maxObjectCount = 3000;

        private Transform _root;
        private Transform[] _transforms = System.Array.Empty<Transform>();
        private MeshRenderer[] _renderers = System.Array.Empty<MeshRenderer>();
        private Vector3[] _positions = System.Array.Empty<Vector3>();
        private float[] _yaws = System.Array.Empty<float>();
        private float[] _speeds = System.Array.Empty<float>();
        private int[] _slots = System.Array.Empty<int>();
        private readonly Plane[] _planes = new Plane[6];
        private int _count;
        private float _time;

        #endregion

        #region Initialize

        protected virtual void OnDisable() => RestoreGlobals();

        #endregion

        #region Hooks cho bài con

        /// <summary>Renderer có bật ở variant này không.</summary>
        protected virtual bool RenderersEnabled(BenchmarkVariant variant) => true;

        /// <summary>Object có xoay mỗi frame không.</summary>
        protected virtual void AfterSpawn(GameObject root, MeshRenderer[] renderers) { }

        protected virtual bool Animated(BenchmarkVariant variant) => false;

        /// <summary>Material cho object có slot <paramref name="slot"/> ở variant này.</summary>
        protected virtual Material MaterialFor(int slot, BenchmarkVariant variant) =>
            materials.Length > 0 ? materials[slot % materials.Length] : null;

        /// <summary>Material của object thứ <paramref name="index"/>. Mặc định theo slot; bài con có thể cho mỗi object một material riêng.</summary>
        protected virtual Material MaterialForObject(int index, int slot, BenchmarkVariant variant) => MaterialFor(slot, variant);

        /// <summary>Số slot material để chia object (mặc định = số material).</summary>
        protected virtual int SlotCount => Mathf.Max(1, materials.Length);

        /// <summary>Đổi setting toàn cục của variant (vd. SRP Batcher). Gọi trước khi spawn.</summary>
        protected virtual void ApplyVariantGlobals(BenchmarkVariant variant) { }

        /// <summary>Trả setting toàn cục về như cũ.</summary>
        protected virtual void RestoreGlobals() { }

        /// <summary>Kiểm thêm điều kiện riêng của bài.</summary>
        protected virtual BenchmarkStatus ValidateVariant(out string reason)
        {
            reason = "";
            return BenchmarkStatus.Valid;
        }

        /// <summary>Ghi thêm setting riêng của bài vào diff A/B.</summary>
        protected virtual void AddVariantSettings(BenchmarkVariant variant, IDictionary<string, string> into) { }

        #endregion

        #region Public

        public override int ExpectedVisibleRenderers => RenderersEnabled(CurrentVariant) ? _count : 0;

        public override void GetVariantSettings(BenchmarkVariant variant, IDictionary<string, string> into)
        {
            into["renderer_enabled"] = RenderersEnabled(variant) ? "true" : "false";
            into["animated"] = Animated(variant) ? "true" : "false";
            into["mesh"] = mesh != null ? $"{mesh.name} ({mesh.vertexCount} verts)" : "none";
            var names = new SortedSet<string>();
            var shaders = new SortedSet<string>();
            for (int s = 0; s < SlotCount; s++)
            {
                Material m = MaterialFor(s, variant);
                if (m == null) continue;
                names.Add(m.name);
                shaders.Add(m.shader.name + (m.shaderKeywords.Length > 0 ? "[" + string.Join(" ", m.shaderKeywords) + "]" : ""));
            }
            into["materials"] = $"{names.Count}: {string.Join(", ", names)}";
            into["shader_variants"] = $"{shaders.Count}: {string.Join(", ", shaders)}";
            into["material_mode"] = "shared materials (sharedMaterial), no MaterialPropertyBlock";
            into["shadow_casting"] = "Off";
            into["receive_shadows"] = "false";
            into["spacing"] = spacing.ToString("0.###");
            into["object_scale"] = objectScale.ToString("0.###");
            into["camera"] = "orthographic, fitted to max grid " + maxObjectCount;
            AddVariantSettings(variant, into);
        }

        public override void Setup(BenchmarkVariant variant, int seed)
        {
            CurrentVariant = variant;
            ApplyVariantGlobals(variant);
            _count = Mathf.Clamp(ObjectCount, 0, maxObjectCount);
            _time = 0f;
            EnsureArrays(_count);

            Bounds maxBounds = DeterministicSpawner.GridBounds(maxObjectCount, spacing, jitter, objectScale);
            BenchmarkCameraController.FitOrthographic(benchmarkCamera, maxBounds);

            DeterministicSpawner.FillGrid(_count, spacing, jitter, seed, _positions, _yaws);
            var speedRng = new System.Random(seed ^ SPEED_SEED_SALT);
            var slotRng = new System.Random(seed ^ MATERIAL_SEED_SALT);
            int slots = SlotCount;
            for (int i = 0; i < _count; i++)
            {
                _speeds[i] = Mathf.Lerp(ROTATE_SPEED_MIN, ROTATE_SPEED_MAX, (float)speedRng.NextDouble());
                _slots[i] = slotRng.Next(slots);
            }

            _root = new GameObject(Id + "_Objects").transform;
            _root.SetParent(transform, false);
            bool visible = RenderersEnabled(variant);
            Vector3 scale = Vector3.one * objectScale;
            for (int i = 0; i < _count; i++)
            {
                var go = new GameObject("Obj_" + i);
                Transform t = go.transform;
                t.SetParent(_root, false);
                t.localPosition = _positions[i];
                t.localRotation = Quaternion.Euler(0f, _yaws[i], 0f);
                t.localScale = scale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = MaterialForObject(i, _slots[i], variant); // KHÔNG dùng renderer.material (tạo instance)
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.enabled = visible;
                _transforms[i] = t;
                _renderers[i] = r;
            }
            AfterSpawn(_root.gameObject, _renderers);
            IsSetUp = true;
        }

        public override void WarmupTick(float deltaTime) => Animate(deltaTime);
        public override void MeasurementTick(float deltaTime) => Animate(deltaTime);

        public override void Teardown()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            for (int i = 0; i < _count; i++)
            {
                _transforms[i] = null;
                _renderers[i] = null;
            }
            _count = 0;
            IsSetUp = false;
            RestoreGlobals();
        }

        public override int CountVisibleRenderers()
        {
            if (benchmarkCamera == null) return 0;
            GeometryUtility.CalculateFrustumPlanes(benchmarkCamera, _planes);
            int visible = 0;
            for (int i = 0; i < _count; i++)
            {
                MeshRenderer r = _renderers[i];
                if (r != null && r.enabled && GeometryUtility.TestPlanesAABB(_planes, r.bounds)) visible++;
            }
            return visible;
        }

        public override BenchmarkStatus Validate(out string reason)
        {
            reason = "";
            if (mesh == null || materials == null || materials.Length == 0) { reason = "Thiếu mesh/material."; return BenchmarkStatus.Invalid; }
            if (benchmarkCamera == null) { reason = "Thiếu benchmark camera."; return BenchmarkStatus.Invalid; }
            if (!IsSetUp) { reason = "Scenario chưa setup."; return BenchmarkStatus.Invalid; }
            if (ObjectCount > maxObjectCount)
            {
                reason = $"ObjectCount {ObjectCount} > max {maxObjectCount} (camera không phủ).";
                return BenchmarkStatus.Invalid;
            }
            bool enabled = RenderersEnabled(CurrentVariant);
            for (int i = 0; i < _count; i++)
            {
                MeshRenderer r = _renderers[i];
                if (r == null) { reason = $"Renderer {i} bị huỷ."; return BenchmarkStatus.Invalid; }
                if (r.sharedMaterial != MaterialForObject(i, _slots[i], CurrentVariant)) { reason = $"Obj_{i} sai shared material."; return BenchmarkStatus.Invalid; }
                if (r.HasPropertyBlock()) { reason = $"Obj_{i} có MaterialPropertyBlock (phá SRP Batcher)."; return BenchmarkStatus.Invalid; }
                if (r.enabled != enabled) { reason = $"Obj_{i} sai trạng thái enabled."; return BenchmarkStatus.Invalid; }
            }
            return ValidateVariant(out reason);
        }

        #endregion

        #region Private

        private void EnsureArrays(int count)
        {
            if (_transforms.Length >= count) return;
            _transforms = new Transform[count];
            _renderers = new MeshRenderer[count];
            _positions = new Vector3[count];
            _yaws = new float[count];
            _speeds = new float[count];
            _slots = new int[count];
        }

        private void Animate(float deltaTime)
        {
            if (!IsSetUp || !Animated(CurrentVariant)) return;
            _time += deltaTime;
            for (int i = 0; i < _count; i++)
                _transforms[i].localRotation = Quaternion.Euler(0f, _yaws[i] + _time * _speeds[i], 0f);
        }

        #endregion
    }
}
