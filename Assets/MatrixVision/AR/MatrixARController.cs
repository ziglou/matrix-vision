using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using MatrixVision.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MatrixVision.AR
{
    /// <summary>
    /// Relie AR Foundation à l'effet Matrix :
    /// <list type="bullet">
    /// <item>les plans détectés (sol, murs, tables) portent les lettres, dessinées par la passe surface ;
    /// chaque plan apparaît en fondu, les plans trop petits ou absorbés par un autre sont écartés ;</item>
    /// <item>la profondeur de l'environnement réel (ARCore Depth), quand elle existe, est publiée pour les shaders :
    /// elle masque en douceur le code derrière les objets réels, et alimente le mode expérimental
    /// « code sur tout le réel » (MatrixEnvironment) ;</item>
    /// <item>vague de balayage à la première surface détectée, relancée par un appui sur l'écran ou la gâchette ;</item>
    /// <item>HUD de diagnostic (appui à 3 doigts, ou bouton Menu en casque) : FPS, plans, maillage, profondeur ;</item>
    /// <item>Meta Quest : maillage de la pièce (prioritaire sur les plans) et permission USE_SCENE.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatrixARController : MonoBehaviour
    {
        [SerializeField] Camera arCamera;
        [SerializeField] ARCameraManager cameraManager;
        [SerializeField] AROcclusionManager occlusionManager;
        [SerializeField] ARPlaneManager planeManager;
        [Tooltip("Maillage de la pièce (Meta Quest 3/3S, Space Setup). S'il existe, il porte le code à la place des plans.")]
        [SerializeField] ARMeshManager meshManager;
        [SerializeField] MatrixManager matrixManager;

        [Tooltip("Meta Quest : demande la permission com.oculus.permission.USE_SCENE, puis active les composants " +
                 "ci-dessous (qui doivent être désactivés dans la scène) une fois la permission accordée.")]
        [SerializeField] bool requestScenePermission;
        [SerializeField] Behaviour[] enableAfterScenePermission;

        [Tooltip("Ignore la profondeur même si le téléphone la supporte (test sans occlusion des objets réels).")]
        [SerializeField] bool forcePlanesOnly;

        [Tooltip("Durée, en secondes, du fondu d'apparition d'un nouveau plan.")]
        [SerializeField] float planeFadeInDuration = 0.35f;

        [Tooltip("Surface minimale, en m², d'un plan pour recevoir du code (écarte seulement les plans minuscules).")]
        [SerializeField] float minPlaneArea = 0.02f;

        [Tooltip("La première vague ne révèle le code qu'à son passage. Désactivé : les lettres s'appliquent dès " +
                 "qu'une surface est détectée, la vague n'est qu'une lueur qui balaie la pièce.")]
        [SerializeField] bool revealWithFirstWave;

        [Tooltip("Message affiché tant qu'aucun plan n'est détecté.")]
        [SerializeField] string scanHint = "Balayez lentement votre environnement…";

        static readonly int EnvDepthId = Shader.PropertyToID("_MatrixEnvDepth");
        static readonly int EnvDepthAvailableId = Shader.PropertyToID("_MatrixEnvDepthAvailable");
        static readonly int DisplayTransformId = Shader.PropertyToID("_MatrixDisplayTransform");
        static readonly int CamInvProjectionId = Shader.PropertyToID("_MatrixCamInvProjection");
        static readonly int CamToWorldId = Shader.PropertyToID("_MatrixCamToWorld");
        static readonly int RevealOriginId = Shader.PropertyToID("_MatrixRevealOrigin");
        static readonly int RevealRadiusId = Shader.PropertyToID("_MatrixRevealRadius");
        static readonly int RevealMaskId = Shader.PropertyToID("_MatrixRevealMask");
        static readonly int RevealGlowId = Shader.PropertyToID("_MatrixRevealGlow");
        static readonly int PlaneHideId = Shader.PropertyToID("_MatrixPlaneHide");
        static readonly int EdgeFadeId = Shader.PropertyToID("_MatrixEdgeFade");

        static readonly int ARCoreEnvironmentDepthId = Shader.PropertyToID("_EnvironmentDepth");

        Matrix4x4 displayMatrix = Matrix4x4.identity;
        Texture environmentDepth;
        int environmentDepthFrame = -100;
        bool depthAvailable;
        int visiblePlaneCount;
        readonly Dictionary<TrackableId, float> planeFadeStart = new();
        MaterialPropertyBlock planeBlock;

        // Vague de balayage
        float waveStartTime = -1f;
        bool waveIsFirst;
        bool firstWaveDone;
        Vector3 waveOrigin;

        // Gestes et HUD
        int gestureMaxTouches;
        float gestureStartTime;
        bool hudVisible;
        float smoothedDeltaTime = 1f / 30f;
        GUIStyle hintStyle, hudStyle, buttonStyle;

        // Maillage de pièce, permission et HUD en XR (Meta Quest)
        readonly Dictionary<int, float> meshFadeStart = new();
        int visibleMeshCount;
        string scenePermissionState = "non demandée";
        TextMesh worldHud;
#if ENABLE_INPUT_SYSTEM
        InputAction waveAction;
        InputAction hudAction;
#endif

        const string ScenePermission = "com.oculus.permission.USE_SCENE";

        static bool IsXRHeadset => UnityEngine.XR.XRSettings.isDeviceActive;

        /// <summary>Vrai quand la profondeur de l'environnement réel est disponible cette frame.</summary>
        public bool DepthAvailable => depthAvailable;

        void Reset()
        {
            arCamera = GetComponentInChildren<Camera>();
            cameraManager = FindAnyObjectByType<ARCameraManager>();
            occlusionManager = FindAnyObjectByType<AROcclusionManager>();
            planeManager = FindAnyObjectByType<ARPlaneManager>();
            matrixManager = FindAnyObjectByType<MatrixManager>();
        }

        void OnEnable()
        {
            planeBlock ??= new MaterialPropertyBlock();
            if (cameraManager != null)
                cameraManager.frameReceived += OnCameraFrame;
            if (occlusionManager != null)
                occlusionManager.frameReceived += OnOcclusionFrame;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
#if ENABLE_INPUT_SYSTEM
            // Manettes Meta Quest : gâchette = vague, bouton Menu = HUD.
            waveAction ??= new InputAction("MatrixWave", InputActionType.Button);
            if (waveAction.bindings.Count == 0)
            {
                waveAction.AddBinding("<XRController>{RightHand}/triggerPressed");
                waveAction.AddBinding("<XRController>{LeftHand}/triggerPressed");
            }
            hudAction ??= new InputAction("MatrixHud", InputActionType.Button);
            if (hudAction.bindings.Count == 0)
            {
                hudAction.AddBinding("<XRController>{LeftHand}/menu");
                hudAction.AddBinding("<XRController>{LeftHand}/menuButton");
            }
            waveAction.Enable();
            hudAction.Enable();
#endif
        }

        void Start()
        {
            if (requestScenePermission)
                RequestScenePermission();
        }

        void OnDisable()
        {
            if (cameraManager != null)
                cameraManager.frameReceived -= OnCameraFrame;
            if (occlusionManager != null)
                occlusionManager.frameReceived -= OnOcclusionFrame;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
#if ENABLE_INPUT_SYSTEM
            waveAction?.Disable();
            hudAction?.Disable();
#endif
            Shader.SetGlobalFloat(EnvDepthAvailableId, 0f);
            Shader.SetGlobalFloat(RevealMaskId, 0f);
            Shader.SetGlobalFloat(RevealGlowId, 0f);
            depthAvailable = false;
        }

        void OnCameraFrame(ARCameraFrameEventArgs args)
        {
            if (args.displayMatrix.HasValue)
                displayMatrix = args.displayMatrix.Value;
        }

        // Profondeur d'environnement en mètres : on ne retient que celle d'ARCore (_EnvironmentDepth).
        // XR Simulation publie un tampon de profondeur brut (_TextureSingleDepth), sans unité exploitable ici ;
        // et interroger TryGetEnvironmentDepthTexture en simulation lève une exception dans AR Foundation 6.6.
        void OnOcclusionFrame(AROcclusionFrameEventArgs args)
        {
            foreach (var external in args.externalTextures)
            {
                if (external.propertyId == ARCoreEnvironmentDepthId && external.texture != null)
                {
                    environmentDepth = external.texture;
                    environmentDepthFrame = Time.frameCount;
                    return;
                }
            }
        }

        void Update()
        {
            smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, Time.unscaledDeltaTime, 0.05f);

            // Profondeur reçue lors des dernières frames d'occlusion (au-delà, considérée comme perdue).
            depthAvailable = !forcePlanesOnly &&
                occlusionManager != null && occlusionManager.enabled &&
                environmentDepth != null && Time.frameCount - environmentDepthFrame <= 5;

            Shader.SetGlobalTexture(EnvDepthId, depthAvailable ? environmentDepth : Texture2D.blackTexture);
            Shader.SetGlobalFloat(EnvDepthAvailableId, depthAvailable ? 1f : 0f);
            Shader.SetGlobalMatrix(DisplayTransformId, displayMatrix);

            // Les plans portent les lettres. Seul le mode expérimental « code sur tout le réel »
            // les remplace (sinon le code serait dessiné deux fois sur les murs et le sol).
            var settings = VolumeManager.instance.stack.GetComponent<MatrixRain>();
            bool environmentCode = depthAvailable && settings != null && settings.realWorldCode.value;
            // Le maillage de pièce (Quest 3) est plus complet que les plans : s'il existe, lui seul porte le code.
            UpdateMeshes(!environmentCode);
            UpdatePlanes(!environmentCode && visibleMeshCount == 0);
            UpdateWave(settings);
            HandleTouches();
            HandleXRInput();
            UpdateWorldHud();
        }

        // Matrices prises au moment du rendu : la pose de la caméra AR est mise à jour juste avant.
        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != arCamera)
                return;
            Shader.SetGlobalMatrix(CamInvProjectionId, camera.projectionMatrix.inverse);
            Shader.SetGlobalMatrix(CamToWorldId, camera.cameraToWorldMatrix);
        }

        void UpdatePlanes(bool codeOnPlanes)
        {
            visiblePlaneCount = 0;
            if (planeManager == null)
                return;

            float now = Time.time;
            foreach (var plane in planeManager.trackables)
            {
                if (!plane.TryGetComponent<MeshRenderer>(out var meshRenderer))
                    continue;

                bool valid = codeOnPlanes &&
                    plane.subsumedBy == null &&
                    plane.trackingState != TrackingState.None &&
                    plane.size.x * plane.size.y >= minPlaneArea;

                // Le fondu démarre quand le plan devient valable (assez grand, suivi, non absorbé).
                if (!valid || !planeFadeStart.TryGetValue(plane.trackableId, out float fadeStart))
                    planeFadeStart[plane.trackableId] = fadeStart = now;

                float hide = valid ? 1f - Mathf.Clamp01((now - fadeStart) / Mathf.Max(planeFadeInDuration, 0.01f)) : 1f;
                meshRenderer.enabled = hide < 1f;
                if (hide < 1f)
                    visiblePlaneCount++;

                planeBlock.SetFloat(PlaneHideId, hide);
                planeBlock.SetFloat(EdgeFadeId, 1f);
                meshRenderer.SetPropertyBlock(planeBlock);
            }
        }

        // Morceaux du maillage de pièce : même fondu d'apparition que les plans (pas de bord adouci : maillage fermé).
        void UpdateMeshes(bool codeOnMeshes)
        {
            visibleMeshCount = 0;
            if (meshManager == null || !meshManager.enabled || meshManager.meshes == null)
                return;

            float now = Time.time;
            foreach (var meshFilter in meshManager.meshes)
            {
                if (meshFilter == null || !meshFilter.TryGetComponent<MeshRenderer>(out var meshRenderer))
                    continue;

                int id = meshFilter.GetInstanceID();
                if (!codeOnMeshes || !meshFadeStart.TryGetValue(id, out float fadeStart))
                    meshFadeStart[id] = fadeStart = now;

                float hide = codeOnMeshes ? 1f - Mathf.Clamp01((now - fadeStart) / Mathf.Max(planeFadeInDuration, 0.01f)) : 1f;
                meshRenderer.enabled = hide < 1f;
                if (hide < 1f)
                    visibleMeshCount++;

                planeBlock.SetFloat(PlaneHideId, hide);
                planeBlock.SetFloat(EdgeFadeId, 0f);
                meshRenderer.SetPropertyBlock(planeBlock);
            }
        }

        // Meta Quest : les données de la pièce (plans, maillage, profondeur) exigent cette permission Android.
        void RequestScenePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                OnScenePermissionGranted(ScenePermission);
                return;
            }
            scenePermissionState = "demandée…";
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += OnScenePermissionGranted;
            callbacks.PermissionDenied += _ => scenePermissionState = "refusée";
            callbacks.PermissionDeniedAndDontAskAgain += _ => scenePermissionState = "refusée (définitivement)";
            UnityEngine.Android.Permission.RequestUserPermission(ScenePermission, callbacks);
#else
            OnScenePermissionGranted(ScenePermission);
#endif
        }

        void OnScenePermissionGranted(string permission)
        {
            scenePermissionState = "accordée";
            if (enableAfterScenePermission == null)
                return;
            foreach (var behaviour in enableAfterScenePermission)
            {
                if (behaviour != null)
                    behaviour.enabled = true;
            }
        }

        void UpdateWave(MatrixRain settings)
        {
            bool enabled = settings != null && settings.scanWave.value && arCamera != null;
            if (!enabled)
            {
                Shader.SetGlobalFloat(RevealMaskId, 0f);
                Shader.SetGlobalFloat(RevealGlowId, 0f);
                return;
            }

            // Première vague dès que la première surface (plan ou maillage) apparaît.
            if (!firstWaveDone && waveStartTime < 0f && visiblePlaneCount + visibleMeshCount > 0)
                StartWave(true);

            float radius = 0f;
            if (waveStartTime >= 0f)
            {
                radius = (Time.time - waveStartTime) * settings.scanWaveSpeed.value;
                if (radius > settings.scanWaveDistance.value + 1.5f)
                {
                    if (waveIsFirst)
                        firstWaveDone = true;
                    waveStartTime = -1f;
                }
            }

            // Option : avant et pendant la première vague, seul l'intérieur de la vague est révélé.
            bool masking = revealWithFirstWave && !firstWaveDone;
            Shader.SetGlobalVector(RevealOriginId, waveOrigin);
            Shader.SetGlobalFloat(RevealRadiusId, radius);
            Shader.SetGlobalFloat(RevealMaskId, masking ? 1f : 0f);
            Shader.SetGlobalFloat(RevealGlowId, waveStartTime >= 0f ? 1f : 0f);
        }

        void StartWave(bool first)
        {
            waveOrigin = arCamera.transform.position;
            waveStartTime = Time.time;
            waveIsFirst = first;
        }

        /// <summary>Relance une vague depuis la position actuelle du téléphone (lueur seule après la première).</summary>
        public void TriggerWave()
        {
            if (arCamera != null && waveStartTime < 0f)
                StartWave(!firstWaveDone);
        }

        // Appui court à 1 doigt : nouvelle vague. Appui à 3 doigts : HUD de diagnostic.
        void HandleTouches()
        {
#if ENABLE_INPUT_SYSTEM
            var screen = Touchscreen.current;
            if (screen == null)
                return;

            int pressed = 0;
            foreach (var touch in screen.touches)
            {
                if (touch.press.isPressed)
                    pressed++;
            }

            if (pressed > 0)
            {
                if (gestureMaxTouches == 0)
                    gestureStartTime = Time.unscaledTime;
                gestureMaxTouches = Mathf.Max(gestureMaxTouches, pressed);
                return;
            }

            if (gestureMaxTouches >= 3)
                hudVisible = !hudVisible;
            else if (gestureMaxTouches == 1 && !hudVisible && Time.unscaledTime - gestureStartTime < 0.35f)
                TriggerWave();
            gestureMaxTouches = 0;
#endif
        }

        // Manettes Meta Quest : gâchette = nouvelle vague, bouton Menu = HUD.
        void HandleXRInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (waveAction != null && waveAction.WasPressedThisFrame())
                TriggerWave();
            if (hudAction != null && hudAction.WasPressedThisFrame())
                hudVisible = !hudVisible;
#endif
        }

        string HudText()
        {
            int planes = planeManager != null ? planeManager.trackables.count : 0;
            int meshes = meshManager != null && meshManager.meshes != null ? meshManager.meshes.Count : 0;
            string mode = occlusionManager != null ? occlusionManager.currentOcclusionPreferenceMode.ToString() : "-";
            return
                $"FPS : {1f / Mathf.Max(smoothedDeltaTime, 1e-4f):0}\n" +
                $"Plans : {visiblePlaneCount} affichés / {planes} détectés\n" +
                $"Maillage : {visibleMeshCount} affichés / {meshes} reçus\n" +
                $"Profondeur ARCore : {(depthAvailable ? "oui" : "non")}{(forcePlanesOnly ? " (ignorée)" : "")}\n" +
                $"Occlusion AR Foundation : {mode}\n" +
                (requestScenePermission ? $"Permission pièce : {scenePermissionState}\n" : "") +
                $"Vague : {(waveStartTime >= 0f ? "en cours" : firstWaveDone ? "terminée" : "en attente d'une surface")}";
        }

        // En casque, OnGUI est invisible : message et HUD sont un texte 3D flottant devant les yeux.
        void UpdateWorldHud()
        {
            if (!IsXRHeadset || arCamera == null)
                return;

            if (worldHud == null)
            {
                var go = new GameObject("Matrix World HUD");
                go.transform.SetParent(arCamera.transform, false);
                go.transform.localPosition = new Vector3(0f, -0.08f, 0.7f);
                go.transform.localScale = Vector3.one * 0.0025f;
                worldHud = go.AddComponent<TextMesh>();
                worldHud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                go.GetComponent<MeshRenderer>().sharedMaterial = worldHud.font.material;
                worldHud.anchor = TextAnchor.MiddleCenter;
                worldHud.alignment = TextAlignment.Left;
                worldHud.fontSize = 48;
                worldHud.color = new Color(0.4f, 1f, 0.55f);
            }

            bool noSurface = visiblePlaneCount + visibleMeshCount == 0;
            string text = hudVisible ? HudText() : noSurface ? scanHint : string.Empty;
            worldHud.gameObject.SetActive(text.Length > 0);
            worldHud.text = text;
        }

        void OnGUI()
        {
            if (IsXRHeadset)
                return;
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            if (visiblePlaneCount + visibleMeshCount == 0 && !string.IsNullOrEmpty(scanHint))
            {
                hintStyle ??= new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 30,
                    wordWrap = true,
                    normal = { textColor = new Color(0.3f, 1f, 0.45f) },
                };
                GUI.Label(new Rect(0f, height * 0.8f, width, height * 0.1f), scanHint, hintStyle);
            }

            if (!hudVisible)
                return;

            hudStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 22,
                padding = new RectOffset(16, 16, 12, 12),
                normal = { textColor = new Color(0.6f, 1f, 0.7f) },
            };
            buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 22 };

            GUI.Box(new Rect(20f, 20f, 520f, 220f), HudText(), hudStyle);

            float y = 255f;
            if (matrixManager != null)
            {
                if (GUI.Button(new Rect(20f, y, 520f, 60f), $"Occlusion : {(matrixManager.Occlusion ? "ON" : "OFF")}", buttonStyle))
                    matrixManager.Occlusion = !matrixManager.Occlusion;
                y += 70f;
                if (GUI.Button(new Rect(20f, y, 520f, 60f), $"Vague de balayage : {(matrixManager.ScanWave ? "ON" : "OFF")}", buttonStyle))
                    matrixManager.ScanWave = !matrixManager.ScanWave;
                y += 70f;
                if (GUI.Button(new Rect(20f, y, 520f, 60f), $"Neo : {(matrixManager.NeoEnabled ? "ON" : "OFF")}", buttonStyle))
                    matrixManager.NeoEnabled = !matrixManager.NeoEnabled;
                y += 70f;
                if (GUI.Button(new Rect(20f, y, 520f, 60f), $"Neo fait de code : {(matrixManager.NeoAsCode ? "ON" : "OFF")}", buttonStyle))
                    matrixManager.NeoAsCode = !matrixManager.NeoAsCode;
                y += 70f;
            }
            if (GUI.Button(new Rect(20f, y, 520f, 60f), $"Ignorer la profondeur : {(forcePlanesOnly ? "ON" : "OFF")}", buttonStyle))
                forcePlanesOnly = !forcePlanesOnly;
            y += 70f;
            if (GUI.Button(new Rect(20f, y, 520f, 60f), "Relancer la vague", buttonStyle))
                TriggerWave();
        }
    }
}
