using MatrixVision.AR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace MatrixVision.Rendering
{
    /// <summary>
    /// Écrit la profondeur d'environnement du casque (Meta Quest) dans le tampon de profondeur, avant les objets
    /// opaques : tous les matériaux sont ainsi occultés par le monde réel, sans shader spécifique.
    /// Ne tourne que si un <see cref="EnvironmentDepthOcclusion"/> actif reçoit la profondeur.
    /// </summary>
    public sealed class EnvironmentDepthOcclusionFeature : ScriptableRendererFeature
    {
        const string ShaderName = "Hidden/MatrixVision/EnvironmentDepthOcclusion";

        [SerializeField] Shader shader;

        Material material;
        OcclusionPass pass;

        public override void Create()
        {
            if (shader == null)
                shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning("EnvironmentDepthOcclusionFeature : shader d'occlusion introuvable.");
                return;
            }
            material = CoreUtils.CreateEngineMaterial(shader);
            pass = new OcclusionPass(material) { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null || !EnvironmentDepthOcclusion.IsActive)
                return;
            if (renderingData.cameraData.cameraType != CameraType.Game || !renderingData.cameraData.xrRendering)
                return;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
            pass = null;
        }

        sealed class OcclusionPass : ScriptableRenderPass
        {
            class PassData
            {
                public Material material;
            }

            readonly Material material;

            public OcclusionPass(Material material)
            {
                this.material = material;
                profilingSampler = new ProfilingSampler("Environment Depth Occlusion");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Environment Depth Occlusion", out var data, profilingSampler))
                {
                    data.material = material;
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                    // Lecture-écriture : les pixels sans profondeur réelle gardent la valeur effacée.
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, new Vector4(1f, 1f, 0f, 0f), d.material, 0));
                }
            }
        }
    }
}
