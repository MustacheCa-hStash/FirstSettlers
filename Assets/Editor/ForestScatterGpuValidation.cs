using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class ForestScatterGpuValidation
{
    [MenuItem("Tools/Foliage/Validate Forest Scatter GPU Rendering")]
    public static void Run()
    {
        Require(Marshal.SizeOf<ForestScatterGpuRenderer.Source>() == 128, "Forest source stride mismatch.");
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Resources/Foliage/ForestScatterCompact.compute");
        var previousPipeline = GraphicsSettings.defaultRenderPipeline; var previousQuality = QualitySettings.renderPipeline;
        var cameraObject = new GameObject("Forest GPU correctness camera"); var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        var warmup = RenderTexture.GetTemporary(16,16,24); bool async = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            camera.cullingMask = 0;
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=warmup });
            ValidateVariation();
            ValidateDistancePolicy();
            foreach (bool fern in new[] { false,true })
            {
                var near = AssetDatabase.LoadAssetAtPath<GameObject>(fern ? ForestFernPrefabBuilder.NearPath : LeafClusterPrefabBuilder.PrefabPath);
                var far = AssetDatabase.LoadAssetAtPath<GameObject>(fern ? ForestFernPrefabBuilder.FarPath : LeafClusterPrefabBuilder.FarPrefabPath);
                var coarse=fern?AssetDatabase.LoadAssetAtPath<GameObject>(ForestFernPrefabBuilder.CoarsePath):null;
                Require(!fern || coarse!=null,"Missing coarse fern asset.");
                var material = near.GetComponent<MeshRenderer>().sharedMaterial;
                Require(ForestScatterGpuRenderer.Supports(compute,material,far.GetComponent<MeshRenderer>().sharedMaterial,
                    coarse!=null?coarse.GetComponent<MeshRenderer>().sharedMaterial:null), "Forest asset fell back to CPU.");
                ValidateRouting(fern);
                ValidateResidency(compute,near,far,coarse,camera,fern);
                foreach (var prefab in coarse!=null?new[]{near,far,coarse}:new[] { near,far })
                {
                    ValidateShaders(prefab.GetComponent<MeshRenderer>().sharedMaterial);
                    ValidatePixels(prefab,camera,fern);
                }
            }
            LeafClusterValidation.Run();
            Debug.Log("FOREST GPU PASS: leaves/ferns >1023 instances; CPU/GPU range/frustum/three-LOD/density-ring parity with moving viewer; no stable-frame source uploads; append/relocation/eviction/slot reuse/visibility; actual near/mid/coarse forward/normal pixel and fade parity; variation table and shader variants. No FPS benchmark.");
        }
        finally
        {
            RenderTexture.ReleaseTemporary(warmup); Object.DestroyImmediate(cameraObject);
            GraphicsSettings.defaultRenderPipeline = previousPipeline; QualitySettings.renderPipeline = previousQuality;
            ShaderUtil.allowAsyncCompilation = async;
        }
    }
    static void ValidateDistancePolicy()
    {
        var grass=new GrassSettings{activeRingRadius=1,billboardRingRadius=3,subChunksPerChunk=10};
        var leaves=new LeafClusterSettings();int signature=leaves.PlacementSignature;
        using var system=new LeafClusterSystem(leaves,1937,128,.3f,10,grass);
        Require(Mathf.Abs(system.RenderDistance-92.16f)<.001f && Mathf.Abs(system.DensityRingSize-3.84f)<.001f,
            "Leaf range/rings do not follow grass with the 20% reduction.");
        var densities=ForestScatterPolicy.Densities(leaves);
        foreach(var point in new[]{new Vector2(3,1),new Vector2(6,.7f),new Vector2(10,.4f),new Vector2(14,.2f),new Vector2(30,.2f)})
            Require(Mathf.Abs(GrassStreamingPolicy.Density(point.x,densities)-point.y)<.0001f,"Leaf ring endpoint density is wrong.");
        leaves.densityRadius3=.2f;leaves.densityBeyond10=0;
        Require(leaves.PlacementSignature==signature,"Density ring tuning reseeds/regenerates placement.");
        leaves.useDistanceDensity=false;
        Require(ForestScatterPolicy.Densities(leaves)==Vector4.one,"Disabling ring thinning did not retain all candidates.");
        var fern=new FernSettings();var thresholds=ForestScatterPolicy.LodDistances(fern,grass,128,.3f);
        Require((thresholds-new Vector4(11.52f,23.04f,24.96f,51.84f)).sqrMagnitude<.001f,"Fern LODs do not follow the grass tiers/billboard boundary.");
        for(uint rank=0;rank<1024;rank++)
        {
            int previous=0;
            for(float d=0;d<=65;d+=.25f)
            {
                int selected=ForestScatterPolicy.SelectLod(LeafClusterGeneration.Unit(rank,193),LeafClusterGeneration.Unit(rank,307),d,thresholds,true,true);
                Require(selected>=previous,"Fern LOD selection reverses or duplicates.");previous=selected;
            }
            Require(previous==2,"Distant fern failed to reach the third LOD.");
        }
        grass.activeRingRadius=2;
        Require(ForestScatterPolicy.LodDistances(fern,grass,128,.3f).z>thresholds.z,"Fern billboard threshold did not track grass tuning.");
        grass.activeRingRadius=1;grass.subChunksPerChunk=2;
        var largeSubchunks=ForestScatterPolicy.LodDistances(fern,grass,128,.3f);
        Require(largeSubchunks.y<=largeSubchunks.z && Mathf.Abs(largeSubchunks.z-19.2f)<.001f,
            "Large grass subchunks pushed the coarse tier beyond the billboard boundary.");
        grass.activeRingRadius=0;
        var zeroRing=ForestScatterPolicy.LodDistances(fern,grass,128,.3f);
        Require(zeroRing.x<zeroRing.y && zeroRing.y<=zeroRing.z && zeroRing.z<zeroRing.w,"Zero detailed-grass radius inverted fern LODs.");
        fern.matchGrassLodDistances=false;
        Require(ForestScatterPolicy.LodDistances(fern,grass,128,.3f)==new Vector4(22,38,40,55),"Manual fern LOD controls failed.");
        var mesh=AssetDatabase.LoadAssetAtPath<GameObject>(ForestFernPrefabBuilder.CoarsePath).GetComponent<MeshFilter>().sharedMesh;
        Require(mesh.triangles.Length/3==52 && mesh.vertexCount==156,"Coarse fern geometry budget changed.");
        Debug.Log("FOREST RINGS PASS: leaf range 92.16, subchunk 3.84; 1/.7/.4/.2 tiers; independent rendering controls; fern grass/manual thresholds; 52-triangle coarse asset.");
    }
    static void ValidateVariation()
    {
        var table = AssetDatabase.LoadAssetAtPath<Texture2D>(LeafScatterVariationBuilder.Path);
        Require(table != null && table.width==256 && table.height==576 && table.filterMode==FilterMode.Point,
            "Missing or incorrectly sampled leaf variation table.");
        var data = table.GetPixels();
        for(int seed=0;seed<8192;seed++) for(int leaf=0;leaf<9;leaf++)
        {
            var shape=data[(seed*9+leaf)*2];var placement=data[(seed*9+leaf)*2+1];
            Require(Mathf.Abs(shape.r*shape.r+shape.g*shape.g-1)<.0001f && shape.b>=.6f && shape.b<=1.4f &&
                shape.a>=.88f && shape.a<=1.06f,"Baked leaf transform escaped its original variation ranges.");
            Require(leaf==0?placement.b<0:placement.b>=0 && placement.b<1,"Baked leaf visibility changed first-leaf retention: seed "+seed+" leaf "+leaf+" threshold "+placement.b);
        }
    }
    static void ValidateRouting(bool fern)
    {
        var settings = fern ? (LeafClusterSettings)new FernSettings() : new LeafClusterSettings();
        using var system = new LeafClusterSystem(settings,1937,128,.3f,10);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var resolve = typeof(LeafClusterSystem).GetMethod("ResolveAssets",flags);
        var configure = typeof(LeafClusterSystem).GetMethod("ConfigureGpu",flags);
        resolve.Invoke(system,null); configure.Invoke(system,null);
        Require(system.UsesGpu,"Default forest system did not resolve its Resources compute path.");
        settings.gpuIndirectRendering=false; configure.Invoke(system,null);
        Require(!system.UsesGpu,"CPU toggle retained the GPU renderer.");
        settings.gpuIndirectRendering=true; configure.Invoke(system,null);
        Require(system.UsesGpu,"GPU toggle could not restore the renderer.");
    }
    public static void RunBatch()
    {
        try
        {
            var texture = LeafScatterVariationBuilder.Build();
            var material = AssetDatabase.LoadAssetAtPath<Material>(LeafClusterPrefabBuilder.MaterialPath);
            material.SetTexture("_LeafVariationTex",texture); EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
            Run(); EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    static void ValidateResidency(ComputeShader compute,GameObject near,GameObject far,GameObject coarse,Camera camera,bool fern)
    {
        using var record = ForestFloorPreview.Fixture(false);
        var settings = fern ? (LeafClusterSettings)new FernSettings() : new LeafClusterSettings();
        LeafClusterGeneration Make(int count,int identity)
        {
            var generation = new LeafClusterGeneration(record,settings,1937,128,.3f,10,.4f);
            for(int i=0;i<count;i++) generation.Instances.Add(new LeafClusterInstance {
                position=new Vector3(i%13==0?100:i%7-3,i%17==0?100:0,5+i%85),
                rotation=Quaternion.Euler(0,i%360,0),scale=1,tint=new Vector4(identity+i,1,1,1),rank=(uint)(identity+i) });
            return generation;
        }
        var generation = Make(1500,1); var coord = new ChunkCoord(0,0);
        var otherCoord = new ChunkCoord(-1,3); var other = Make(128,20000);
        var expected = new List<LeafClusterInstance>();
        var planes = new[] {new Plane(Vector3.right,10),new Plane(Vector3.left,10),new Plane(Vector3.up,10),
            new Plane(Vector3.down,10),new Plane(Vector3.forward,0),new Plane(Vector3.back,100)};
        Mesh nm=near.GetComponent<MeshFilter>().sharedMesh,fm=far.GetComponent<MeshFilter>().sharedMesh;
        Material nmat=near.GetComponent<MeshRenderer>().sharedMaterial,fmat=far.GetComponent<MeshRenderer>().sharedMaterial;
        Mesh cm=coarse!=null?coarse.GetComponent<MeshFilter>().sharedMesh:null;
        Material cmat=coarse!=null?coarse.GetComponent<MeshRenderer>().sharedMaterial:null;
        using(var renderer = new ForestScatterGpuRenderer(compute,fern?3:2))
        {
            void Draw(bool hasFar=true,float range=100,bool rings=false,float viewerZ=0,bool enableCoarse=true)
            {
                Vector3 viewer=new Vector3(0,0,viewerZ);Vector4 densities=rings?new Vector4(1,.7f,.4f,.2f):Vector4.one;
                bool hasCoarse=hasFar && fern && enableCoarse;
                renderer.Draw(nm,nmat,Matrix4x4.identity,hasFar?fm:null,hasFar?fmat:null,Matrix4x4.identity,
                    camera,planes,viewer,range,8,18,30,enableCoarse?cm:null,enableCoarse?cmat:null,Matrix4x4.identity,40,55,3.84f,densities);
                var seen = new HashSet<int>();
                for(int lod=0;lod<(fern?3:2);lod++) foreach(uint id in renderer.ReadVisible(lod))
                {
                    var source=renderer.ReadSource(id);int identity=Mathf.RoundToInt(source.Tint.x);
                    Require(seen.Add(identity),"GPU culling duplicated a source or rendered stale block records.");
                    float distance=new Vector2(source.Sphere.x-viewer.x,source.Sphere.z-viewer.z).magnitude;
                    Require(lod==ForestScatterPolicy.SelectLod(source.Selection.x,source.Selection.w,distance,
                        new Vector4(18,30,40,55),hasFar,hasCoarse),"GPU LOD differs from CPU selection.");
                }
                var wanted = new HashSet<int>();
                foreach(var instance in expected)
                    if(new Vector2(instance.position.x-viewer.x,instance.position.z-viewer.z).magnitude<=range+.4f &&
                        LeafClusterGeneration.Unit(instance.rank,277)<GrassStreamingPolicy.Density(
                            new Vector2(instance.position.x-viewer.x,instance.position.z-viewer.z).magnitude/3.84f,densities) &&
                        LeafClusterSystem.InsideFrustum(instance.position,.4f,planes)) wanted.Add(Mathf.RoundToInt(instance.tint.x));
                Require(seen.SetEquals(wanted),"GPU range/frustum/residency selection differs from CPU.");
            }
            renderer.Sync(coord,generation,true,.4f,fern);expected.AddRange(generation.Instances);Draw();
            Require(renderer.ReadVisible(0).Length+renderer.ReadVisible(1).Length+(fern?renderer.ReadVisible(2).Length:0)>1023,"Fixture did not cross CPU batching limit.");
            int uploads=renderer.SourceUploads;
            for(int frame=0;frame<3;frame++) {renderer.Sync(coord,generation,true,.4f,fern);Draw(true,92.16f,true,frame*18);}
            Require(renderer.SourceUploads==uploads,"Stable frame re-uploaded instance records.");
            Draw(true,100,false,0,false);Draw(); // Coarse disabled/re-enabled without stale output.
            renderer.Sync(coord,generation,false,.4f,fern);expected.Clear();Draw();
            renderer.Sync(coord,generation,true,.4f,fern);expected.AddRange(generation.Instances);Draw(false);
            // Force a block relocation and buffer growth, then check old records are rejected.
            generation.Instances.AddRange(Make(900,5000).Instances);
            renderer.Sync(coord,generation,true,.4f,fern);expected.Clear();expected.AddRange(generation.Instances);Draw();
            renderer.Sync(otherCoord,other,true,.4f,fern);expected.AddRange(other.Instances);Draw();
            renderer.Remove(coord);var replacement=Make(47,30000);renderer.Sync(coord,replacement,true,.4f,fern);
            expected.Clear();expected.AddRange(replacement.Instances);expected.AddRange(other.Instances);Draw();Draw(true,50);
            renderer.Remove(coord);renderer.Remove(otherCoord);expected.Clear();Draw();
            Require(renderer.ResidentInstances==0 && renderer.DrawCalls==0,"Empty residency retained rendering.");
        }
    }
    static void ValidateShaders(Material source)
    {
        var material = new Material(source) {enableInstancing=true};
        try
        {
            // Draw commands below select actual CPU/procedural variants. Unity's
            // internal instancing keywords cannot be enabled manually.
            for(int pass=0;pass<material.passCount;pass++)ShaderUtil.CompilePass(material,pass,true);
            foreach(var message in ShaderUtil.GetShaderMessages(source.shader))
                Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
        }
        finally {Object.DestroyImmediate(material);}
    }
    static void ValidatePixels(GameObject prefab,Camera camera,bool fern)
    {
        var mesh=prefab.GetComponent<MeshFilter>().sharedMesh;var source=prefab.GetComponent<MeshRenderer>().sharedMaterial;
        var cpu=new Material(source){enableInstancing=true};var gpu=new Material(source){enableInstancing=true};
        var transform=new[] {Matrix4x4.TRS(new Vector3(-.7f,0,2),Quaternion.Euler(0,37,0),Vector3.one*(fern?1.5f:1.1f)),
            Matrix4x4.TRS(new Vector3(.8f,.05f,2.8f),Quaternion.Euler(0,143,0),Vector3.one*(fern?1.8f:.8f))};
        var tint=new[] {new Vector4(.8f,1,.8f,1),new Vector4(1,.85f,.7f,1)};
        var scatter=new[] {fern?Vector4.zero:LeafClusterSystem.ScatterParams(37),fern?Vector4.zero:LeafClusterSystem.ScatterParams(973)};
        var records=new ForestScatterGpuRenderer.Source[2];
        for(int i=0;i<2;i++)records[i]=new ForestScatterGpuRenderer.Source{Transform=transform[i],Tint=tint[i],Scatter=scatter[i]};
        var buffer=new ComputeBuffer(2,128);buffer.SetData(records);var indices=new ComputeBuffer(2,4);indices.SetData(new uint[]{0,1});
        var args=new ComputeBuffer(5,4,ComputeBufferType.IndirectArguments);
        args.SetData(new uint[]{mesh.GetIndexCount(0),2,mesh.GetIndexStart(0),(uint)mesh.GetBaseVertex(0),0});
        var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGBFloat);var pixels=new Texture2D(256,256,TextureFormat.RGBAFloat,false,true);
        var command=new CommandBuffer();var properties=new MaterialPropertyBlock();RenderTexture previous=RenderTexture.active;
        camera.transform.position=new Vector3(0,1.8f,-2);camera.transform.LookAt(new Vector3(0,0,2));camera.aspect=1;camera.fieldOfView=50;
        try
        {
            target.Create();
            Color[] Render(bool indirect,int pass,float fadeEnd)
            {
                properties.Clear();properties.SetBuffer("_ForestSources",buffer);properties.SetBuffer("_ForestVisible",indices);
                properties.SetMatrix("_ForestMeshLocal",Matrix4x4.identity);properties.SetVectorArray("_LeafInstanceTint",tint);
                properties.SetVectorArray("_LeafScatterParams",scatter);properties.SetVector("_LeafViewer",new Vector4(0,0,0,1));
                properties.SetFloat("_FadeStart",0);properties.SetFloat("_FadeEnd",fadeEnd);
                command.Clear();command.SetRenderTarget(target);command.ClearRenderTarget(true,true,Color.black);
                command.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));
                command.SetGlobalMatrix("unity_MatrixVP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);
                command.SetGlobalVector("_WorldSpaceCameraPos",camera.transform.position);command.SetGlobalVector("_MainLightPosition",new Vector4(0,1,0,0));
                command.SetGlobalVector("_MainLightColor",Vector4.one);
                if(indirect)command.DrawMeshInstancedIndirect(mesh,0,gpu,pass,args,0,properties);
                else command.DrawMeshInstanced(mesh,0,cpu,pass,transform,2,properties);
                Graphics.ExecuteCommandBuffer(command);RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,256,256),0,0);pixels.Apply();return pixels.GetPixels();
            }
            foreach(int pass in new[]{0,2}) foreach(float fade in new[]{100f,4f,.1f})
            {
                var a=Render(false,pass,fade);var b=Render(true,pass,fade);double difference=0;
                for(int i=0;i<a.Length;i++)difference+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);
                Require(difference/(a.Length*3)<.75/255,"Forest CPU/indirect pixel mismatch: "+prefab.name+" pass "+pass);
                // Float targets retain negative normals; normal-pass alpha is
                // zero while the cleared target's alpha is one.
                int coverage=a.Count(p=>pass==2?p.a<.5f:p.r+p.g+p.b>.06f);
                int gpuCoverage=b.Count(p=>pass==2?p.a<.5f:p.r+p.g+p.b>.06f);
                Require(Math.Abs(coverage-gpuCoverage)<=2,"Forest CPU/indirect coverage mismatch: "+prefab.name);
                Require(fade==100?coverage>30:fade>.1f?coverage>0:coverage==0,"Forest render/fade fixture has wrong coverage: "+prefab.name+" pass "+pass+" fade "+fade+" pixels "+coverage);
            }
        }
        finally
        {
            RenderTexture.active=previous;command.Dispose();buffer.Release();indices.Release();args.Release();target.Release();
            Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);Object.DestroyImmediate(cpu);Object.DestroyImmediate(gpu);
        }
    }
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
