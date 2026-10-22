using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace MatrixVision.AR
{
    /// <summary>
    /// Meta Quest : les plans, le maillage de la pièce et la profondeur d'environnement exigent la permission
    /// Android com.oculus.permission.USE_SCENE (« données de l'espace »). Les composants AR qui en dépendent
    /// restent désactivés dans la scène ; ce composant demande la permission au démarrage, puis les active.
    /// Le sous-système d'occlusion de Meta échoue définitivement s'il démarre sans elle.
    /// L'occlusion exige en plus que le passthrough tourne déjà : démarrée trop tôt (dès Start, avant que la
    /// session OpenXR suive et que la couche passthrough soit créée), elle échoue avec
    /// InsufficientResourcesPassthroughFB. Les <see cref="AROcclusionManager"/> sont donc activés plus tard,
    /// une fois la session en suivi et la caméra passthrough en marche.
    /// </summary>
    public sealed class ScenePermissionGate : MonoBehaviour
    {
        const string ScenePermission = "com.oculus.permission.USE_SCENE";

        [Tooltip("Composants à activer une fois la permission accordée (ARPlaneManager, ARMeshManager, AROcclusionManager…).")]
        [SerializeField] Behaviour[] enableAfterPermission;

        [Tooltip("Caméra passthrough à attendre avant de démarrer l'occlusion (trouvée automatiquement si vide).")]
        [SerializeField] ARCameraManager cameraManager;

        [Tooltip("Frames d'attente supplémentaires après le démarrage du passthrough, avant l'occlusion.")]
        [SerializeField, Min(0)] int framesAfterPassthrough = 30;

        [Tooltip("Délai maximal d'attente du passthrough, en secondes ; au-delà l'occlusion reste désactivée.")]
        [SerializeField, Min(1f)] float passthroughTimeout = 10f;

        [Tooltip("Démarrer aussi l'occlusion en Play Mode via Meta Quest Link. Exige « Passthrough over Meta Quest Link » " +
                 "activé dans l'application Meta Horizon Link ; sinon l'occlusion échoue (InsufficientResourcesPassthroughFB).")]
        [SerializeField] bool occlusionInEditor;

        public string State { get; private set; } = "non demandée";

        void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                OnGranted(ScenePermission);
                return;
            }
            State = "demandée…";
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += OnGranted;
            callbacks.PermissionDenied += _ => State = "refusée";
            callbacks.PermissionDeniedAndDontAskAgain += _ => State = "refusée (définitivement)";
            UnityEngine.Android.Permission.RequestUserPermission(ScenePermission, callbacks);
#else
            OnGranted(ScenePermission);
#endif
        }

        void OnGranted(string permission)
        {
            State = "accordée";
            Debug.Log($"ScenePermissionGate : {permission} accordée, activation des composants AR.");
            if (enableAfterPermission == null)
                return;
            bool hasOcclusion = false;
            foreach (var behaviour in enableAfterPermission)
            {
                if (behaviour == null)
                    continue;
                if (behaviour is AROcclusionManager)
                    hasOcclusion = true;
                else
                    behaviour.enabled = true;
            }
            if (hasOcclusion)
                StartCoroutine(EnableOcclusionWhenPassthroughReady());
        }

        IEnumerator EnableOcclusionWhenPassthroughReady()
        {
#if UNITY_EDITOR
            if (!occlusionInEditor)
            {
                Debug.Log("ScenePermissionGate : occlusion non démarrée dans l’éditeur (Link). Cocher « Occlusion In Editor » " +
                          "si le passthrough over Link est activé.");
                yield break;
            }
#endif
            if (cameraManager == null)
                cameraManager = FindAnyObjectByType<ARCameraManager>();

            float deadline = Time.realtimeSinceStartup + passthroughTimeout;
            while (ARSession.state < ARSessionState.SessionTracking ||
                   cameraManager == null || !cameraManager.enabled ||
                   cameraManager.subsystem == null || !cameraManager.subsystem.running)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogWarning($"ScenePermissionGate : passthrough pas prêt après {passthroughTimeout} s " +
                                     $"(session : {ARSession.state}), occlusion laissée désactivée.");
                    yield break;
                }
                yield return null;
            }
            for (int i = 0; i < framesAfterPassthrough; i++)
                yield return null;

            State = "accordée, occlusion active";
            Debug.Log("ScenePermissionGate : passthrough prêt, activation de l'occlusion.");
            foreach (var behaviour in enableAfterPermission)
            {
                if (behaviour is AROcclusionManager)
                    behaviour.enabled = true;
            }
        }
    }
}
