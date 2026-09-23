using UnityEngine;

namespace MatrixVision.Rendering
{
    /// <summary>
    /// Couvre l'objet de code Matrix : active le Rendering Layer "MatrixCode" sur ses Renderers,
    /// que la passe surface de <see cref="MatrixRainRendererFeature"/> dessine.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class MatrixCodeTarget : MonoBehaviour
    {
        public const string RenderingLayerName = "MatrixCode";
        const int FallbackRenderingLayer = 15;

        [Tooltip("Applique aussi le code aux Renderers des enfants.")]
        [SerializeField] bool includeChildren = true;

        /// <summary>Bit du Rendering Layer "MatrixCode" (couche 15 s'il n'est pas déclaré).</summary>
        public static uint RenderingLayerBit
        {
            get
            {
                int index = RenderingLayerMask.NameToRenderingLayer(RenderingLayerName);
                return 1u << (index > 0 ? index : FallbackRenderingLayer);
            }
        }

        void OnEnable() => Apply(true);

        void OnDisable() => Apply(false);

        void Apply(bool enable)
        {
            uint bit = RenderingLayerBit;
            var renderers = includeChildren ? GetComponentsInChildren<Renderer>(true) : GetComponents<Renderer>();
            foreach (var r in renderers)
                r.renderingLayerMask = enable ? r.renderingLayerMask | bit : r.renderingLayerMask & ~bit;
        }
    }
}
