using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace TemplateProject.Rendering
{
    /// <summary>
    /// Render feature that adjusts the camera color target luminance depending on the stencil buffer.
    /// Pixels with stencil value equal to <see cref="Settings.stencilReference"/> are brightened,
    /// all the remaining pixels are darkened.
    /// </summary>
    public sealed class StencilBrightnessRenderFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public sealed class Settings
        {
            [Tooltip("Shader used to evaluate the stencil-aware brightness adjustment.")]
            public Shader shader;

            [Tooltip("Brightness multiplier applied to pixels whose stencil value equals the configured reference.")]
            [Min(0.0f)]
            public float brightMultiplier = 1.25f;

            [Tooltip("Brightness multiplier applied to pixels whose stencil value does not match the configured reference.")]
            [Min(0.0f)]
            public float dimMultiplier = 0.75f;

            [Tooltip("Stencil reference that selects the pixels to brighten.")]
            [Range(0, 255)]
            public int stencilReference = 1;
        }

        sealed class StencilBrightnessPass : ScriptableRenderPass
        {
            sealed class PassData
            {
                public Material material;
                public Vector4 parameters;
                public TextureHandle source;
                public Vector4 scaleBias;
                public Vector4 scaleBiasRt;
            }

            sealed class CopyPassData
            {
                public Material material;
                public TextureHandle source;
                public Vector4 scaleBias;
                public Vector4 scaleBiasRt;
            }

            static readonly int _Params = Shader.PropertyToID("_StencilBrightnessParams");
            static readonly int _StencilRef = Shader.PropertyToID("_StencilRef");
            static readonly int _Source = Shader.PropertyToID("_StencilBrightnessSource");
            static readonly int _ScaleBias = Shader.PropertyToID("_ScaleBias");
            static readonly int _ScaleBiasRt = Shader.PropertyToID("_ScaleBiasRt");
            static readonly ProfilingSampler s_ProfilingSampler = new ProfilingSampler("StencilBrightnessPass");
            static readonly ProfilingSampler s_ResolveProfilingSampler = new ProfilingSampler("ResolveStencilBrightness");

            readonly Settings m_Settings;
            Material m_Material;

            public StencilBrightnessPass(Settings settings)
            {
                m_Settings = settings;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                profilingSampler = s_ProfilingSampler;
            }

            void EnsureMaterial()
            {
                if (m_Settings.shader == null)
                    return;

                if (m_Material == null || m_Material.shader != m_Settings.shader)
                {
                    CoreUtils.Destroy(m_Material);
                    m_Material = CoreUtils.CreateEngineMaterial(m_Settings.shader);
                }
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                EnsureMaterial();
                if (m_Material == null)
                    return;

                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                TextureHandle colorTarget = resources.activeColorTexture;
                TextureHandle depthTarget = resources.activeDepthTexture;

                if (!colorTarget.IsValid())
                    return;

                RenderTextureDescriptor cameraDescriptor = cameraData.cameraTargetDescriptor;
                TextureDesc tempDesc = new TextureDesc(Vector2.one, true, true)
                {
                    colorFormat = cameraDescriptor.graphicsFormat,
                    name = "StencilBrightnessColor",
                    depthBufferBits = DepthBits.None,
                    enableRandomWrite = false,
                    clearBuffer = false,
                    dimension = cameraDescriptor.dimension,
                    enableMSAA = cameraDescriptor.msaaSamples > 1,
                    bindTextureMS = cameraDescriptor.bindMS
                };

                TextureHandle tempTarget = renderGraph.CreateTexture(tempDesc);

                bool yFlip = cameraData.IsCameraProjectionMatrixFlipped();
                Vector4 scaleBias = yFlip ? new Vector4(1f, -1f, 0f, 1f) : new Vector4(1f, 1f, 0f, 0f);
                Vector4 scaleBiasRt = new Vector4(1f, 1f, 0f, 0f);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Stencil Brightness", out PassData passData, profilingSampler))
                {
                    passData.material = m_Material;
                    passData.parameters = new Vector4(m_Settings.brightMultiplier, m_Settings.dimMultiplier, m_Settings.stencilReference, 0f);
                    passData.source = colorTarget;
                    passData.scaleBias = scaleBias;
                    passData.scaleBiasRt = scaleBiasRt;
                    builder.UseTexture(colorTarget, AccessFlags.Read);
                    builder.SetRenderAttachment(tempTarget, 0, AccessFlags.Write);
                    if (depthTarget.IsValid())
                        builder.SetRenderAttachmentDepth(depthTarget, AccessFlags.Read);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                    {
                        data.material.SetVector(_Params, data.parameters);
                        data.material.SetFloat(_StencilRef, data.parameters.z);

                        ctx.cmd.SetGlobalVector(_ScaleBias, data.scaleBias);
                        ctx.cmd.SetGlobalVector(_ScaleBiasRt, data.scaleBiasRt);
                        ctx.cmd.EnableKeyword(ShaderKeywordStrings.UseDrawProcedural);
                        ctx.cmd.SetGlobalTexture(_Source, data.source);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 1, MeshTopology.Triangles, 3);
                        ctx.cmd.DisableKeyword(ShaderKeywordStrings.UseDrawProcedural);
                    });
                }

                using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("Resolve Stencil Brightness", out CopyPassData copyData, s_ResolveProfilingSampler))
                {
                    copyData.material = m_Material;
                    copyData.source = tempTarget;
                    copyData.scaleBias = scaleBias;
                    copyData.scaleBiasRt = scaleBiasRt;
                    builder.UseTexture(tempTarget, AccessFlags.Read);
                    builder.SetRenderAttachment(colorTarget, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc((CopyPassData data, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.SetGlobalVector(_ScaleBias, data.scaleBias);
                        ctx.cmd.SetGlobalVector(_ScaleBiasRt, data.scaleBiasRt);
                        ctx.cmd.EnableKeyword(ShaderKeywordStrings.UseDrawProcedural);
                        ctx.cmd.SetGlobalTexture(_Source, data.source);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 2, MeshTopology.Triangles, 3);
                        ctx.cmd.DisableKeyword(ShaderKeywordStrings.UseDrawProcedural);
                    });
                }
            }

            public void Dispose()
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
        }

        [SerializeField]
        Settings m_Settings = new Settings();

        StencilBrightnessPass m_Pass;

        /// <inheritdoc />
        public override void Create()
        {
            m_Pass = new StencilBrightnessPass(m_Settings);
        }

        /// <inheritdoc />
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Settings.shader == null)
                return;

            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                m_Pass?.Dispose();
        }
    }
}
