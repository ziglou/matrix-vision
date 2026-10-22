using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MatrixVision.Neo;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MatrixVision.Editor
{
    /// <summary>
    /// Construit le prefab du personnage à partir du modèle Neo fourni (Assets/Art/neo : OBJ sans .mtl + textures).
    /// L'OBJ est lu ici même pour retrouver ses 16 morceaux (l'importeur d'Unity les fusionne faute de .mtl),
    /// chacun reçoit sa texture. Le modèle n'a pas de squelette : il garde la pose dans laquelle il a été modélisé.
    /// Menu MatrixVision > Neo.
    /// </summary>
    public static class MatrixNeoBuilder
    {
        const string ObjPath = "Assets/Art/neo/source/Neo.obj";
        const string TextureFolder = "Assets/Art/neo/textures";
        const string OutputFolder = "Assets/MatrixVision/Neo";
        const string PrefabPath = OutputFolder + "/MatrixNeo.prefab";
        const string MeshPath = OutputFolder + "/Neo_Mesh.asset";
        const string MaterialFolder = OutputFolder + "/Materials";
        const float Height = 1.8f;

        // Morceau de l'OBJ → texture, déduit de la position de chaque morceau sur le corps (devant = +Z).
        static readonly Dictionary<string, string> PartTextures = new()
        {
            { "0002_untitledoutout", "face" },        // visage
            { "0012_untitledoutout", "head" },        // crâne
            { "0007_untitledoutout", "head" },        // côtés de la tête
            { "0004_untitledoutout", "hair2" },       // cheveux
            { "0013_untitledoutout", "eyes" },
            { "0015_untitledoutout", "teeth" },
            { "0014_untitledoutout", "glasses" },     // lunettes et branches
            { "0006_untitledoutout", "neck" },
            { "0008_untitledoutout", "topfront" },    // manteau, haut devant
            { "0003_untitledoutout", "topback" },     // manteau, haut dos
            { "0001_untitledoutout", "arms" },
            { "0010_untitledoutout", "hands" },
            { "0009_untitledoutout", "pants" },
            { "0000_untitledoutout", "skirtfront" },  // pans du manteau, devant
            { "0005_untitledoutout", "skirtback" },   // pans du manteau, dos
            { "0011_untitledoutout", "shoes" },
        };

        [MenuItem("MatrixVision/Neo/Construire le prefab")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("Neo (modèle fourni)\n");
            if (!File.Exists(ObjPath))
                return "ERREUR : modèle introuvable : " + ObjPath;
            Directory.CreateDirectory(MaterialFolder);

            var parts = ReadObj(ObjPath);
            log.AppendLine($"OBJ : {parts.Count} morceaux, " + string.Join(", ", parts.Select(p => p.name.Substring(0, 4))));

            // Mise à l'échelle (1,80 m), pieds à y = 0.
            float minY = parts.SelectMany(p => p.positions).Min(v => v.y);
            float maxY = parts.SelectMany(p => p.positions).Max(v => v.y);
            float scale = Height / (maxY - minY);
            foreach (var part in parts)
                for (int i = 0; i < part.positions.Count; i++)
                    part.positions[i] = new Vector3(part.positions[i].x, part.positions[i].y - minY, part.positions[i].z) * scale;
            log.AppendLine($"Échelle ×{scale:0.0000}");

            // Maillage à sous-maillages (un par morceau), enregistré comme asset.
            var mesh = new Mesh { name = "Neo", indexFormat = IndexFormat.UInt32 };
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            foreach (var part in parts)
            {
                positions.AddRange(part.positions);
                normals.AddRange(part.normals);
                uvs.AddRange(part.uvs);
            }
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = parts.Count;
            int offset = 0;
            for (int s = 0; s < parts.Count; s++)
            {
                mesh.SetTriangles(parts[s].triangles.Select(t => t + offset).ToList(), s);
                offset += parts[s].positions.Count;
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            // Contenu remplacé en place : supprimer puis recréer l'asset casserait les références existantes.
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = "Neo";
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, MeshPath);
            }
            AssetDatabase.SaveAssets();

            var materials = parts.Select(p => MaterialFor(p.name)).ToArray();

            var root = new GameObject("MatrixNeo");
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            // Lumière douce de face : les scènes AR n'ont pas d'éclairage propre.
            var light = new GameObject("Neo Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.rotation = Quaternion.Euler(35f, 200f, 0f);
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;

            var neo = root.AddComponent<MatrixNeo>();
            var so = new SerializedObject(neo);
            so.FindProperty("invisibleMaterial").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Material>("Assets/MatrixVision/AR/MatrixInvisible.mat");
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            log.AppendLine($"Hauteur {mesh.bounds.size.y:0.00} m, largeur {mesh.bounds.size.x:0.00} m ; prefab : {PrefabPath}");
            return log.ToString();
        }

        class ObjPart
        {
            public string name;
            public readonly List<Vector3> positions = new();
            public readonly List<Vector3> normals = new();
            public readonly List<Vector2> uvs = new();
            public readonly List<int> triangles = new();
            public readonly Dictionary<string, int> lookup = new();
        }

        // Lecteur OBJ minimal (v, vt, vn, o, f). X est inversé et les faces retournées, comme l'importeur d'Unity.
        static List<ObjPart> ReadObj(string path)
        {
            var v = new List<Vector3>();
            var vt = new List<Vector2>();
            var vn = new List<Vector3>();
            var parts = new List<ObjPart>();
            ObjPart current = null;
            var culture = CultureInfo.InvariantCulture;

            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;
                var t = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                switch (t[0])
                {
                    case "v":
                        v.Add(new Vector3(-float.Parse(t[1], culture), float.Parse(t[2], culture), float.Parse(t[3], culture)));
                        break;
                    case "vt":
                        vt.Add(new Vector2(float.Parse(t[1], culture), float.Parse(t[2], culture)));
                        break;
                    case "vn":
                        vn.Add(new Vector3(-float.Parse(t[1], culture), float.Parse(t[2], culture), float.Parse(t[3], culture)));
                        break;
                    case "o":
                    case "g":
                        current = new ObjPart { name = t.Length > 1 ? t[1] : "part" + parts.Count };
                        parts.Add(current);
                        break;
                    case "f":
                        if (current == null)
                        {
                            current = new ObjPart { name = "default" };
                            parts.Add(current);
                        }
                        var corners = t.Skip(1).Select(c => Corner(current, c, v, vt, vn)).ToList();
                        for (int i = 1; i + 1 < corners.Count; i++)
                        {
                            // Ordre inversé : X est inversé, il faut retourner les faces.
                            current.triangles.Add(corners[0]);
                            current.triangles.Add(corners[i + 1]);
                            current.triangles.Add(corners[i]);
                        }
                        break;
                }
            }
            return parts.Where(p => p.triangles.Count > 0).ToList();
        }

        static int Corner(ObjPart part, string token, List<Vector3> v, List<Vector2> vt, List<Vector3> vn)
        {
            if (part.lookup.TryGetValue(token, out int index))
                return index;
            var ids = token.Split('/');
            int Resolve(string s, int count) => int.Parse(s) is var n && n < 0 ? count + n : n - 1;
            var position = v[Resolve(ids[0], v.Count)];
            var uv = ids.Length > 1 && ids[1].Length > 0 ? vt[Resolve(ids[1], vt.Count)] : Vector2.zero;
            var normal = ids.Length > 2 && ids[2].Length > 0 ? vn[Resolve(ids[2], vn.Count)] : Vector3.up;
            index = part.positions.Count;
            part.positions.Add(position);
            part.uvs.Add(uv);
            part.normals.Add(normal);
            part.lookup[token] = index;
            return index;
        }

        static Material MaterialFor(string partName)
        {
            string textureName = PartTextures.TryGetValue(partName, out var name) ? name : "topfront";
            string path = $"{MaterialFolder}/Neo_{textureName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{textureName}.png");
            material.SetTexture("_BaseMap", texture);
            material.color = Color.white;
            material.SetFloat("_Smoothness", textureName == "glasses" ? 0.9f : textureName.StartsWith("skirt") || textureName.StartsWith("top") ? 0.45f : 0.2f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
