using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

public sealed class GroundContactShadowRendererFeature : ScriptableRendererFeature
{
    [SerializeField] Shader shader;
    [SerializeField] Shader depthCopyShader;
    Material material;
    ContactPass pass;

    public override void Create()
    {
        pass?.Dispose();
        CoreUtils.Destroy(material);
        pass = null;
        if (shader == null || depthCopyShader == null) return;
        material = CoreUtils.CreateEngineMaterial(shader);
        pass = new ContactPass(material, depthCopyShader) { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData;
        if (pass == null || camera.renderType != CameraRenderType.Base ||
            !WorldManager.TryGetGroundContactSettings(camera.camera, out var settings)) return;
        Light sun = RenderSettings.sun;
        if (sun == null || sun.type != LightType.Directional || sun.shadows == LightShadows.None ||
            sun.intensity <= .01f || -sun.transform.forward.y <= .12f) return;
        pass.Settings = settings;
        pass.SunDirection = -sun.transform.forward;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Dispose();
        CoreUtils.Destroy(material);
    }

    sealed class ContactPass : ScriptableRenderPass
    {
        public Vector4 Settings;
        public Vector3 SunDirection;
        readonly Material material;
        readonly CopyDepthPass copy;
        static readonly MaterialPropertyBlock Properties = new();
        static readonly ProfilingSampler TraceSampler = new("FS.Contact.Trace");
        static readonly ProfilingSampler CompositeSampler = new("FS.Contact.Composite");

        sealed class Data
        {
            public Material material;
            public TextureHandle depth, mask;
            public Matrix4x4 vp, inverseVP;
            public Vector4 settings, sun, camera, forward, texel;
            public int index;
        }

        public ContactPass(Material material, Shader depthShader)
        {
            this.material = material;
            copy = new CopyDepthPass(RenderPassEvent.BeforeRenderingTransparents, depthShader);
        }

        public void Dispose() => copy.Dispose();

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>();
            if (!resources.activeDepthTexture.IsValid() || !resources.activeColorTexture.IsValid() ||
                !resources.mainShadowsTexture.IsValid()) return;
            var desc = camera.cameraTargetDescriptor;
            if (desc.dimension != TextureDimension.Tex2D) return;
            int width = Mathf.Max(1, (desc.width + 1) / 2);
            int height = Mathf.Max(1, (desc.height + 1) / 2);
            var depth = graph.CreateTexture(new TextureDesc(desc.width, desc.height)
            {
                name = "Ground Contact Opaque Depth",
                colorFormat = GraphicsFormat.R32_SFloat,
                filterMode = FilterMode.Point
            });
            // The completed opaque attachment includes indirect grass omitted by a depth prepass.
            copy.Render(graph, frameData, depth, resources.activeDepthTexture, false, "FS.Contact.OpaqueDepth");
            var mask = graph.CreateTexture(new TextureDesc(width, height)
            {
                name = "Ground Contact Shadow and Depth",
                colorFormat = GraphicsFormat.R16G16_SFloat,
                filterMode = FilterMode.Point
            });
            Matrix4x4 view = camera.GetViewMatrix();
            Matrix4x4 cameraToWorld = view.inverse;
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(),
                !resources.isActiveTargetBackBuffer || camera.targetTexture != null) * view;
            using (var builder = graph.AddRasterRenderPass<Data>("FS.Contact.Trace", out var data, TraceSampler))
            {
                data.material = material;
                data.depth = depth;
                data.vp = vp;
                data.inverseVP = vp.inverse;
                data.settings = Settings;
                data.sun = SunDirection;
                data.camera = cameraToWorld.GetColumn(3);
                data.forward = -cameraToWorld.GetColumn(2);
                builder.UseTexture(depth);
                builder.UseTexture(resources.mainShadowsTexture);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (Data data, RasterGraphContext context) => Draw(data, context));
            }
            using (var builder = graph.AddRasterRenderPass<Data>("FS.Contact.Composite", out var data, CompositeSampler))
            {
                data.material = material;
                data.depth = depth;
                data.mask = mask;
                data.settings = Settings;
                data.texel = new Vector4(1f / width, 1f / height, width, height);
                data.index = 1;
                builder.UseTexture(depth);
                builder.UseTexture(mask);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                // Grass marks the stencil on the opaque attachment as it draws.
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.SetRenderFunc(static (Data data, RasterGraphContext context) => Draw(data, context));
            }
        }

        static void Draw(Data data, RasterGraphContext context)
        {
            Properties.Clear();
            Properties.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
            Properties.SetTexture("_ContactDepth", ((RTHandle)data.depth).rt);
            Properties.SetVector("_ContactSettings", data.settings);
            if (data.index == 0)
            {
                Properties.SetMatrix("_ContactVP", data.vp);
                Properties.SetMatrix("_ContactInverseVP", data.inverseVP);
                Properties.SetVector("_ContactCamera", data.camera);
                Properties.SetVector("_ContactForward", data.forward);
                Properties.SetVector("_ContactSun", data.sun);
            }
            else
            {
                Properties.SetTexture("_ContactMask", ((RTHandle)data.mask).rt);
                Properties.SetVector("_ContactTexel", data.texel);
            }
            context.cmd.DrawProcedural(Matrix4x4.identity, data.material, data.index, MeshTopology.Triangles, 3, 1, Properties);
        }
    }
}
