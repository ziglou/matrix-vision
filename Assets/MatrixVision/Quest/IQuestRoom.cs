using System.Collections.Generic;
using UnityEngine;

namespace MatrixVision.Quest
{
    /// <summary>
    /// Ce que le <see cref="GameManager"/> doit savoir de la pièce réelle.
    /// Implémenté par ArQuestRoom quand AR Foundation (OpenXR) est installé ;
    /// sans implémentation, le GameManager se replie sur un plateau virtuel et sur ses surfaces de repli.
    /// </summary>
    public interface IQuestRoom
    {
        /// <summary>Vrai quand la pièce scannée est chargée.</summary>
        bool IsReady { get; }

        /// <summary>Hauteur du sol, en mètres (coordonnée Y monde).</summary>
        float FloorHeight { get; }

        /// <summary>
        /// Surface plane où poser les pilules : la table si elle existe, sinon la plus grande surface
        /// horizontale dont la hauteur au-dessus du sol est comprise entre <paramref name="minHeight"/> et <paramref name="maxHeight"/>.
        /// </summary>
        /// <param name="center">Centre de la surface, en coordonnées monde.</param>
        /// <param name="size">Largeur et profondeur de la surface, en mètres.</param>
        bool TryFindTable(float minHeight, float maxHeight, out Vector3 center, out Vector2 size);

        /// <summary>Objets qui portent le maillage des surfaces de la pièce (table, sol, murs, plafond, meubles).</summary>
        IEnumerable<GameObject> GetSurfaces();

        /// <summary>Opacité du passthrough : 1 = la pièce réelle, 0 = noir.</summary>
        void SetPassthroughOpacity(float opacity);
    }
}
