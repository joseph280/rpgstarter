using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace RPGStarter.Rendering
{
    /// <summary>
    /// URP renderer feature that draws a Sobel outline over the scene using
    /// scene depth + normals. Add this to the URP Renderer Data asset's
    /// Renderer Features list (Settings/URP/PC_Renderer.asset).
    ///
    /// Pair with the RPGStarter/PostProcess/Outline shader.
    /// </summary>
    [DisallowMultipleRendererFeature("RPGStarter Outline")]
    public sealed class OutlineRendererFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("Outline shader. Assign Assets/_Project/Art/Shaders/Outline.shader.")]
            public Shader shader;

            [ColorUsage(false, false)]
            public Color outlineColor = Color.black;

            [Range(0.5f, 4f)]
            public float thickness = 1f;

            [Range(0.0001f, 0.01f)]
            public float depthThreshold = 0.001f;

            [Range(0.05f, 1f)]
            public float normalThreshold = 0.4f;

            [Tooltip("When in the rendering pipeline to inject the outline.")]
            public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingTransparents;
        }

        [SerializeField] private Settings settings = new Settings();
        private OutlinePass _pass;
        private Material _material;

        public override void Create()
        {
            if (settings.shader == null)
            {
                _pass = null;
                return;
            }

            if (_material == null || _material.shader != settings.shader)
            {
                CoreUtils.Destroy(_material);
                _material = CoreUtils.CreateEngineMaterial(settings.shader);
            }

            _pass = new OutlinePass(_material, settings)
            {
                renderPassEvent = settings.injectionPoint
            };
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null) return;
            if (renderingData.cameraData.cameraType != CameraType.Game &&
                renderingData.cameraData.cameraType != CameraType.SceneView) return;

            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        // ─────────────────────────────────────────────────────────────────────

        private sealed class OutlinePass : ScriptableRenderPass
        {
            private static readonly int OutlineColorID    = Shader.PropertyToID("_OutlineColor");
            private static readonly int ThicknessID       = Shader.PropertyToID("_OutlineThickness");
            private static readonly int DepthThresholdID  = Shader.PropertyToID("_DepthThreshold");
            private static readonly int NormalThresholdID = Shader.PropertyToID("_NormalThreshold");

            private readonly Material _material;
            private readonly Settings _settings;

            public OutlinePass(Material material, Settings settings)
            {
                _material = material;
                _settings = settings;
                profilingSampler = new ProfilingSampler("RPGStarter Outline");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                _material.SetColor(OutlineColorID,    _settings.outlineColor);
                _material.SetFloat(ThicknessID,       _settings.thickness);
                _material.SetFloat(DepthThresholdID,  _settings.depthThreshold);
                _material.SetFloat(NormalThresholdID, _settings.normalThreshold);

                var resourceData = frameData.Get<UniversalResourceData>();
                var source       = resourceData.activeColorTexture;
                var destDesc     = renderGraph.GetTextureDesc(source);
                destDesc.name    = "_RPGStarterOutlineTemp";
                destDesc.clearBuffer = false;
                var dest         = renderGraph.CreateTexture(destDesc);

                var blitParams   = new RenderGraphUtils.BlitMaterialParameters(source, dest, _material, 0);
                renderGraph.AddBlitPass(blitParams, "RPGStarter Outline Blit");

                resourceData.cameraColor = dest;
            }
        }
    }
}
