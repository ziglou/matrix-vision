using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Management;

namespace MatrixVision.Tests
{
    /// <summary>
    /// Tests de bout en bout dans l'éditeur avec XR Simulation (pièce de test MatrixTestRoom) :
    /// la scène démarre sans erreur, la caméra simulée balaie la pièce, des surfaces (plans ou maillage)
    /// reçoivent du code, la vague part, et une capture d'écran contient bien des lettres vertes.
    /// XR Simulation est activé le temps des tests seulement (le projet n'a pas de loader XR imposé pour l'éditeur,
    /// pour ne pas toucher aux autres scènes). Captures dans Temp/MatrixVisionTests.
    /// </summary>
    public class MatrixSimulationTests : IPrebuildSetup
    {
        const float DetectionTimeout = 40f;
        const float MinimumCodeCoverage = 0.003f; // part minimale des pixels occupés par des lettres

        static readonly Type ControllerType = Type.GetType("MatrixVision.AR.MatrixARController, Assembly-CSharp", true);
        // Assembly XR Simulation : éditeur et PC uniquement, donc atteinte par réflexion (le projet cible Android).
        static readonly Type PoseProviderType =
            Type.GetType("UnityEngine.XR.Simulation.SimulationCameraPoseProvider, Unity.XR.Simulation");

        // Avant l'entrée en mode Play : XR Simulation pour ce lancement seulement (MatrixSimulationPlayMode,
        // qui rétablit la configuration d'origine en sortant du mode Play).
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("MatrixVision.Editor.MatrixSimulationPlayMode, Assembly-CSharp-Editor", true)
                .GetMethod("EnableForNextPlayMode")
                .Invoke(null, null);
#endif
        }

        [UnityTearDown]
        public IEnumerator StopXR()
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && manager.isInitializationComplete)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator PhoneScene_CodeAppearsOnDetectedPlanes()
        {
            // Rendu du téléphone (Mobile_RPAsset), quel que soit le niveau de qualité courant de l'éditeur.
            yield return WithQuality("Mobile", RunScene("Assets/Scenes/MatrixAR.unity", "visiblePlaneCount", "phone"));
        }

        [UnityTest]
        public IEnumerator QuestScene_CodeAppearsOnRoomMesh()
        {
            // Rendu du casque (Quest_RPAsset : mode XR léger, voile sur le passthrough) et non celui du téléphone.
            yield return WithQuality("Quest", RunScene("Assets/Scenes/MatrixQuest.unity", "visibleMeshCount", "quest"));
        }

        static IEnumerator WithQuality(string level, IEnumerator test)
        {
            int previousQuality = QualitySettings.GetQualityLevel();
            int quality = Array.IndexOf(QualitySettings.names, level);
            Assert.GreaterOrEqual(quality, 0, $"Niveau de qualité « {level} » absent");
            QualitySettings.SetQualityLevel(quality, true);
            try
            {
                yield return test;
            }
            finally
            {
                QualitySettings.SetQualityLevel(previousQuality, true);
            }
        }

        IEnumerator RunScene(string scenePath, string surfaceCounter, string captureName)
        {
            // Redémarrage complet de XR, comme au lancement de l'appli : XR Simulation ne prépare sa pièce
            // qu'au démarrage de la session, et un maillage démarré après coup ne la recevrait jamais.
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            Assert.IsNotNull(manager, "XR Plug-in Management absent");
            if (manager.isInitializationComplete)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
                yield return null;
            }
            manager.InitializeLoaderSync();
            var loader = manager.activeLoader;
            Assert.IsNotNull(loader, "XR Simulation n'a pas démarré");
            StringAssert.Contains("Simulation", loader.GetType().Name, "Le mode Play doit utiliser XR Simulation");
            manager.StartSubsystems();

            yield return SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
            yield return null;

            var controller = UnityEngine.Object.FindFirstObjectByType(ControllerType);
            Assert.IsNotNull(controller, "MatrixARController absent de la scène");
            var counter = ControllerType.GetField(surfaceCounter, BindingFlags.Instance | BindingFlags.NonPublic);
            var waveStart = ControllerType.GetField("waveStartTime", BindingFlags.Instance | BindingFlags.NonPublic);
            var firstWaveDone = ControllerType.GetField("firstWaveDone", BindingFlags.Instance | BindingFlags.NonPublic);

            // Balayage lent de la pièce par la caméra simulée, jusqu'à ce que des surfaces portent du code.
            float start = Time.realtimeSinceStartup;
            int surfaces = 0;
            bool waveSeen = false;
            while (Time.realtimeSinceStartup - start < DetectionTimeout)
            {
                float elapsed = Time.realtimeSinceStartup - start;
                SetSimulatedCameraPose(new Vector3(0f, 1.5f, -1.2f),
                    Quaternion.Euler(25f + 10f * Mathf.Sin(elapsed * 0.7f), 55f * Mathf.Sin(elapsed * 0.35f), 0f));
                surfaces = (int)counter.GetValue(controller);
                waveSeen |= (float)waveStart.GetValue(controller) >= 0f || (bool)firstWaveDone.GetValue(controller);
                if (surfaces > 0 && elapsed > 4f)
                    break;
                yield return null;
            }
            Assert.Greater(surfaces, 0, $"Aucune surface avec du code après {DetectionTimeout} s ({surfaceCounter})");
            Assert.IsTrue(waveSeen, "La vague de balayage n'est pas partie à la première surface");

            // Regard vers la table et le mur du fond, fin des fondus, puis capture.
            SetSimulatedCameraPose(new Vector3(0f, 1.5f, -1.2f), Quaternion.Euler(28f, 20f, 0f));
            float settle = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - settle < 2f)
                yield return null;
            yield return new WaitForEndOfFrame();

            var capture = ScreenCapture.CaptureScreenshotAsTexture(CaptureScale());
            try
            {
                string folder = Path.Combine(Application.dataPath, "..", "Temp", "MatrixVisionTests");
                Directory.CreateDirectory(folder);
                string file = Path.GetFullPath(Path.Combine(folder, captureName + ".png"));
                File.WriteAllBytes(file, capture.EncodeToPNG());

                float coverage = CodeCoverage(capture);
                Debug.Log($"[MatrixVision] {captureName} : {surfaces} surface(s) avec du code, lettres sur {coverage:P2} de l'image — {file}");
                Assert.Greater(coverage, MinimumCodeCoverage, $"Presque aucune lettre à l'image ({coverage:P2}) : voir {file}");
            }
            finally
            {
                UnityEngine.Object.Destroy(capture);
            }

            yield return CheckNeo(captureName);
        }

        // Neo : apparu après le délai, matérialisé, au sol, à distance raisonnable devant la caméra et tourné vers elle.
        IEnumerator CheckNeo(string captureName)
        {
            var spawnerType = Type.GetType("MatrixVision.Neo.MatrixNeoSpawner, Assembly-CSharp", true);
            var neoType = Type.GetType("MatrixVision.Neo.MatrixNeo, Assembly-CSharp", true);
            var spawner = UnityEngine.Object.FindFirstObjectByType(spawnerType);
            Assert.IsNotNull(spawner, "MatrixNeoSpawner absent de la scène");

            Component neo = null;
            bool materialized = false;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 20f)
            {
                neo = (Component)spawnerType.GetProperty("Neo").GetValue(spawner);
                materialized = neo != null && (bool)neoType.GetProperty("Materialized").GetValue(neo);
                if (materialized)
                    break;
                yield return null;
            }
            Assert.IsNotNull(neo, "Neo n'est pas apparu");
            Assert.IsTrue(materialized, "Neo ne s'est pas matérialisé en 20 s");

            var camera = Camera.main;
            var toNeo = Vector3.ProjectOnPlane(neo.transform.position - camera.transform.position, Vector3.up);
            Assert.That(toNeo.magnitude, Is.InRange(2.0f, 3.8f), "Neo doit apparaître à plus de 2 m, sans être trop loin");
            // Au sol (pièce de test : sol à 1,5 m sous la caméra), pas sur la table ou le canapé.
            Assert.That(camera.transform.position.y - neo.transform.position.y, Is.InRange(1.3f, 1.7f), "Neo doit être posé au sol");
            Assert.Greater(Vector3.Dot(neo.transform.forward, -toNeo.normalized), 0.9f, "Neo doit faire face à la caméra");

            // Capture de Neo, caméra tournée vers son buste depuis sa position actuelle.
            var target = neo.transform.position + Vector3.up * 1.2f;
            var eye = camera.transform.position;
            SetSimulatedCameraPose(eye, Quaternion.LookRotation(target - eye, Vector3.up));
            float settle = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - settle < 1f)
                yield return null;
            yield return new WaitForEndOfFrame();
            var capture = ScreenCapture.CaptureScreenshotAsTexture();
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "MatrixVisionTests", captureName + "_neo.png"));
            File.WriteAllBytes(file, capture.EncodeToPNG());
            UnityEngine.Object.Destroy(capture);
            Debug.Log($"[MatrixVision] {captureName} : Neo à {toNeo.magnitude:0.00} m — {file}");
        }

        // Capture d'au moins ~1600 px de large : dans une petite vue Game, les glyphes sont trop flous pour être comptés.
        static int CaptureScale() => Mathf.Clamp(Mathf.CeilToInt(1600f / Mathf.Max(1, Screen.width)), 1, 4);

        // Pixels de lettres : vert vif, nettement plus lumineux que la vidéo teintée ou le passthrough assombri.
        static float CodeCoverage(Texture2D image)
        {
            var pixels = image.GetPixels32();
            int code = 0;
            foreach (var p in pixels)
            {
                if (p.g > 130 && p.g > p.r + 40 && p.g > p.b + 20)
                    code++;
            }
            return pixels.Length > 0 ? (float)code / pixels.Length : 0f;
        }

        // La caméra simulée est pilotée au clavier et à la souris : on lui impose une pose (méthode privée du paquet).
        static void SetSimulatedCameraPose(Vector3 position, Quaternion rotation)
        {
            if (PoseProviderType == null)
                return;
            var provider = UnityEngine.Object.FindFirstObjectByType(PoseProviderType);
            if (provider == null)
                return;
            PoseProviderType
                .GetMethod("UpdatePose", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(provider, new object[] { new Pose(position, rotation) });
        }
    }
}
