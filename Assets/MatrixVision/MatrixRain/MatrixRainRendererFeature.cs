using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace MatrixVision.Rendering
{
    /// <summary>
    /// Effet Matrix en passes Render Graph, actives quand l'override <see cref="MatrixRain"/> l'est :
    /// pluie plein écran (atténuée sur la géométrie), code sur l'environnement réel (AR, si la
    /// profondeur est disponible), puis code qui épouse les objets.
    /// </summary>
    public sealed class MatrixRainRendererFeature : ScriptableRendererFeature
    {
        const string RainShaderName = "Hidden/MatrixVision/MatrixRain";
        const string SurfaceShaderName = "Hidden/MatrixVision/MatrixSurface";
        const string EnvironmentShaderName = "Hidden/MatrixVision/MatrixEnvironment";
        const string PassthroughDimShaderName = "Hidden/MatrixVision/MatrixPassthroughDim";

        [SerializeField] Shader shader;
        [SerializeField] Shader surfaceShader;
        [SerializeField] Shader environmentShader;
        [SerializeField] Shader passthroughDimShader;
        [Tooltip("Casques en réalité mixte (Meta Quest) : pas de texture intermédiaire ni de copie de profondeur " +
                 "(performances du passthrough). Seuls le voile sur le passthrough et le code des surfaces sont dessinés, " +
                 "directement à l'écran ; pas de pluie plein écran ni de teinte de l'image.")]
        [SerializeField] bool lightweightXR;
        [Tooltip("Atlas SDF 8x8 des glyphes (menu MatrixVision > Bake Glyph Atlas).")]
        [SerializeField] Texture2D glyphAtlas;
        [Tooltip("Avant le post-process d'URP : les têtes de colonne HDR rayonnent grâce au Bloom. " +
                 "AfterRenderingPostProcessing donne un vert plus plat, sans lueur.")]
        [SerializeField] RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

        Material material;
        Material surfaceMaterial;
        Material environmentMaterial;
        Material passthroughDimMaterial;
        MatrixRainPass pass;
        MatrixEnvironmentPass environmentPass;
        MatrixPassthroughDimPass passthroughDimPass;
        MatrixSurfacePass surfacePass;
        bool warnedMissingAtlas;

        public override void Create()
        {
            if (shader == null)
                shader = Shader.Find(RainShaderName);
            if (surfaceShader == null)
                surfaceShader = Shader.Find(SurfaceShaderName);
            if (environmentShader == null)
                environmentShader = Shader.Find(EnvironmentShaderName);
            if (passthroughDimShader == null)
                passthroughDimShader = Shader.Find(PassthroughDimShaderName);
            if (shader == null || surfaceShader == null || environmentShader == null || passthroughDimShader == null)
            {
                Debug.LogWarning("MatrixRainRendererFeature : shaders Matrix introuvables.");
                return;
            }

            material = CoreUtils.CreateEngineMaterial(shader);
            surfaceMaterial = CoreUtils.CreateEngineMaterial(surfaceShader);
            environmentMaterial = CoreUtils.CreateEngineMaterial(environmentShader);
            passthroughDimMaterial = CoreUtils.CreateEngineMaterial(passthroughDimShader);
            // Mode XR léger : pas de copie de profondeur, donc test de profondeur matériel (surfaces cachées masquées).
            surfaceMaterial.SetFloat("_MatrixZTest", (float)(lightweightXR ? CompareFunction.LessEqual : CompareFunction.Always));
            pass = new MatrixRainPass(material) { renderPassEvent = injectionPoint };
            // Après la pluie plein écran (ou le voile), pour que la teinte ne recolore pas le code.
            environmentPass = new MatrixEnvironmentPass(environmentMaterial) { renderPassEvent = injectionPoint + 1 };
            passthroughDimPass = new MatrixPassthroughDimPass(passthroughDimMaterial) { renderPassEvent = injectionPoint + 1 };
            surfacePass = new MatrixSurfacePass(surfaceMaterial, lightweightXR) { renderPassEvent = injectionPoint + 2 };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null)
                return;

            var cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;

            var settings = VolumeManager.instance.stack.GetComponent<MatrixRain>();
            if (settings == null || !settings.IsActive())
                return;

            if (glyphAtlas == null && !warnedMissingAtlas)
            {
                Debug.LogWarning("MatrixRainRendererFeature : aucun atlas de glyphes. Lancez MatrixVision > Bake Glyph Atlas.");
                warnedMissingAtlas = true;
            }

            bool surfaceActive = settings.surfaceCode.value && settings.surfaceIntensity.value > 0f;
            Shader.SetGlobalFloat(Ids.SkipSceneDepth, lightweightXR ? 1f : 0f);

            if (lightweightXR)
            {
                PushGlobals(settings, surfaceActive, false);
                if (settings.passthroughDim.value > 0f)
                    renderer.EnqueuePass(passthroughDimPass);
                if (surfaceActive)
                {
                    surfacePass.Setup(settings.surfaceReplace.value ? 1 : 0,
                        settings.surfaceAllObjects.value ? uint.MaxValue : MatrixCodeTarget.RenderingLayerBit);
                    renderer.EnqueuePass(surfacePass);
                }
                return;
            }

            // Profondeur de l'environnement réel : publiée chaque frame par MatrixARController (AR uniquement).
            bool environmentActive = surfaceActive && settings.realWorldCode.value &&
                Shader.GetGlobalFloat(Ids.EnvDepthAvailable) > 0.5f;
            PushGlobals(settings, surfaceActive, environmentActive);

            // La passe plein écran porte aussi la teinte verte : elle tourne même sans pluie au premier plan.
            if (settings.screenRain.value || settings.sceneTint.value > 0f)
                renderer.EnqueuePass(pass);

            if (environmentActive)
                renderer.EnqueuePass(environmentPass);

            if (surfaceActive)
            {
                surfacePass.Setup(settings.surfaceReplace.value ? 1 : 0,
                    settings.surfaceAllObjects.value ? uint.MaxValue : MatrixCodeTarget.RenderingLayerBit);
                renderer.EnqueuePass(surfacePass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(surfaceMaterial);
            CoreUtils.Destroy(environmentMaterial);
            CoreUtils.Destroy(passthroughDimMaterial);
            material = null;
            surfaceMaterial = null;
            environmentMaterial = null;
            passthroughDimMaterial = null;
            pass = null;
            environmentPass = null;
            passthroughDimPass = null;
            surfacePass = null;
        }

        static class Ids
        {
            public static readonly int GlyphAtlas = Shader.PropertyToID("_MatrixGlyphAtlas");
            public static readonly int Color = Shader.PropertyToID("_MatrixColor");
            public static readonly int Intensity = Shader.PropertyToID("_MatrixIntensity");
            public static readonly int Speed = Shader.PropertyToID("_MatrixSpeed");
            public static readonly int TrailLength = Shader.PropertyToID("_MatrixTrailLength");
            public static readonly int GlyphChange = Shader.PropertyToID("_MatrixGlyphChange");
            public static readonly int Glow = Shader.PropertyToID("_MatrixGlow");
            public static readonly int Flicker = Shader.PropertyToID("_MatrixFlicker");
            public static readonly int CellSize = Shader.PropertyToID("_MatrixCellSize");
            public static readonly int SceneTint = Shader.PropertyToID("_MatrixSceneTint");
            public static readonly int BackgroundOnGeometry = Shader.PropertyToID("_MatrixBackgroundOnGeometry");
            public static readonly int SurfaceIntensity = Shader.PropertyToID("_MatrixSurfaceIntensity");
            public static readonly int SurfaceCellSize = Shader.PropertyToID("_MatrixSurfaceCellSize");
            public static readonly int SurfaceWorldFall = Shader.PropertyToID("_MatrixSurfaceWorldFall");
            public static readonly int SurfaceRim = Shader.PropertyToID("_MatrixSurfaceRim");
            public static readonly int ScreenRain = Shader.PropertyToID("_MatrixScreenRain");
            public static readonly int DepthLayers = Shader.PropertyToID("_MatrixDepthLayers");
            public static readonly int SceneBrightness = Shader.PropertyToID("_MatrixSceneBrightness");
            public static readonly int HeadBrightness = Shader.PropertyToID("_MatrixHeadBrightness");
            public static readonly int TrailBrightness = Shader.PropertyToID("_MatrixTrailBrightness");
            public static readonly int EnvDepthAvailable = Shader.PropertyToID("_MatrixEnvDepthAvailable");
            public static readonly int EnvMaxDistance = Shader.PropertyToID("_MatrixEnvMaxDistance");
            public static readonly int EnvCodeActive = Shader.PropertyToID("_MatrixEnvCodeActive");
            public static readonly int EnvOcclusionEnabled = Shader.PropertyToID("_MatrixEnvOcclusionEnabled");
            public static readonly int SkipSceneDepth = Shader.PropertyToID("_MatrixSkipSceneDepth");
            public static readonly int PassthroughDim = Shader.PropertyToID("_MatrixPassthroughDim");
        }

        // Paramètres partagés par les shaders (voir MatrixCommon.hlsl et MatrixEnvironmentDepth.hlsl).
        void PushGlobals(MatrixRain s, bool surfaceActive, bool environmentActive)
        {
            Shader.SetGlobalFloat(Ids.EnvCodeActive, environmentActive ? 1f : 0f);
            Shader.SetGlobalFloat(Ids.EnvOcclusionEnabled, s.occlusion.value ? 1f : 0f);
            Shader.SetGlobalFloat(Ids.PassthroughDim, s.passthroughDim.value);
            Shader.SetGlobalFloat(Ids.EnvMaxDistance, s.realWorldMaxDistance.value);
            Shader.SetGlobalFloat(Ids.ScreenRain, s.screenRain.value ? 1f : 0f);
            Shader.SetGlobalFloat(Ids.DepthLayers, s.screenDepthLayers.value ? 1f : 0f);
            Shader.SetGlobalFloat(Ids.SceneBrightness, s.sceneBrightness.value);
            Shader.SetGlobalFloat(Ids.HeadBrightness, s.headBrightness.value);
            Shader.SetGlobalFloat(Ids.TrailBrightness, s.trailBrightness.value);
            Shader.SetGlobalFloat(Ids.SurfaceRim, s.surfaceRim.value);
            Shader.SetGlobalTexture(Ids.GlyphAtlas, glyphAtlas != null ? glyphAtlas : Texture2D.blackTexture);
            Shader.SetGlobalColor(Ids.Color, s.color.value);
            Shader.SetGlobalFloat(Ids.Intensity, s.intensity.value);
            Shader.SetGlobalFloat(Ids.Speed, s.speed.value);
            Shader.SetGlobalFloat(Ids.TrailLength, s.trailLength.value);
            Shader.SetGlobalFloat(Ids.GlyphChange, s.glyphChangeRate.value);
            Shader.SetGlobalFloat(Ids.Glow, s.glow.value);
            Shader.SetGlobalFloat(Ids.Flicker, s.flicker.value);
            Shader.SetGlobalFloat(Ids.CellSize, s.cellSize.value);
            Shader.SetGlobalFloat(Ids.SceneTint, s.sceneTint.value);
            // Sans code sur les objets, rien ne remplace la pluie devant eux : on la garde entière.
            Shader.SetGlobalFloat(Ids.BackgroundOnGeometry, surfaceActive ? s.backgroundOnGeometry.value : 1f);
            Shader.SetGlobalFloat(Ids.SurfaceIntensity, s.surfaceIntensity.value);
            Shader.SetGlobalFloat(Ids.SurfaceCellSize, s.surfaceCellSize.value);
            Shader.SetGlobalFloat(Ids.SurfaceWorldFall, s.surfaceWorldFall.value ? 1f : 0f);
        }

        /// <summary>Pluie plein écran : copie la couleur de la caméra à travers le shader MatrixRain.</summary>
        sealed class MatrixRainPass : ScriptableRenderPass
        {
            class PassData
            {
                public TextureHandle source;
                public Material material;
            }

            readonly Material material;

            public MatrixRainPass(Material material)
            {
                this.material = material;
                profilingSampler = new ProfilingSampler("Matrix Rain");
                // La passe lit la couleur de la caméra : il faut une texture intermédiaire, pas le back buffer.
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer)
                    return;

                var source = resourceData.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "_MatrixRainColor";
                desc.clearBuffer = false;
                var destination = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Matrix Rain", out var data, profilingSampler))
                {
                    data.source = source;
                    data.material = material;
                    builder.UseTexture(source);
                    if (resourceData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourceData.cameraDepthTexture);
                    builder.SetRenderAttachment(destination, 0);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, 0));
                }

                resourceData.cameraColor = destination;
            }
        }

        /// <summary>
        /// Code sur l'environnement réel (AR) : triangle plein écran additif, le shader MatrixEnvironment
        /// replace chaque pixel dans le monde grâce à la profondeur publiée par MatrixARController.
        /// </summary>
        sealed class MatrixEnvironmentPass : ScriptableRenderPass
        {
            class PassData
            {
                public Material material;
            }

            readonly Material material;

            public MatrixEnvironmentPass(Material material)
            {
                this.material = material;
                profilingSampler = new ProfilingSampler("Matrix Environment");
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer)
                    return;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Matrix Environment", out var data, profilingSampler))
                {
                    data.material = material;
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, new Vector4(1f, 1f, 0f, 0f), d.material, 0));
                }
            }
        }

        /// <summary>
        /// Voile sombre teinté sur le passthrough d'un casque (Meta Quest) : triangle plein écran en alpha
        /// prémultiplié, dessiné directement dans la cible de la caméra, sans rien y relire.
        /// </summary>
        sealed class MatrixPassthroughDimPass : ScriptableRenderPass
        {
            class PassData
            {
                public Material material;
            }

            readonly Material material;

            public MatrixPassthroughDimPass(Material material)
            {
                this.material = material;
                profilingSampler = new ProfilingSampler("Matrix Passthrough Dim");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Matrix Passthrough Dim", out var data, profilingSampler))
                {
                    data.material = material;
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, new Vector4(1f, 1f, 0f, 0f), d.material, 0));
                }
            }
        }

        /// <summary>Code sur les objets : redessine les objets filtrés avec le shader MatrixSurface.</summary>
        sealed class MatrixSurfacePass : ScriptableRenderPass
        {
            class PassData
            {
                public RendererListHandle rendererList;
            }

            static readonly List<ShaderTagId> ShaderTags = new()
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit"),
            };

            readonly Material material;
            readonly bool lightweight;
            int materialPass;
            uint renderingLayerMask;

            // lightweight : casque XR, dessin direct à l'écran, sans texture intermédiaire ni copie de profondeur.
            public MatrixSurfacePass(Material material, bool lightweight)
            {
                this.material = material;
                this.lightweight = lightweight;
                profilingSampler = new ProfilingSampler("Matrix Surface");
                requiresIntermediateTexture = !lightweight;
                if (!lightweight)
                    ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public void Setup(int materialPass, uint renderingLayerMask)
            {
                this.materialPass = materialPass;
                this.renderingLayerMask = renderingLayerMask;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer && !lightweight)
                    return;

                var renderingData = frameData.Get<UniversalRenderingData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();

                var drawing = RenderingUtils.CreateDrawingSettings(ShaderTags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                drawing.overrideMaterial = material;
                drawing.overrideMaterialPassIndex = materialPass;
                var filtering = new FilteringSettings(RenderQueueRange.all, -1, renderingLayerMask);
                var rendererList = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Matrix Surface", out var data, profilingSampler))
                {
                    data.rendererList = rendererList;
                    builder.UseRendererList(rendererList);
                    if (!lightweight && resourceData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourceData.cameraDepthTexture);
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                    if (lightweight)
                        builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.rendererList));
                }
            }
        }
    }
}
