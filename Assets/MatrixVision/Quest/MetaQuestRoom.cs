#if MATRIX_META_XR
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace MatrixVision.Quest
{
    /// <summary>
    /// Pièce réelle vue par le Meta XR SDK : surfaces de MR Utility Kit et passthrough du casque.
    /// À placer sur le même objet que le <see cref="GameManager"/>, avec un MRUK et un Effect Mesh dans la scène
    /// (l'Effect Mesh donne aux surfaces un maillage, que le code Matrix recouvre).
    /// Compilé seulement quand MATRIX_META_XR est défini (voir MetaXrDefine, ajouté automatiquement).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MatrixVision/Meta Quest Room")]
    public sealed class MetaQuestRoom : MonoBehaviour, IQuestRoom
    {
        [Tooltip("Couche passthrough du Camera Rig (Building Block Passthrough).")]
        [SerializeField] OVRPassthroughLayer passthroughLayer;

        bool sceneLoaded;

        void Start()
        {
            if (passthroughLayer == null)
                passthroughLayer = FindAnyObjectByType<OVRPassthroughLayer>();

            if (MRUK.Instance != null)
                MRUK.Instance.RegisterSceneLoadedCallback(() => sceneLoaded = true);
            else
                Debug.LogWarning("MetaQuestRoom : aucun MRUK dans la scène, la pièce ne sera pas chargée.");
        }

        MRUKRoom Room => MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;

        public bool IsReady => sceneLoaded && Room != null;

        public float FloorHeight
        {
            get
            {
                var floor = Room != null ? Room.FloorAnchor : null;
                return floor != null ? floor.transform.position.y : 0f;
            }
        }

        public bool TryFindTable(float minHeight, float maxHeight, out Vector3 center, out Vector2 size)
        {
            center = default;
            size = default;
            var room = Room;
            if (room == null)
                return false;

            float floor = FloorHeight;
            MRUKAnchor best = null;
            float bestScore = 0f;
            foreach (var anchor in room.Anchors)
            {
                if (!anchor.PlaneRect.HasValue)
                    continue;

                // Sur une ancre plane de MRUK, l'axe Z local est la normale du plan.
                bool horizontalUp = Vector3.Dot(anchor.transform.forward, Vector3.up) > 0.9f;
                float height = anchor.transform.position.y - floor;
                if (!horizontalUp || height < minHeight || height > maxHeight)
                    continue;

                var rect = anchor.PlaneRect.Value;
                float area = rect.width * rect.height;
                // Une surface étiquetée table passe toujours devant une surface quelconque.
                float score = area + (anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) ? 1000f : 0f);
                if (score > bestScore)
                {
                    best = anchor;
                    bestScore = score;
                }
            }

            if (best == null)
                return false;

            var plane = best.PlaneRect.Value;
            center = best.transform.TransformPoint(plane.center);
            size = plane.size;
            return true;
        }

        public IEnumerable<GameObject> GetSurfaces()
        {
            var room = Room;
            if (room == null)
                yield break;
            // L'Effect Mesh crée le maillage de chaque surface en enfant de son ancre.
            foreach (var anchor in room.Anchors)
                yield return anchor.gameObject;
        }

        public void SetPassthroughOpacity(float opacity)
        {
            if (passthroughLayer != null)
                passthroughLayer.textureOpacity = Mathf.Clamp01(opacity);
        }
    }
}
#endif
