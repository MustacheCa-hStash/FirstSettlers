using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

/// <summary>RenderGraph-only: water coverage, reduced-resolution volume, depth-aware composite.</summary>
public sealed class DistantFogRendererFeature : ScriptableRendererFeature
{
    [SerializeField] Shader fogShader;
    [SerializeField] Shader depthCopyShader;
    Material material;
    FogPass pass;

    public override void Create()
    {
        pass?.Dispose();
        CoreUtils.Destroy(material);
        if (fogShader == null) fogShader = Shader.Find("Hidden/FirstSettlers/DistantFog");
        if (depthCopyShader == null) depthCopyShader = Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
        if (fogShader != null) material = CoreUtils.CreateEngineMaterial(fogShader);
        pass = new FogPass(material, depthCopyShader) { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        pass.requiresIntermediateTexture = true;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData.camera;
        if (material == null || camera.cameraType == CameraType.Preview ||
            renderingData.cameraData.renderType == CameraRenderType.Overlay) return;
        var layer = DistantFogLayer.ForCamera(camera);
        if (layer == null || layer.density <= 0) return;
        pass.Layer = layer;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing) { pass?.Dispose(); CoreUtils.Destroy(material); }

    sealed class FogPass : ScriptableRenderPass
    {
        public DistantFogLayer Layer;
        readonly Material material;
        readonly FogDepthCopy depthCopy;
        static readonly ShaderTagId WaterTag = new ShaderTagId("DistantFogWaterMask");
        static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        static readonly ProfilingSampler DepthSampler = new ProfilingSampler("FS.Fog.SceneDepth");
        static readonly ProfilingSampler WaterSampler = new ProfilingSampler("FS.Fog.WaterCoverage");
        static readonly ProfilingSampler MarchSampler = new ProfilingSampler("FS.Fog.Raymarch");
        static readonly ProfilingSampler CompositeSampler = new ProfilingSampler("FS.Fog.Composite");

        struct Settings
        {
            public Matrix4x4 inverseVP;
            public Vector4 camera, distance, height, shape, clear, wind, quality, color, fogTexel;
            public Texture3D noise;
        }
        class DrawData
        {
            public Material material;
            public int shaderPass;
            public Settings settings;
            public TextureHandle depth, water, fog;
        }
        class WaterData { public RendererListHandle list; }

        sealed class FogDepthCopy : CopyDepthPass
        {
            public FogDepthCopy(Shader shader) : base(RenderPassEvent.BeforeRenderingPostProcessing, shader)
                { profilingSampler = DepthSampler; }
        }
        public FogPass(Material material, Shader depthShader)
        {
            this.material = material;
            depthCopy = new FogDepthCopy(depthShader);
        }
        public void Dispose() => depthCopy.Dispose();

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>();
            if (Layer == null || !resources.activeDepthTexture.IsValid()) return;
            var descriptor = camera.cameraTargetDescriptor;
            int downsample = Mathf.Max(1, (int)Layer.resolution);
            int width = Mathf.Max(1, (descriptor.width + downsample - 1) / downsample);
            int height = Mathf.Max(1, (descriptor.height + downsample - 1) / downsample);
            Color color = Layer.followDayNightColor ? RenderSettings.fogColor : Layer.fogColor;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) color = color.linear;
            Matrix4x4 view = camera.GetViewMatrix();
            var settings = new Settings
            {
                inverseVP = (GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(),
                    !resources.isActiveTargetBackBuffer || camera.targetTexture != null) * view).inverse,
                camera = new Vector4(view.inverse.m03, view.inverse.m13, view.inverse.m23, camera.camera.orthographic ? 1 : 0),
                distance = new Vector4(Layer.startDistance, Mathf.Max(1, Layer.distanceFeather), Layer.maxDistance, Layer.density),
                height = new Vector4(Layer.bottomHeight, Layer.topHeight, Layer.heightFeather, Layer.billowHeight),
                shape = new Vector4(1f / Mathf.Max(1, Layer.noiseTileSize), Layer.noiseStrength, Layer.detailStrength, Layer.shading),
                clear = new Vector4(Layer.clearHeights.x, Mathf.Max(Layer.clearHeights.x + .1f, Layer.clearHeights.y), Layer.WaterHeight, Layer.fogWater ? 1 : 0),
                wind = new Vector4(Layer.wind.x, Layer.wind.y, Layer.wind.z, Time.time),
                quality = new Vector4(Mathf.Clamp(Layer.steps, 8, 96), Layer.dithering, downsample, 0),
                color = color,
                fogTexel = new Vector4(1f / width, 1f / height, width, height),
                noise = Layer.Noise
            };

            // _CameraDepthTexture can come from the SSAO normals prepass. Custom terrain
            // and indirect vegetation need not have that pass, despite writing real depth.
            // Copy the finished attachment instead; never alter SSAO's or water's inputs.
            var depthDesc = new TextureDesc(descriptor.width, descriptor.height)
            {
                name = "Distant Fog Scene Depth", colorFormat = GraphicsFormat.R32_SFloat,
                clearBuffer = false, filterMode = FilterMode.Point,
                dimension = descriptor.dimension, slices = descriptor.volumeDepth
            };
            TextureHandle sceneDepth = graph.CreateTexture(depthDesc);
            depthCopy.Render(graph, frameData, sceneDepth, resources.activeDepthTexture,
                false, "FS.Fog.SceneDepth");

            // An R8 coverage mask is enough because all water shares one world-space plane.
            // Use actual water shader passes and camera depth: no phantom plane over dry land.
            var waterDesc = new TextureDesc(descriptor.width, descriptor.height)
            {
                name = "Distant Fog Water Coverage", colorFormat = GraphicsFormat.R8_UNorm,
                clearBuffer = true, clearColor = Color.clear, filterMode = FilterMode.Point,
                dimension = descriptor.dimension, slices = descriptor.volumeDepth
            };
            TextureHandle water = graph.CreateTexture(waterDesc);
            using (var builder = graph.AddRasterRenderPass<WaterData>("FS.Fog.WaterCoverage", out var data, WaterSampler))
            {
                var rendering = frameData.Get<UniversalRenderingData>();
                var lights = frameData.Get<UniversalLightData>();
                var drawing = RenderingUtils.CreateDrawingSettings(WaterTag, rendering, camera, lights, SortingCriteria.CommonTransparent);
                int mask = Layer.fogWater ? (camera.camera.cullingMask & (1 << 4)) : 0;
                var filtering = new FilteringSettings(RenderQueueRange.transparent, mask);
                data.list = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));
                builder.UseRendererList(data.list);
                builder.SetRenderAttachment(water, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.SetRenderFunc(static (WaterData data, RasterGraphContext context) => context.cmd.DrawRendererList(data.list));
            }

            // RG = scalar in-scatter + transmission; BA = depth + surface type for upsampling.
            var fogDesc = new TextureDesc(width, height)
            {
                name = "Distant Fog Volume", colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                clearBuffer = false, filterMode = FilterMode.Point,
                dimension = descriptor.dimension, slices = descriptor.volumeDepth
            };
            TextureHandle fog = graph.CreateTexture(fogDesc);
            using (var builder = graph.AddRasterRenderPass<DrawData>("FS.Fog.Raymarch", out var data, MarchSampler))
            {
                SetData(data, settings, sceneDepth, water, TextureHandle.nullHandle, 0);
                builder.UseTexture(data.depth); builder.UseTexture(water);
                builder.SetRenderAttachment(fog, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (DrawData data, RasterGraphContext context) => Draw(data, context));
            }
            using (var builder = graph.AddRasterRenderPass<DrawData>("FS.Fog.Composite", out var data, CompositeSampler))
            {
                SetData(data, settings, sceneDepth, water, fog, 1);
                builder.UseTexture(data.depth); builder.UseTexture(water); builder.UseTexture(fog);
                // Hardware blending avoids a full-resolution scene-color copy.
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (DrawData data, RasterGraphContext context) => Draw(data, context));
            }
        }

        void SetData(DrawData data, Settings settings, TextureHandle depth, TextureHandle water, TextureHandle fog, int shaderPass)
        {
            data.material = material; data.shaderPass = shaderPass; data.settings = settings;
            data.depth = depth; data.water = water; data.fog = fog;
        }
        static void Draw(DrawData data, RasterGraphContext context)
        {
            Properties.Clear();
            Properties.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
            var s = data.settings;
            Properties.SetMatrix("_FogInverseVP", s.inverseVP);
            Properties.SetVector("_FogCamera", s.camera);
            Properties.SetVector("_FogDistance", s.distance);
            Properties.SetVector("_FogHeight", s.height);
            Properties.SetVector("_FogShape", s.shape);
            Properties.SetVector("_FogClear", s.clear);
            Properties.SetVector("_FogWind", s.wind);
            Properties.SetVector("_FogQuality", s.quality);
            Properties.SetVector("_FogColor", s.color);
            Properties.SetVector("_FogTexture_TexelSize", s.fogTexel);
            Properties.SetTexture("_FogNoise", s.noise);
            Properties.SetTexture("_FogDepth", ((RTHandle)data.depth).rt);
            Properties.SetTexture("_FogWater", ((RTHandle)data.water).rt);
            if (data.fog.IsValid()) Properties.SetTexture("_FogTexture", ((RTHandle)data.fog).rt);
            context.cmd.DrawProcedural(Matrix4x4.identity, data.material, data.shaderPass, MeshTopology.Triangles, 3, 1, Properties);
        }
    }
}
