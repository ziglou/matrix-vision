using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MatrixVision.Rendering
{
    /// <summary>
    /// Override de Volume pour l'effet "pluie de code" façon Matrix.
    /// Rendu par <see cref="MatrixRainRendererFeature"/>.
    /// </summary>
    [Serializable, VolumeComponentMenu("MatrixVision/Matrix Rain")]
    public sealed class MatrixRain : VolumeComponent, IPostProcessComponent
    {
        [Header("Général")]
        [Tooltip("Force de l'effet. 0 désactive les passes.")]
        public ClampedFloatParameter intensity = new(0f, 0f, 1f);

        [Tooltip("Couleur des glyphes (multipliée par les luminosités de tête et de traînée).")]
        public ColorParameter color = new(new Color(0.1f, 1f, 0.3f), true, false, true);

        [Tooltip("Vitesse de chute, en cellules par seconde.")]
        public ClampedFloatParameter speed = new(12f, 1f, 50f);

        [Tooltip("Longueur de la traînée, en cellules.")]
        public ClampedFloatParameter trailLength = new(18f, 2f, 60f);

        [Tooltip("Nombre de changements de glyphe par seconde.")]
        public ClampedFloatParameter glyphChangeRate = new(4f, 0f, 20f);

        [Tooltip("Halo lumineux autour des glyphes.")]
        public ClampedFloatParameter glow = new(0.5f, 0f, 1f);

        [Tooltip("Scintillement aléatoire de la luminosité des glyphes.")]
        public ClampedFloatParameter flicker = new(0.3f, 0f, 1f);

        [Tooltip("Luminosité HDR de la tête de colonne. Au-delà de 1, le Bloom la fait rayonner.")]
        public ClampedFloatParameter headBrightness = new(4f, 1f, 12f);

        [Tooltip("Luminosité HDR de la traînée.")]
        public ClampedFloatParameter trailBrightness = new(1f, 0.2f, 5f);

        [Header("Pluie plein écran")]
        [Tooltip("Lettres qui tombent au premier plan, sur tout l'écran.")]
        public BoolParameter screenRain = new(true);

        [Tooltip("Ajoute un plan de petits glyphes lointains et un plan de gros glyphes épars tout près (parallaxe).")]
        public BoolParameter screenDepthLayers = new(true);

        [Tooltip("Taille d'une cellule (un glyphe), en pixels.")]
        public ClampedFloatParameter cellSize = new(16f, 6f, 64f);

        [Tooltip("Teinte verte monochrome appliquée à la scène.")]
        public ClampedFloatParameter sceneTint = new(0.9f, 0f, 1f);

        [Tooltip("Luminosité de la scène teintée. Bas = ambiance sombre où le code ressort.")]
        public ClampedFloatParameter sceneBrightness = new(0.12f, 0f, 1f);

        [Tooltip("Pluie plein écran conservée devant les objets 3D (0 = seulement sur le ciel).")]
        public ClampedFloatParameter backgroundOnGeometry = new(0.15f, 0f, 1f);

        [Header("Code sur les objets")]
        [Tooltip("Lettres qui coulent sur la surface des objets 3D.")]
        public BoolParameter surfaceCode = new(true);

        [Tooltip("Tous les objets de la scène (sol compris) reçoivent le code. " +
                 "Décoché : seulement les objets qui ont le composant MatrixCodeTarget.")]
        public BoolParameter surfaceAllObjects = new(true);

        [Tooltip("Force du code qui épouse les objets.")]
        public ClampedFloatParameter surfaceIntensity = new(1f, 0f, 2f);

        [Tooltip("Liseré vert sur la silhouette des objets (absent des surfaces horizontales comme le sol).")]
        public ClampedFloatParameter surfaceRim = new(0.5f, 0f, 1f);

        [Tooltip("Taille d'une cellule sur les objets, en mètres.")]
        public ClampedFloatParameter surfaceCellSize = new(0.14f, 0.01f, 1f);

        [Tooltip("Remplace l'apparence des objets par du code (look « vision de Neo ») au lieu de l'ajouter par-dessus.")]
        public BoolParameter surfaceReplace = new(false);

        [Tooltip("Code fixe dans le monde (les objets le traversent) au lieu d'être attaché à l'objet.")]
        public BoolParameter surfaceWorldFall = new(false);

        public bool IsActive() => intensity.value > 0f;
    }
}
