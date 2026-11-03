using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CardboardXR.Demos.Portal
{
    // Your hand on the Moon, from HandTracker's 21 joints (MediaPipe's order), while you're through
    // the portal and the hand is tracked. Two ways to show it:
    //   Glove: an EVA glove built over the joints, white fabric with dark rubber fingertips, a metal
    //   wrist ring and a ribbed sleeve, lit by the Moon's sun and casting its shadow on the soil.
    //   Real: the same shapes a little fuller, in CardboardXR/DepthMask, so the passthrough camera
    //   image shows through where the hand is: your own hand, with a thin edge of your room around the
    //   fingers, since the shapes come from the joints, not the hand's outline. The glove still casts
    //   the shadow, unseen.
    // Each fingertip presses into the soil (MoonSoil) as a small sphere, and throws dust as it moves.
    // Built in code; everything is in the Moon world's space. Logs [Moon].
    public class MoonHand : MonoBehaviour
    {
        public enum Look
        {
            Glove,
            Real,
        }

        // MediaPipe's connections drawn as rods, leaving out the palm's (the palm volume covers those).
        static readonly (int from, int to)[] k_Bones =
        {
            (0, 1), (1, 2), (2, 3), (3, 4),
            (5, 6), (6, 7), (7, 8),
            (9, 10), (10, 11), (11, 12),
            (13, 14), (14, 15), (15, 16),
            (17, 18), (18, 19), (19, 20),
            (5, 9), (9, 13), (13, 17),
        };
        static readonly int[] k_Tips = { 4, 8, 12, 16, 20 };
        const float k_GloveRadius = 0.0118f;
        const float k_TipRadius = 0.0105f;
        // The cutout is fuller than the joints, to cover the real finger's width.
        const float k_RealScale = 1.15f;
        const float k_RealTipRadius = 0.008f;

        sealed class Part
        {
            public Transform Transform;
            public int From;
            public int To;
            public float Radius;
        }

        sealed class Rig
        {
            public GameObject Root;
            public readonly List<Part> Rods = new();
            public readonly List<Part> Knobs = new();
            public Transform Palm;
            public Transform Wrist;
            public readonly List<MeshRenderer> Renderers = new();
        }

        MoonAssets m_Assets;
        MoonSoil m_Soil;
        MoonDust m_Dust;
        Rig m_Glove;
        Rig m_Real;
        Look m_Look = Look.Glove;
        bool m_Shown;
        readonly Vector3[] m_Joints = new Vector3[21];
        readonly Vector3[] m_LastTips = new Vector3[5];
        bool m_HasLastTips;
        static Mesh s_Torus;

        public Look Current => m_Look;

        public void Init(MoonAssets assets, MoonSoil soil, MoonDust dust)
        {
            m_Assets = assets;
            m_Soil = soil;
            m_Dust = dust;
            m_Glove = Build("Glove", false);
            m_Real = Build("Real Hand", true);
            SetShown(false);
        }

        public void SetLook(Look look)
        {
            m_Look = look;
            Debug.Log($"[Moon] Hand shown as {(look == Look.Glove ? "the suit glove" : "the real hand, through the camera")}");
            ApplyLook();
        }

        void ApplyLook()
        {
            m_Glove.Root.SetActive(m_Shown);
            m_Real.Root.SetActive(m_Shown && m_Look == Look.Real);
            var mode = m_Look == Look.Glove ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            foreach (var renderer in m_Glove.Renderers)
                renderer.shadowCastingMode = mode;
        }

        void SetShown(bool shown)
        {
            if (shown == m_Shown && m_Glove.Root.activeSelf == shown)
                return;
            m_Shown = shown;
            m_HasLastTips = false;
            ApplyLook();
        }

        Rig Build(string name, bool mask)
        {
            var rig = new Rig { Root = new GameObject(name) };
            rig.Root.transform.SetParent(transform, false);
            var scale = mask ? k_RealScale : 1f;
            Material Fabric() => mask ? m_Assets.Mask : m_Assets.Fabric;
            Material Rubber() => mask ? m_Assets.Mask : m_Assets.Rubber;
            foreach (var (from, to) in k_Bones)
            {
                var tip = System.Array.IndexOf(k_Tips, to) >= 0;
                var radius = k_GloveRadius * scale * (from <= 4 ? 1.1f : 1f) * (tip ? 0.92f : 1f);
                rig.Rods.Add(new Part
                {
                    Transform = Primitive(rig, PrimitiveType.Cylinder, $"Bone {from}-{to}", tip ? Rubber() : Fabric(), mask),
                    From = from, To = to, Radius = radius,
                });
            }
            for (var j = 1; j < 21; j++)
            {
                var tip = System.Array.IndexOf(k_Tips, j) >= 0;
                rig.Knobs.Add(new Part
                {
                    Transform = Primitive(rig, PrimitiveType.Sphere, $"Joint {j}", tip ? Rubber() : Fabric(), mask),
                    From = j, Radius = k_GloveRadius * scale * (tip ? 0.92f : 1.02f),
                });
            }
            rig.Palm = Primitive(rig, PrimitiveType.Sphere, "Palm", Fabric(), mask);
            rig.Wrist = new GameObject("Wrist").transform;
            rig.Wrist.SetParent(rig.Root.transform, false);
            if (mask)
            {
                // The forearm, so the camera shows your arm too, not a hand floating on the Moon.
                var arm = Primitive(rig, PrimitiveType.Cylinder, "Forearm", m_Assets.Mask, true, rig.Wrist);
                arm.localPosition = new Vector3(0f, 0f, -0.15f);
                arm.localRotation = Quaternion.Euler(90f, 0f, 0f);
                arm.localScale = new Vector3(0.075f, 0.15f, 0.075f);
                return rig;
            }
            var cuff = Primitive(rig, PrimitiveType.Cylinder, "Gauntlet", m_Assets.Fabric, false, rig.Wrist);
            cuff.localPosition = new Vector3(0f, 0f, -0.035f);
            cuff.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cuff.localScale = new Vector3(0.1f, 0.045f, 0.1f);
            Ring(rig, "Wrist Ring", m_Assets.Metal, -0.082f, 0.056f, 0.009f);
            Ring(rig, "Ring Band", m_Assets.Band, -0.093f, 0.058f, 0.004f);
            var sleeve = Primitive(rig, PrimitiveType.Cylinder, "Sleeve", m_Assets.Fabric, false, rig.Wrist);
            sleeve.localPosition = new Vector3(0f, 0f, -0.4f);
            sleeve.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sleeve.localScale = new Vector3(0.13f, 0.3f, 0.13f);
            for (var i = 0; i < 4; i++)
                Ring(rig, $"Convolute {i}", m_Assets.Fabric, -0.15f - i * 0.055f, 0.066f, 0.008f);
            return rig;
        }

        void Ring(Rig rig, string name, Material material, float z, float radius, float tube)
        {
            s_Torus ??= Torus(1f, 0.15f, 40, 10);
            var ring = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)).transform;
            ring.SetParent(rig.Wrist, false);
            ring.localPosition = new Vector3(0f, 0f, z);
            // The torus is in the xy plane, around z; its tube is 0.15 of its radius.
            ring.localScale = new Vector3(radius, radius, tube / 0.15f * 1f);
            ring.GetComponent<MeshFilter>().sharedMesh = s_Torus;
            var renderer = ring.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            rig.Renderers.Add(renderer);
        }

        Transform Primitive(Rig rig, PrimitiveType type, string name, Material material, bool mask, Transform parent = null)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent != null ? parent : rig.Root.transform, false);
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.receiveShadows = !mask;
            renderer.shadowCastingMode = mask ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (!mask)
                rig.Renderers.Add(renderer);
            return part.transform;
        }

        // Shows and poses the hand from the tracker's joints (world space), if it's tracked and inside.
        public void Follow(bool inside, Vector3[] worldJoints, bool tracked, Transform head)
        {
            if (!inside || !tracked || worldJoints == null || worldJoints.Length < 21)
            {
                SetShown(false);
                return;
            }
            SetShown(true);
            for (var i = 0; i < 21; i++)
                m_Joints[i] = transform.InverseTransformPoint(worldJoints[i]);
            Pose(m_Glove, transform.InverseTransformPoint(head.position));
            if (m_Look == Look.Real)
                Pose(m_Real, transform.InverseTransformPoint(head.position));
            TouchSoil();
        }

        void Pose(Rig rig, Vector3 head)
        {
            foreach (var rod in rig.Rods)
            {
                var a = m_Joints[rod.From];
                var b = m_Joints[rod.To];
                var along = b - a;
                rod.Transform.localPosition = (a + b) / 2f;
                rod.Transform.localRotation = along.sqrMagnitude > 1e-10f ? Quaternion.FromToRotation(Vector3.up, along) : Quaternion.identity;
                // A cylinder is 1 across and 2 long.
                rod.Transform.localScale = new Vector3(rod.Radius * 2f, along.magnitude / 2f, rod.Radius * 2f);
            }
            foreach (var knob in rig.Knobs)
            {
                knob.Transform.localPosition = m_Joints[knob.From];
                knob.Transform.localScale = Vector3.one * knob.Radius * 2f;
            }
            // The palm: from the wrist toward the middle knuckle, across from the little finger's
            // knuckle to the index finger's.
            var wrist = m_Joints[0];
            var forward = m_Joints[9] - wrist;
            var across = m_Joints[5] - m_Joints[17];
            if (forward.sqrMagnitude < 1e-8f || across.sqrMagnitude < 1e-8f)
                return;
            var up = Vector3.Cross(forward, across);
            var frame = Quaternion.LookRotation(forward, up);
            var length = forward.magnitude;
            var width = across.magnitude;
            var scale = rig == m_Real ? k_RealScale : 1f;
            rig.Palm.localPosition = (wrist + m_Joints[5] + m_Joints[9] + m_Joints[13] + m_Joints[17]) / 5f;
            rig.Palm.localRotation = frame;
            rig.Palm.localScale = new Vector3((width + 0.03f) * scale, 0.042f * scale, (length + 0.02f) * scale);
            // The sleeve runs back toward the elbow, about between the forearm's line and the shoulder.
            var shoulder = head + Vector3.down * 0.4f;
            var back = (-forward.normalized + (shoulder - wrist).normalized).normalized;
            rig.Wrist.localPosition = wrist;
            rig.Wrist.localRotation = Quaternion.LookRotation(-back, up);
        }

        // Each fingertip pressed into the soil, and dust where it moves through it.
        void TouchSoil()
        {
            var radius = m_Look == Look.Glove ? k_TipRadius : k_RealTipRadius;
            for (var t = 0; t < k_Tips.Length; t++)
            {
                var tip = m_Joints[k_Tips[t]];
                if (!m_Soil.Contains(tip.x, tip.z))
                    continue;
                var moved = m_Soil.Press(tip, radius);
                if (moved > 0f && m_HasLastTips)
                {
                    var motion = tip - m_LastTips[t];
                    var speed = motion.magnitude / Mathf.Max(Time.deltaTime, 1e-3f);
                    if (speed > 0.05f || Random.value < 0.05f)
                        m_Dust.Kick(transform.TransformPoint(new Vector3(tip.x, m_Soil.Grid.HeightAt(tip.x, tip.z), tip.z)),
                            transform.TransformDirection(new Vector3(motion.x, 0f, motion.z).normalized), Mathf.Clamp(speed, 0.2f, 1.5f),
                            Mathf.Clamp(Mathf.RoundToInt(speed * 6f), 1, 8));
                }
                m_LastTips[t] = tip;
            }
            m_HasLastTips = true;
        }

        // A torus around z, in the xy plane, of radius 1 with a tube of the given radius.
        static Mesh Torus(float radius, float tube, int segments, int sides)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (var s = 0; s <= segments; s++)
            {
                var a = s / (float)segments * Mathf.PI * 2f;
                var center = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
                for (var t = 0; t <= sides; t++)
                {
                    var b = t / (float)sides * Mathf.PI * 2f;
                    var normal = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a) * Mathf.Cos(b), Mathf.Sin(b));
                    vertices.Add(center + normal * tube);
                    normals.Add(normal);
                    if (s == 0 || t == 0)
                        continue;
                    var p = (s - 1) * (sides + 1) + t - 1;
                    var q = s * (sides + 1) + t - 1;
                    triangles.AddRange(new[] { p, p + 1, q, q, p + 1, q + 1 });
                }
            }
            var mesh = new Mesh { name = "Torus" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            var tangents = new Vector4[vertices.Count];
            for (var i = 0; i < tangents.Length; i++)
                tangents[i] = new Vector4(1f, 0f, 0f, 1f);
            mesh.tangents = tangents;
            mesh.SetUVs(0, new List<Vector2>(new Vector2[vertices.Count]));
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
