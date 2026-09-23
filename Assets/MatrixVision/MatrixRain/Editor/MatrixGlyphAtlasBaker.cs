using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;

namespace MatrixVision.Rendering.Editor
{
    /// <summary>
    /// Génère MatrixGlyphAtlas.png : 64 glyphes Matrix (katakana demi-chasse, chiffres, symboles)
    /// en distance field (SDF), en miroir horizontal comme dans le film, sur une grille 8x8.
    /// </summary>
    public static class MatrixGlyphAtlasBaker
    {
        const string Glyphs = "ｦｱｳｴｵｶｷｹｺｻｼｽｾｿﾀﾂﾃﾅﾆﾇﾈﾊﾋﾎﾏﾐﾑﾒﾓﾔﾕﾗﾘﾜ012345789Z:.\"=*+-<>¦|";
        const string FontFolder = "Assets/MatrixVision/MatrixRain/Fonts";
        const string AtlasPath = "Assets/MatrixVision/MatrixRain/MatrixGlyphAtlas.png";
        const int Cell = 64;
        const int Grid = 8;
        const int PointSize = 44;
        const int Padding = 8;

        [MenuItem("MatrixVision/Bake Glyph Atlas")]
        public static void Bake()
        {
            var fontGuids = AssetDatabase.FindAssets("t:Font", new[] { FontFolder });
            if (fontGuids.Length == 0)
            {
                Debug.LogError($"MatrixGlyphAtlasBaker : aucune police dans {FontFolder}.");
                return;
            }
            var font = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(fontGuids[0]));

            var temporarySettings = EnsureTmpSettings();
            TMP_FontAsset fontAsset;
            byte[] source;
            try
            {
                fontAsset = TMP_FontAsset.CreateFontAsset(font, PointSize, Padding, GlyphRenderMode.SDFAA, 1024, 1024,
                    AtlasPopulationMode.Dynamic, false);
                if (!fontAsset.TryAddCharacters(Glyphs, out string missing))
                    Debug.LogWarning($"MatrixGlyphAtlasBaker : caractères absents de la police : {missing}");
                source = ReadAlpha(fontAsset.atlasTexture);
            }
            finally
            {
                ReleaseTmpSettings(temporarySettings);
            }

            var atlas = fontAsset.atlasTexture;
            int atlasWidth = atlas.width;
            int atlasHeight = atlas.height;

            int size = Cell * Grid;
            var pixels = new Color32[size * size];
            int count = 0;

            foreach (char c in Glyphs)
            {
                if (count >= Grid * Grid)
                    break;
                if (!fontAsset.characterLookupTable.TryGetValue(c, out var character))
                    continue;

                var rect = character.glyph.glyphRect;
                int x0 = rect.x - Padding, y0 = rect.y - Padding;
                int w = rect.width + 2 * Padding, h = rect.height + 2 * Padding;

                // Glyphe n°i : colonne i % 8, ligne i / 8 depuis le haut (les textures sont stockées de bas en haut).
                int cellX = (count % Grid) * Cell;
                int cellY = (Grid - 1 - count / Grid) * Cell;
                int ox = cellX + (Cell - w) / 2;
                int oy = cellY + (Cell - h) / 2;

                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sx = x0 + x, sy = y0 + y;
                    int dx = ox + (w - 1 - x); // miroir horizontal
                    int dy = oy + y;
                    if (sx < 0 || sy < 0 || sx >= atlasWidth || sy >= atlasHeight)
                        continue;
                    if (dx < cellX || dy < cellY || dx >= cellX + Cell || dy >= cellY + Cell)
                        continue;
                    byte v = source[sy * atlasWidth + sx];
                    pixels[dy * size + dx] = new Color32(v, v, v, 255);
                }
                count++;
            }

            // Cases restantes : on réutilise les premiers glyphes, pour que les 64 cases soient valides.
            for (int i = count; i < Grid * Grid && count > 0; i++)
                CopyCell(pixels, size, i % count, i);

            var output = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            output.SetPixels32(pixels);
            output.Apply();
            File.WriteAllBytes(AtlasPath, output.EncodeToPNG());
            Object.DestroyImmediate(output);
            Object.DestroyImmediate(atlas);
            Object.DestroyImmediate(fontAsset.material);
            Object.DestroyImmediate(fontAsset);

            AssetDatabase.ImportAsset(AtlasPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            // Mipmaps + anisotrope : les glyphes restent nets et stables au loin et sur les sols vus en rasant.
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            importer.anisoLevel = 16;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.alphaSource = TextureImporterAlphaSource.None;
            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.format = TextureImporterFormat.R8;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            AssignToRendererFeatures(texture);
            Debug.Log($"MatrixGlyphAtlasBaker : {count} glyphes écrits dans {AtlasPath}.");
        }

        static readonly FieldInfo TmpSettingsInstance =
            typeof(TMP_Settings).GetField("s_Instance", BindingFlags.NonPublic | BindingFlags.Static);

        // CreateFontAsset lit TMP_Settings, absent tant que les "TMP Essential Resources" ne sont pas importées :
        // on fournit des réglages temporaires en mémoire plutôt que d'ajouter ces ressources au projet.
        static TMP_Settings EnsureTmpSettings()
        {
            if (TmpSettingsInstance == null || TmpSettingsInstance.GetValue(null) != null)
                return null;
            if (Resources.Load<TMP_Settings>("TMP Settings") != null)
                return null;

            var settings = ScriptableObject.CreateInstance<TMP_Settings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            TmpSettingsInstance.SetValue(null, settings);
            return settings;
        }

        static void ReleaseTmpSettings(TMP_Settings settings)
        {
            if (settings == null)
                return;
            TmpSettingsInstance.SetValue(null, null);
            Object.DestroyImmediate(settings);
        }

        // Canal alpha (distance SDF) de l'atlas TMP, lisible ou non.
        static byte[] ReadAlpha(Texture2D texture)
        {
            Color32[] colors;
            if (texture.isReadable)
            {
                colors = texture.GetPixels32();
            }
            else
            {
                var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(texture, rt);
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                colors = copy.GetPixels32();
                Object.DestroyImmediate(copy);
            }

            var alpha = new byte[colors.Length];
            for (int i = 0; i < colors.Length; i++)
                alpha[i] = colors[i].a;
            return alpha;
        }

        static void CopyCell(Color32[] pixels, int size, int from, int to)
        {
            int fx = (from % Grid) * Cell, fy = (Grid - 1 - from / Grid) * Cell;
            int tx = (to % Grid) * Cell, ty = (Grid - 1 - to / Grid) * Cell;
            for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
                pixels[(ty + y) * size + tx + x] = pixels[(fy + y) * size + fx + x];
        }

        static void AssignToRendererFeatures(Texture2D texture)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var feature in data.rendererFeatures)
                {
                    if (feature is not MatrixRainRendererFeature)
                        continue;
                    var so = new SerializedObject(feature);
                    so.FindProperty("glyphAtlas").objectReferenceValue = texture;
                    so.ApplyModifiedProperties();
                }
            }
            AssetDatabase.SaveAssets();
        }
    }
}
