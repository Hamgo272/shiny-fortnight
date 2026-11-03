using UnityEngine;

namespace CardboardXR.Demos.Portal
{
    // The patch of ground by the doorway that gives under a fingertip or a boot (a HeightGrid at
    // about a centimeter). A fingertip is a sphere pressed in: the ground under it drops to the
    // sphere's surface, and part of what it pushed aside rises in a low ring around it, as regolith
    // does, capped a few millimeters above where the ground began. A boot print is a sole with
    // cross-ribbed tread pressed a centimeter or so in. Near the patch's edge both fade out, since
    // the edge has to meet the coarser ground around it. Pure (no Unity objects), for testing.
    public sealed class MoonSoil
    {
        // How much of what a fingertip pushes aside rises around it, and how high it can rise.
        const float k_Lift = 0.45f;
        const float k_MaxRise = 0.007f;
        // The edge fades over this far, in meters.
        const float k_EdgeFade = 0.15f;

        public readonly HeightGrid Grid;
        readonly float[] m_Edge;

        public MoonSoil(HeightGrid grid)
        {
            Grid = grid;
            m_Edge = new float[grid.N * grid.N];
            for (var j = 0; j < grid.N; j++)
            {
                for (var i = 0; i < grid.N; i++)
                    m_Edge[j * grid.N + i] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.02f + k_EdgeFade, grid.Inside(grid.X(i), grid.Z(j))));
            }
        }

        public bool Contains(float x, float z) => Grid.Inside(x, z) > 0f;

        // Presses a sphere at center (in the grid's space) into the ground; returns how much ground it
        // moved, in meter-heights summed over grid points (0 if it didn't touch).
        public float Press(Vector3 center, float radius)
        {
            var g = Grid;
            var ci = Mathf.RoundToInt((center.x - g.X0) / g.Step);
            var cj = Mathf.RoundToInt((center.z - g.Z0) / g.Step);
            var reach = Mathf.CeilToInt(radius * 2.4f / g.Step);
            if (ci + reach < 0 || cj + reach < 0 || ci - reach >= g.N || cj - reach >= g.N)
                return 0f;
            var moved = 0f;
            var ringCount = 0;
            for (var j = cj - reach; j <= cj + reach; j++)
            {
                for (var i = ci - reach; i <= ci + reach; i++)
                {
                    if (i < 0 || j < 0 || i >= g.N || j >= g.N)
                        continue;
                    var k = j * g.N + i;
                    var dx = g.X(i) - center.x;
                    var dz = g.Z(j) - center.z;
                    var d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < radius)
                    {
                        var bottom = center.y - Mathf.Sqrt(radius * radius - d * d);
                        if (g.Heights[k] > bottom)
                        {
                            var target = Mathf.Lerp(g.Heights[k], bottom, m_Edge[k]);
                            moved += g.Heights[k] - target;
                            g.Heights[k] = target;
                        }
                    }
                    else if (d < radius * 2.4f)
                    {
                        ringCount++;
                    }
                }
            }
            if (moved <= 0f)
                return 0f;
            var lift = moved * k_Lift / Mathf.Max(1, ringCount);
            for (var j = cj - reach; j <= cj + reach; j++)
            {
                for (var i = ci - reach; i <= ci + reach; i++)
                {
                    if (i < 0 || j < 0 || i >= g.N || j >= g.N)
                        continue;
                    var dx = g.X(i) - center.x;
                    var dz = g.Z(j) - center.z;
                    var d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < radius || d >= radius * 2.4f)
                        continue;
                    var k = j * g.N + i;
                    g.Heights[k] = Mathf.Min(g.Original[k] + k_MaxRise, g.Heights[k] + lift * m_Edge[k]);
                }
            }
            g.Touch(ci - reach, cj - reach, ci + reach, cj + reach);
            return moved;
        }

        // A boot print centered at (x, z), its toe toward yaw (radians, 0 along +z).
        public void Boot(float x, float z, float yaw)
        {
            var g = Grid;
            const float halfLength = 0.165f;
            const float halfWidth = 0.068f;
            var cos = Mathf.Cos(yaw);
            var sin = Mathf.Sin(yaw);
            var ci = Mathf.RoundToInt((x - g.X0) / g.Step);
            var cj = Mathf.RoundToInt((z - g.Z0) / g.Step);
            var reach = Mathf.CeilToInt(0.22f / g.Step);
            for (var j = cj - reach; j <= cj + reach; j++)
            {
                for (var i = ci - reach; i <= ci + reach; i++)
                {
                    if (i < 0 || j < 0 || i >= g.N || j >= g.N)
                        continue;
                    var k = j * g.N + i;
                    var dx = g.X(i) - x;
                    var dz = g.Z(j) - z;
                    // Along the sole toward the toe, and across it.
                    var u = dx * sin + dz * cos;
                    var v = dx * cos - dz * sin;
                    var width = halfWidth * (u < 0f ? 0.88f : 1f);
                    var s = Mathf.Pow(Mathf.Abs(u / halfLength), 4f) + Mathf.Pow(Mathf.Abs(v / width), 4f);
                    if (s < 1f)
                    {
                        var rib = Mathf.Sin((u + Mathf.Abs(v) * 0.35f) / 0.0105f * Mathf.PI) > 0.25f ? 0.0032f : 0f;
                        var inner = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 1f, s));
                        var depth = (0.011f + 0.003f * Mathf.Clamp01(-u / halfLength)) * inner - rib * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 1f, s)));
                        g.Heights[k] = Mathf.Min(g.Heights[k], g.Original[k] - depth * m_Edge[k]);
                    }
                    else if (s < 1.6f)
                    {
                        var rim = 0.0035f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 1.6f, s)));
                        g.Heights[k] = Mathf.Max(g.Heights[k], g.Original[k] + rim * m_Edge[k]);
                    }
                }
            }
            g.Touch(ci - reach, cj - reach, ci + reach, cj + reach);
        }
    }
}
