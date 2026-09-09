using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

// Owned by the foliage root so chunk destruction/pooling cannot leak GPU buffers.
// The CPU generator's selected rank prefixes are authoritative on both draw paths.
public sealed class GrassIndirectRenderer : MonoBehaviour
{
    private Batch near, far;

    public static bool IsSupported(ComputeShader shader, Material material)
    {
        return shader != null && material != null && material.enableInstancing &&
            material.GetTag("GrassIndirect", false, "False") == "True" &&
            SystemInfo.supportsComputeShaders && SystemInfo.supportsInstancing &&
            SystemInfo.supportsIndirectArgumentsBuffer && SystemInfo.graphicsShaderLevel >= 45 &&
            shader.HasKernel("CullGrass") && shader.HasKernel("PrefixGrass") && shader.HasKernel("ScatterGrass");
    }

    public void Draw(bool billboard, ComputeShader shader, Mesh mesh, Material material,
        List<GrassRenderBatch> selected, int revision, MaterialPropertyBlock properties,
        Camera camera, Vector4[] planes, bool receiveShadows)
    {
        Batch batch = billboard ? far : near;
        if (batch == null || !batch.Matches(shader, mesh, material))
        {
            batch?.Dispose();
            batch = new Batch(shader, mesh, material);
            if (billboard) far = batch; else near = batch;
        }
        batch.Upload(selected, revision);
        batch.Draw(properties, camera, planes, receiveShadows);
    }

    public void Release(bool billboard)
    {
        if (billboard) { far?.Dispose(); far = null; }
        else { near?.Dispose(); near = null; }
    }

    public void ReleaseAll() { Release(false); Release(true); }
    private void OnDisable() => ReleaseAll();
    private void OnDestroy() => ReleaseAll();

    [StructLayout(LayoutKind.Sequential)]
    public struct Instance
    {
        public Matrix4x4 ObjectToWorld;
        public Vector4 Data;
    }

    public sealed class Batch : IDisposable
    {
        private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("FS.Grass.IndirectUpload");
        private static readonly ProfilerMarker CullMarker = new ProfilerMarker("FS.Grass.IndirectCull");
        private readonly ComputeShader shader;
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly int cull, prefix, scatter;
        private ComputeBuffer instances, offsets, groups, visible, args;
        private int capacity, count, uploadedRevision;
        private bool uploaded;
        private Bounds bounds;
        private Bounds meshBounds;

        public Batch(ComputeShader shader, Mesh mesh, Material material)
        {
            this.shader = shader; this.mesh = mesh; this.material = material;
            cull = shader.FindKernel("CullGrass"); prefix = shader.FindKernel("PrefixGrass");
            scatter = shader.FindKernel("ScatterGrass");
        }

        public bool Matches(ComputeShader candidate, Mesh geometry, Material appearance)
            => shader == candidate && mesh == geometry && material == appearance;

        public void Upload(List<GrassRenderBatch> selected, int revision)
        {
            if (uploaded && uploadedRevision == revision && meshBounds == mesh.bounds) return;
            using (UploadMarker.Auto())
            {
                count = 0;
                foreach (var batch in selected) count += batch.matrices.Length;
                uploadedRevision = revision;
                uploaded = true;
                meshBounds = mesh.bounds;
                if (count == 0) return;
                if (capacity < count)
                {
                    ReleaseBuffers();
                    capacity = Mathf.NextPowerOfTwo(Mathf.Max(64, count));
                    instances = new ComputeBuffer(capacity, Marshal.SizeOf<Instance>());
                    offsets = new ComputeBuffer(capacity, 4);
                    groups = new ComputeBuffer((capacity + 63) / 64, 4);
                    visible = new ComputeBuffer(capacity, 4);
                    args = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
                }
                var data = new Instance[count];
                int index = 0;
                foreach (var batch in selected)
                    for (int i = 0; i < batch.matrices.Length; i++)
                    {
                        Matrix4x4 transform = batch.matrices[i];
                        data[index] = new Instance { ObjectToWorld = transform, Data = batch.instanceData[i] };
                        Bounds instanceBounds = TransformBounds(meshBounds, transform);
                        if (index++ == 0) bounds = instanceBounds;
                        else bounds.Encapsulate(instanceBounds);
                    }
                instances.SetData(data);
                args.SetData(new uint[] { mesh.GetIndexCount(0), 0, mesh.GetIndexStart(0), (uint)mesh.GetBaseVertex(0), 0 });
            }
        }

        public static Bounds TransformBounds(Bounds local, Matrix4x4 transform)
        {
            Vector3 e = local.extents;
            var extents = new Vector3(
                Mathf.Abs(transform.m00) * e.x + Mathf.Abs(transform.m01) * e.y + Mathf.Abs(transform.m02) * e.z,
                Mathf.Abs(transform.m10) * e.x + Mathf.Abs(transform.m11) * e.y + Mathf.Abs(transform.m12) * e.z,
                Mathf.Abs(transform.m20) * e.x + Mathf.Abs(transform.m21) * e.y + Mathf.Abs(transform.m22) * e.z);
            return new Bounds(transform.MultiplyPoint3x4(local.center), extents * 2);
        }

        public static Vector3 WindPadding(Material material)
        {
            float bend = material.HasProperty("_WindStrength") ? Mathf.Abs(material.GetFloat("_WindStrength")) : 0;
            float flutter = material.HasProperty("_WindFlutterStrength") ? Mathf.Abs(material.GetFloat("_WindFlutterStrength")) : 0;
            return new Vector3(bend + flutter, bend * 0.18f, bend + flutter);
        }

        public void Draw(MaterialPropertyBlock properties, Camera camera, Vector4[] planes, bool receiveShadows)
        {
            if (count == 0) return;
            Vector3 wind = WindPadding(material);
            int groupCount = (count + 63) / 64;
            using (CullMarker.Auto())
            {
                shader.SetInt("_GrassCount", count);
                shader.SetInt("_GrassGroupCount", groupCount);
                shader.SetInt("_GrassFrustumEnabled", camera != null && planes != null ? 1 : 0);
                if (planes != null) shader.SetVectorArray("_GrassFrustum", planes);
                shader.SetVector("_GrassMeshCenter", meshBounds.center);
                shader.SetVector("_GrassMeshExtents", meshBounds.extents);
                shader.SetVector("_GrassWindPadding", wind);
                shader.SetBuffer(cull, "_GrassInstances", instances);
                shader.SetBuffer(cull, "_GrassOffsets", offsets);
                shader.SetBuffer(cull, "_GrassGroups", groups);
                shader.Dispatch(cull, groupCount, 1, 1);
                shader.SetBuffer(prefix, "_GrassGroups", groups);
                shader.SetBuffer(prefix, "_GrassArgs", args);
                shader.Dispatch(prefix, 1, 1, 1);
                shader.SetBuffer(scatter, "_GrassOffsets", offsets);
                shader.SetBuffer(scatter, "_GrassGroups", groups);
                shader.SetBuffer(scatter, "_GrassVisibleIndices", visible);
                shader.Dispatch(scatter, groupCount, 1, 1);
            }
            properties.SetBuffer("_GrassInstances", instances);
            properties.SetBuffer("_GrassVisibleIndices", visible);
            Bounds drawBounds = bounds;
            drawBounds.Expand(wind * 2);
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, drawBounds, args, 0, properties,
                ShadowCastingMode.Off, receiveShadows, 0, camera, LightProbeUsage.Off);
        }

        private void ReleaseBuffers()
        {
            instances?.Release(); offsets?.Release(); groups?.Release(); visible?.Release(); args?.Release();
            instances = offsets = groups = visible = args = null;
            capacity = 0;
        }

        public void Dispose() { ReleaseBuffers(); uploaded = false; count = 0; }

#if UNITY_EDITOR
        public Instance[] ReadSourcesForValidation()
        {
            var result = new Instance[count];
            if (count > 0) instances.GetData(result, 0, 0, count);
            return result;
        }

        public uint[] ReadVisibleForValidation()
        {
            if (count == 0) return Array.Empty<uint>();
            var arguments = new uint[5]; args.GetData(arguments);
            var result = new uint[arguments[1]];
            if (result.Length > 0) visible.GetData(result, 0, 0, result.Length);
            return result;
        }
#endif
    }
}
