using System.Collections;
using UnityEngine;

namespace MatrixVision.Neo
{
    /// <summary>
    /// Apparition du personnage : il se matérialise en code Matrix des pieds à la tête (ligne de balayage),
    /// puis, selon le mode, le code s'estompe pour révéler le personnage, ou le personnage reste fait de code.
    /// Le code est dessiné par la passe surface Matrix ; ce composant la pilote par MaterialPropertyBlock.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatrixNeo : MonoBehaviour
    {
        [Tooltip("Matériau sans couleur (profondeur seule) : le personnage n'est visible que par son code.")]
        [SerializeField] Material invisibleMaterial;

        [Tooltip("Durée, en secondes, de la montée de la ligne de matérialisation.")]
        [SerializeField] float materializeDuration = 1.8f;

        [Tooltip("Durée, en secondes, du fondu du code quand le personnage se révèle.")]
        [SerializeField] float revealDuration = 1.2f;

        static readonly int PlaneHideId = Shader.PropertyToID("_MatrixPlaneHide");
        static readonly int ForceObjectSpaceId = Shader.PropertyToID("_MatrixForceObjectSpace");
        static readonly int ClipEnabledId = Shader.PropertyToID("_MatrixClipEnabled");
        static readonly int ClipHeightId = Shader.PropertyToID("_MatrixClipHeight");

        Renderer[] renderers;
        Material[][] originalMaterials;
        MaterialPropertyBlock block;
        bool asCode;
        Coroutine appearance;

        /// <summary>Vrai une fois la matérialisation terminée.</summary>
        public bool Materialized { get; private set; }

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            originalMaterials = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
                originalMaterials[i] = renderers[i].sharedMaterials;
            block = new MaterialPropertyBlock();
        }

        /// <summary>Lance l'apparition. asCode : le personnage reste entièrement fait de code.</summary>
        public void Materialize(bool madeOfCode)
        {
            asCode = madeOfCode;
            if (appearance != null)
                StopCoroutine(appearance);
            appearance = StartCoroutine(Appear());
        }

        /// <summary>Change le rendu sans rejouer l'apparition (interrupteur du Matrix Manager).</summary>
        public void SetMadeOfCode(bool madeOfCode)
        {
            if (madeOfCode == asCode || !Materialized)
            {
                asCode = madeOfCode;
                return;
            }
            asCode = madeOfCode;
            SetInvisible(asCode);
            Apply(asCode ? 0f : 1f, false, 0f);
        }

        IEnumerator Appear()
        {
            Materialized = false;
            SetInvisible(true);
            GetHeightRange(out float bottom, out float top);

            // Montée de la ligne des pieds à la tête : le code naît sous elle.
            for (float t = 0f; t < materializeDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / materializeDuration);
                Apply(0f, true, Mathf.Lerp(bottom - 0.05f, top + 0.05f, k));
                yield return null;
            }
            Apply(0f, false, 0f);

            if (!asCode)
            {
                // Le personnage apparaît sous le code, puis le code s'estompe.
                SetInvisible(false);
                for (float t = 0f; t < revealDuration; t += Time.deltaTime)
                {
                    Apply(Mathf.SmoothStep(0f, 1f, t / revealDuration), false, 0f);
                    yield return null;
                }
                Apply(1f, false, 0f);
            }

            Materialized = true;
            appearance = null;
        }

        void SetInvisible(bool invisible)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!invisible || invisibleMaterial == null)
                {
                    renderers[i].sharedMaterials = originalMaterials[i];
                    continue;
                }
                var materials = new Material[originalMaterials[i].Length];
                for (int m = 0; m < materials.Length; m++)
                    materials[m] = invisibleMaterial;
                renderers[i].sharedMaterials = materials;
            }
        }

        // codeHide : 0 = code pleinement visible, 1 = aucun code sur le personnage.
        void Apply(float codeHide, bool clip, float clipHeight)
        {
            foreach (var r in renderers)
            {
                r.GetPropertyBlock(block);
                block.SetFloat(PlaneHideId, codeHide);
                block.SetFloat(ForceObjectSpaceId, 1f);
                block.SetFloat(ClipEnabledId, clip ? 1f : 0f);
                block.SetFloat(ClipHeightId, clipHeight);
                r.SetPropertyBlock(block);
            }
        }

        void GetHeightRange(out float bottom, out float top)
        {
            var bounds = new Bounds(transform.position, Vector3.zero);
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            bottom = bounds.min.y;
            top = bounds.max.y;
        }
    }
}
