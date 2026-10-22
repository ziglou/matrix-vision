# MatrixVision

Effet « pluie de code » façon Matrix pour Unity 6 (URP), en temps réel :

- **Pluie plein écran** en trois plans de profondeur, avec de vrais glyphes Matrix (katakana demi-chasse en miroir, chiffres, symboles).
- **Code qui épouse les objets 3D** : projection triplanaire en espace objet, le code suit la forme, la position et la rotation de chaque objet, sol compris.
- **Têtes de colonne HDR** qui rayonnent avec le Bloom, scène assombrie et teintée en vert.
- **Matrix Manager** : un composant pour activer ou couper chaque effet (pluie, code sur les objets, teinte, « vision de Neo », lueur, code sur le réel).
- **Réalité augmentée (Android, ARCore)** : on filme son environnement et le code coule sur les murs, le sol, les portes et les objets réels.

## Prérequis

- Unity **6000.3.11f1** (Unity 6.3), Universal Render Pipeline 17.3
- Git LFS (`git lfs install`) avant de cloner : la police et les textures sont stockées en LFS.

## Démarrer

1. Cloner le dépôt, puis l'ouvrir avec Unity Hub (**Add → Add project from disk**).
2. Ouvrir `Assets/Scenes/SampleScene.unity`.
3. Sélectionner **Matrix Demo** pour les interrupteurs du **Matrix Manager** ; les réglages fins (couleur, vitesse, tailles, luminosités) sont dans le **Global Volume**, override **MatrixVision → Matrix Rain**.

## Réalité augmentée sur téléphone (Android, ARCore)

Scène `Assets/Scenes/MatrixAR.unity`, construite avec AR Foundation 6.6 et ARCore.

- Les lettres s'appliquent sur les **plans détectés** (sol, murs texturés, tables), avec un fondu d'apparition et des bords adoucis.
- Avec l'API Depth d'ARCore, les objets réels placés devant un plan masquent son code (occlusion douce).
- **Limite :** ARCore détecte mal les murs blancs et unis.
- **Gestes :** appui court = vague de balayage ; appui à 3 doigts = HUD de diagnostic (FPS, plans, interrupteurs).
- Réglages : composant **Matrix AR** (Matrix Manager + `MatrixARController`) et profil `Assets/Settings/MatrixARProfile.asset`.

## Réalité mixte sur Meta Quest 3/3S (OpenXR)

Scène `Assets/Scenes/MatrixQuest.unity`, construite avec AR Foundation 6.6 et Unity OpenXR: Meta 2.6.

- Le code couvre le **maillage de la pièce** fourni par la Configuration de l'espace (Space Setup) du casque : murs, sol, plafond, meubles. À défaut, il couvre les plans.
- Le passthrough est assombri et teinté par un voile semi-transparent : l'application n'a pas accès à l'image caméra.
- Rendu en mode « XR léger » : sans HDR, post-process ni texture intermédiaire (`Quest_RPAsset`, `Quest_Renderer`).
- **Manettes :** gâchette = vague de balayage ; bouton Menu (manette gauche) = HUD flottant.
- **Prérequis dans le casque :** mode développeur, Configuration de l'espace faite dans la pièce, et permission « données de l'espace » acceptée au premier lancement.

## Neo

Deux secondes après le lancement, dans les scènes `MatrixAR` et `MatrixQuest`, Neo apparaît face à soi, **au sol** : jamais sur une table, un canapé ou au milieu d'un meuble (place dégagée vérifiée), et **pas trop près** pour ne pas surprendre (entre 2,2 et 3,6 m, de préférence 2,6 m, dans le champ de vision).

- **Apparition :** il se matérialise en code Matrix des pieds à la tête, avec une ligne de balayage lumineuse.
- **Deux rendus :** le code s'estompe et révèle le personnage, ou il reste entièrement fait de code (interrupteur **Neo As Code** du Matrix Manager ; bouton du HUD sur téléphone).
- **Désactivation :** interrupteur **Neo Enabled**.
- **Modèle :** `Assets/Art/neo/source/Neo.obj`, converti en prefab par `MatrixVision → Neo → Construire le prefab`.

## Tester sans appareil : XR Simulation et tests automatiques

**XR Simulation** (AR Foundation) fait tourner `MatrixAR` et `MatrixQuest` dans l'éditeur, dans une pièce virtuelle : `Assets/MatrixVision/Simulation/MatrixTestRoom.prefab`. Elle comprend sol, plafond, un mur blanc uni, une porte, une table, une chaise, une étagère et un canapé. La simulation y découvre les plans et le maillage comme un vrai appareil.

1. Menu **MatrixVision → Simulation → Activer XR Simulation (pièce de test)**, une seule fois. Il crée la pièce si besoin, la sélectionne et active **Run In Background**. XR Simulation s'active ensuite tout seul en lançant `MatrixAR` ou `MatrixQuest` (et pendant les tests), puis se désactive en sortant du mode Play : les autres scènes du projet (par exemple les exemples XR Interaction Toolkit) ne sont pas touchées.
2. Ouvrir `MatrixAR` ou `MatrixQuest`, puis appuyer sur **Play**. Pour se déplacer : clic droit maintenu, puis ZQSD/WASD et la souris.

**Tests automatiques** (Window → General → Test Runner, ou en ligne de commande) :

```powershell
./Tools/run-tests.ps1               # tests d'édition + tests en mode Play
./Tools/run-tests.ps1 -EditModeOnly # tests d'édition seulement (quelques secondes)
```

| Tests | Vérifient |
|---|---|
| Édition (`Assets/MatrixVision/Tests/EditMode`) | shaders sans erreur, Renderer Features complètes (shaders, atlas, mode XR léger), qualité Quest sans HDR, scènes dans la liste de build, scène téléphone (lettres uniquement sur les plans), scène Quest (passthrough, maillage après permission), bascule téléphone ↔ Quest |
| Mode Play (`Assets/MatrixVision/Tests/PlayMode`) | dans la pièce simulée : du code apparaît sur les plans (téléphone) ou le maillage (Quest), la vague part, et une capture contient bien des lettres (captures dans `Temp/MatrixVisionTests`) |

**Limites :** la simulation ne remplace pas l'appareil pour les performances, les murs blancs mal détectés par ARCore, le bruit de sa profondeur, ni le rendu exact du passthrough. En simulation, la profondeur d'environnement est ignorée : elle n'est pas en mètres comme celle d'ARCore.

## Construire et installer (téléphone ou Quest)

Téléphone et Quest ciblent tous deux Android et partagent les réglages XR. Le menu **MatrixVision → Build Target** applique la configuration de la cible avant le build :

| Cible | Loader XR | API graphique | Qualité | API min. |
|---|---|---|---|---|
| Téléphone | ARCore | OpenGL ES 3 | Mobile | 25 |
| Meta Quest | OpenXR (+ fonctionnalités Meta) | Vulkan | Quest | 32 |

Le script `Tools/deploy-android.ps1` enchaîne configuration, build, installation (`adb`), lancement et captures d'écran. L'éditeur Unity doit être ouvert sur le projet, avec le CLI Unity installé.

```powershell
./Tools/deploy-android.ps1 -Target Phone
./Tools/deploy-android.ps1 -Target Quest
./Tools/deploy-android.ps1 -Target Quest -BuildOnly   # sans appareil branché
```

## Organisation

| Chemin | Rôle |
|---|---|
| `Assets/MatrixVision/MatrixManager.cs` | Interrupteurs de l'expérience (Volume caché de priorité haute) |
| `Assets/MatrixVision/MatrixRain/MatrixRain.cs` | Override de Volume : tous les réglages de l'effet |
| `Assets/MatrixVision/MatrixRain/MatrixRainRendererFeature.cs` | Passes Render Graph : pluie plein écran, code sur le réel (AR), code sur les objets |
| `Assets/MatrixVision/MatrixRain/MatrixCommon.hlsl` | Glyphes, gouttes, couleurs, projection triplanaire (partagé par les shaders) |
| `Assets/MatrixVision/MatrixRain/MatrixEnvironment.shader` | Code sur l'environnement réel à partir de la profondeur ARCore |
| `Assets/MatrixVision/MatrixRain/MatrixEnvironmentDepth.hlsl` | Lecture de la profondeur ARCore, reconstruction des positions monde |
| `Assets/MatrixVision/AR/MatrixARController.cs` | Pont AR Foundation → shaders, repli sur les plans sans profondeur |
| `Assets/MatrixVision/AR/MatrixARPlane.prefab` | Plan AR invisible, dessiné en code par la passe surface |
| `Assets/MatrixVision/AR/MatrixARPlaneMesh.cs` | Maillage de plan à bord adouci (vertex color) |
| `Assets/MatrixVision/AR/MatrixARMesh.prefab` | Morceau du maillage de pièce du Quest, dessiné en code |
| `Assets/MatrixVision/MatrixRain/MatrixPassthroughDim.shader` | Voile sombre et vert sur le passthrough du Quest |
| `Assets/MatrixVision/Editor/MatrixBuildTargets.cs` | Menu **MatrixVision → Build Target** (téléphone / Quest) |
| `Tools/deploy-android.ps1` | Build, installation, lancement et captures sur l'appareil |
| `Assets/MatrixVision/Editor/MatrixSimulationSetup.cs` | Menu **MatrixVision → Simulation** : pièce de test et XR Simulation |
| `Assets/MatrixVision/Editor/MatrixSimulationPlayMode.cs` | XR Simulation uniquement pour le mode Play de `MatrixAR` et `MatrixQuest` |
| `Assets/MatrixVision/Tests/` | Tests automatiques (édition et mode Play avec XR Simulation) |
| `Tools/run-tests.ps1` | Lance tous les tests depuis la ligne de commande |
| `Assets/MatrixVision/Neo/` | Personnage : prefab, `MatrixNeo` (apparition), `MatrixNeoSpawner` (placement au sol, à distance) |
| `Assets/MatrixVision/Editor/MatrixNeoBuilder.cs` | Menu **MatrixVision → Neo** : construit le prefab du personnage |
| `Assets/MatrixVision/MatrixRain/MatrixRain.shader` | Pluie plein écran, teinte, atténuation sur la géométrie |
| `Assets/MatrixVision/MatrixRain/MatrixSurface.shader` | Code sur les objets (Overlay / Replace) |
| `Assets/MatrixVision/MatrixRain/MatrixCodeTarget.cs` | Marque un objet pour le mode « objets choisis » |
| `Assets/MatrixVision/MatrixRain/Editor/MatrixGlyphAtlasBaker.cs` | Menu **MatrixVision → Bake Glyph Atlas** |

## Licences

La police **Noto Sans JP** (`Assets/MatrixVision/MatrixRain/Fonts/`) est distribuée sous licence SIL Open Font License 1.1 — voir `OFL.txt` dans ce dossier.
