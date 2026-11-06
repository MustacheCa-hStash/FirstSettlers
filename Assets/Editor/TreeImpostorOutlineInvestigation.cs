using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class TreeImpostorOutlineInvestigation
{
    const string Folder = ".utmp/tree-outlines";
    static readonly int[] Budgets = { 0, 4, 8, 12, 16 };
    sealed class Species
    {
        public string Name;
        public float Radius;
        public Vector3 Center;
        public Material[] Lit = new Material[5], Coverage = new Material[5], Footprint = new Material[5];
        public Mesh[] Meshes = new Mesh[5];
        public readonly List<TreeImpostorOutlineBinding> Bindings = new();
    }
    sealed class DrawGroup : IDisposable
    {
        public Species Species;
        public ComputeBuffer Instances;
        public ComputeBuffer[] Arguments = new ComputeBuffer[5];
        public MaterialPropertyBlock Block = new();
        public void Dispose() { Instances?.Release();foreach(var args in Arguments)args?.Release(); }
    }
    static readonly List<Species> species = new();
    static Camera camera;
    static IEnumerator routine;
    static Mesh pressureMesh, quad;
    static Material pressure;
    static long lostPixels;
    static bool performanceOnly;
    static bool qualityOnly, regressions;
    public static void RunPerformanceBatch() { performanceOnly = true; RunBatch(); }
    public static void RunValidationBatch() { qualityOnly = regressions = true; RunBatch(); }

    public static void RunBatch()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            ShaderUtil.allowAsyncCompilation = false;
            RenderSettings.fog = false; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.gray;
            var sun = new GameObject("Outline study sun").AddComponent<Light>(); sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(40,-30,0); sun.intensity = 1;
            camera = new GameObject("Outline study camera").AddComponent<Camera>(); camera.enabled = false;
            camera.nearClipPlane = .1f; camera.farClipPlane = 12000;
            var bootstrap = new RenderTexture(16,16,24); bootstrap.Create();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = bootstrap });
            bootstrap.Release(); Object.DestroyImmediate(bootstrap);
            GenerateCoverageShaders(); LoadSpecies();
            quad = Quad(); pressureMesh = PressureMesh(); pressure = new Material(Shader.Find("Hidden/TreeOutlinePressure"));
            routine = Run(); EditorApplication.update += Tick;
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Tick()
    {
        try { if (!routine.MoveNext()) Finish(0); }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Finish(int code)
    {
        EditorApplication.update -= Tick; (routine as IDisposable)?.Dispose();
        foreach (var item in species)
        {
            foreach (var binding in item.Bindings) binding?.Dispose();
            foreach (var mat in item.Lit) if (mat != null) Object.DestroyImmediate(mat);
            foreach (var mat in item.Coverage) if (mat != null) Object.DestroyImmediate(mat);
            foreach (var mat in item.Footprint) if (mat != null) Object.DestroyImmediate(mat);
            foreach (var mesh in item.Meshes) if (mesh != null) Object.DestroyImmediate(mesh);
        }
        species.Clear();
        if (camera != null) Object.DestroyImmediate(camera.gameObject);
        if (pressure != null) Object.DestroyImmediate(pressure);
        if (pressureMesh != null) Object.DestroyImmediate(pressureMesh);
        if (quad != null) Object.DestroyImmediate(quad);
        foreach (string name in new[] { "TreeHQOctaImpostor", "SpruceOctaImpostor" }) AssetDatabase.DeleteAsset("Assets/Editor/TreeOutlineCoverage" + name + ".shader");
        foreach (string name in new[] { "TreeHQOctaImpostor", "SpruceOctaImpostor" }) AssetDatabase.DeleteAsset("Assets/Editor/TreeOutlineFootprint" + name + ".shader");
        if(code==0&&regressions)
        {
            try { TreeImpostorOutlineValidation.RunIntegrated();DistantTreeResidentValidation.RunAllBatch();return; }
            catch(Exception error){Debug.LogException(error);code=1;}
        }
        EditorApplication.Exit(code);
    }
    static Mesh Quad()
    {
        var mesh = new Mesh { vertices = new[] { new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(-1,1,0),new Vector3(1,1,0) },
            uv = new[] { Vector2.zero,Vector2.right,Vector2.up,Vector2.one }, triangles = new[] {0,2,1,1,2,3} };
        mesh.RecalculateBounds(); return mesh;
    }
    static Mesh PressureMesh()
    {
        const int side = 256; var vertices = new Vector3[(side+1)*(side+1)]; var indices = new int[side*side*6];
        for (int y=0;y<=side;y++) for(int x=0;x<=side;x++) vertices[x+y*(side+1)] = new Vector3(x*2f/side-1,y*2f/side-1,0);
        for(int y=0;y<side;y++) for(int x=0;x<side;x++)
        { int v=x+y*(side+1),i=(x+y*side)*6; indices[i]=v;indices[i+1]=v+1;indices[i+2]=v+side+1;indices[i+3]=v+1;indices[i+4]=v+side+2;indices[i+5]=v+side+1; }
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32, vertices=vertices,triangles=indices }; mesh.RecalculateBounds(); return mesh;
    }
    static void GenerateCoverageShaders()
    {
        foreach(string name in new[]{"TreeHQOctaImpostor","SpruceOctaImpostor"})
        {
            string text = File.ReadAllText("Assets/Shaders/"+name+".shader");
            text = text.Replace("Shader \"Custom/"+name+"\"","Shader \"Hidden/TreeOutlineCoverage/"+name+"\"");
            text=text.Replace("CBUFFER_START(UnityPerMaterial)","CBUFFER_START(UnityPerMaterial)\n float _OutlineGeometryOnly;");
            int properties=text.IndexOf('{',text.IndexOf("Properties"));
            text=text.Insert(properties+1,"\n _OutlineGeometryOnly(\"Geometry only\", Float) = 0\n");
            int begin=text.IndexOf("half4 frag(Varyings input)"),brace=text.IndexOf('{',begin),end=brace+1,depth=1;
            while(depth>0){if(text[end]=='{')depth++;if(text[end]=='}')depth--;end++;}
            string frag = @"half4 frag(Varyings input) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(input);
                if (_OutlineGeometryOnly > 1.5) return half4(input.proxyUV,(input.atlasFrame.x+input.atlasFrame.y*_FramesPerAxis),1);
                if (_OutlineGeometryOnly > .5) return half4(1,1,1,1);
                half4 a = SampleAtlas(TEXTURE2D_ARGS(_AlbedoCoverage,sampler_AlbedoCoverage),input.atlasFrame,input.proxyUV,_AlbedoCoverage_TexelSize.zw);
                clip(a.a-_Cutoff); half4 id=SampleMaterialIdAtlasPoint(input.atlasFrame,input.proxyUV);
                clip(max(id.r,id.g)-.01h); return half4(1,1,1,a.a); }";
            File.WriteAllText("Assets/Editor/TreeOutlineCoverage"+name+".shader",text.Substring(0,begin)+frag+text.Substring(end));
            var footprint=(text.Substring(0,begin)+"half4 frag(Varyings input) : SV_Target { return half4(1,1,1,1); }"+text.Substring(end))
                .Replace("Hidden/TreeOutlineCoverage/","Hidden/TreeOutlineFootprint/");
            File.WriteAllText("Assets/Editor/TreeOutlineFootprint"+name+".shader",footprint);
        }
        AssetDatabase.Refresh();
    }
    static void LoadSpecies()
    {
        var library=AssetDatabase.LoadAssetAtPath<TreeImpostorOutlineLibrary>(TreeImpostorOutlineBaker.AssetPath);
        if(library==null||library.entries==null||library.entries.Length!=6)throw new Exception("Bake the six outline entries first");
        foreach(string name in new[]{"RedMaple","SugarMaple","Oak","Birch","WhitePine","Spruce"})
        {
            var source=AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/M_Trees/{name}HQ/{name}HQ_OctaImpostor_M.mat");
            var item=new Species{Name=name,Radius=source.GetFloat("_CaptureRadius"),Center=source.GetVector("_CaptureCenterLS")};
            for(int q=0;q<Budgets.Length;q++)
            {
                var material=new Material(source){enableInstancing=true};material.SetFloat("_WindStrength",0);
                material.DisableKeyword("TREE_IMPOSTOR_OUTLINE");material.DisableKeyword("PROCEDURAL_INSTANCING_ON");
                var mesh=Quad();mesh.bounds=DistantTreeGpuBatch.RenderingBounds(mesh.bounds,material);
                TreeImpostorOutlineBinding binding=null;
                if(q>0)
                {
                    binding=TreeImpostorOutlineBinding.Apply(material,mesh,library,(TreeImpostorOutlineQuality)Budgets[q]);
                    if(binding==null)throw new Exception("Missing fitted outline: "+name+" "+Budgets[q]);item.Bindings.Add(binding);
                }
                item.Lit[q]=material;item.Meshes[q]=mesh;
                item.Coverage[q]=new Material(material);item.Coverage[q].shader=Shader.Find("Hidden/TreeOutlineCoverage/"+source.shader.name.Substring(7));
                if(q>0)item.Coverage[q].EnableKeyword("TREE_IMPOSTOR_OUTLINE");
                item.Footprint[q]=new Material(material);item.Footprint[q].shader=Shader.Find("Hidden/TreeOutlineFootprint/"+source.shader.name.Substring(7));
                if(q>0)item.Footprint[q].EnableKeyword("TREE_IMPOSTOR_OUTLINE");
                if(binding!=null){binding.Bind(item.Coverage[q]);binding.Bind(item.Footprint[q]);}
                item.Coverage[q].SetFloat("_WindStrength",.15f);item.Footprint[q].SetFloat("_WindStrength",.15f);
                ShaderUtil.CompilePass(material,0,true);ShaderUtil.CompilePass(item.Coverage[q],0,true);
            }
            species.Add(item);
        }
    }
    static Vector3 Decode(Vector2 uv)
    {
        var e=uv*2-Vector2.one;var n=new Vector3(e.x,e.y,1-Mathf.Abs(e.x)-Mathf.Abs(e.y));
        if(n.z<0){float x=n.x;n.x=(1-Mathf.Abs(n.y))*(x>=0?1:-1);n.y=(1-Mathf.Abs(x))*(n.y>=0?1:-1);}return n.normalized;
    }
    static void CameraCommands(CommandBuffer command,RenderTexture target)
    {
        command.SetRenderTarget(target);command.ClearRenderTarget(true,true,Color.black);
        var projection=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true);
        command.SetViewProjectionMatrices(camera.worldToCameraMatrix,projection);
        command.SetGlobalMatrix("unity_MatrixVP",projection*camera.worldToCameraMatrix);
        command.SetGlobalVector("_WorldSpaceCameraPos",camera.transform.position);
    }
    static Color32[] Pixels(RenderTexture target)
    {
        var prior=RenderTexture.active;RenderTexture resolved=null;
        if(target.antiAliasing>1){resolved=RenderTexture.GetTemporary(target.width,target.height,0);Graphics.Blit(target,resolved);}
        var texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try{RenderTexture.active=resolved!=null?resolved:target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();return texture.GetPixels32();}
        finally{RenderTexture.active=prior;Object.DestroyImmediate(texture);if(resolved!=null)RenderTexture.ReleaseTemporary(resolved);}
    }
    static Matrix4x4[] ViewGrid(Species item,bool boundaries)
    {
        var directions=new List<Vector3>();
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)
        {
            if(!boundaries)directions.Add(Decode(new Vector2((x+.5f)/8,(y+.5f)/8)));
            else
            {
                if(x<7)foreach(float sign in new[]{-1f,1f})directions.Add(Decode(new Vector2((x+1f)/8+sign*.0001f,(y+.5f)/8)));
                if(y<7)foreach(float sign in new[]{-1f,1f})directions.Add(Decode(new Vector2((x+.5f)/8,(y+1f)/8+sign*.0001f)));
            }
        }
        int side=boundaries?16:8;float spacing=item.Radius*2.2f;camera.orthographic=true;camera.orthographicSize=spacing*side*.5f;
        camera.aspect=1;camera.transform.SetPositionAndRotation(new Vector3(0,0,-10000),Quaternion.identity);
        var result=new Matrix4x4[directions.Count];
        for(int i=0;i<result.Length;i++)
        {
            var center=new Vector3((i%side-(side-1)*.5f)*spacing,(i/side-(side-1)*.5f)*spacing,0);
            var rotation=Quaternion.FromToRotation(directions[i],(camera.transform.position-center).normalized);
            result[i]=Matrix4x4.TRS(center-rotation*item.Center,rotation,Vector3.one);
        }
        return result;
    }
    static IEnumerator Run()
    {
        if(!performanceOnly)using(var quality=new StreamWriter(Folder+"/quality.csv"))
        {
            quality.WriteLine("species,views,cellPixels,msaa,vertices,referencePixels,lostPixels,addedPixels,geometryClippedPixels");
            foreach(var item in species)foreach(bool boundaries in new[]{false,true})foreach(int cell in new[]{32,128})
            {
                int side=boundaries?16:8,size=side*cell;var matrices=ViewGrid(item,boundaries);
                foreach(int msaa in new[]{1,4})
                {
                    var target=new RenderTexture(size,size,24){antiAliasing=msaa};target.Create();Color32[] reference=null;
                    for(int q=0;q<5;q++)
                    {
                        using(var command=new CommandBuffer())
                        {CameraCommands(command,target);command.DrawMeshInstanced(item.Meshes[q],0,item.Coverage[q],0,matrices,matrices.Length);Graphics.ExecuteCommandBuffer(command);}
                        var actual=Pixels(target);if(reference==null)reference=actual;int missing=0,added=0,covered=0;
                        for(int p=0;p<actual.Length;p++){bool a=actual[p].r>20,b=reference[p].r>20;if(b)covered++;if(b&&!a)missing++;if(a&&!b)added++;}
                        int geometryMissing=0;
                        if(q>0)
                        {
                            using(var command=new CommandBuffer())
                            {CameraCommands(command,target);command.DrawMeshInstanced(item.Meshes[q],0,item.Footprint[q],0,matrices,matrices.Length);Graphics.ExecuteCommandBuffer(command);}
                            var geometry=Pixels(target);int geometryCovered=0;foreach(var pixel in geometry)if(pixel.r>20)geometryCovered++;
                            if(geometryMissing>0)Debug.Log("GEOMETRY_COVERED "+geometryCovered);
                            item.Coverage[q].SetFloat("_OutlineGeometryOnly",0);
                            for(int p=0;p<geometry.Length;p++)if(reference[p].r>20&&geometry[p].r<=20)geometryMissing++;
                        }
                        quality.WriteLine($"{item.Name},{(boundaries?"boundaries":"64-centers")},{cell},{msaa},{Budgets[q]},{covered},{missing},{added},{geometryMissing}");quality.Flush();lostPixels+=missing;
                        if(q==0&&covered==0)throw new Exception("Coverage fixture rendered no pixels");
                        if(geometryMissing>0)
                        {
                            var diagnostic=new RenderTexture(size,size,24,RenderTextureFormat.ARGBFloat);diagnostic.Create();
                            item.Coverage[0].SetFloat("_OutlineGeometryOnly",2);
                            using(var command=new CommandBuffer()){CameraCommands(command,diagnostic);command.DrawMeshInstanced(item.Meshes[0],0,item.Coverage[0],0,matrices,matrices.Length);Graphics.ExecuteCommandBuffer(command);}
                            var previous=RenderTexture.active;RenderTexture.active=diagnostic;
                            var texture=new Texture2D(size,size,TextureFormat.RGBAFloat,false);texture.ReadPixels(new Rect(0,0,size,size),0,0);texture.Apply();var values=texture.GetPixels();
                            var entry=AssetDatabase.LoadAssetAtPath<TreeImpostorOutlineLibrary>(TreeImpostorOutlineBaker.AssetPath).Find(item.Lit[0]);
                            var shape=entry.shapes[q-1];
                            int reports=0;
                            for(int p=0;p<actual.Length&&reports<8;p++)if(reference[p].r>20&&actual[p].r<=20)
                            {
                                var value=values[p];int frame=Mathf.RoundToInt(value.b);var polygon=new List<Vector2>();
                                for(int v=0;v<shape.vertices;v++)polygon.Add(shape.uv[frame*shape.vertices+v]);
                                Debug.Log($"OUTLINE_DIAGNOSTIC pixel={p%size},{p/size} uv={value.r:F6},{value.g:F6} frame={frame} contained={TreeImpostorOutlineBaker.Contains(polygon,new Vector2(value.r,value.g))} rect={polygon[0]} {polygon[1]} {polygon[2]} {polygon[3]}");reports++;
                            }
                            RenderTexture.active=previous;Object.DestroyImmediate(texture);diagnostic.Release();Object.DestroyImmediate(diagnostic);item.Coverage[0].SetFloat("_OutlineGeometryOnly",0);
                            throw new Exception($"Fitted geometry clipped {geometryMissing} pixels: {item.Name} q={Budgets[q]} boundaries={boundaries} cell={cell} msaa={msaa}");
                        }
                        if(missing>Math.Max(8,covered*.01))throw new Exception($"Fitted interpolation changed excessive coverage: {item.Name} q={Budgets[q]} missing={missing}/{covered}");
                    }
                    target.Release();Object.DestroyImmediate(target);yield return null;
                }
            }
        }
        if(!performanceOnly)Debug.Log("TREE_OUTLINE_COVERAGE_PASS: all six visuals, 64 frame centers, both sides of every octahedral frame boundary, 32/128 pixel cells, MSAA 1/4; zero geometric silhouette clipping; raster differences recorded separately.");
        if(qualityOnly)yield break;
        var performance=Performance();while(performance.MoveNext())yield return null;
    }
    static IEnumerator Performance()
    {
        using var csv=new StreamWriter(Folder+"/performance.csv");
        csv.WriteLine("workload,pressure,width,height,trees,verticesPerTree,pass,syncMedianMs,syncP95Ms,treeGpuMedianMs,totalGpuMedianMs,treeGpuSamples,totalGpuSamples,changedPixels,meanRgbError");
        var treeSampler=CustomSampler.Create("FS.TreeOutline.Trees",true);
        var totalSampler=CustomSampler.Create("FS.TreeOutline.Total",true);
        var treeRecorder=treeSampler.GetRecorder();var totalRecorder=totalSampler.GetRecorder();
        treeRecorder.enabled=true;totalRecorder.enabled=true;
        foreach(int height in new[]{1440,2160})foreach(string workload in new[]{"mixed-near","mixed-horizon","sparse","maple","spruce"})
        {
            if(height==2160&&(workload=="maple"||workload=="spruce"))continue;
            int width=height*16/9,count=workload=="mixed-horizon"?8192:workload=="sparse"?128:1024;
            camera.orthographic=false;camera.fieldOfView=60;camera.aspect=16f/9;
            camera.transform.SetPositionAndRotation(new Vector3(0,16,0),Quaternion.Euler(2,0,0));
            var groups=Forest(workload,count);
            var target=new RenderTexture(width,height,24);target.Create();
            try
            {
                foreach(string load in new[]{"none","vertex","fragment","both"})
                {
                    if(load!="none"&&(height==2160||workload=="sparse"||workload=="maple"||workload=="spruce"))continue;
                    Color32[] reference=null;
                    for(int pass=0;pass<2;pass++)for(int step=0;step<5;step++)
                    {
                        int q=pass==0?step:4-step;
                        using var command=new CommandBuffer();CameraCommands(command,target);
                        command.BeginSample(totalSampler);
                        if(load=="vertex"||load=="both")
                        {
                            var block=new MaterialPropertyBlock();block.SetInt("_VertexIterations",16);block.SetInt("_FragmentIterations",0);
                            for(int i=0;i<3;i++)command.DrawMesh(pressureMesh,Matrix4x4.identity,pressure,0,0,block);
                        }
                        if(load=="fragment"||load=="both")
                        {
                            var block=new MaterialPropertyBlock();block.SetInt("_VertexIterations",0);block.SetInt("_FragmentIterations",16);
                            command.DrawMesh(quad,Matrix4x4.identity,pressure,0,0,block);
                        }
                        command.BeginSample(treeSampler);
                        foreach(var group in groups)
                            command.DrawMeshInstancedIndirect(group.Species.Meshes[q],0,group.Species.Lit[q],0,group.Arguments[q],0,group.Block);
                        command.EndSample(treeSampler);command.EndSample(totalSampler);
                        var sync=new List<double>();var treeGpu=new List<double>();var totalGpu=new List<double>();
                        for(int frame=0;frame<32;frame++)
                        {
                            long start=Stopwatch.GetTimestamp();Graphics.ExecuteCommandBuffer(command);
                            var read=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);read.WaitForCompletion();
                            if(read.hasError)throw new Exception("Outline timing GPU fence failed");
                            if(frame>=8)
                            {
                                sync.Add((Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency);
                                if(treeRecorder.gpuSampleBlockCount>0)treeGpu.Add(treeRecorder.gpuElapsedNanoseconds/1000000.0);
                                if(totalRecorder.gpuSampleBlockCount>0)totalGpu.Add(totalRecorder.gpuElapsedNanoseconds/1000000.0);
                            }
                            yield return null;
                        }
                        var pixels=Pixels(target);if(reference==null)reference=pixels;long difference=0;int changed=0;
                        for(int i=0;i<pixels.Length;i++)
                        {int d=Math.Abs(pixels[i].r-reference[i].r)+Math.Abs(pixels[i].g-reference[i].g)+Math.Abs(pixels[i].b-reference[i].b);difference+=d;if(d>9)changed++;}
                        // Interpolation can change quantized RGB by a unit; broad geometry loss cannot.
                        double meanError=difference/(pixels.Length*3.0*255);
                        if(meanError>.0002)throw new Exception("Outline lit-render parity failed: "+workload+" q="+Budgets[q]+" pixels="+changed+" error="+meanError);
                        if(workload=="mixed-near"&&load=="none"&&height==1440&&pass==0)
                        {
                            var preview=new Texture2D(width,height,TextureFormat.RGBA32,false);preview.SetPixels32(pixels);preview.Apply();
                            File.WriteAllBytes(Folder+"/lit-"+Budgets[q]+".png",preview.EncodeToPNG());Object.DestroyImmediate(preview);
                        }
                        sync.Sort();treeGpu.Sort();totalGpu.Sort();
                        csv.WriteLine($"{workload},{load},{width},{height},{count},{Budgets[q]},{pass},{sync[sync.Count/2]:F6},{sync[22]:F6},{(treeGpu.Count>0?treeGpu[treeGpu.Count/2]:0):F6},{(totalGpu.Count>0?totalGpu[totalGpu.Count/2]:0):F6},{treeGpu.Count},{totalGpu.Count},{changed},{meanError:F9}");csv.Flush();
                    }
                    Debug.Log($"TREE_OUTLINE_TIMING {workload} {height} {load} complete");
                }
            }
            finally {foreach(var group in groups)group.Dispose();target.Release();Object.DestroyImmediate(target);}
        }
        treeRecorder.enabled=false;totalRecorder.enabled=false;
        Debug.Log("TREE_OUTLINE_INVESTIGATION_PASS: geometry budgets, lit rendering parity, synthetic additional vertex/fragment work and reversed timing order.");
    }
    static List<DrawGroup> Forest(string workload,int count)
    {
        var buckets=new List<Matrix4x4>[6];for(int i=0;i<6;i++)buckets[i]=new List<Matrix4x4>();
        var random=new System.Random(41271);
        for(int i=0;i<count;i++)
        {
            int index=workload=="maple"?0:workload=="spruce"?5:i%6;
            float z=workload=="mixed-horizon"?1000+(float)random.NextDouble()*3000:200+(float)random.NextDouble()*600;
            float x=((float)random.NextDouble()*2-1)*z*.8f;
            float y=Mathf.Sin(x*.02f)*4+Mathf.Cos(z*.01f)*3;
            buckets[index].Add(Matrix4x4.TRS(new Vector3(x,y,z),Quaternion.Euler(0,(float)random.NextDouble()*360,0),Vector3.one*1.4f));
        }
        var result=new List<DrawGroup>();
        for(int index=0;index<6;index++)
        {
            if(buckets[index].Count==0)continue;
            var item=species[index];var group=new DrawGroup{Species=item};
            var instances=new DistantTreeGpuBatch.VisibleInstance[buckets[index].Count];
            for(int i=0;i<instances.Length;i++)instances[i]=new DistantTreeGpuBatch.VisibleInstance{ObjectToWorld=buckets[index][i],Tint=Vector4.one,Fade=new Vector4(1,1,0,0)};
            group.Instances=new ComputeBuffer(instances.Length,96);group.Instances.SetData(instances);
            group.Block.SetBuffer("_DistantTreeInstances",group.Instances);group.Block.SetFloat("_DistantTreeEnabled",1);group.Block.SetFloat("_DistantTreeBillboard",1);
            for(int q=0;q<5;q++)
            {
                item.Lit[q].EnableKeyword("PROCEDURAL_INSTANCING_ON");
                group.Arguments[q]=new ComputeBuffer(5,4,ComputeBufferType.IndirectArguments);
                group.Arguments[q].SetData(new uint[]{item.Meshes[q].GetIndexCount(0),(uint)instances.Length,item.Meshes[q].GetIndexStart(0),(uint)item.Meshes[q].GetBaseVertex(0),0});
            }
            result.Add(group);
        }
        return result;
    }
}
