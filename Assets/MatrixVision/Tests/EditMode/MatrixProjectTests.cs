using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace MatrixVision.Tests
{
    /// <summary>
    /// Vérifications rapides (quelques secondes), sans mode Play : shaders, câblage du rendu,
    /// configuration des scènes AR (téléphone) et Quest, bascule entre les deux cibles de build.
    /// Les composants du projet (assembly par défaut) sont atteints par réflexion.
    /// </summary>
    public class MatrixProjectTests
    {
        const string PhoneScene = "Assets/Scenes/MatrixAR.unity";
        const string QuestScene = "Assets/Scenes/MatrixQuest.unity";

        static readonly string[] Shaders =
        {
            "Hidden/MatrixVision/MatrixRain",
            "Hidden/MatrixVision/MatrixSurface",
            "Hidden/MatrixVision/MatrixEnvironment",
            "Hidden/MatrixVision/MatrixPassthroughDim",
            "MatrixVision/Invisible",
        };

        static Type ProjectType(string fullName, string assembly = "Assembly-CSharp") =>
            Type.GetType($"{fullName}, {assembly}", true);

        [Test]
        public void Shaders_CompileWithoutErrors()
        {
            foreach (var name in Shaders)
            {
                var shader = Shader.Find(name);
                Assert.IsNotNull(shader, $"Shader introuvable : {name}");
                Assert.IsFalse(ShaderUtil.ShaderHasError(shader),
                    $"{name} : " + string.Join(" | ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
            }
        }

        // Les profils appliqués à toutes les scènes (assets URP, profil par défaut) ne portent pas l'effet Matrix :
        // seules les scènes Matrix l'activent, par leur propre Volume ou par le MatrixManager.
        [Test]
        public void OtherScenes_AreNotAffectedByMatrixRain()
        {
            var rainType = ProjectType("MatrixVision.Rendering.MatrixRain");
            var profiles = Enumerable.Range(0, QualitySettings.names.Length)
                .Select(i => QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset)
                .Where(a => a != null && a.volumeProfile != null)
                .Select(a => a.volumeProfile)
                .ToList();
            if (GraphicsSettings.GetRenderPipelineSettings<URPDefaultVolumeProfileSettings>() is { } defaults && defaults.volumeProfile != null)
                profiles.Add(defaults.volumeProfile);
            Assert.IsNotEmpty(profiles);

            foreach (var profile in profiles.Distinct())
            {
                var rain = profile.components.FirstOrDefault(c => c.GetType() == rainType);
                if (rain == null)
                    continue;
                var intensity = (VolumeParameter<float>)rainType.GetField("intensity").GetValue(rain);
                Assert.IsFalse(rain.active && intensity.overrideState && intensity.value > 0f,
                    $"{profile.name} (profil global) active la pluie Matrix dans toutes les scènes");
            }
        }

        [Test]
        public void OtherScenes_DoNotStartXRSimulation()
        {
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (settings == null || settings.AssignedSettings == null)
                return;
            Assert.IsFalse(settings.AssignedSettings.activeLoaders.Any(l => l.GetType().Name == "SimulationLoader"),
                "XR Simulation ne doit être activé qu'en lançant MatrixAR ou MatrixQuest (MatrixSimulationPlayMode)");
        }

        [Test]
        public void RendererFeatures_AreFullyWired()
        {
            var featureType = ProjectType("MatrixVision.Rendering.MatrixRainRendererFeature");
            var renderers = AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Settings" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid)))
                .ToList();
            Assert.That(renderers.Select(r => r.name), Is.SupersetOf(new[] { "PC_Renderer", "Mobile_Renderer", "Quest_Renderer" }));

            foreach (var renderer in renderers)
            {
                var feature = renderer.rendererFeatures.FirstOrDefault(f => f != null && featureType.IsInstanceOfType(f));
                Assert.IsNotNull(feature, $"{renderer.name} : Renderer Feature Matrix absente");
                var so = new SerializedObject(feature);
                foreach (var field in new[] { "shader", "surfaceShader", "environmentShader", "passthroughDimShader", "glyphAtlas" })
                    Assert.IsNotNull(so.FindProperty(field).objectReferenceValue, $"{renderer.name} : {field} non référencé (absent du build)");
                Assert.AreEqual(renderer.name == "Quest_Renderer", so.FindProperty("lightweightXR").boolValue,
                    $"{renderer.name} : mode XR léger incorrect");
                Assert.IsTrue(renderer.rendererFeatures.Any(f => f is ARBackgroundRendererFeature),
                    $"{renderer.name} : fond caméra AR absent");
            }
        }

        [Test]
        public void QualityLevels_UseTheRightRenderPipeline()
        {
            var names = QualitySettings.names;
            Assert.That(names, Is.SupersetOf(new[] { "Mobile", "PC", "Quest" }));
            var quest = (UniversalRenderPipelineAsset)QualitySettings.GetRenderPipelineAssetAt(Array.IndexOf(names, "Quest"));
            Assert.AreEqual("Quest_RPAsset", quest.name);
            Assert.IsFalse(quest.supportsHDR, "Quest : HDR à désactiver (performances du passthrough)");
        }

        [Test]
        public void ScenesAreInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            Assert.Contains(PhoneScene, scenes);
            Assert.Contains(QuestScene, scenes);
        }

        [Test]
        public void PhoneScene_LettersOnlyOnPlanes()
        {
            WithScene(PhoneScene, scene =>
            {
                var occlusion = Find<AROcclusionManager>(scene);
                Assert.IsNotNull(occlusion);
                // Le fond caméra ne doit pas écrire la profondeur réelle (elle rejetait le code des plans).
                Assert.AreEqual(OcclusionPreferenceMode.NoOcclusion, occlusion.requestedOcclusionPreferenceMode);

                var manager = new SerializedObject(FindProjectComponent(scene, "MatrixVision.MatrixManager"));
                Assert.IsFalse(manager.FindProperty("screenRain").boolValue, "Pas de lettres flottantes en overlay");
                Assert.IsFalse(manager.FindProperty("realWorldCode").boolValue, "Code reconstruit depuis la profondeur : expérimental");

                var planes = Find<ARPlaneManager>(scene);
                Assert.IsNotNull(planes.planePrefab.GetComponent(ProjectType("MatrixVision.AR.MatrixARPlaneMesh")),
                    "Le prefab de plan doit avoir le maillage à bord adouci");
            });
        }

        [Test]
        public void QuestScene_IsReadyForPassthroughAndRoomMesh()
        {
            WithScene(QuestScene, scene =>
            {
                var camera = Find<Unity.XR.CoreUtils.XROrigin>(scene).Camera;
                Assert.AreEqual(CameraClearFlags.SolidColor, camera.clearFlags);
                Assert.AreEqual(0f, camera.backgroundColor.a, "Passthrough : fond de caméra transparent requis");

                var meshes = Find<ARMeshManager>(scene);
                Assert.IsNotNull(meshes, "ARMeshManager absent");
                Assert.IsFalse(meshes.enabled, "Le maillage doit attendre la permission USE_SCENE");

                var controller = new SerializedObject(FindProjectComponent(scene, "MatrixVision.AR.MatrixARController"));
                Assert.IsTrue(controller.FindProperty("requestScenePermission").boolValue);
                var after = controller.FindProperty("enableAfterScenePermission");
                var enabledLater = Enumerable.Range(0, after.arraySize).Select(i => after.GetArrayElementAtIndex(i).objectReferenceValue).ToList();
                Assert.Contains(meshes, enabledLater);
            });
        }

        [Test]
        public void BuildTargets_SwitchBetweenPhoneAndQuest()
        {
            var targets = ProjectType("MatrixVision.Editor.MatrixBuildTargets", "Assembly-CSharp-Editor");
            bool wasQuest = ActiveAndroidLoader() == "OpenXRLoader";
            try
            {
                targets.GetMethod("ConfigureQuest").Invoke(null, null);
                Assert.AreEqual("OpenXRLoader", ActiveAndroidLoader());
                Assert.AreEqual(GraphicsDeviceType.Vulkan, PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)[0]);
                Assert.GreaterOrEqual((int)PlayerSettings.Android.minSdkVersion, 32);

                targets.GetMethod("ConfigurePhone").Invoke(null, null);
                Assert.AreEqual("ARCoreLoader", ActiveAndroidLoader());
                Assert.AreEqual(GraphicsDeviceType.OpenGLES3, PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)[0]);
                Assert.AreEqual(AndroidSdkVersions.AndroidApiLevel25, PlayerSettings.Android.minSdkVersion);
            }
            finally
            {
                targets.GetMethod(wasQuest ? "ConfigureQuest" : "ConfigurePhone").Invoke(null, null);
            }
        }

        [Test]
        public void ArScenes_SpawnNeo()
        {
            foreach (var path in new[] { PhoneScene, QuestScene })
            {
                WithScene(path, scene =>
                {
                    var spawner = new SerializedObject(FindProjectComponent(scene, "MatrixVision.Neo.MatrixNeoSpawner"));
                    foreach (var field in new[] { "neoPrefab", "arCamera", "planeManager", "matrixManager" })
                        Assert.IsNotNull(spawner.FindProperty(field).objectReferenceValue, $"{scene.name} : MatrixNeoSpawner.{field} non assigné");
                    Assert.AreEqual(2f, spawner.FindProperty("delay").floatValue, 0.01f, "Neo apparaît 2 s après le lancement");
                });
            }
        }

        [Test]
        public void ArScenes_CameraAndSurfacesShareTheSameSpace()
        {
            foreach (var path in new[] { PhoneScene, QuestScene })
            {
                WithScene(path, scene =>
                {
                    var origin = Find<Unity.XR.CoreUtils.XROrigin>(scene);
                    Assert.AreEqual(Vector3.zero, origin.transform.position, $"{scene.name} : XR Origin hors de l'origine");
                    Assert.AreEqual(Quaternion.identity, origin.transform.rotation, $"{scene.name} : XR Origin tourné");
                    // Un décalage de hauteur de caméra la sortirait du repère des plans et du maillage détectés.
                    Assert.AreEqual(0f, origin.CameraYOffset, $"{scene.name} : Camera Y Offset doit valoir 0 en AR");
                });
            }
        }

        static string ActiveAndroidLoader()
        {
            var manager = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android).AssignedSettings;
            Assert.AreEqual(1, manager.activeLoaders.Count, "Un seul loader XR attendu pour Android");
            return manager.activeLoaders[0].GetType().Name;
        }

        static Component FindComponent(Scene scene, Type type)
        {
            var component = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren(type, true))
                .FirstOrDefault();
            Assert.IsNotNull(component, $"{type.Name} absent de {scene.name}");
            return component;
        }

        static T Find<T>(Scene scene) where T : Component => (T)FindComponent(scene, typeof(T));

        static Component FindProjectComponent(Scene scene, string fullName) => FindComponent(scene, ProjectType(fullName));

        // Ouvre la scène en additif le temps du test, sans toucher aux scènes déjà ouvertes par l'utilisateur.
        static void WithScene(string path, Action<Scene> check)
        {
            var scene = SceneManager.GetSceneByPath(path);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;
            if (!alreadyOpen)
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                check(scene);
            }
            finally
            {
                if (!alreadyOpen)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
