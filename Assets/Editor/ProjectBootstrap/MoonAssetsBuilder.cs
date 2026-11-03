using System.IO;
using System.Linq;
using CardboardXR.Demos.Portal;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectBootstrap
{
    // The Moon world's assets (MoonAssets), made once like the demo scenes, and given to the portal
    // prefab if it has none. The scanned regolith, rocks and the Earth are checked in under
    // Portal/Moon (see THIRD_PARTY_NOTICES.md); this sets their import settings, makes the textures
    // drawn in code (the suit's weave, a star's dot), and the materials. Delete Moon Assets.asset to
    // have them all made again.
    public static class MoonAssetsBuilder
    {
        const string PortalFolder = "Assets/_Project/Demos/Portal";
        const string MoonFolder = PortalFolder + "/Moon";
        const string TexturesFolder = MoonFolder + "/Textures";
        const string RocksFolder = MoonFolder + "/Rocks";
        const string MaterialsFolder = MoonFolder + "/Materials";
        const string AssetPath = MoonFolder + "/Moon Assets.asset";
        const string PrefabPath = PortalFolder + "/Portal.prefab";
        const string MaskPath = PortalFolder + "/Depth Mask.mat";
        const string MoonShaderPath = "Assets/_Project/Shaders/Moon.shader";
        static readonly string[] k_Rocks = { "MoonRock01", "MoonRock03", "MoonRock05" };

        // Makes the assets if they're missing and puts them on the portal prefab; returns them.
        public static MoonAssets Ensure()
        {
            var assets = AssetDatabase.LoadAssetAtPath<MoonAssets>(AssetPath);
            if (assets == null)
                assets = Make();
            if (assets != null)
                GiveToPortal(assets);
            return assets;
        }

        static MoonAssets Make()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(MoonShaderPath);
            if (shader == null || !AssetDatabase.IsValidFolder(TexturesFolder))
            {
                Debug.LogWarning("[MoonAssets] The Moon shader or textures are missing; the portal will open the night world");
                return null;
            }
            StereoBuilder.EnsureFolder(MaterialsFolder);
            Import($"{TexturesFolder}/Regolith_Diffuse.jpg", false, true);
            Import($"{TexturesFolder}/Regolith_Normal.jpg", true, true);
            Import($"{TexturesFolder}/RegolithMacro_Diffuse.jpg", false, true);
            Import($"{TexturesFolder}/RegolithMacro_Normal.jpg", true, true);
            Import($"{TexturesFolder}/Earth.jpg", false, false);
            var weave = WritePng($"{TexturesFolder}/Suit Weave.png", WeaveNormal(256), true);
            var dot = WritePng($"{TexturesFolder}/Star.png", StarDot(32), false);

            var assets = ScriptableObject.CreateInstance<MoonAssets>();
            var regolith = Load($"{TexturesFolder}/Regolith_Diffuse.jpg");
            var regolithNormal = Load($"{TexturesFolder}/Regolith_Normal.jpg");
            var macro = Load($"{TexturesFolder}/RegolithMacro_Diffuse.jpg");
            var macroNormal = Load($"{TexturesFolder}/RegolithMacro_Normal.jpg");

            // The patch and near ground: the 4 m scan, broken up by the 31 m one.
            assets.Ground = Moon(shader, "Moon Ground", new Color(1.05f, 1.03f, 1f), regolith, regolithNormal, 1f / 4f);
            assets.Ground.SetTexture("_MacroMap", macro);
            assets.Ground.SetTexture("_MacroBump", macroNormal);
            assets.Ground.SetFloat("_MacroScale", 4f / 31f);
            assets.Ground.SetFloat("_MacroStrength", 0.55f);
            assets.Ground.SetFloat("_MacroMean", MeanAlbedo($"{TexturesFolder}/RegolithMacro_Diffuse.jpg"));
            assets.Ground.SetFloat("_VertexColor", 1f);
            assets.Ground.SetFloat("_Lunar", 1f);
            // Far off, the 31 m scan alone.
            assets.FarGround = Moon(shader, "Moon Far Ground", new Color(1.05f, 1.03f, 1f), macro, macroNormal, 1f / 31f);
            assets.FarGround.SetFloat("_VertexColor", 1f);
            assets.FarGround.SetFloat("_Lunar", 1f);

            assets.RockMeshes = new Mesh[k_Rocks.Length];
            assets.RockMaterials = new Material[k_Rocks.Length];
            for (var i = 0; i < k_Rocks.Length; i++)
            {
                var model = $"{RocksFolder}/{k_Rocks[i]}.fbx";
                if (AssetImporter.GetAtPath(model) is ModelImporter importer)
                {
                    importer.importTangents = ModelImporterTangents.CalculateMikk;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
                assets.RockMeshes[i] = AssetDatabase.LoadAllAssetsAtPath(model).OfType<Mesh>().FirstOrDefault();
                Import($"{RocksFolder}/{k_Rocks[i]}_Diffuse.jpg", false, true);
                Import($"{RocksFolder}/{k_Rocks[i]}_Normal.jpg", true, true);
                var material = Moon(shader, $"{k_Rocks[i]}", new Color(0.95f, 0.94f, 0.92f), Load($"{RocksFolder}/{k_Rocks[i]}_Diffuse.jpg"),
                    Load($"{RocksFolder}/{k_Rocks[i]}_Normal.jpg"), 1f);
                material.SetFloat("_Lunar", 0.7f);
                assets.RockMaterials[i] = material;
            }
            assets.RockMeshes = assets.RockMeshes.Where(m => m != null).ToArray();

            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            assets.Sky = StereoBuilder.CreateMaterial("Moon Sky", unlit, Color.black, null, 1f, MaterialsFolder);
            assets.Stars = StereoBuilder.CreateMaterial("Moon Stars", unlit, new Color(0.95f, 0.97f, 1f), dot, 1f, MaterialsFolder);
            Additive(assets.Stars);

            assets.Earth = Moon(shader, "Earth", new Color(1.15f, 1.15f, 1.15f), Load($"{TexturesFolder}/Earth.jpg"), null, 1f);
            assets.Earth.SetColor("_Ambient", Color.black);

            assets.Fabric = Moon(shader, "Suit Fabric", new Color(0.86f, 0.85f, 0.81f), null, weave, 3f);
            assets.Fabric.SetFloat("_BumpScale", 0.6f);
            assets.Fabric.SetColor("_Ambient", new Color(0.03f, 0.033f, 0.04f));
            assets.Rubber = Moon(shader, "Suit Rubber", new Color(0.07f, 0.07f, 0.075f), null, null, 1f);
            assets.Rubber.SetFloat("_Specular", 0.12f);
            assets.Rubber.SetFloat("_Gloss", 18f);
            assets.Metal = Moon(shader, "Suit Metal", new Color(0.55f, 0.57f, 0.6f), null, null, 1f);
            assets.Metal.SetFloat("_Specular", 0.9f);
            assets.Metal.SetFloat("_Gloss", 90f);
            assets.Band = Moon(shader, "Suit Band", new Color(0.06f, 0.17f, 0.45f), null, null, 1f);
            assets.Band.SetFloat("_Specular", 0.5f);
            assets.Band.SetFloat("_Gloss", 60f);

            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            assets.Dust = StereoBuilder.SaveAsset(new Material(particles) { name = "Moon Dust", color = new Color(0.5f, 0.49f, 0.46f) },
                $"{MaterialsFolder}/Moon Dust.mat");
            assets.Dust.SetTexture("_BaseMap", dot);
            assets.Mask = AssetDatabase.LoadAssetAtPath<Material>(MaskPath);

            AssetDatabase.CreateAsset(assets, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MoonAssets] Made {AssetPath}");
            return assets;
        }

        static void GiveToPortal(MoonAssets assets)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                return;
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var portal = root.GetComponent<PortalWorld>();
                if (portal == null)
                    return;
                var serialized = new SerializedObject(portal);
                var moon = serialized.FindProperty("m_Moon");
                if (moon == null || moon.objectReferenceValue == assets)
                    return;
                moon.objectReferenceValue = assets;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[MoonAssets] Gave the Moon to the portal prefab");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Material Moon(Shader shader, string name, Color color, Texture albedo, Texture normal, float tiling)
        {
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            if (albedo != null)
                material.SetTexture("_BaseMap", albedo);
            if (normal != null)
                material.SetTexture("_BumpMap", normal);
            material.SetTextureScale("_BaseMap", Vector2.one * tiling);
            return StereoBuilder.SaveAsset(material, $"{MaterialsFolder}/{name}.mat");
        }

        static Texture2D Load(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        static void Import(string path, bool normal, bool repeat)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                return;
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.mipmapEnabled = true;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.anisoLevel = 8;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        // The mean linear albedo of a texture file, which the macro layer's tint is relative to.
        static float MeanAlbedo(string path)
        {
            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
                return 0.4f;
            var pixels = texture.GetPixels32();
            double sum = 0;
            for (var i = 0; i < pixels.Length; i += 97)
                sum += ((Color)pixels[i]).linear.grayscale;
            Object.DestroyImmediate(texture);
            return (float)(sum / (pixels.Length / 97.0));
        }

        static Texture2D WritePng(string path, Texture2D texture, bool normal)
        {
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            Import(path, normal, true);
            return Load(path);
        }

        // A woven fabric's normals, like the suits' outer layer: a fine basket weave.
        static Texture2D WeaveNormal(int size)
        {
            const int threads = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = x / (float)size * threads;
                    var v = y / (float)size * threads;
                    var over = ((int)u + (int)v) % 2 == 0;
                    var nx = Mathf.Cos(u * Mathf.PI * 2f) * (over ? 0.35f : 0.1f);
                    var ny = Mathf.Cos(v * Mathf.PI * 2f) * (over ? 0.1f : 0.35f);
                    var n = new Vector3(nx, ny, 1f).normalized;
                    pixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // A soft round dot, white with a fading alpha, for stars and dust.
        static Texture2D StarDot(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                    var a = Mathf.Clamp01(1f - d);
                    a *= a;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // Transparent and additive, set up as URP's material inspector would.
        static void Additive(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
        }
    }
}
