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
| `Assets/MatrixVision/MatrixRain/MatrixReveal.cs` | Révélation progressive du code sur les objets (sphère qui grandit) |
| `Assets/MatrixVision/Quest/GameManager.cs` | Manager principal de la quête : une méthode par étape |
| `Assets/MatrixVision/Quest/IQuestRoom.cs` | Ce que la quête doit savoir de la pièce réelle |
| `Assets/MatrixVision/Quest/MetaQuestRoom.cs` | Pièce réelle via le Meta XR SDK (MR Utility Kit + passthrough) |
| `Assets/MatrixVision/Quest/Editor/MetaXrDefine.cs` | Ajoute `MATRIX_META_XR` quand le Meta XR SDK est installé |

## Quête Matrix (Meta Quest 3)

On entre dans l'application en réalité mixte, une table est détectée et deux pilules y apparaissent. On en prend une à la main et on l'amène à la bouche :

- **bleue** : une voix dit « Très bien, je te laisse tranquille » ou « Tu loupes tellement de secrets » ;
- **rouge** : le code Matrix se dévoile depuis la table, puis sur le sol, les murs et le plafond, jusqu'à recouvrir toute la pièce. Neo apparaît ensuite devant soi, d'abord en code, puis solide, et dit « Tu as fait le bon choix. Ton aventure ne fait que commencer. »

Chaque étape est une méthode du `GameManager` :

| Étape | Méthode |
|---|---|
| 0. Base réalité mixte | `SetupMixedReality()` |
| 1. Détection de la surface plane | `DetectPlaneSurface()` |
| 2. Les deux pilules | `SpawnPills()` |
| 3. Le geste « manger » | `WaitForSwallow()` |
| 4. Branche bleue | `PlayBluePillEnding()` |
| 5. Branche rouge : la matrice se dévoile | `RevealMatrix()` |
| 6. Apparition de Neo | `SummonNeo()` |
| 7. Fin | `FinishQuest()` |

### Mise en place

1. Installer le **Meta XR All-in-One SDK** (avec **MR Utility Kit**) depuis le Package Manager. Le symbole `MATRIX_META_XR` s'ajoute tout seul et active `MetaQuestRoom`.
2. Dans une scène, ajouter les Building Blocks **Camera Rig**, **Passthrough** et **Hand Tracking**, puis un **MRUK** et un **Effect Mesh** (les surfaces de la pièce reçoivent un maillage, que le code Matrix recouvre).
3. Créer un objet avec **Game Manager** et **Meta Quest Room**, et un **Matrix Manager** dans la scène.
4. Créer les prefabs des pilules avec le Building Block **Hand Grab** (Interaction SDK) et les assigner au Game Manager. Sans prefab, des capsules de test sont créées, sans saisie.
5. Placer Neo dans la scène, désactivé, pieds à l'origine et regard vers +Z, et l'assigner. Assigner les sons (déglutition, répliques bleues, montée de la matrice, réplique de Neo).
6. Sur le casque : build **Android**, permission d'accès à la scène (`com.oculus.permission.USE_SCENE`, ajoutée par le SDK), pièce scannée une fois dans les paramètres du Quest (Space Setup).

Sans le SDK, ou si aucune table n'est trouvée, un plateau virtuel est posé devant le joueur. Dans l'éditeur, le menu contextuel du Game Manager (en Play) propose **Test : avaler la pilule rouge / bleue**.

## Licences

La police **Noto Sans JP** (`Assets/MatrixVision/MatrixRain/Fonts/`) est distribuée sous licence SIL Open Font License 1.1 — voir `OFL.txt` dans ce dossier.
