using UnityEngine;

namespace MatrixVision.Rendering
{
    /// <summary>
    /// Révélation progressive du code sur les objets : une sphère qui grandit depuis un point.
    /// Hors de la sphère, la passe surface ne dessine rien ; sur son bord, un front lumineux.
    /// Inactive par défaut : le code couvre les objets partout, comme avant.
    /// </summary>
    public static class MatrixReveal
    {
        /// <summary>Vrai pendant et après une révélation, jusqu'à <see cref="Clear"/>.</summary>
        public static bool Active { get; private set; }

        /// <summary>Centre de la sphère, en coordonnées monde.</summary>
        public static Vector3 Center { get; set; }

        /// <summary>Rayon courant de la sphère, en mètres.</summary>
        public static float Radius { get; set; }

        /// <summary>Épaisseur du front lumineux, en mètres.</summary>
        public static float Edge { get; set; } = 0.35f;

        public static void Begin(Vector3 center)
        {
            Active = true;
            Center = center;
            Radius = 0f;
        }

        public static void Clear() => Active = false;

        /// <summary>xyz = centre, w = rayon ; w négatif = pas de révélation.</summary>
        internal static Vector4 Sphere => Active ? new Vector4(Center.x, Center.y, Center.z, Radius) : new Vector4(0f, 0f, 0f, -1f);
    }
}
