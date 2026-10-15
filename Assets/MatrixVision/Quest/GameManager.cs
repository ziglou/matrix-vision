using System.Collections;
using System.Collections.Generic;
using MatrixVision.Rendering;
using UnityEngine;

namespace MatrixVision.Quest
{
    /// <summary>
    /// Manager principal de la quête Matrix sur Meta Quest 3 : une méthode par étape du plan.
    /// 0. <see cref="SetupMixedReality"/> · 1. <see cref="DetectPlaneSurface"/> · 2. <see cref="SpawnPills"/> ·
    /// 3. <see cref="WaitForSwallow"/> · 4. <see cref="PlayBluePillEnding"/> · 5. <see cref="RevealMatrix"/> ·
    /// 6. <see cref="SummonNeo"/> · 7. <see cref="FinishQuest"/>.
    /// La saisie des pilules à la main vient des composants Hand Grab (Interaction SDK) de leurs prefabs :
    /// le manager regarde seulement si une pilule arrive à la bouche.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MatrixVision/Game Manager")]
    public sealed class GameManager : MonoBehaviour
    {
        public enum Pill { None, Red, Blue }

        [Header("Références")]
        [Tooltip("Caméra du casque (CenterEyeAnchor). Vide : Camera.main.")]
        [SerializeField] Transform head;

        [Tooltip("Matrix Manager de la scène : l'effet est coupé au départ et piloté par la quête.")]
        [SerializeField] MatrixManager matrixManager;

        [Tooltip("Composant qui implémente IQuestRoom (MetaQuestRoom). Vide : cherché sur cet objet.")]
        [SerializeField] MonoBehaviour roomSource;

        [Header("1. Surface plane")]
        [Tooltip("Hauteur minimale d'une table au-dessus du sol, en mètres.")]
        [SerializeField] float minTableHeight = 0.5f;

        [Tooltip("Hauteur maximale d'une table au-dessus du sol, en mètres.")]
        [SerializeField] float maxTableHeight = 1.2f;

        [Tooltip("Attente maximale de la pièce scannée avant le plateau virtuel, en secondes.")]
        [SerializeField] float surfaceTimeout = 5f;

        [Tooltip("Plateau posé devant le joueur si aucune table n'est trouvée. Vide : un cube plat.")]
        [SerializeField] GameObject fallbackTablePrefab;

        [Tooltip("Hauteur du plateau virtuel sous la tête, en mètres (environ la taille).")]
        [SerializeField] float fallbackTableDrop = 0.55f;

        [Tooltip("Surfaces utilisées sans Meta XR SDK (test dans l'éditeur avec une fausse pièce).")]
        [SerializeField] GameObject[] fallbackSurfaces = new GameObject[0];

        [Header("2. Pilules")]
        [SerializeField] GameObject redPillPrefab;
        [SerializeField] GameObject bluePillPrefab;

        [Tooltip("Écart entre les deux pilules, en mètres.")]
        [SerializeField] float pillSpacing = 0.2f;

        [Tooltip("Hauteur des pilules au-dessus de la surface, en mètres.")]
        [SerializeField] float pillHover = 0.03f;

        [Header("3. Geste « manger »")]
        [Tooltip("Position de la bouche par rapport à la tête (repère local de la caméra), en mètres.")]
        [SerializeField] Vector3 mouthOffset = new(0f, -0.08f, 0.05f);

        [Tooltip("Rayon de la zone de la bouche, en mètres.")]
        [SerializeField] float mouthRadius = 0.1f;

        [Tooltip("Temps passé par la pilule dans la zone de la bouche pour être avalée, en secondes.")]
        [SerializeField] float swallowTime = 0.5f;

        [SerializeField] AudioClip swallowSound;

        [Header("4. Pilule bleue")]
        [Tooltip("Une réplique est choisie au hasard.")]
        [SerializeField] AudioClip[] bluePillLines = new AudioClip[0];

        [Tooltip("Relance la quête après la réplique de la pilule bleue.")]
        [SerializeField] bool restartAfterBlue = true;

        [Header("5. Révélation de la matrice")]
        [Tooltip("Durée pour que la matrice recouvre toute la pièce, en secondes.")]
        [SerializeField] float revealDuration = 9f;

        [Tooltip("Rayon final de la révélation, en mètres : assez pour couvrir la pièce.")]
        [SerializeField] float revealRadius = 12f;

        [Tooltip("Opacité finale du passthrough : bas = la matrice prend tout le champ de vision.")]
        [SerializeField, Range(0f, 1f)] float finalPassthroughOpacity = 0.1f;

        [SerializeField] AudioClip revealSound;

        [Header("6. Neo")]
        [Tooltip("Neo dans la scène, désactivé au départ. Pieds à l'origine, regard vers +Z.")]
        [SerializeField] GameObject neo;

        [Tooltip("Attente entre la fin de la révélation et l'apparition de Neo, en secondes.")]
        [SerializeField] float neoDelay = 3f;

        [Tooltip("Distance de Neo devant le joueur, en mètres.")]
        [SerializeField] float neoDistance = 1.5f;

        [Tooltip("Durée pendant laquelle Neo n'est que du code avant de se matérialiser, en secondes.")]
        [SerializeField] float neoMaterializeTime = 2.5f;

        [Tooltip("« Tu as fait le bon choix. Ton aventure ne fait que commencer. »")]
        [SerializeField] AudioClip neoLine;

        [Tooltip("Source audio sur la tête de Neo (son spatialisé). Vide : créée sur Neo.")]
        [SerializeField] AudioSource neoVoice;

        [Tooltip("Trigger de l'Animator de Neo lancé quand il parle. Vide : aucun.")]
        [SerializeField] string neoTalkTrigger = "Talk";

        [Header("7. Fin")]
        [Tooltip("Images par seconde visées sur le casque.")]
        [SerializeField] int targetFrameRate = 72;

        IQuestRoom room;
        AudioSource sfx;
        Vector3 tableCenter;
        GameObject fallbackTable;
        GameObject redPill;
        GameObject bluePill;
        Pill chosen;

        /// <summary>Pilule avalée par le joueur (None tant qu'il n'a pas choisi).</summary>
        public Pill Chosen => chosen;

        void Awake()
        {
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
            if (matrixManager == null)
                matrixManager = FindAnyObjectByType<MatrixManager>();
            room = roomSource as IQuestRoom;
            if (room == null && TryGetComponent<IQuestRoom>(out var found))
                room = found;
            if (head == null)
            {
                Debug.LogError("GameManager : aucune caméra de casque, la quête ne peut pas démarrer.");
                enabled = false;
                return;
            }

            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            if (neo != null)
                neo.SetActive(false);
        }

        void Start()
        {
            if (enabled)
                StartCoroutine(RunQuest());
        }

        void OnDisable() => MatrixReveal.Clear();

        /// <summary>Enchaîne les étapes de la quête.</summary>
        IEnumerator RunQuest()
        {
            SetupMixedReality();
            yield return DetectPlaneSurface();

            while (true)
            {
                SpawnPills();
                yield return WaitForSwallow();

                if (chosen == Pill.Blue)
                {
                    yield return PlayBluePillEnding();
                    if (restartAfterBlue)
                        continue;
                }
                else
                {
                    yield return RevealMatrix();
                    yield return SummonNeo();
                }

                FinishQuest();
                yield break;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Étape 0 : base réalité mixte
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Étape 0. Passthrough visible et effet Matrix coupé : on doit voir sa pièce et ses mains.
        /// Le Camera Rig, le Passthrough et le Hand Tracking viennent des Building Blocks du Meta XR SDK.
        /// </summary>
        public void SetupMixedReality()
        {
            Application.targetFrameRate = targetFrameRate;

            // Fond transparent : le passthrough apparaît derrière tout ce qui n'est pas dessiné.
            var cam = head != null ? head.GetComponent<Camera>() : null;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.clear;
            }

            room?.SetPassthroughOpacity(1f);
            MatrixReveal.Clear();
            if (matrixManager != null)
                matrixManager.MatrixEnabled = false;

            if (room == null)
                Debug.Log("GameManager : pas de pièce réelle (Meta XR SDK absent), mode test éditeur.");
        }

        // ---------------------------------------------------------------------------------------
        // Étape 1 : détection de la surface plane
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Étape 1. Trouve la table (ou la plus grande surface horizontale à hauteur de table) dans la pièce scannée.
        /// Sans pièce ou sans table, pose un plateau virtuel devant le joueur à hauteur de taille.
        /// </summary>
        public IEnumerator DetectPlaneSurface()
        {
            float waited = 0f;
            while (room != null && !room.IsReady && waited < surfaceTimeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (room != null && room.IsReady &&
                room.TryFindTable(minTableHeight, maxTableHeight, out var center, out var size))
            {
                tableCenter = center;
                Debug.Log($"GameManager : surface trouvée en {center}, {size.x:0.00} x {size.y:0.00} m.");
                yield break;
            }

            Vector3 forward = FlatForward();
            tableCenter = head.position + forward * 0.5f + Vector3.down * fallbackTableDrop;

            if (fallbackTablePrefab != null)
            {
                fallbackTable = Instantiate(fallbackTablePrefab, tableCenter, Quaternion.LookRotation(forward));
            }
            else
            {
                fallbackTable = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fallbackTable.name = "Plateau virtuel";
                fallbackTable.transform.SetPositionAndRotation(tableCenter + Vector3.down * 0.01f, Quaternion.LookRotation(forward));
                fallbackTable.transform.localScale = new Vector3(0.6f, 0.02f, 0.4f);
            }
            Debug.Log("GameManager : aucune table trouvée, plateau virtuel posé devant le joueur.");
        }

        // ---------------------------------------------------------------------------------------
        // Étape 2 : les deux pilules
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Étape 2. Pose la pilule rouge à gauche et la bleue à droite sur la surface, face au joueur.
        /// Les prefabs portent la saisie à la main (Hand Grab de l'Interaction SDK) et leur pulsation lumineuse.
        /// </summary>
        public void SpawnPills()
        {
            chosen = Pill.None;
            DestroyPills();

            // Direction de la table vers le joueur, à plat : les pilules s'alignent face à lui.
            Vector3 toPlayer = head.position - tableCenter;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 1e-4f)
                toPlayer = -FlatForward();
            var facing = Quaternion.LookRotation(-toPlayer.normalized);
            Vector3 right = facing * Vector3.right;
            Vector3 basePosition = tableCenter + Vector3.up * pillHover;

            redPill = CreatePill(redPillPrefab, Color.red, basePosition - right * (pillSpacing * 0.5f), facing, "Pilule rouge");
            bluePill = CreatePill(bluePillPrefab, Color.blue, basePosition + right * (pillSpacing * 0.5f), facing, "Pilule bleue");
        }

        // ---------------------------------------------------------------------------------------
        // Étape 3 : le geste « manger »
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Étape 3. Attend qu'une pilule reste <see cref="swallowTime"/> secondes dans la zone de la bouche.
        /// Elle est alors avalée, l'autre s'efface, et le choix est verrouillé dans <see cref="Chosen"/>.
        /// </summary>
        public IEnumerator WaitForSwallow()
        {
            float redTime = 0f, blueTime = 0f;
            while (chosen == Pill.None)
            {
                Vector3 mouth = head.TransformPoint(mouthOffset);
                redTime = NearMouth(redPill, mouth) ? redTime + Time.deltaTime : 0f;
                blueTime = NearMouth(bluePill, mouth) ? blueTime + Time.deltaTime : 0f;

                if (redTime >= swallowTime)
                    chosen = Pill.Red;
                else if (blueTime >= swallowTime)
                    chosen = Pill.Blue;
                yield return null;
            }

            if (swallowSound != null)
                sfx.PlayOneShot(swallowSound);
            DestroyPills();
            Debug.Log($"GameManager : pilule {(chosen == Pill.Red ? "rouge" : "bleue")} avalée.");
        }

        // ---------------------------------------------------------------------------------------
        // Étape 4 : branche bleue
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Étape 4. Joue une réplique au hasard (« Très bien, je te laisse tranquille », « Tu loupes tellement de secrets »).
        /// </summary>
        public IEnumerator PlayBluePillEnding()
        {
            if (bluePillLines.Length == 0)
            {
                Debug.LogWarning("GameManager : aucune réplique pour la pilule bleue.");
                yield return new WaitForSeconds(1f);
                yield break;
            }

            var line = bluePillLines[Random.Range(0, bluePillLines.Length)];
            sfx.PlayOneShot(line);
            yield return new WaitForSeconds(line.length + 1.5f);
        }

        // ---------------------------------------------------------------------------------------
        // Étape 5 : branche rouge, la matrice se dévoile
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Étape 5. Couvre toutes les surfaces de la pièce de code Matrix, en une sphère qui grandit depuis la table :
        /// la table d'abord, puis le sol, les murs et le plafond. Le passthrough s'assombrit en même temps,
        /// et la pluie plein écran arrive à la fin pour prendre tout le champ de vision.
        /// </summary>
        public IEnumerator RevealMatrix()
        {
            foreach (var surface in RoomSurfaces())
            {
                if (surface != null && surface.GetComponent<MatrixCodeTarget>() == null)
                    surface.AddComponent<MatrixCodeTarget>();
            }

            if (matrixManager != null)
            {
                // Seules les surfaces de la pièce (MatrixCodeTarget) reçoivent le code, en mode « vision de Neo » :
                // le code opaque cache le passthrough là où la révélation est passée.
                matrixManager.ScreenRain = false;
                matrixManager.SceneTint = false;
                matrixManager.SurfaceCode = true;
                matrixManager.AllObjects = false;
                matrixManager.NeoVision = true;
                matrixManager.MatrixEnabled = true;
            }

            if (revealSound != null)
                sfx.PlayOneShot(revealSound);

            MatrixReveal.Begin(tableCenter);
            for (float t = 0f; t < revealDuration; t += Time.deltaTime)
            {
                float k = t / revealDuration;
                // Lent au début (la table), puis la vague accélère vers les murs et le plafond.
                MatrixReveal.Radius = Mathf.Lerp(0f, revealRadius, k * k);
                room?.SetPassthroughOpacity(Mathf.Lerp(1f, finalPassthroughOpacity, k));
                yield return null;
            }

            MatrixReveal.Radius = revealRadius;
            room?.SetPassthroughOpacity(finalPassthroughOpacity);
            if (matrixManager != null)
                matrixManager.ScreenRain = true;
        }

        // ---------------------------------------------------------------------------------------
        // Étape 6 : apparition de Neo
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Étape 6. Fait apparaître Neo debout devant le joueur : d'abord une silhouette de code,
        /// qui se solidifie en scintillant, puis il dit sa réplique.
        /// </summary>
        public IEnumerator SummonNeo()
        {
            if (neo == null)
            {
                Debug.LogWarning("GameManager : aucun Neo assigné.");
                yield break;
            }

            yield return new WaitForSeconds(neoDelay);

            Vector3 forward = FlatForward();
            Vector3 position = head.position + forward * neoDistance;
            position.y = room != null && room.IsReady ? room.FloorHeight : 0f;
            neo.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-forward));

            // Silhouette de code : Neo est dessiné comme les surfaces de la pièce, en « vision de Neo ».
            var code = neo.AddComponent<MatrixCodeTarget>();
            neo.SetActive(true);
            yield return new WaitForSeconds(neoMaterializeTime);

            // Scintillement entre code et matière, de plus en plus de matière, puis Neo reste solide.
            for (int i = 0; i < 6; i++)
            {
                code.enabled = !code.enabled;
                yield return new WaitForSeconds(code.enabled ? 0.12f - i * 0.015f : 0.05f + i * 0.04f);
            }
            Destroy(code);

            if (neoLine == null)
                yield break;

            if (neoVoice == null)
            {
                neoVoice = neo.AddComponent<AudioSource>();
                neoVoice.spatialBlend = 1f;
            }
            var animator = neo.GetComponentInChildren<Animator>();
            if (animator != null && !string.IsNullOrEmpty(neoTalkTrigger))
                animator.SetTrigger(neoTalkTrigger);
            neoVoice.PlayOneShot(neoLine);
            yield return new WaitForSeconds(neoLine.length);
        }

        // ---------------------------------------------------------------------------------------
        // Étape 7 : fin
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Étape 7. Termine la quête. Le reste de l'étape (build Android, permission d'accès à la scène,
        /// tests dans plusieurs pièces) se fait dans les réglages du projet : voir le README.
        /// </summary>
        public void FinishQuest()
        {
            if (fallbackTable != null)
                Destroy(fallbackTable);
            Debug.Log($"GameManager : quête terminée (pilule {(chosen == Pill.Red ? "rouge" : "bleue")}).");
        }

        // ---------------------------------------------------------------------------------------
        // Tests dans l'éditeur, sans casque : menu contextuel du composant en Play.
        // ---------------------------------------------------------------------------------------

        [ContextMenu("Test : avaler la pilule rouge")]
        void DebugSwallowRed() => DebugSwallow(redPill);

        [ContextMenu("Test : avaler la pilule bleue")]
        void DebugSwallowBlue() => DebugSwallow(bluePill);

        void DebugSwallow(GameObject pill)
        {
            if (pill != null && head != null)
                pill.transform.position = head.TransformPoint(mouthOffset);
        }

        // ---------------------------------------------------------------------------------------

        IEnumerable<GameObject> RoomSurfaces()
        {
            if (room != null && room.IsReady)
            {
                foreach (var surface in room.GetSurfaces())
                    yield return surface;
            }
            foreach (var surface in fallbackSurfaces)
                yield return surface;
            if (fallbackTable != null)
                yield return fallbackTable;
        }

        GameObject CreatePill(GameObject prefab, Color color, Vector3 position, Quaternion rotation, string name)
        {
            GameObject pill;
            if (prefab != null)
            {
                pill = Instantiate(prefab, position, rotation);
            }
            else
            {
                // Pilule de test : une capsule couchée, sans saisie à la main.
                pill = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                pill.transform.SetPositionAndRotation(position, rotation * Quaternion.Euler(0f, 0f, 90f));
                pill.transform.localScale = new Vector3(0.02f, 0.02f, 0.02f);
                pill.GetComponent<Renderer>().material.color = color;
            }
            pill.name = name;
            return pill;
        }

        void DestroyPills()
        {
            if (redPill != null)
                Destroy(redPill);
            if (bluePill != null)
                Destroy(bluePill);
            redPill = null;
            bluePill = null;
        }

        bool NearMouth(GameObject pill, Vector3 mouth) =>
            pill != null && (pill.transform.position - mouth).sqrMagnitude <= mouthRadius * mouthRadius;

        Vector3 FlatForward()
        {
            Vector3 forward = head.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        }
    }
}
