using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CardboardXR.Demos.Portal
{
    // The Moon, behind the portal: the second of its worlds (PortalWorld.Kind). Built in code when the
    // portal is placed, into the world's front half (beyond the doorway) and back half (around the
    // player once through), like the night world:
    //   The ground in three nested grids (MoonTerrain's heights): a 940 m square at 5 m, the 80 m
    //   around the door at 25 cm, and a 3 m soil patch in front of the door at 1 cm that gives under
    //   a fingertip or a boot (MoonSoil). Each coarser grid dips a little under the finer one, so they
    //   never fight, and the patch's edge follows the ground around it.
    //   Scanned moon rocks, a black sky with stars, and the Earth, 1.9 degrees wide, low over the
    //   horizon, lit by the sun so it shows a phase.
    //   One low sun, 11 degrees up: the main light while the Moon exists (RenderSettings.sun), with
    //   shadows out to k_ShadowDistance at a higher resolution, put back as they were on leaving.
    // Through the doorway, your hand is the suit glove or your real hand (MoonHand), and you leave boot
    // prints in the soil as you walk. Heights are worked out on a thread so placing the portal
    // doesn't stall: the portal rises out of the floor meanwhile.
    //   Lite (set before Build) is the Moon as a backdrop, for watching a window on it (the Apps demo's
    //   Moon cinema): the sky, the Earth, the ground and its rocks, but no sun shadows (the render
    //   settings are left alone), a coarser near ground, and no soil patch, dust, boot prints or hand,
    //   so it costs little next to the app's video. Logs [Moon].
    public class MoonWorld : MonoBehaviour
    {
        // The portal's mask box for the Moon: around the whole sky, out to the horizon.
        public const float k_BoxHalfWidth = 520f;
        public const float k_BoxHeight = 470f;
        public const float k_BoxDepth = 530f;
        public const float k_BoxBottom = -60f;
        const float k_SkyRadius = 490f;
        const float k_StarRadius = 470f;
        const float k_EarthDistance = 450f;
        const float k_EarthDegrees = 1.9f;
        const float k_ShadowDistance = 16f;
        const int k_ShadowResolution = 2048;
        const float k_SunIntensity = 2.6f;
        // Toward the sun: ahead and to the right of the door, 11 degrees up.
        static readonly Vector3 k_SunDirection = new Vector3(0.82f, 0.19f, 0.54f).normalized;
        static readonly Vector3 k_EarthDirection = new Vector3(-0.38f, 0.42f, 0.83f).normalized;
        static readonly int k_ShadowCatcherStrength = Shader.PropertyToID("_ShadowCatcherStrength");
        // A boot print every this far walked across the soil, alternating feet.
        const float k_Stride = 0.62f;
        const float k_FootOffset = 0.11f;

        MoonAssets m_Assets;
        Transform m_Front;
        Transform m_Back;
        HeightGrid m_Far;
        HeightGrid m_Near;
        MoonSoil m_Soil;
        MoonHand m_Hand;
        MoonDust m_Dust;
        Light m_Sun;
        Light m_PreviousSun;
        float m_PreviousShadowDistance;
        int m_PreviousShadowResolution;
        float m_PreviousCatcher;
        bool m_ChangedRendering;
        bool m_Inside;
        bool m_Ready;
        Vector3 m_LastStep;
        bool m_HasStep;
        bool m_LeftFoot;
        MoonHand.Look m_Look = MoonHand.Look.Glove;

        // A backdrop only: see the class comment. Set before Build.
        public bool Lite { get; set; }

        public MoonHand.Look HandLook
        {
            get => m_Look;
            set
            {
                m_Look = value;
                if (m_Hand != null)
                    m_Hand.SetLook(value);
            }
        }

        public void SetInside(bool inside)
        {
            m_Inside = inside;
            m_HasStep = false;
        }

        public void Build(MoonAssets assets, Transform front, Transform back)
        {
            m_Assets = assets;
            m_Front = front;
            m_Back = back;
            BuildSun();
            BuildSky();
            StartCoroutine(BuildGround());
        }

        IEnumerator BuildGround()
        {
            var started = Time.realtimeSinceStartup;
            var task = Task.Run(() =>
            {
                var lite = Lite;
                var near = new HeightGrid(-40f, -40f, 80f, lite ? 120 : 320, (x, z) =>
                {
                    var h = MoonTerrain.Height(x, z, 0.3f, out var albedo);
                    return (h - 0.04f * Mathf.Clamp01(Inside(x, z, -1.5f, -0.5f, 3f) / 0.25f), albedo);
                });
                var far = new HeightGrid(-470f, -470f, 940f, 188, (x, z) =>
                {
                    var h = MoonTerrain.Height(x, z, 12f, out var albedo);
                    return (h - 0.25f * Mathf.Clamp01(Inside(x, z, -40f, -40f, 80f) / 5f), albedo);
                });
                // The patch's edge follows the near grid as drawn there (exactly, along the edge), and
                // goes over to the full detail, every crater and millimeter relief, within 15 cm.
                if (lite)
                    return (near, far, null);
                var nearRaw = new HeightGrid(-2f, -1f, 4f, 16, (x, z) => (MoonTerrain.Height(x, z, 0.3f), 1f));
                var patch = new HeightGrid(-1.5f, -0.5f, 3f, 300, (x, z) =>
                {
                    var full = MoonTerrain.Height(x, z, 0f, out var albedo) + Noise.Fbm(x / 0.07f, z / 0.07f, 3, 61) * 0.0035f;
                    var e = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.15f, Inside(x, z, -1.5f, -0.5f, 3f)));
                    return (Mathf.Lerp(nearRaw.HeightAt(x, z), full, e), albedo);
                });
                return (near, far, patch);
            });
            while (!task.IsCompleted)
                yield return null;
            if (task.IsFaulted)
            {
                Debug.LogError($"[Moon] Couldn't make the ground: {task.Exception?.GetBaseException()}");
                yield break;
            }
            var (nearGrid, farGrid, patchGrid) = task.Result;
            m_Near = nearGrid;
            m_Far = farGrid;
            m_Far.Build("Far Ground", m_Back, m_Front, m_Assets.FarGround, false);
            m_Near.Build("Near Ground", m_Back, m_Front, m_Assets.Ground, true);
            if (Lite)
            {
                BuildRocks();
                Debug.Log($"[Moon] Backdrop made in {Time.realtimeSinceStartup - started:F1} s");
                yield break;
            }
            patchGrid.Build("Soil", m_Back, m_Front, m_Assets.Ground, true);
            m_Soil = new MoonSoil(patchGrid);
            BuildRocks();

            m_Dust = new GameObject("Dust").AddComponent<MoonDust>();
            m_Dust.transform.SetParent(transform, false);
            m_Dust.Init(m_Assets.Dust);
            m_Hand = new GameObject("Hand").AddComponent<MoonHand>();
            m_Hand.transform.SetParent(transform, false);
            m_Hand.Init(m_Assets, m_Soil, m_Dust);
            m_Hand.SetLook(m_Look);
            m_Ready = true;
            Debug.Log($"[Moon] Ground made in {Time.realtimeSinceStartup - started:F1} s");
        }

        static float Inside(float x, float z, float x0, float z0, float size) =>
            Mathf.Min(Mathf.Min(x - x0, x0 + size - x), Mathf.Min(z - z0, z0 + size - z));

        void BuildSun()
        {
            m_Sun = new GameObject("Sun").AddComponent<Light>();
            m_Sun.transform.SetParent(transform, false);
            m_Sun.transform.localRotation = Quaternion.LookRotation(-k_SunDirection);
            m_Sun.type = LightType.Directional;
            m_Sun.color = new Color(1f, 0.97f, 0.92f);
            m_Sun.intensity = k_SunIntensity;
            m_Sun.shadows = Lite ? LightShadows.None : LightShadows.Hard;
            m_Sun.shadowStrength = 1f;
            m_Sun.shadowBias = 0.02f;
            m_Sun.shadowNormalBias = 0.3f;
            m_Sun.renderMode = LightRenderMode.ForcePixel;

            m_PreviousSun = RenderSettings.sun;
            RenderSettings.sun = m_Sun;
            m_PreviousCatcher = Shader.GetGlobalFloat(k_ShadowCatcherStrength);
            if (!Lite && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                m_PreviousShadowDistance = urp.shadowDistance;
                m_PreviousShadowResolution = urp.mainLightShadowmapResolution;
                urp.shadowDistance = k_ShadowDistance;
                urp.mainLightShadowmapResolution = k_ShadowResolution;
                m_ChangedRendering = true;
            }
        }

        void OnDestroy()
        {
            if (RenderSettings.sun == m_Sun)
                RenderSettings.sun = m_PreviousSun;
            Shader.SetGlobalFloat(k_ShadowCatcherStrength, m_PreviousCatcher);
            if (m_ChangedRendering && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = m_PreviousShadowDistance;
                urp.mainLightShadowmapResolution = m_PreviousShadowResolution;
            }
        }

        void BuildSky()
        {
            Dome("Sky Back", m_Back, 90f, 270f);
            Dome("Sky Front", m_Front, -90f, 90f);
            var random = new System.Random(5);
            Stars("Stars Back", m_Back, random, false);
            random = new System.Random(5);
            Stars("Stars Front", m_Front, random, true);

            var earth = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            earth.name = "Earth";
            Destroy(earth.GetComponent<Collider>());
            earth.transform.SetParent(m_Front, false);
            earth.transform.localPosition = k_EarthDirection * k_EarthDistance;
            earth.transform.localScale = Vector3.one * (2f * k_EarthDistance * Mathf.Tan(k_EarthDegrees / 2f * Mathf.Deg2Rad));
            // Africa and Europe toward the Moon, the poles roughly up.
            earth.transform.localRotation = Quaternion.Euler(-18f, 200f, 12f);
            var renderer = earth.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_Assets.Earth;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // Half a sphere of black sky, from longitude from to to (degrees, 0 along +z), pole to pole.
        void Dome(string name, Transform parent, float from, float to)
        {
            const int rings = 24;
            const int segments = 32;
            var vertices = new Vector3[(rings + 1) * (segments + 1)];
            var triangles = new int[rings * segments * 6];
            var w = 0;
            for (var r = 0; r <= rings; r++)
            {
                var latitude = Mathf.Lerp(-90f, 90f, r / (float)rings) * Mathf.Deg2Rad;
                for (var s = 0; s <= segments; s++)
                {
                    var longitude = Mathf.Lerp(from, to, s / (float)segments) * Mathf.Deg2Rad;
                    vertices[r * (segments + 1) + s] = new Vector3(Mathf.Cos(latitude) * Mathf.Sin(longitude), Mathf.Sin(latitude),
                        Mathf.Cos(latitude) * Mathf.Cos(longitude)) * k_SkyRadius;
                    if (r == 0 || s == 0)
                        continue;
                    var a = (r - 1) * (segments + 1) + s - 1;
                    var b = r * (segments + 1) + s - 1;
                    triangles[w++] = a;
                    triangles[w++] = b;
                    triangles[w++] = a + 1;
                    triangles[w++] = a + 1;
                    triangles[w++] = b;
                    triangles[w++] = b + 1;
                }
            }
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            mesh.RecalculateBounds();
            AddMesh(name, parent, mesh, m_Assets.Sky, false);
        }

        // Stars as small quads facing the center, on the half of the sky where z is >= 0 (front) or < 0.
        // The same random sequence makes both halves, each keeping its own side.
        void Stars(string name, Transform parent, System.Random random, bool front)
        {
            const int count = 5000;
            var vertices = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();
            for (var i = 0; i < count; i++)
            {
                var u = (float)random.NextDouble() * 2f - 1f;
                var a = (float)random.NextDouble() * Mathf.PI * 2f;
                var bright = Mathf.Pow((float)random.NextDouble(), 6f);
                var s = Mathf.Sqrt(1f - u * u);
                var direction = new Vector3(s * Mathf.Cos(a), u, s * Mathf.Sin(a));
                if (direction.z >= 0f != front)
                    continue;
                // About 1.5 to 6 arcminutes: a pixel or two at the viewer's resolution, the brightest more.
                var size = k_StarRadius * Mathf.Deg2Rad * Mathf.Lerp(0.03f, 0.11f, bright);
                var center = direction * k_StarRadius;
                var right = Vector3.Cross(direction, Mathf.Abs(direction.y) < 0.99f ? Vector3.up : Vector3.right).normalized * size;
                var up = Vector3.Cross(right, direction).normalized * size;
                var b = vertices.Count;
                vertices.Add(center - right - up);
                vertices.Add(center + right - up);
                vertices.Add(center + right + up);
                vertices.Add(center - right + up);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 1f));
                triangles.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            AddMesh(name, parent, mesh, m_Assets.Stars, false);
        }

        void BuildRocks()
        {
            if (m_Assets.RockMeshes == null || m_Assets.RockMeshes.Length == 0)
                return;
            var random = new System.Random(77);
            float Next() => (float)random.NextDouble();
            void Place(float x, float z, float size)
            {
                var index = random.Next(m_Assets.RockMeshes.Length);
                var mesh = m_Assets.RockMeshes[index];
                var extent = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z);
                var scale = size / Mathf.Max(extent, 1e-3f);
                var parent = z >= 0f ? m_Front : m_Back;
                var rock = AddMesh(mesh.name, parent, mesh, m_Assets.RockMaterials[Mathf.Min(index, m_Assets.RockMaterials.Length - 1)], true);
                var y = MoonTerrain.Height(x, z, 0.3f) - mesh.bounds.min.y * scale - size * 0.12f;
                rock.localPosition = new Vector3(x, y, z);
                rock.localRotation = Quaternion.Euler(0f, Next() * 360f, (Next() - 0.5f) * 14f);
                rock.localScale = new Vector3(scale * (0.8f + Next() * 0.4f), scale * (0.7f + Next() * 0.4f), scale * (0.8f + Next() * 0.4f));
            }
            for (var i = 0; i < 60; i++)
            {
                var a = Next() * Mathf.PI * 2f;
                var d = 2.5f + Mathf.Pow(Next(), 0.8f) * 32f;
                var x = Mathf.Sin(a) * d;
                var z = Mathf.Cos(a) * d;
                if (MoonTerrain.KeepClear(x, z, 0.4f))
                    continue;
                Place(x, z, 0.06f + Mathf.Pow(Next(), 5f) * 0.9f);
            }
            Place(-4.2f, 7.5f, 1.4f);
            Place(11f, 16f, 2.2f);
            Place(-3f, 4.9f, 0.32f);
            Place(2.9f, 4.7f, 0.2f);
            Place(-7f, -6f, 1.1f);
        }

        Transform AddMesh(string name, Transform parent, Mesh mesh, Material material, bool shadows)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = shadows;
            return go.transform;
        }

        void LateUpdate()
        {
            // Detected room surfaces catch the main light's shadows on the camera image; with the
            // Moon's sun as the main light, the Moon's ground would darken the real floor.
            Shader.SetGlobalFloat(k_ShadowCatcherStrength, 0f);
            if (!m_Ready)
                return;
            var core = CoreServices.Instance;
            if (core == null)
                return;
            var tracker = core.HandTracker;
            m_Hand.Follow(m_Inside, tracker != null ? tracker.Joints : null,
                tracker != null && tracker.isActiveAndEnabled && tracker.IsTracking, core.Head);
            Steps(core.Head);
            m_Soil.Grid.Apply();
        }

        // Boot prints under you as you walk across the soil, a stride apart, left and right of your path.
        void Steps(Transform head)
        {
            if (!m_Inside)
                return;
            var here = transform.InverseTransformPoint(head.position);
            here.y = 0f;
            if (!m_HasStep)
            {
                m_LastStep = here;
                m_HasStep = true;
                return;
            }
            var walked = here - m_LastStep;
            if (walked.magnitude < k_Stride)
                return;
            var direction = walked.normalized;
            var side = Vector3.Cross(Vector3.up, direction) * (m_LeftFoot ? -k_FootOffset : k_FootOffset);
            var foot = here + side;
            if (m_Soil.Contains(foot.x, foot.z))
            {
                m_Soil.Boot(foot.x, foot.z, Mathf.Atan2(direction.x, direction.z));
                m_Dust.Kick(transform.TransformPoint(new Vector3(foot.x, m_Soil.Grid.HeightAt(foot.x, foot.z), foot.z)),
                    transform.TransformDirection(direction), 0.5f, 6);
            }
            m_LeftFoot = !m_LeftFoot;
            m_LastStep = here;
        }

        // Smooths the soil back to how it was.
        public void SmoothSoil()
        {
            if (m_Soil == null)
                return;
            m_Soil.Grid.Restore();
            Debug.Log("[Moon] Soil smoothed");
        }
    }
}
