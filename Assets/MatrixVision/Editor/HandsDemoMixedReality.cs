using System.Linq;
using System.Text;
using MatrixVision.AR;
using MatrixVision.Rendering;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR;

namespace MatrixVision.Editor
{
    /// <summary>
    /// Menu MatrixVision → Hands Demo : passe la scène d'exemple « Hands Interaction Demo » du XR Interaction
    /// Toolkit en réalité mixte sur Meta Quest (OpenXR) : passthrough, plans, maillage de la pièce, occlusion par
    /// la profondeur d'environnement et suivi des mains. Idempotent : peut être relancé sans effet de bord.
    /// </summary>
    public static class HandsDemoMixedReality
    {
        public const string HandsDemoScene = "Assets/Samples/XR Interaction Toolkit/3.3.2/Hands Interaction Demo/HandsDemoScene.unity";

        const string Folder = "Assets/MatrixVision/AR";
        const string OverlayShaderName = "MatrixVision/MixedRealityOverlay";
        const string OcclusionShaderPath = "Assets/MatrixVision/MatrixRain/EnvironmentDepthOcclusion.shader";
        const string PlaneMaterialPath = Folder + "/MixedRealityPlane.mat";
        const string MeshMaterialPath = Folder + "/MixedRealityMesh.mat";
        const string PlanePrefabPath = Folder + "/MixedRealityPlane.prefab";
        const string MeshPrefabPath = Folder + "/MixedRealityMesh.prefab";
        const string QuestRendererPath = "Assets/Settings/Quest_Renderer.asset";
        const string MeshManagerName = "AR Mesh Manager";
        const string SessionName = "AR Session";

        [MenuItem("MatrixVision/Hands Demo/Configurer la réalité mixte (Meta Quest)")]
        public static void ConfigureMenu() => Debug.Log(Configure());

        public static string Configure()
        {
            var log = new StringBuilder("Hands Demo en réalité mixte (Meta Quest)\n");
            log.Append(MatrixBuildTargets.ConfigureQuest());
            EnableHandRemoval(log);
            AddOcclusionFeature(log);
            var planePrefab = GetOrCreatePrefab(PlanePrefabPath, PlaneMaterialPath,
                new Color(0.1f, 0.75f, 1f, 0.10f), new Color(0.3f, 0.9f, 1f, 0.55f), typeof(ARPlane), typeof(ARPlaneMeshVisualizer));
            var meshPrefab = GetOrCreatePrefab(MeshPrefabPath, MeshMaterialPath,
                new Color(0.1f, 1f, 0.35f, 0.04f), new Color(0.3f, 1f, 0.5f, 0.35f));
            ConfigureScene(planePrefab.GetComponent<ARPlane>(), meshPrefab.GetComponent<MeshFilter>(), log);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        // Les mains réelles sont retirées de la profondeur : les mains virtuelles du XRI les remplacent.
        static void EnableHandRemoval(StringBuilder log)
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var occlusion = settings != null ? settings.GetFeatures().FirstOrDefault(f => f.GetType().Name == "AROcclusionFeature") : null;
            if (occlusion == null)
            {
                log.AppendLine("ERREUR : fonctionnalité OpenXR « Meta Quest: Occlusion » introuvable.");
                return;
            }
            var serialized = new SerializedObject(occlusion);
            serialized.FindProperty("m_EnableHandRemoval").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("Occlusion : retrait des mains de la profondeur activé");
        }

        static void AddOcclusionFeature(StringBuilder log)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(QuestRendererPath);
            if (data == null)
            {
                log.AppendLine($"ERREUR : {QuestRendererPath} introuvable.");
                return;
            }
            var feature = data.rendererFeatures.OfType<EnvironmentDepthOcclusionFeature>().FirstOrDefault();
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<EnvironmentDepthOcclusionFeature>();
                feature.name = "EnvironmentDepthOcclusion";
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

                var serializedData = new SerializedObject(data);
                var features = serializedData.FindProperty("m_RendererFeatures");
                features.InsertArrayElementAtIndex(features.arraySize);
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                var map = serializedData.FindProperty("m_RendererFeatureMap");
                map.InsertArrayElementAtIndex(map.arraySize);
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                serializedData.ApplyModifiedPropertiesWithoutUndo();
            }
            // Référence directe au shader : il est ainsi inclus dans le build.
            var serializedFeature = new SerializedObject(feature);
            serializedFeature.FindProperty("shader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(OcclusionShaderPath);
            serializedFeature.ApplyModifiedPropertiesWithoutUndo();
            feature.Create();
            EditorUtility.SetDirty(data);
            log.AppendLine("Quest_Renderer : passe « Environment Depth Occlusion » présente");
        }

        static GameObject GetOrCreatePrefab(string prefabPath, string materialPath, Color veil, Color grid, params System.Type[] components)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find(OverlayShaderName));
                material.SetColor("_Color", veil);
                material.SetColor("_GridColor", grid);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
                return prefab;

            var go = new GameObject(System.IO.Path.GetFileNameWithoutExtension(prefabPath));
            foreach (var type in components)
                go.AddComponent(type);
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Pas de collider : le CharacterController de l'origine XR se heurterait au sol réel, qui le suit.
            prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static void ConfigureScene(ARPlane planePrefab, MeshFilter meshPrefab, StringBuilder log)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != HandsDemoScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    log.AppendLine("Annulé : scène courante non enregistrée.");
                    return;
                }
                scene = EditorSceneManager.OpenScene(HandsDemoScene);
            }

            var origin = Object.FindAnyObjectByType<XROrigin>();
            if (origin == null || origin.Camera == null)
            {
                log.AppendLine("ERREUR : XR Origin ou caméra introuvable dans la scène.");
                return;
            }

            // Passthrough : fond transparent, le compositeur affiche la caméra du casque derrière.
            var camera = origin.Camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            GetOrAdd<ARCameraManager>(camera.gameObject);
            GetOrAdd<ARCameraBackground>(camera.gameObject);

            var occlusionManager = GetOrAdd<AROcclusionManager>(camera.gameObject);
            occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
            occlusionManager.enabled = false;
            GetOrAdd<ARShaderOcclusion>(camera.gameObject);
            var depthOcclusion = GetOrAdd<EnvironmentDepthOcclusion>(camera.gameObject);
            var serializedOcclusion = new SerializedObject(depthOcclusion);
            serializedOcclusion.FindProperty("occlusionManager").objectReferenceValue = occlusionManager;
            serializedOcclusion.ApplyModifiedPropertiesWithoutUndo();

            var planeManager = GetOrAdd<ARPlaneManager>(origin.gameObject);
            planeManager.planePrefab = planePrefab != null ? planePrefab.gameObject : null;
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            planeManager.enabled = false;

            var meshTransform = origin.transform.Find(MeshManagerName);
            if (meshTransform == null)
            {
                meshTransform = new GameObject(MeshManagerName).transform;
                meshTransform.SetParent(origin.transform, false);
            }
            var meshManager = GetOrAdd<ARMeshManager>(meshTransform.gameObject);
            meshManager.meshPrefab = meshPrefab;
            meshManager.enabled = false;

            var session = Object.FindAnyObjectByType<ARSession>();
            var sessionObject = session != null ? session.gameObject : new GameObject(SessionName);
            GetOrAdd<ARSession>(sessionObject);
            GetOrAdd<ARInputManager>(sessionObject);
            var gate = GetOrAdd<ScenePermissionGate>(sessionObject);
            var serializedGate = new SerializedObject(gate);
            var behaviours = serializedGate.FindProperty("enableAfterPermission");
            Behaviour[] gated = { planeManager, meshManager, occlusionManager };
            behaviours.arraySize = gated.Length;
            for (int i = 0; i < gated.Length; i++)
                behaviours.GetArrayElementAtIndex(i).objectReferenceValue = gated[i];
            serializedGate.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"Scène {scene.name} : passthrough, AR Session, plans, maillage, occlusion (après permission USE_SCENE)");
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }
    }
}
