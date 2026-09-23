# MatrixVision

Effet « pluie de code » façon Matrix pour Unity 6 (URP), en temps réel :

- **Pluie plein écran** en trois plans de profondeur, avec de vrais glyphes Matrix (katakana demi-chasse en miroir, chiffres, symboles).
- **Code qui épouse les objets 3D** : projection triplanaire en espace objet, le code suit la forme, la position et la rotation de chaque objet, sol compris.
- **Têtes de colonne HDR** qui rayonnent avec le Bloom, scène assombrie et teintée en vert.
- **Matrix Manager** : un composant pour activer ou couper chaque effet (pluie, code sur les objets, teinte, « vision de Neo », lueur).

## Prérequis

- Unity **6000.3.11f1** (Unity 6.3), Universal Render Pipeline 17.3
- Git LFS (`git lfs install`) avant de cloner : la police et les textures sont stockées en LFS.

## Démarrer

1. Cloner le dépôt, puis l'ouvrir avec Unity Hub (**Add → Add project from disk**).
2. Ouvrir `Assets/Scenes/SampleScene.unity`.
3. Sélectionner **Matrix Demo** pour les interrupteurs du **Matrix Manager** ; les réglages fins (couleur, vitesse, tailles, luminosités) sont dans le **Global Volume**, override **MatrixVision → Matrix Rain**.

## Organisation

| Chemin | Rôle |
|---|---|
| `Assets/MatrixVision/MatrixManager.cs` | Interrupteurs de l'expérience (Volume caché de priorité haute) |
| `Assets/MatrixVision/MatrixRain/MatrixRain.cs` | Override de Volume : tous les réglages de l'effet |
| `Assets/MatrixVision/MatrixRain/MatrixRainRendererFeature.cs` | Passes Render Graph : pluie plein écran + code sur les objets |
| `Assets/MatrixVision/MatrixRain/MatrixCommon.hlsl` | Glyphes, gouttes, couleurs (partagé par les deux shaders) |
| `Assets/MatrixVision/MatrixRain/MatrixRain.shader` | Pluie plein écran, teinte, atténuation sur la géométrie |
| `Assets/MatrixVision/MatrixRain/MatrixSurface.shader` | Code sur les objets (Overlay / Replace) |
| `Assets/MatrixVision/MatrixRain/MatrixCodeTarget.cs` | Marque un objet pour le mode « objets choisis » |
| `Assets/MatrixVision/MatrixRain/Editor/MatrixGlyphAtlasBaker.cs` | Menu **MatrixVision → Bake Glyph Atlas** |

## Licences

La police **Noto Sans JP** (`Assets/MatrixVision/MatrixRain/Fonts/`) est distribuée sous licence SIL Open Font License 1.1 — voir `OFL.txt` dans ce dossier.
