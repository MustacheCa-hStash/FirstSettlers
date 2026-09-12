using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEngine;

public static class GrassStreamingValidation
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Set(object o, string field, object value) => o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(o, value);
    [MenuItem("Tools/Terrain/Validate Resident Grass Streaming")]
    public static void Run()
    {
        ValidatePolicy(); ValidateStreaming(); ValidateGpu();
        Debug.Log("RESIDENT GRASS PASS: queue-independent versions, bounded-job eventual publication, empty results, cache/re-entry, settings invalidation, diagonal distance, shared GPU selection and slot updates.");
    }
    public static void RunBatch() { try { Run(); EditorApplication.Exit(0); } catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); } }
    private static void ValidatePolicy()
    {
        Check(GrassStreamingPolicy.DistanceToSquare(new Vector2(15.9f,15.9f), new Vector2(24,24),8) < 0.15f, "Diagonal neighboring ground excluded.");
        Check(GrassStreamingPolicy.Priority(100, 10, false, 100) < GrassStreamingPolicy.Priority(0,0,true,100), "Old distant work can starve.");
        for (int i = 0; i < 100; i++)
        {
            float blend = i / 99f;
            int selected = GrassStreamingPolicy.Select(0.1f, 0.5f, 10, 2, Vector4.one, 1, blend, 100, 10);
            Check(selected == (0.5f < blend ? 1 : 0), "Transition dropped or duplicated a full-density candidate.");
        }
        var state = new GrassStream.State();
        Check(state.NeedsData, "Fresh state considered generated.");
        state.DataVersion = 1; Check(state.NeedsUpload, "Completed data lost publication demand.");
        state.DisplayVersion = 1; state.DesiredVersion = 2;
        Check(state.NeedsData && state.DisplayVersion == 1, "Replacement erased displayed version.");
    }
    private static void ValidateStreaming()
    {
        var record = BillboardGrassStreamingValidation.CreateRecord();
        var root = new GameObject("stream regression");
        var runtime = (ChunkRuntime)FormatterServices.GetUninitializedObject(typeof(ChunkRuntime)); Set(runtime,"root",root);
        var manager = (ChunkManager)FormatterServices.GetUninitializedObject(typeof(ChunkManager));
        Set(manager,"chunkRecords", new Dictionary<ChunkCoord,ChunkRecord>{{record.ChunkCoord,record}});
        Set(manager,"loadedChunks", new Dictionary<ChunkCoord,ChunkRuntime>{{record.ChunkCoord,runtime}});
        var settings = new GrassSettings { cellsPerAxis=16, subChunksPerChunk=2, maxConcurrentGrassJobs=1,
            maxSubChunkGenerationsPerFrame=1, maxGrassUploadsPerFrame=1, billboardRingRadius=3 };
        var coords = new List<ChunkCoord>{record.ChunkCoord};
        using (var stream = new GrassStream(settings,null,null,1234,16,1,10,_=>{},()=>false))
        {
            void Tick() => stream.Update(manager,coords,Vector3.zero,null,null,null,null,null,null);
            void Drain(int expected)
            {
                var timeout = System.Diagnostics.Stopwatch.StartNew();
                do { Tick(); System.Threading.Thread.Yield(); }
                while ((!record.FoliageData.nearGrassGenerated || !stream.IsSettledForValidation() || record.FoliageData.GetTotalNearGrassInstanceCount() != expected) && timeout.Elapsed.TotalSeconds < 15);
                Tick(); Tick();
                Check(record.FoliageData.nearGrassGenerated && stream.IsSettledForValidation() && record.FoliageData.GetTotalNearGrassInstanceCount() == expected, "Bounded streaming failed to converge while stationary.");
            }
            Drain(256);
            Check(record.FoliageData.GetTotalNearGrassInstanceCount()==256, "Shared distribution lost candidates.");
            var cached=record.FoliageData.nearGrassInstancesBySubChunk;
            coords.Clear(); Tick(); coords.Add(record.ChunkCoord); Drain(256);
            Check(ReferenceEquals(cached,record.FoliageData.nearGrassInstancesBySubChunk), "Renderer re-entry discarded candidate cache.");
            record.FoliageData.ClearNearGrass(); Drain(256);
            Check(record.FoliageData.GetTotalNearGrassInstanceCount()==256, "External invalidation was not recovered.");
            settings.cellsPerAxis=24; Drain(576);
            Check(record.FoliageData.GetTotalNearGrassInstanceCount()==576, "Settings replacement retained stale candidates.");
            settings.cellsPerAxis=32; Tick(); settings.cellsPerAxis=8; Drain(64);
            Check(record.FoliageData.GetTotalNearGrassInstanceCount()==64, "Stale in-flight job overwrote new settings.");
            Set(record,"surfaceTypeMap",new SurfaceType[19,19]); Drain(0);
            Check(record.FoliageData.GetTotalNearGrassInstanceCount()==0, "Empty replacement not published.");
            settings.cellsPerAxis=16; Tick(); coords.Clear(); Tick(); // disposal with retiring work
        }
        UnityEngine.Object.DestroyImmediate(root);
    }
    private static void ValidateGpu()
    {
        var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/GrassCompact.compute");
        var material=new Material(Shader.Find("Custom/GrassInstancedTerrainTint")){enableInstancing=true};
        var mesh=new Mesh{vertices=new[]{Vector3.zero,Vector3.up,Vector3.right},triangles=new[]{0,1,2}}; mesh.RecalculateBounds();
        var settings=new GrassSettings{grassCompactShader=shader,billboardCoverage=1,densityRadius3=1,densityRadius6=1,densityRadius10=1,densityBeyond10=1};
        Debug.Log($"GRASS GPU CAPABILITIES compute={SystemInfo.supportsComputeShaders} instancing={SystemInfo.supportsInstancing} indirect={SystemInfo.supportsIndirectArgumentsBuffer} level={SystemInfo.graphicsShaderLevel} tag={material.GetTag("GrassIndirect",false,"False")} resident={shader.HasKernel("CullResidentGrass")} old={shader.HasKernel("CullGrass")}/{shader.HasKernel("PrefixGrass")}/{shader.HasKernel("ScatterGrass")}");
        foreach (var message in ShaderUtil.GetComputeShaderMessages(shader)) Debug.Log($"GRASS COMPUTE MESSAGE {message.message}");
        Check(GrassIndirectRenderer.IsSupported(shader,material) && shader.HasKernel("CullResidentGrass"), "Resident GPU path unavailable.");
        var candidates=new List<FoliageInstanceData>();
        for(uint i=0;i<100;i++) candidates.Add(new FoliageInstanceData(new Vector3(i*.01f,0,10),Quaternion.identity,Vector3.one,i*42949672u,.25f));
        using(var renderer=new ResidentGrassRenderer(2,20,2))
        {
            renderer.Upload(0,candidates,Matrix4x4.identity,mesh,mesh);
            renderer.Upload(1,new List<FoliageInstanceData>(),Matrix4x4.identity,mesh,mesh);
            foreach(float blend in new[]{0f,.25f,.5f,1f})
            {
                renderer.SetBlend(0,blend);
                renderer.Draw(settings,mesh,material,mesh,material,null,null,Vector3.zero,4,100,10);
                var near=renderer.ReadVisibleForValidation(0); var far=renderer.ReadVisibleForValidation(1);
                var ids=new HashSet<uint>(near);
                foreach(var id in far) Check(ids.Add(id),"GPU representation overlap.");
                Check(ids.Count==100,"GPU transition lost grass.");
                foreach(var id in near) Check(GrassStreamingPolicy.RepresentationRank(candidates[(int)id].selectionRank)>=blend,"CPU/GPU near selection mismatch.");
                foreach(var id in far) Check(GrassStreamingPolicy.RepresentationRank(candidates[(int)id].selectionRank)<blend,"CPU/GPU far selection mismatch.");
            }
            var original=renderer.ReadSlotForValidation(0);
            renderer.Upload(1,candidates,Matrix4x4.Translate(Vector3.right),mesh,mesh);
            Check(renderer.ReadSlotForValidation(0)[50].ObjectToWorld==original[50].ObjectToWorld,"Partial upload changed neighbor slot.");
            renderer.Upload(0,new List<FoliageInstanceData>(),Matrix4x4.identity,mesh,mesh);
            renderer.SetBlend(1,1); settings.billboardCoverage=0;
            renderer.Draw(settings,mesh,material,mesh,material,null,null,Vector3.zero,4,100,10);
            Check(renderer.ReadVisibleForValidation(0).Length==0 && renderer.ReadVisibleForValidation(1).Length==0,"Zero density retained stale grass.");
            settings.gpuIndirectRendering=false;
            renderer.Draw(settings,mesh,material,mesh,material,null,null,Vector3.zero,4,100,10);
            settings.gpuIndirectRendering=true; settings.billboardCoverage=1;
            renderer.Draw(settings,mesh,material,mesh,material,null,null,Vector3.zero,4,100,10);
            Check(renderer.ReadVisibleForValidation(1).Length==100,"GPU recreation lost cached data.");
        }
        UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(mesh);
    }
}
