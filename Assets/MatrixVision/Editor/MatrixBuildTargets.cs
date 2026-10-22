using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.XR.ARCore;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace MatrixVision.Editor
{
    /// <summary>
    /// Le téléphone (ARCore) et le Meta Quest (OpenXR) ciblent tous deux Android, dont les réglages XR sont partagés.
    /// Ce menu applique la configuration de l'une ou l'autre cible avant son build :
    /// loader XR, API graphique, niveau de qualité (asset URP), réglages ARCore et fonctionnalités OpenXR Meta.
    /// </summary>
    public static class MatrixBuildTargets
    {
        public const string PhoneScene = "Assets/Scenes/MatrixAR.unity";
        public const string QuestScene = "Assets/Scenes/MatrixQuest.unity";

        const string ARCoreLoader = "UnityEngine.XR.ARCore.ARCoreLoader";
        const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string PhoneQualityLevel = "Mobile";
        const string QuestQualityLevel = "Quest";

        // Fonctionnalités OpenXR à activer pour le Quest (par nom de type : pas de dépendance de compilation).
        static readonly HashSet<string> QuestFeatures = new()
        {
            "MetaQuestFeature",                    // Build Meta Quest (manifeste, appareils ciblés)
            "ARSessionFeature",                    // Meta Quest : Session
            "ARCameraFeature",                     // Meta Quest : Camera (passthrough)
            "ARPlaneFeature",                      // Meta Quest : Planes (Space Setup)
            "ARMeshFeature",                       // Meta Quest : Meshing (maillage de la pièce)
            "AROcclusionFeature",                  // Meta Quest : Occlusion (profondeur d'environnement)
            "HandTracking",                        // XR Hands : suivi des mains
            "MetaHandTrackingAim",                 // XR Hands : visée et pincement des mains (Meta)
            "HandInteractionProfile",              // profil d'interaction des mains (XR_EXT_hand_interaction)
            "OculusTouchControllerProfile",        // manettes Touch
            "MetaQuestTouchPlusControllerProfile", // manettes Touch Plus (Quest 3)
        };

        [MenuItem("MatrixVision/Build Target/Téléphone (ARCore)")]
        public static void ConfigurePhoneMenu() => Debug.Log(ConfigurePhone());

        [MenuItem("MatrixVision/Build Target/Meta Quest (OpenXR)")]
        public static void ConfigureQuestMenu() => Debug.Log(ConfigureQuest());

        public static string ConfigurePhone()
        {
            var log = new StringBuilder("Configuration téléphone (ARCore)\n");
            SetOnlyLoader(ARCoreLoader, log);
            SetGraphicsApi(GraphicsDeviceType.OpenGLES3, log);
            SetAndroidQuality(PhoneQualityLevel, log);
            SetMinSdk(AndroidSdkVersions.AndroidApiLevel25, log); // ARCore : Android 7.1+

            var arcore = ARCoreSettings.GetOrCreateSettings();
            arcore.requirement = ARCoreSettings.Requirement.Required;
            arcore.depth = ARCoreSettings.Requirement.Optional;
            EditorUtility.SetDirty(arcore);
            log.AppendLine("ARCore : requis, profondeur optionnelle");

            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        public static string ConfigureQuest()
        {
            var log = new StringBuilder("Configuration Meta Quest (OpenXR)\n");
            SetOnlyLoader(OpenXRLoader, log);
            SetGraphicsApi(GraphicsDeviceType.Vulkan, log);
            SetAndroidQuality(QuestQualityLevel, log);
            SetMinSdk(AndroidSdkVersions.AndroidApiLevel32, log); // passthrough Meta : Android 12L+

            // ARCore optionnel : aucune dépendance au Play Store ARCore dans le manifeste du Quest.
            var arcore = ARCoreSettings.GetOrCreateSettings();
            arcore.requirement = ARCoreSettings.Requirement.Optional;
            EditorUtility.SetDirty(arcore);

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null)
            {
                log.AppendLine("ERREUR : réglages OpenXR Android introuvables.");
                return log.ToString();
            }
            var enabled = new List<string>();
            foreach (var feature in settings.GetFeatures())
            {
                if (!QuestFeatures.Contains(feature.GetType().Name))
                    continue;
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
                enabled.Add(feature.GetType().Name);
            }
            var missing = QuestFeatures.Except(enabled).ToList();
            log.AppendLine("OpenXR : " + string.Join(", ", enabled) + (missing.Count > 0 ? " — ABSENTES : " + string.Join(", ", missing) : ""));

            // Recommandation Meta : réduire la latence des manettes et du casque.
            settings.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
            EditorUtility.SetDirty(settings);

            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        static void SetOnlyLoader(string loaderType, StringBuilder log)
        {
            var perBuild = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            var manager = perBuild != null ? perBuild.AssignedSettings : null;
            if (manager == null)
            {
                log.AppendLine("ERREUR : XR Plug-in Management Android non configuré.");
                return;
            }
            foreach (var other in new[] { ARCoreLoader, OpenXRLoader })
            {
                if (other != loaderType)
                    XRPackageMetadataStore.RemoveLoader(manager, other, BuildTargetGroup.Android);
            }
            XRPackageMetadataStore.AssignLoader(manager, loaderType, BuildTargetGroup.Android);
            perBuild.InitManagerOnStart = true;
            EditorUtility.SetDirty(perBuild);
            log.AppendLine("Loader XR Android : " + string.Join(", ", manager.activeLoaders.Select(l => l.GetType().Name)));
        }

        static void SetMinSdk(AndroidSdkVersions version, StringBuilder log)
        {
            PlayerSettings.Android.minSdkVersion = version;
            log.AppendLine("API Android minimale : " + version);
        }

        static void SetGraphicsApi(GraphicsDeviceType api, StringBuilder log)
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { api });
            log.AppendLine("API graphique Android : " + api);
        }

        // Niveau de qualité par défaut d'Android : il choisit l'asset URP (Mobile_RPAsset ou Quest_RPAsset).
        static void SetAndroidQuality(string levelName, StringBuilder log)
        {
            int level = System.Array.IndexOf(QualitySettings.names, levelName);
            if (level < 0)
            {
                log.AppendLine($"ERREUR : niveau de qualité « {levelName} » introuvable.");
                return;
            }
            var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var defaults = quality.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < defaults.arraySize; i++)
            {
                var entry = defaults.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == "Android")
                    entry.FindPropertyRelative("second").intValue = level;
            }
            quality.ApplyModifiedPropertiesWithoutUndo();
            var asset = QualitySettings.GetRenderPipelineAssetAt(level);
            log.AppendLine($"Qualité Android : {levelName} ({(asset != null ? asset.name : "asset par défaut")})");
        }
    }
}
