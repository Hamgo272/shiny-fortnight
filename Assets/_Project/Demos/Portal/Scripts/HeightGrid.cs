using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CardboardXR.Demos.Portal
{
    // A square grid of heights, drawn as meshes: one for the rows on the room's side of the doorway
    // (z < 0) and one for the world's side, since the portal shows the two halves separately. The
    // row at z = 0 is in both. Heights can change (the soil you press, see MoonSoil); Apply then
    // updates normals, colors and the meshes for the rows that changed. uv is the position in meters
    // across x and z, so materials tile by the meter. Albedo (about 1) goes into the vertex color, and
    // the soil darkens where it's pressed down from its first height.
    public sealed class HeightGrid
    {
        public readonly float X0;
        public readonly float Z0;
        public readonly float Size;
        public readonly int Segments;
        public readonly int N;
        public readonly float Step;
        public readonly float[] Heights;
        public readonly float[] Original;
        readonly float[] m_Albedo;
        readonly Vector3[] m_Normals;
        readonly List<Piece> m_Pieces = new();
        int m_DirtyI0 = int.MaxValue, m_DirtyJ0 = int.MaxValue, m_DirtyI1 = -1, m_DirtyJ1 = -1;

        sealed class Piece
        {
            public int RowStart;
            public int RowEnd;
            public Mesh Mesh;
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Color32[] Colors;
        }

        public HeightGrid(float x0, float z0, float size, int segments, Func<float, float, (float height, float albedo)> sample)
        {
            X0 = x0;
            Z0 = z0;
            Size = size;
            Segments = segments;
            N = segments + 1;
            Step = size / segments;
            Heights = new float[N * N];
            m_Albedo = new float[N * N];
            m_Normals = new Vector3[N * N];
            for (var j = 0; j < N; j++)
            {
                for (var i = 0; i < N; i++)
                {
                    var (height, albedo) = sample(X(i), Z(j));
                    Heights[j * N + i] = height;
                    m_Albedo[j * N + i] = albedo;
                }
            }
            Original = (float[])Heights.Clone();
            NormalsIn(0, 0, Segments, Segments);
        }

        public float X(int i) => X0 + i * Step;
        public float Z(int j) => Z0 + j * Step;

        // How far (x, z) is inside the grid's edge, in meters; negative outside.
        public float Inside(float x, float z) => Mathf.Min(Mathf.Min(x - X0, X0 + Size - x), Mathf.Min(z - Z0, Z0 + Size - z));

        // The height between grid points, as the mesh draws it; or the first height, before any change.
        public float HeightAt(float x, float z, bool original = false)
        {
            var h = original ? Original : Heights;
            var fx = Mathf.Clamp((x - X0) / Step, 0f, Segments - 1e-4f);
            var fz = Mathf.Clamp((z - Z0) / Step, 0f, Segments - 1e-4f);
            var i = (int)fx;
            var j = (int)fz;
            var tx = fx - i;
            var tz = fz - j;
            var a = h[j * N + i];
            var b = h[j * N + i + 1];
            var c = h[(j + 1) * N + i];
            var d = h[(j + 1) * N + i + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        // The two meshes, as children of back and front, with the material, casting shadows if asked.
        public void Build(string name, Transform back, Transform front, Material material, bool castShadows)
        {
            var split = Mathf.Clamp(Mathf.RoundToInt(-Z0 / Step), 0, Segments);
            if (split > 0)
                AddPiece($"{name} Back", back, 0, split, material, castShadows);
            if (split < Segments)
                AddPiece($"{name} Front", front, split, Segments, material, castShadows);
        }

        void AddPiece(string name, Transform parent, int rowStart, int rowEnd, Material material, bool castShadows)
        {
            var rows = rowEnd - rowStart + 1;
            var count = rows * N;
            var piece = new Piece
            {
                RowStart = rowStart,
                RowEnd = rowEnd,
                Vertices = new Vector3[count],
                Normals = new Vector3[count],
                Colors = new Color32[count],
            };
            var uvs = new Vector2[count];
            var tangents = new Vector4[count];
            for (var r = 0; r < rows; r++)
            {
                var j = rowStart + r;
                for (var i = 0; i < N; i++)
                {
                    var k = r * N + i;
                    uvs[k] = new Vector2(X(i), Z(j));
                    // uv runs along x and z, so the tangent is x flattened onto the surface, and the
                    // bitangent (normal x tangent, flipped by w) is z.
                    var n = m_Normals[j * N + i];
                    var t = (Vector3.right - n * n.x).normalized;
                    tangents[k] = new Vector4(t.x, t.y, t.z, -1f);
                }
            }
            var triangles = new int[(rows - 1) * Segments * 6];
            var w = 0;
            for (var r = 0; r < rows - 1; r++)
            {
                for (var i = 0; i < Segments; i++)
                {
                    var a = r * N + i;
                    var b = a + N;
                    triangles[w++] = a;
                    triangles[w++] = b;
                    triangles[w++] = a + 1;
                    triangles[w++] = a + 1;
                    triangles[w++] = b;
                    triangles[w++] = b + 1;
                }
            }
            piece.Mesh = new Mesh { name = name, indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            piece.Mesh.MarkDynamic();
            Fill(piece, rowStart, rowEnd);
            piece.Mesh.vertices = piece.Vertices;
            piece.Mesh.normals = piece.Normals;
            piece.Mesh.colors32 = piece.Colors;
            piece.Mesh.uv = uvs;
            piece.Mesh.tangents = tangents;
            piece.Mesh.triangles = triangles;
            piece.Mesh.RecalculateBounds();
            // Room for the soil to rise and sink without the mesh being culled early.
            var bounds = piece.Mesh.bounds;
            bounds.Expand(new Vector3(0f, 0.1f, 0f));
            piece.Mesh.bounds = bounds;
            m_Pieces.Add(piece);

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = piece.Mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        void Fill(Piece piece, int j0, int j1)
        {
            for (var j = Mathf.Max(j0, piece.RowStart); j <= Mathf.Min(j1, piece.RowEnd); j++)
            {
                for (var i = 0; i < N; i++)
                {
                    var g = j * N + i;
                    var k = (j - piece.RowStart) * N + i;
                    piece.Vertices[k] = new Vector3(X(i), Heights[g], Z(j));
                    piece.Normals[k] = m_Normals[g];
                    // Pressed regolith packs down and looks darker, as the Apollo boot prints did.
                    var packed = 1f - 0.14f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0008f, 0.006f, Original[g] - Heights[g]));
                    var a = Mathf.Clamp01(m_Albedo[g] * packed / 1.6f);
                    piece.Colors[k] = new Color(a, a * 0.98f, a * 0.95f, 1f);
                }
            }
        }

        void NormalsIn(int i0, int j0, int i1, int j1)
        {
            for (var j = Mathf.Max(0, j0); j <= Mathf.Min(N - 1, j1); j++)
            {
                for (var i = Mathf.Max(0, i0); i <= Mathf.Min(N - 1, i1); i++)
                {
                    var l = Heights[j * N + Mathf.Max(0, i - 1)];
                    var r = Heights[j * N + Mathf.Min(N - 1, i + 1)];
                    var d = Heights[Mathf.Max(0, j - 1) * N + i];
                    var u = Heights[Mathf.Min(N - 1, j + 1) * N + i];
                    m_Normals[j * N + i] = new Vector3(-(r - l) / (2f * Step), 1f, -(u - d) / (2f * Step)).normalized;
                }
            }
        }

        // Marks grid points i0..i1 by j0..j1 as changed, for the next Apply.
        public void Touch(int i0, int j0, int i1, int j1)
        {
            m_DirtyI0 = Mathf.Min(m_DirtyI0, i0);
            m_DirtyJ0 = Mathf.Min(m_DirtyJ0, j0);
            m_DirtyI1 = Mathf.Max(m_DirtyI1, i1);
            m_DirtyJ1 = Mathf.Max(m_DirtyJ1, j1);
        }

        // Sends what changed since the last Apply to the meshes.
        public void Apply()
        {
            if (m_DirtyI1 < 0)
                return;
            var i0 = Mathf.Max(0, m_DirtyI0 - 1);
            var j0 = Mathf.Max(0, m_DirtyJ0 - 1);
            var i1 = Mathf.Min(N - 1, m_DirtyI1 + 1);
            var j1 = Mathf.Min(N - 1, m_DirtyJ1 + 1);
            NormalsIn(i0, j0, i1, j1);
            foreach (var piece in m_Pieces)
            {
                if (j1 < piece.RowStart || j0 > piece.RowEnd)
                    continue;
                Fill(piece, j0, j1);
                piece.Mesh.vertices = piece.Vertices;
                piece.Mesh.normals = piece.Normals;
                piece.Mesh.colors32 = piece.Colors;
            }
            m_DirtyI0 = m_DirtyJ0 = int.MaxValue;
            m_DirtyI1 = m_DirtyJ1 = -1;
        }

        public void Restore()
        {
            Array.Copy(Original, Heights, Heights.Length);
            Touch(0, 0, Segments, Segments);
        }
    }
}
