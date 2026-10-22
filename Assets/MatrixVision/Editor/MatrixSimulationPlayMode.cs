using System.Linq;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MatrixVision.Editor
{
    /// <summary>
    /// XR Simulation uniquement pour les scènes Matrix : en entrant en mode Play depuis MatrixAR ou MatrixQuest,
    /// le loader XR de l'éditeur (réglages Standalone) passe sur XR Simulation (pièce de test) ; il retrouve
    /// sa configuration d'origine en sortant du mode Play. Les autres scènes du projet (exemples XR Interaction
    /// Toolkit, etc.) gardent leur comportement normal.
    /// </summary>
    [InitializeOnLoad]
    public static class MatrixSimulationPlayMode
    {
        const string SimulationLoader = "UnityEngine.XR.Simulation.SimulationLoader";
        const string SavedLoadersKey = "MatrixVision.SavedStandaloneLoaders";
        static readonly string[] SimulatedScenes = { MatrixBuildTargets.PhoneScene, MatrixBuildTargets.QuestScene };

        static MatrixSimulationPlayMode()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
                EnableForMatrixScenes();
            else if (change == PlayModeStateChange.EnteredEditMode)
                Restore();
        }

        static void EnableForMatrixScenes()
        {
            if (SimulatedScenes.Contains(SceneManager.GetActiveScene().path))
                EnableForNextPlayMode();
        }

        /// <summary>
        /// XR Simulation pour le prochain mode Play seulement (appelé aussi par les tests en mode Play avant leur
        /// lancement) ; la configuration d'origine revient en sortant du mode Play.
        /// </summary>
        public static void EnableForNextPlayMode()
        {
            var manager = StandaloneManager();
            if (manager == null)
                return;

            var current = manager.activeLoaders.Select(l => l.GetType().FullName).ToList();
            if (current.Count == 1 && current[0] == SimulationLoader)
                return;

            SessionState.SetString(SavedLoadersKey, string.Join(";", current));
            foreach (var loader in current)
                XRPackageMetadataStore.RemoveLoader(manager, loader, BuildTargetGroup.Standalone);
            XRPackageMetadataStore.AssignLoader(manager, SimulationLoader, BuildTargetGroup.Standalone);
            Debug.Log("[MatrixVision] XR Simulation activé pour ce mode Play (pièce de test).");
        }

        static void Restore()
        {
            string saved = SessionState.GetString(SavedLoadersKey, null);
            if (saved == null)
                return;
            SessionState.EraseString(SavedLoadersKey);

            var manager = StandaloneManager();
            if (manager == null)
                return;
            XRPackageMetadataStore.RemoveLoader(manager, SimulationLoader, BuildTargetGroup.Standalone);
            foreach (var loader in saved.Split(';').Where(l => l.Length > 0))
                XRPackageMetadataStore.AssignLoader(manager, loader, BuildTargetGroup.Standalone);
            AssetDatabase.SaveAssets();
        }

        static UnityEngine.XR.Management.XRManagerSettings StandaloneManager()
        {
            var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            return settings != null ? settings.AssignedSettings : null;
        }
    }
}
