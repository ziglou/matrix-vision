using MatrixVision.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MatrixVision
{
    /// <summary>
    /// Point unique pour activer ou désactiver les effets de l'expérience Matrix.
    /// Crée un Volume global caché, de priorité haute, dont les overrides passent devant le profil
    /// de la scène : les interrupteurs sont ici, les réglages fins (couleur, vitesse, tailles) restent dans le profil.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("MatrixVision/Matrix Manager")]
    public sealed class MatrixManager : MonoBehaviour
    {
        const float VolumePriority = 1000f;

        [Tooltip("Active ou coupe tout l'effet Matrix.")]
        [SerializeField] bool matrixEnabled = true;

        [Header("Plein écran")]
        [Tooltip("Lettres qui tombent au premier plan.")]
        [SerializeField] bool screenRain = true;

        [Tooltip("Teinte verte de la scène. Désactivée : couleurs d'origine.")]
        [SerializeField] bool sceneTint = true;

        [Header("Objets")]
        [Tooltip("Lettres qui coulent sur la surface des objets.")]
        [SerializeField] bool surfaceCode = true;

        [Tooltip("Tous les objets (sol compris). Désactivé : seulement ceux qui ont MatrixCodeTarget.")]
        [SerializeField] bool allObjects = true;

        [Tooltip("« Vision de Neo » : les objets deviennent noir-vert, uniquement du code.")]
        [SerializeField] bool neoVision;

        [Header("Lueur")]
        [Tooltip("Bloom réglé pour faire rayonner les têtes de colonne. Désactivé : Bloom du profil de la scène.")]
        [SerializeField] bool glow = true;

        [Tooltip("Force du Bloom quand la lueur est active.")]
        [SerializeField, Range(0f, 5f)] float glowIntensity = 1.5f;

        GameObject volumeObject;
        VolumeProfile profile;
        MatrixRain overrides;
        Bloom bloom;

        public bool MatrixEnabled { get => matrixEnabled; set { matrixEnabled = value; Apply(); } }
        public bool ScreenRain { get => screenRain; set { screenRain = value; Apply(); } }
        public bool SceneTint { get => sceneTint; set { sceneTint = value; Apply(); } }
        public bool SurfaceCode { get => surfaceCode; set { surfaceCode = value; Apply(); } }
        public bool AllObjects { get => allObjects; set { allObjects = value; Apply(); } }
        public bool NeoVision { get => neoVision; set { neoVision = value; Apply(); } }
        public bool Glow { get => glow; set { glow = value; Apply(); } }

        void OnEnable()
        {
            // Volume et profil en mémoire seulement : recréés à chaque activation, jamais sauvegardés dans la scène.
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            overrides = profile.Add<MatrixRain>();
            bloom = profile.Add<Bloom>();

            volumeObject = new GameObject("Matrix Manager Volume") { hideFlags = HideFlags.HideAndDontSave };
            volumeObject.layer = gameObject.layer;
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = VolumePriority;
            volume.sharedProfile = profile;

            Apply();
        }

        void OnDisable()
        {
            DestroyNow(volumeObject);
            DestroyNow(profile);
            volumeObject = null;
            profile = null;
            overrides = null;
            bloom = null;
        }

        void OnValidate() => Apply();

        void Apply()
        {
            if (overrides == null)
                return;

            // Effet coupé : intensité forcée à 0, les passes de rendu ne tournent plus.
            overrides.intensity.overrideState = !matrixEnabled;
            overrides.intensity.value = 0f;

            Force(overrides.screenRain, screenRain);
            Force(overrides.surfaceCode, surfaceCode);
            Force(overrides.surfaceAllObjects, allObjects);
            Force(overrides.surfaceReplace, neoVision);

            // Teinte active : on laisse la valeur du profil de la scène.
            overrides.sceneTint.overrideState = !sceneTint;
            overrides.sceneTint.value = 0f;

            // Seuil sous 1 : seules les têtes HDR et les glyphes illuminés rayonnent, pas la scène.
            bool bloomOn = matrixEnabled && glow;
            bloom.active = bloomOn;
            bloom.intensity.overrideState = bloomOn;
            bloom.intensity.value = glowIntensity;
            bloom.threshold.overrideState = bloomOn;
            bloom.threshold.value = 1.2f;
            bloom.scatter.overrideState = bloomOn;
            bloom.scatter.value = 0.6f;
        }

        static void Force(BoolParameter parameter, bool value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        static void DestroyNow(Object obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
