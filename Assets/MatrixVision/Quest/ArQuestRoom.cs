#if MATRIX_AR_FOUNDATION
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace MatrixVision.Quest
{
    /// <summary>
    /// Pièce réelle vue par OpenXR, via AR Foundation et le paquet Unity OpenXR: Meta :
    /// plans de la pièce scannée (Space Setup du Quest), maillage 3D de la pièce s'il est activé, passthrough.
    /// À placer sur le même objet que le <see cref="GameManager"/>, avec un AR Session et, sous le XR Origin,
    /// un AR Plane Manager (et un AR Mesh Manager pour couvrir aussi les meubles).
    /// Compilé seulement quand MATRIX_AR_FOUNDATION est défini (voir XrDefines, ajouté automatiquement).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MatrixVision/AR Quest Room")]
    public sealed class ArQuestRoom : MonoBehaviour, IQuestRoom
    {
        [Tooltip("Plans de la pièce scannée. Vide : cherché dans la scène.")]
        [SerializeField] ARPlaneManager planeManager;

        [Tooltip("Maillage 3D de la pièce (meubles compris). Facultatif. Vide : cherché dans la scène.")]
        [SerializeField] ARMeshManager meshManager;

        [Tooltip("Caméra du casque, qui porte l'AR Camera Manager. Vide : Camera.main.")]
        [SerializeField] Camera xrCamera;

        [Tooltip("Rend les surfaces de la pièce invisibles mais occultantes : les objets virtuels passent derrière " +
                 "la vraie table et les vrais murs. Le code Matrix les recouvre quand même.")]
        [SerializeField] bool occludeWithRoom = true;

        [Tooltip("Shader MatrixVision/Room Occluder (à assigner pour qu'il soit inclus dans le build).")]
        [SerializeField] Shader occluderShader;

        [Tooltip("La pièce est prête quand aucun nouveau plan n'est arrivé depuis ce temps, en secondes.")]
        [SerializeField] float settleTime = 0.5f;

        Material occluder;
        int planeCount = -1;
        float lastChange;

        void Awake()
        {
            if (planeManager == null)
                planeManager = FindAnyObjectByType<ARPlaneManager>();
            if (meshManager == null)
                meshManager = FindAnyObjectByType<ARMeshManager>();
            if (xrCamera == null)
                xrCamera = Camera.main;
            if (planeManager == null)
                Debug.LogWarning("ArQuestRoom : aucun AR Plane Manager, la pièce ne sera pas chargée.");

            if (occludeWithRoom)
            {
                if (occluderShader == null)
                    occluderShader = Shader.Find("MatrixVision/Room Occluder");
                if (occluderShader != null)
                    occluder = new Material(occluderShader) { name = "Matrix Room Occluder" };
            }
        }

        void OnDestroy()
        {
            if (occluder != null)
                Destroy(occluder);
        }

        void Update()
        {
            int count = planeManager != null ? planeManager.trackables.count : 0;
            if (count != planeCount)
            {
                planeCount = count;
                lastChange = Time.time;
            }
            if (occluder != null)
                ApplyOccluder();
        }

        public bool IsReady => planeCount > 0 && Time.time - lastChange >= settleTime;

        public float FloorHeight
        {
            get
            {
                // Le plan étiqueté sol, sinon le plus bas des plans horizontaux, sinon l'origine (suivi au sol).
                float floor = float.MaxValue;
                foreach (var plane in Planes())
                {
                    if (plane.alignment != PlaneAlignment.HorizontalUp)
                        continue;
                    if ((plane.classifications & PlaneClassifications.Floor) != 0)
                        return plane.center.y;
                    floor = Mathf.Min(floor, plane.center.y);
                }
                return floor < float.MaxValue ? floor : 0f;
            }
        }

        public bool TryFindTable(float minHeight, float maxHeight, out Vector3 center, out Vector2 size)
        {
            center = default;
            size = default;
            float floor = FloorHeight;
            ARPlane best = null;
            float bestScore = 0f;
            foreach (var plane in Planes())
            {
                float height = plane.center.y - floor;
                if (plane.alignment != PlaneAlignment.HorizontalUp || height < minHeight || height > maxHeight)
                    continue;

                // Une surface étiquetée table passe toujours devant une surface quelconque.
                float score = plane.size.x * plane.size.y +
                              ((plane.classifications & PlaneClassifications.Table) != 0 ? 1000f : 0f);
                if (score > bestScore)
                {
                    best = plane;
                    bestScore = score;
                }
            }

            if (best == null)
                return false;
            center = best.center;
            size = best.size;
            return true;
        }

        public IEnumerable<GameObject> GetSurfaces()
        {
            foreach (var plane in Planes())
                yield return plane.gameObject;
            if (meshManager != null && meshManager.meshes != null)
            {
                foreach (var mesh in meshManager.meshes)
                {
                    if (mesh != null)
                        yield return mesh.gameObject;
                }
            }
        }

        /// <summary>
        /// Le passthrough d'OpenXR apparaît là où l'image a un alpha à 0. Pour l'assombrir, on remplit le fond
        /// d'un noir d'autant plus opaque que l'opacité demandée est basse.
        /// </summary>
        public void SetPassthroughOpacity(float opacity)
        {
            if (xrCamera == null)
                return;
            xrCamera.clearFlags = CameraClearFlags.SolidColor;
            xrCamera.backgroundColor = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(opacity));
        }

        IEnumerable<ARPlane> Planes()
        {
            if (planeManager == null)
                yield break;
            foreach (var plane in planeManager.trackables)
                yield return plane;
        }

        // Les plans et les morceaux de maillage arrivent au fil de l'eau : on leur donne le matériau occultant.
        void ApplyOccluder()
        {
            foreach (var surface in GetSurfaces())
            {
                var r = surface.GetComponent<MeshRenderer>();
                if (r != null && r.sharedMaterial != occluder)
                    r.sharedMaterial = occluder;
            }
        }
    }
}
#endif
