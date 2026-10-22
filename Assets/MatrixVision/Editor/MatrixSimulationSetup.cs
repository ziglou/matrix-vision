using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;

namespace MatrixVision.Editor
{
    /// <summary>
    /// Test sans appareil avec XR Simulation (AR Foundation) : une pièce virtuelle, avec murs, porte, sol, plafond
    /// et meubles, où la simulation découvre plans et maillage comme un vrai téléphone ou un Quest.
    /// Menu MatrixVision > Simulation. Les scènes MatrixAR et MatrixQuest se testent alors en mode Play dans l'éditeur
    /// (déplacement : clic droit + ZQSD/WASD) et par les tests automatiques (Window > General > Test Runner).
    /// </summary>
    public static class MatrixSimulationSetup
    {
        public const string RoomPrefabPath = "Assets/MatrixVision/Simulation/MatrixTestRoom.prefab";
        const string MaterialFolder = "Assets/MatrixVision/Simulation/Materials";
        const string DefaultEnvironmentPath = "Packages/com.unity.xr.arfoundation/Assets/Prefabs/DefaultSimulationEnvironment.prefab";
        const string PreferencesPath = "Assets/XR/UserSimulationSettings/Resources/XRSimulationPreferences.asset";
        const string SimulationLoader = "UnityEngine.XR.Simulation.SimulationLoader";

        [MenuItem("MatrixVision/Simulation/Activer XR Simulation (pièce de test)")]
        public static void SetupMenu() => Debug.Log(Setup());

        /// <summary>
        /// Crée la pièce de test si besoin et la sélectionne. XR Simulation n'est activé qu'en lançant MatrixAR ou
        /// MatrixQuest (<see cref="MatrixSimulationPlayMode"/>) ; les autres scènes ne sont pas concernées.
        /// </summary>
        public static string Setup()
        {
            var log = new StringBuilder("XR Simulation\n");
            var room = AssetDatabase.LoadAssetAtPath<GameObject>(RoomPrefabPath) ?? CreateTestRoom(log);

            // Environnement actif de la simulation
            var preferences = AssetDatabase.LoadAllAssetsAtPath(PreferencesPath).FirstOrDefault();
            if (preferences != null)
            {
                var so = new SerializedObject(preferences);
                so.FindProperty("m_EnvironmentPrefab").objectReferenceValue = room;
                so.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("Environnement : " + RoomPrefabPath);
            }
            else
            {
                log.AppendLine("ERREUR : " + PreferencesPath + " introuvable.");
            }

            // Le mode Play de l'éditeur utilise les réglages XR « Standalone ». XR Simulation n'y est pas activé en
            // permanence (les autres scènes du projet n'en veulent pas) : MatrixSimulationPlayMode l'active seulement
            // en lançant MatrixAR ou MatrixQuest. Ici, on s'assure juste que les réglages existent, sans loader imposé.
            var perBuild = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (perBuild == null)
            {
                EditorBuildSettings.TryGetConfigObject(UnityEngine.XR.Management.XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget all);
                if (all != null && !all.HasSettingsForBuildTarget(BuildTargetGroup.Standalone))
                {
                    all.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
                    all.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
                }
                perBuild = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            }
            var manager = perBuild.AssignedSettings;
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                XRPackageMetadataStore.RemoveLoader(manager, SimulationLoader, BuildTargetGroup.Standalone);
            perBuild.InitManagerOnStart = true;
            EditorUtility.SetDirty(perBuild);
            log.AppendLine("Loader éditeur (Standalone) hors scènes Matrix : " +
                (manager.activeLoaders.Count > 0 ? string.Join(", ", manager.activeLoaders.Select(l => l.GetType().Name)) : "aucun"));
            log.AppendLine("XR Simulation : activé automatiquement en lançant MatrixAR ou MatrixQuest");

            // Sans cela, le mode Play se fige dès que l'éditeur perd le focus (tests lancés à distance).
            PlayerSettings.runInBackground = true;
            log.AppendLine("Run In Background : activé");

            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        [MenuItem("MatrixVision/Simulation/Reconstruire la pièce de test")]
        public static void RebuildMenu()
        {
            AssetDatabase.DeleteAsset(RoomPrefabPath);
            Debug.Log(Setup());
        }

        // Pièce de 5 × 4 m, 2,6 m sous plafond : mur uni, mur avec porte, table, chaise, étagère, canapé.
        static GameObject CreateTestRoom(StringBuilder log)
        {
            System.IO.Directory.CreateDirectory(MaterialFolder);
            var wallMat = CreateMaterial("Wall", new Color(0.92f, 0.92f, 0.9f));
            var floorMat = CreateMaterial("Floor", new Color(0.45f, 0.33f, 0.22f));
            var woodMat = CreateMaterial("Wood", new Color(0.55f, 0.38f, 0.22f));
            var fabricMat = CreateMaterial("Fabric", new Color(0.25f, 0.3f, 0.42f));
            var doorMat = CreateMaterial("Door", new Color(0.7f, 0.66f, 0.6f));

            var root = new GameObject("MatrixTestRoom");

            // Composant SimulationEnvironment (interne au paquet) copié depuis l'environnement par défaut.
            var defaultEnvironment = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultEnvironmentPath);
            var source = defaultEnvironment.GetComponents<Component>().First(c => c.GetType().Name == "SimulationEnvironment");
            var environment = root.AddComponent(source.GetType());
            EditorUtility.CopySerialized(source, environment);
            var env = new SerializedObject(environment);
            env.FindProperty("m_CameraStartingPose.position").vector3Value = new Vector3(0f, 1.5f, -1.2f);
            env.FindProperty("m_CameraStartingPose.rotation").quaternionValue = Quaternion.Euler(15f, 0f, 0f);
            env.FindProperty("m_CameraMovementBounds").boundsValue = new Bounds(new Vector3(0f, 1.3f, 0f), new Vector3(4.6f, 2.4f, 3.6f));
            env.FindProperty("m_DefaultViewPose.position").vector3Value = new Vector3(0f, 4f, -6f);
            env.FindProperty("m_DefaultViewPose.rotation").quaternionValue = Quaternion.Euler(30f, 0f, 0f);
            env.FindProperty("m_DefaultViewPivot").vector3Value = new Vector3(0f, 1f, 0f);
            env.FindProperty("m_DefaultViewSize").floatValue = 6f;
            env.ApplyModifiedPropertiesWithoutUndo();

            // Structure : sol, plafond, murs (dont un mur blanc uni au fond et une porte à gauche)
            Box(root, "Floor", new Vector3(0f, -0.05f, 0f), new Vector3(5f, 0.1f, 4f), floorMat);
            Box(root, "Ceiling", new Vector3(0f, 2.65f, 0f), new Vector3(5f, 0.1f, 4f), wallMat);
            Box(root, "Wall Back (uni)", new Vector3(0f, 1.3f, 2.05f), new Vector3(5f, 2.6f, 0.1f), wallMat);
            Box(root, "Wall Front", new Vector3(0f, 1.3f, -2.05f), new Vector3(5f, 2.6f, 0.1f), wallMat);
            Box(root, "Wall Right", new Vector3(2.55f, 1.3f, 0f), new Vector3(0.1f, 2.6f, 4.2f), wallMat);
            Box(root, "Wall Left A", new Vector3(-2.55f, 1.3f, -1.15f), new Vector3(0.1f, 2.6f, 1.9f), wallMat);
            Box(root, "Wall Left B", new Vector3(-2.55f, 1.3f, 1.6f), new Vector3(0.1f, 2.6f, 1f), wallMat);
            Box(root, "Wall Left Lintel", new Vector3(-2.55f, 2.35f, 0.65f), new Vector3(0.1f, 0.5f, 0.9f), wallMat);
            Box(root, "Door", new Vector3(-2.52f, 1.05f, 0.65f), new Vector3(0.05f, 2.1f, 0.88f), doorMat);

            // Mobilier
            Box(root, "Table Top", new Vector3(0.9f, 0.74f, 0.6f), new Vector3(1.4f, 0.04f, 0.8f), woodMat);
            foreach (var x in new[] { 0.25f, 1.55f })
                foreach (var z in new[] { 0.25f, 0.95f })
                    Box(root, "Table Leg", new Vector3(x, 0.36f, z), new Vector3(0.06f, 0.72f, 0.06f), woodMat);
            Box(root, "Chair Seat", new Vector3(0.9f, 0.45f, -0.1f), new Vector3(0.45f, 0.05f, 0.45f), woodMat);
            Box(root, "Chair Back", new Vector3(0.9f, 0.72f, -0.32f), new Vector3(0.45f, 0.5f, 0.04f), woodMat);
            Box(root, "Chair Base", new Vector3(0.9f, 0.21f, -0.1f), new Vector3(0.4f, 0.42f, 0.4f), woodMat);
            Box(root, "Shelf", new Vector3(2.3f, 0.9f, 1.4f), new Vector3(0.4f, 1.8f, 0.9f), woodMat);
            Box(root, "Sofa Base", new Vector3(-1.4f, 0.22f, 1.55f), new Vector3(1.8f, 0.44f, 0.8f), fabricMat);
            Box(root, "Sofa Back", new Vector3(-1.4f, 0.65f, 1.88f), new Vector3(1.8f, 0.5f, 0.15f), fabricMat);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RoomPrefabPath);
            Object.DestroyImmediate(root);
            log.AppendLine("Pièce de test créée : " + RoomPrefabPath);
            return prefab;
        }

        static void Box(GameObject parent, string name, Vector3 center, Vector3 size, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent.transform, false);
            box.transform.localPosition = center;
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Material CreateMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
