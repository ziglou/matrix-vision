using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace MatrixVision.Neo
{
    /// <summary>
    /// Fait apparaître le personnage quelques secondes après le lancement, sur une zone de sol libre :
    /// ni sur une table ou un meuble, ni dans un mur, visible depuis la caméra, et à distance respectable
    /// (au moins <see cref="minDistance"/> m) pour ne pas surprendre. Tourné vers la caméra.
    /// Les plans et le maillage de la pièce portent des colliders : les vérifications sont des requêtes physiques.
    /// Suit les interrupteurs Neo / Neo fait de code du <see cref="MatrixManager"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatrixNeoSpawner : MonoBehaviour
    {
        [SerializeField] MatrixNeo neoPrefab;
        [SerializeField] Camera arCamera;
        [SerializeField] ARPlaneManager planeManager;
        [SerializeField] MatrixManager matrixManager;

        [Tooltip("Délai, en secondes, entre le lancement et l'apparition.")]
        [SerializeField] float delay = 2f;

        [Header("Emplacement")]
        [Tooltip("Distance minimale, en mètres, entre la caméra et le personnage (ne pas surprendre).")]
        [SerializeField] float minDistance = 2.2f;

        [Tooltip("Distance préférée, en mètres.")]
        [SerializeField] float preferredDistance = 2.6f;

        [Tooltip("Distance maximale, en mètres.")]
        [SerializeField] float maxDistance = 3.6f;

        [Tooltip("Écart maximal, en degrés, avec la direction du regard (le personnage reste dans le champ).")]
        [SerializeField] float maxAngle = 45f;

        [Tooltip("Rayon libre autour du personnage, en mètres (murs, meubles).")]
        [SerializeField] float clearanceRadius = 0.4f;

        [Tooltip("Hauteur du personnage, en mètres.")]
        [SerializeField] float characterHeight = 1.8f;

        [Tooltip("Sans emplacement sûr après ce délai (s), on accepte une zone de sol pas encore détectée (jamais une table).")]
        [SerializeField] float fallbackAfter = 12f;

        [Tooltip("Hauteur supposée des yeux au-dessus du sol, tant qu'aucun sol n'est détecté.")]
        [SerializeField] float assumedEyeHeight = 1.5f;

        [Tooltip("Pendant ce temps après l'apparition, le personnage se recale sur le sol s'il est détecté plus tard.")]
        [SerializeField] float floorSnapWindow = 10f;

        const float FloorTolerance = 0.12f;
        const float SearchInterval = 0.25f;

        MatrixNeo neo;
        float spawnTime = -1f;
        float nextSearch;
        bool onDetectedFloor;

        /// <summary>Personnage apparu (null avant l'apparition).</summary>
        public MatrixNeo Neo => neo;

        void Reset()
        {
            arCamera = Camera.main;
            planeManager = FindAnyObjectByType<ARPlaneManager>();
            matrixManager = FindAnyObjectByType<MatrixManager>();
        }

        void Update()
        {
            bool enabledByManager = matrixManager == null || (matrixManager.MatrixEnabled && matrixManager.NeoEnabled);
            bool madeOfCode = matrixManager != null && matrixManager.NeoAsCode;

            if (neo == null)
            {
                if (enabledByManager && Time.timeSinceLevelLoad >= delay && Time.time >= nextSearch && neoPrefab != null && arCamera != null)
                {
                    nextSearch = Time.time + SearchInterval;
                    bool strict = Time.timeSinceLevelLoad < delay + fallbackAfter;
                    if (TryFindSpot(strict, out var position, out onDetectedFloor))
                        Spawn(position, madeOfCode);
                }
                return;
            }

            neo.gameObject.SetActive(enabledByManager);
            neo.SetMadeOfCode(madeOfCode);

            // Apparu sur un sol estimé : dès que le sol réel est détecté, on l'y repose.
            if (!onDetectedFloor && Time.time - spawnTime < floorSnapWindow && TryGetFloorHeight(out float floor))
            {
                var position = neo.transform.position;
                neo.transform.position = new Vector3(position.x, floor, position.z);
                onDetectedFloor = true;
            }
        }

        void Spawn(Vector3 position, bool madeOfCode)
        {
            var toCamera = Vector3.ProjectOnPlane(arCamera.transform.position - position, Vector3.up);
            var rotation = Quaternion.LookRotation(toCamera.sqrMagnitude > 1e-4f ? toCamera : Vector3.forward, Vector3.up);
            neo = Instantiate(neoPrefab, position, rotation);
            spawnTime = Time.time;
            neo.Materialize(madeOfCode);
        }

        /// <summary>
        /// Cherche un emplacement : devant la caméra (distances et angles, du plus au moins préféré), sur le sol,
        /// sans meuble dessous ni obstacle autour, et visible. strict : le sol doit avoir été détecté à cet endroit.
        /// </summary>
        public bool TryFindSpot(bool strict, out Vector3 spot, out bool onFloor)
        {
            spot = default;
            onFloor = TryGetFloorHeight(out float floor);
            if (!onFloor)
            {
                if (strict)
                    return false;
                floor = arCamera.transform.position.y - assumedEyeHeight;
            }

            var cameraPosition = arCamera.transform.position;
            var forward = Vector3.ProjectOnPlane(arCamera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.ProjectOnPlane(arCamera.transform.up, Vector3.up);
            forward.Normalize();

            float[] distances = { preferredDistance, preferredDistance - 0.2f, preferredDistance + 0.4f, minDistance, maxDistance };
            float[] angles = { 0f, 15f, -15f, 30f, -30f, maxAngle, -maxAngle };
            foreach (float d in distances)
            {
                float distance = Mathf.Clamp(d, minDistance, maxDistance);
                foreach (float angle in angles)
                {
                    var direction = Quaternion.AngleAxis(Mathf.Clamp(angle, -maxAngle, maxAngle), Vector3.up) * forward;
                    var candidate = cameraPosition + direction * distance;
                    candidate.y = floor;
                    if (IsFreeFloorSpot(candidate, cameraPosition, strict))
                    {
                        spot = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        bool IsFreeFloorSpot(Vector3 spot, Vector3 cameraPosition, bool strict)
        {
            // Rien au-dessus du sol à cet endroit : le premier obstacle rencontré en descendant doit être le sol.
            var from = spot + Vector3.up * (characterHeight + 0.4f);
            if (Physics.Raycast(from, Vector3.down, out var hit, characterHeight + 0.4f + FloorTolerance + 0.3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.point.y > spot.y + FloorTolerance)
                    return false; // table, lit, meuble…
            }
            else if (strict)
            {
                return false; // sol pas encore détecté à cet endroit
            }

            // Place libre autour (murs, meubles), sans toucher le sol lui-même.
            var bottom = spot + Vector3.up * (clearanceRadius + FloorTolerance + 0.05f);
            var top = spot + Vector3.up * (characterHeight - clearanceRadius);
            if (Physics.CheckCapsule(bottom, top, clearanceRadius, ~0, QueryTriggerInteraction.Ignore))
                return false;

            // Visible depuis la caméra (pas derrière un mur).
            return !Physics.Linecast(cameraPosition, spot + Vector3.up * (characterHeight * 0.8f), ~0, QueryTriggerInteraction.Ignore);
        }

        // Sol : plus bas plan horizontal orienté vers le haut, au moins 80 cm sous les yeux (écarte tables et
        // bureaux), à moins de 6 m ; à défaut (maillage de pièce seul), le premier obstacle sous la caméra.
        bool TryGetFloorHeight(out float height)
        {
            height = 0f;
            var cameraPosition = arCamera.transform.position;
            bool found = false;
            if (planeManager != null)
            {
                foreach (var plane in planeManager.trackables)
                {
                    if (plane.alignment != PlaneAlignment.HorizontalUp || plane.subsumedBy != null)
                        continue;
                    float y = plane.center.y;
                    if (cameraPosition.y - y < 0.8f || Vector3.Distance(plane.center, cameraPosition) > 6f)
                        continue;
                    if (!found || y < height)
                        height = y;
                    found = true;
                }
            }
            if (!found && Physics.Raycast(cameraPosition, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)
                && cameraPosition.y - hit.point.y >= 0.8f)
            {
                height = hit.point.y;
                found = true;
            }
            return found;
        }
    }
}
