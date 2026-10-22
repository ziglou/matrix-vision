using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace MatrixVision.AR
{
    /// <summary>
    /// Occlusion par le monde réel pour tous les matériaux, sans les modifier : chaque frame où la profondeur
    /// d'environnement du casque est disponible, <see cref="Rendering.EnvironmentDepthOcclusionFeature"/> écrit
    /// cette profondeur dans le tampon de profondeur avant les objets opaques. Les objets virtuels placés derrière
    /// un mur, un meuble ou une personne échouent alors au test de profondeur et laissent voir le passthrough.
    /// À placer sur la caméra XR, avec <see cref="AROcclusionManager"/> et <see cref="ARShaderOcclusion"/>
    /// (ce dernier publie la texture de profondeur et ses matrices de reprojection).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class EnvironmentDepthOcclusion : MonoBehaviour
    {
        static readonly int WorldToTrackingId = Shader.PropertyToID("_MV_WorldToTracking");
        static readonly int BiasId = Shader.PropertyToID("_MV_OcclusionBias");

        [SerializeField] AROcclusionManager occlusionManager;
        [Tooltip("Recul de la profondeur réelle, en mètres : évite que les objets posés sur une surface réelle " +
                 "(ou les plans affichés) clignotent à cause du bruit de la profondeur.")]
        [SerializeField, Min(0f)] float bias = 0.03f;
        [Tooltip("Recul supplémentaire proportionnel à la distance (le bruit de la profondeur croît avec elle).")]
        [SerializeField, Range(0f, 0.2f)] float relativeBias = 0.02f;

        /// <summary>Vrai quand la profondeur d'environnement est reçue : la passe d'occlusion tourne alors.</summary>
        public static bool IsActive { get; private set; }

        Camera xrCamera;
        Transform trackingSpace;
        bool frameReceived;

        void Reset() => occlusionManager = GetComponent<AROcclusionManager>();

        void Awake()
        {
            xrCamera = GetComponent<Camera>();
            if (occlusionManager == null)
                occlusionManager = GetComponent<AROcclusionManager>();
            // Les poses de la caméra de profondeur sont exprimées dans l'espace de session (celui des trackables).
            var origin = GetComponentInParent<XROrigin>();
            trackingSpace = origin != null && origin.TrackablesParent != null ? origin.TrackablesParent : transform.parent;
        }

        void OnEnable()
        {
            if (occlusionManager != null)
                occlusionManager.frameReceived += OnFrameReceived;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        void OnDisable()
        {
            if (occlusionManager != null)
                occlusionManager.frameReceived -= OnFrameReceived;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            frameReceived = false;
            IsActive = false;
        }

        void OnFrameReceived(AROcclusionFrameEventArgs args) => frameReceived = true;

        void Update()
        {
            IsActive = frameReceived && occlusionManager != null && occlusionManager.enabled &&
                occlusionManager.subsystem != null && occlusionManager.subsystem.running;
        }

        // Après la locomotion de la frame : l'espace de session suit l'origine XR.
        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != xrCamera)
                return;
            Shader.SetGlobalMatrix(WorldToTrackingId, trackingSpace != null ? trackingSpace.worldToLocalMatrix : Matrix4x4.identity);
            Shader.SetGlobalVector(BiasId, new Vector4(bias, relativeBias, 0f, 0f));
        }
    }
}
