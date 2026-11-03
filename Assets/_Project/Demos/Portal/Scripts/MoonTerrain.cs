using System.Collections.Generic;
using UnityEngine;

namespace CardboardXR.Demos.Portal
{
    // The Moon world's ground, as a height and an albedo at any point, in the world's space: x across
    // the doorway, z from it into the world, y up from the floor the portal stands on. Rolling ground
    // from value noise, and craters of every size: a bowl inside, a raised rim, ejecta falling off as
    // r^-3 outside, and around the fresh ones a bright blanket of rays. The ground near the doorway is
    // flattened, and kept clear of craters, so the player steps through onto level ground at floor
    // height. Deterministic: the same craters every time. Pure, for testing.
    public static class MoonTerrain
    {
        // Kept clear of craters and rocks: around the doorway, where the player walks in and back.
        public static bool KeepClear(float x, float z, float margin) =>
            Mathf.Abs(x) < 2.6f + margin && z > -4f - margin && z < 4.5f + margin;

        sealed class Crater
        {
            public float X, Z, R, Depth, Rim, Fresh;
            public int Seed;
        }

        const float k_Bucket = 8f;
        static readonly Dictionary<long, List<Crater>> s_Buckets = new();
        static float s_Origin;
        static bool s_Made;

        // The height at (x, z), leaving out craters smaller than minRadius (a coarse mesh can't show
        // them, and sampling one at a vertex leaves a spike), and the albedo there, about 1.
        public static float Height(float x, float z, float minRadius, out float albedo)
        {
            Make();
            return Raw(x, z, minRadius, out albedo) - s_Origin;
        }

        public static float Height(float x, float z, float minRadius) => Height(x, z, minRadius, out _);

        static float Raw(float x, float z, float minRadius, out float albedo)
        {
            var h = Noise.Fbm(x / 70f, z / 70f, 4, 11) * 2.6f + Noise.Fbm(x / 11f, z / 11f, 3, 23) * 0.28f;
            var d = Mathf.Sqrt(x * x + (z - 0.5f) * (z - 0.5f));
            h *= 0.12f + 0.88f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 16f, d));
            albedo = 1f + 0.07f * Noise.Fbm(x / 5f, z / 5f, 3, 41) + 0.045f * Noise.Fbm(x / 0.9f, z / 0.9f, 2, 43);
            if (!s_Buckets.TryGetValue(Key(Mathf.FloorToInt(x / k_Bucket), Mathf.FloorToInt(z / k_Bucket)), out var list))
                return h;
            foreach (var c in list)
            {
                if (c.R < minRadius)
                    continue;
                var dx = x - c.X;
                var dz = z - c.Z;
                var r = Mathf.Sqrt(dx * dx + dz * dz) / c.R;
                if (r > 3f)
                    continue;
                if (r < 1f)
                {
                    h += (c.Depth + c.Rim) * r * r - c.Depth;
                    albedo -= 0.035f * (1f - r);
                }
                else
                {
                    h += c.Rim * Mathf.Pow(r, -3f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.2f, 3f, r)));
                }
                if (c.Fresh > 0f)
                {
                    var angle = Mathf.Atan2(dz, dx);
                    var ray = 0.45f + 0.55f * Mathf.Pow(Noise.Value(angle * 6f + 20f, 0.5f, c.Seed), 2.2f);
                    albedo += c.Fresh * 0.26f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 2.8f, r))) * ray;
                }
            }
            return h;
        }

        static long Key(int bx, int bz) => ((long)bx << 32) ^ (uint)bz;

        static readonly object s_Lock = new();

        static void Make()
        {
            if (s_Made)
                return;
            lock (s_Lock)
            {
                if (s_Made)
                    return;
                MakeCraters();
                s_Made = true;
            }
        }

        static void MakeCraters()
        {
            var random = new System.Random(42);
            float Next() => (float)random.NextDouble();
            for (var i = 0; i < 14; i++)
            {
                var a = Next() * Mathf.PI * 2f;
                var d = 45f + Next() * 380f;
                Add(Mathf.Sin(a) * d, Mathf.Cos(a) * d, 12f + Next() * 45f, 0.12f + Next() * 0.08f, Next() < 0.2f ? 0.6f : 0f);
            }
            for (var i = 0; i < 60; i++)
            {
                var a = Next() * Mathf.PI * 2f;
                var d = 8f + Next() * 60f;
                var radius = 1.8f + Next() * 7f;
                var x = Mathf.Sin(a) * d;
                var z = Mathf.Cos(a) * d;
                if (Mathf.Sqrt(x * x + z * z) < radius * 1.4f + 5f || KeepClear(x, z, radius * 1.3f))
                    continue;
                Add(x, z, radius, 0.1f + Next() * 0.1f, Next() < 0.15f ? 1f : 0f);
            }
            // A fresh crater ahead and to the right, its rays reaching toward the door.
            Add(6.5f, 10f, 4.2f, 0.19f, 1f);
            for (var i = 0; i < 900; i++)
            {
                var x = (Next() * 2f - 1f) * 40f;
                var z = (Next() * 2f - 1f) * 40f;
                var radius = 0.12f + Mathf.Pow(Next(), 3f) * 1.6f;
                if (KeepClear(x, z, radius))
                    continue;
                Add(x, z, radius, 0.12f + Next() * 0.1f, Next() < 0.06f ? 1f : 0f);
            }
            // In reach of the soil patch: one small fresh crater, and pits to touch.
            Add(0.62f, 1.75f, 0.3f, 0.2f, 1f);
            for (var i = 0; i < 26; i++)
                Add(-1.3f + Next() * 2.6f, 0.4f + Next() * 1.9f, 0.03f + Next() * 0.07f, 0.18f, 0f);
            s_Origin = 0f;
            s_Origin = Raw(0f, 0f, 0f, out _);
        }

        static void Add(float x, float z, float radius, float depthRatio, float fresh)
        {
            var depth = radius * depthRatio;
            var crater = new Crater { X = x, Z = z, R = radius, Depth = depth, Rim = depth * 0.22f, Fresh = fresh, Seed = s_Buckets.Count * 17 + 3 };
            var reach = 3f * radius;
            for (var bx = Mathf.FloorToInt((x - reach) / k_Bucket); bx <= Mathf.FloorToInt((x + reach) / k_Bucket); bx++)
            {
                for (var bz = Mathf.FloorToInt((z - reach) / k_Bucket); bz <= Mathf.FloorToInt((z + reach) / k_Bucket); bz++)
                {
                    var key = Key(bx, bz);
                    if (!s_Buckets.TryGetValue(key, out var list))
                        s_Buckets[key] = list = new List<Crater>();
                    list.Add(crater);
                }
            }
        }
    }

    // Value noise on an integer lattice, from a hash: deterministic and the same on every platform.
    public static class Noise
    {
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                var h = x * 374761393 + y * 668265263 + seed * 982451653;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                h ^= (int)((uint)h >> 16);
                return (uint)h / 4294967296f;
            }
        }

        public static float Value(float x, float y, int seed)
        {
            var ix = Mathf.FloorToInt(x);
            var iy = Mathf.FloorToInt(y);
            var fx = x - ix;
            var fy = y - iy;
            var ux = fx * fx * (3f - 2f * fx);
            var uy = fy * fy * (3f - 2f * fy);
            var a = Hash(ix, iy, seed);
            var b = Hash(ix + 1, iy, seed);
            var c = Hash(ix, iy + 1, seed);
            var d = Hash(ix + 1, iy + 1, seed);
            return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy;
        }

        // Octaves of value noise, -1 to 1 or so.
        public static float Fbm(float x, float y, int octaves, int seed)
        {
            var sum = 0f;
            var amplitude = 0.5f;
            var frequency = 1f;
            for (var i = 0; i < octaves; i++)
            {
                sum += amplitude * (Value(x * frequency, y * frequency, seed + i) * 2f - 1f);
                frequency *= 2.03f;
                amplitude *= 0.5f;
            }
            return sum;
        }
    }
}
