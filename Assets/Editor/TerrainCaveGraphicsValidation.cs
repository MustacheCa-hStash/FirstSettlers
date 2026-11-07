using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

/// <summary>Actual production URP cave meshes, fallback GPU lookup, sunlight and local flames.</summary>
public static class TerrainCaveGraphicsValidation
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    public static void RunBatch()
    {
        try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    [MenuItem("Tools/Terrain/Render Procedural Cave Validation")]
    public static void Run()
    {
        checks=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
        Check(GraphicsSettings.defaultRenderPipeline!=null,"Production PC URP asset is available.");
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;RenderSettings.fog=false;
        var authored=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Terrain/M_TerrainBase.mat");
        Check(authored!=null&&authored.shader.isSupported,"Production terrain material is supported.");
        var material=new Material(authored);
        // Real chunks receive CPU-produced control maps. This synthetic all-rock
        // landscape supplies explicit maps so its retained exterior is visible too.
        var rockControl=new Texture2D(1,1,TextureFormat.RGBA32,false,true);rockControl.SetPixel(0,0,new Color(0,0,0,1));rockControl.Apply();
        var emptyControl=new Texture2D(1,1,TextureFormat.RGBA32,false,true);emptyControl.SetPixel(0,0,Color.clear);emptyControl.Apply();
        material.SetTexture("_ControlMap0",rockControl);material.SetTexture("_ControlMap1",emptyControl);material.SetTexture("_ControlMap2",emptyControl);
        var settings=ScriptableObject.CreateInstance<TerrainSkyAccessSettings>();settings.blendSeconds=0;
        var camera=new GameObject("Procedural cave render camera").AddComponent<Camera>();camera.tag="MainCamera";camera.enabled=false;
        camera.nearClipPlane=.05f;camera.farClipPlane=220;camera.fieldOfView=72;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.006f,.008f,.012f);
        camera.GetUniversalAdditionalCameraData().renderShadows=true;
        var sun=new GameObject("Procedural cave daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.shadows=LightShadows.Hard;sun.shadowStrength=.85f;sun.intensity=0;sun.shadowBias=.02f;sun.shadowNormalBias=0;
        sun.transform.rotation=Quaternion.Euler(70,25,0);RenderSettings.sun=sun;
        try
        {
            ValidateFallback(settings);
            var surface=new TerrainCaveValidation.Surface((x,z)=>x<0?29.8:x<2?29.8+x*12.1:x<142?54:Math.Max(37.5,54-(x-142)*.45));
            var layout=TerrainCaveLayout.Representative(new double3(0,30,0));var caves=new[]{layout};
            var generator=new TerrainCaveDensityGenerator(surface,(a,b)=>layout.Intersects(a,b)?caves:Array.Empty<TerrainCaveLayout>());
            using var host=new TerrainCaveValidation.Host(surface,generator,new Vector3(85,29.2f,12),material,settings);
            host.Ready();
            // Keep the initial natural fallback for comparison. Cached sky traces remain
            // optional asynchronous work and must not turn an untraced chamber outdoors.
            host.Runtime.SkyAccess.YieldToTerrain();
            camera.transform.SetPositionAndRotation(new Vector3(73,31.2f,14),Quaternion.LookRotation(new Vector3(28,1,-2)));
            settings.enabled=false;host.Runtime.Update();using var outdoorAmbient=Capture(camera,"Room_OrdinaryAmbient.png");
            settings.enabled=true;host.Runtime.Update();using var ambient=Capture(camera,"Room_Ambient.png");
            double ordinary=Mean(outdoorAmbient),dark=Mean(ambient);
            Check(dark<ordinary*.7&&dark>.001,$"Natural cave ambient remains dim but readable: ordinary={ordinary:0.0000}, cave={dark:0.0000}.");
            settings.naturalCaveAmbientMultiplier=.20f;host.Runtime.Update();using var brighter=Capture(camera,"Room_BrightnessControl.png");
            Check(Mean(brighter)>dark*1.1,"Independent natural-cave ambient control affects the real receiver shader.");
            settings.naturalCaveAmbientMultiplier=.10f;host.Runtime.Update();
            sun.intensity=1.2f;using var day=Capture(camera,"Room_Daylight.png");
            Check(Mean(day)<ordinary*.8,"Intact cave ceilings block daylight in the production shader.");
            sun.intensity=0;
            var torch=new GameObject("Procedural cave local flame");var source=torch.AddComponent<LocalFlameSource>();source.localEmitter=Vector3.zero;source.transform.position=camera.transform.position+new Vector3(1,.2f,0);source.flicker=0;source.stationary=false;
            var lighting=LocalFlameLighting.Ensure();lighting.automaticRendering=false;lighting.targetCamera=camera;lighting.Register(source);
            for(int i=0;i<16;i++)lighting.RenderForCamera(camera,1/60f);
            Check(Physics.Raycast(new Vector3(76,33,14),Vector3.down,out var torchFloor,8,GameplayLayers.SolidSurfaceMask),"Torch fixture floor exists.");
            var probe=LightProbe(torchFloor.point+Vector3.up*.02f);
            Check(probe.r>.99&&probe.g>.99&&probe.b>.99&&probe.a>.01,"GPU light list and both shadow layers illuminate the actual floor.");
            using var lit=Capture(camera,"Room_Torch.png");double torchMean=Mean(lit);
            Check(lighting.ActiveLights==1&&Foreground(lit)>Foreground(ambient)*1.5,$"Carried-source direct lighting reveals the nearby walking floor: ambient={Foreground(ambient):0.0000}, torch={Foreground(lit):0.0000}.");
            Check(host.Runtime.CanIlluminate((float3)source.Position,12),"Published cave shells satisfy the production flame render-coverage guard.");
            source.transform.position+=Vector3.forward*3;for(int i=0;i<3;i++)lighting.RenderForCamera(camera,1/60f);
            using var moved=Capture(camera,"Room_TorchMoved.png");Check(Difference(lit,moved)>.001,"Moving the carried source updates illumination/shadows.");
            Object.DestroyImmediate(torch);DestroyFixtureLighting(lighting);
            sun.intensity=1.2f;
            camera.transform.SetPositionAndRotation(new Vector3(-8,32.5f,-14),Quaternion.LookRotation(new Vector3(14,1,14)));
            using var entrance=Capture(camera,"CliffEntrance.png");Check(Mean(entrance)>.005,"Cliff entrance renders visible exterior and cavern surfaces.");
            camera.transform.SetPositionAndRotation(new Vector3(185,42,-18),Quaternion.LookRotation(new Vector3(-17,-5,18)));
            using var ground=Capture(camera,"GroundEntrance.png");Check(Mean(ground)>.005,"Sloping ground entrance renders.");
            ValidateCurvedReceiver(material,camera,sun);
            ValidateNaturalNetworks(material,settings,camera,sun);
            foreach(var message in ShaderUtil.GetShaderMessages(material.shader))Check(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,"Production cave shader compilation: "+message.message);
            Debug.Log($"PROCEDURAL CAVES P4 GRAPHICS COMPLETE: {checks} checks; GPU conservative fallback, independent cave ambient, intact daylight occlusion, production local flame/moving shadows and both entrance renders. Room means: ordinary {ordinary:0.0000}, ambient {dark:0.0000}, torch {torchMean:0.0000}.");
        }
        finally{Object.DestroyImmediate(settings);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(sun.gameObject);Object.DestroyImmediate(material);Object.DestroyImmediate(rockControl);Object.DestroyImmediate(emptyControl);}
    }
    private static void ValidateCurvedReceiver(Material authored, Camera camera, Light sun)
    {
        // A convex height surface gives every sampled point a clear ray to an
        // above-surface emitter. Any loss against the reference is self-shadowing.
        var anchor = new Vector3(8192, 4321, -6144);
        const int cells = 40, row = cells + 1;
        var vertices = new Vector3[row * row];
        var normals = new Vector3[vertices.Length];
        var exposure = new Vector2[vertices.Length];
        var indices = new List<int>(cells * cells * 6);
        float Height(float x, float z) => .12f * x * x + .10f * z * z;
        for (int z = 0; z < row; z++)
            for (int x = 0; x < row; x++)
            {
                float px = -6 + x * .3f, pz = -6 + z * .3f;
                int index = z * row + x;
                vertices[index] = anchor + new Vector3(px, Height(px, pz), pz);
                normals[index] = new Vector3(-.24f * px, 1, -.20f * pz).normalized;
                exposure[index] = Vector2.one;
                if (x == cells || z == cells) continue;
                indices.Add(index);
                indices.Add(index + row);
                indices.Add(index + 1);
                indices.Add(index + 1);
                indices.Add(index + row);
                indices.Add(index + row + 1);
            }

        var mesh = new Mesh { vertices = vertices, normals = normals, uv2 = exposure };
        mesh.SetTriangles(indices, 0);
        var root = new GameObject("Distant curved terrain receiver");
        var material = new Material(authored);
        material.EnableKeyword("_TERRAIN_EXCAVATION");
        material.SetFloat("_ExcavationNormalStrength", 0);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        var collider = root.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        Physics.SyncTransforms();

        var torch = new GameObject("Distant receiver torch");
        torch.transform.position = anchor + new Vector3(.4f, Height(.4f, -4) + 1.7f, -4);
        var source = torch.AddComponent<LocalFlameSource>();
        source.localEmitter = Vector3.zero;
        source.stationary = false;
        source.flicker = 0;
        var lighting = LocalFlameLighting.Ensure();
        lighting.automaticRendering = false;
        lighting.targetCamera = camera;
        lighting.Register(source);
        typeof(LocalFlameSource).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(source, null);
        try
        {
            sun.intensity = 0;
            camera.transform.SetPositionAndRotation(anchor + new Vector3(0, Height(0, -4) + 1.6f, -4), Quaternion.LookRotation(new Vector3(0, -.3f, 1)));
            source.burning = false;
            lighting.RenderForCamera(camera, 1 / 60f);
            using var ambient = Capture(camera, "DistantReceiver_Unlit.png");

            source.burning = true;
            for (int frame = 0; frame < 16; frame++) lighting.RenderForCamera(camera, 1 / 60f);
            using var reference = Capture(camera, null);
            LocalFlameGeometry.Set(root, new[] { new LocalFlameGeometry.Part(mesh, Matrix4x4.identity, material, renderer: renderer) });
            lighting.RenderForCamera(camera, 1 / 60f);
            using var lit = Capture(camera, "DistantReceiver_Lit.png");
            Check(lighting.ActiveLights == 1, "Distant receiver source is active.");

            int samples = 0, shadowed = 0;
            for (int y = 40; y < 600; y += 4)
                for (int x = 160; x < 1120; x += 4)
                {
                    var ray = camera.ViewportPointToRay(new Vector3((x + .5f) / 1280, (y + .5f) / 800, 0));
                    if (!collider.Raycast(ray, out var hit, 15)) continue;
                    var local = hit.point - anchor;
                    if (Math.Abs(local.x) > 5 || Math.Abs(local.z) > 5 || (hit.point - source.Position).sqrMagnitude > 64) continue;
                    float baseLight = ambient.Texture.GetPixel(x, y).grayscale;
                    float expected = reference.Texture.GetPixel(x, y).grayscale - baseLight;
                    if (expected < .008f) continue;
                    samples++;
                    if (lit.Texture.GetPixel(x, y).grayscale - baseLight < expected * .8f) shadowed++;
                }
            Debug.Log($"CAVE RECEIVER PRECISION: {shadowed}/{samples} curved receiver samples lost more than 20% of clear torch light.");
            Check(samples > 100 && shadowed < samples * .005, "Distant curved terrain avoids false local-light shadows.");
        }
        finally{LocalFlameGeometry.Remove(root);Object.DestroyImmediate(torch);DestroyFixtureLighting(lighting);Object.DestroyImmediate(root);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);}
    }

    public static void RunReceiverPlaneBatch()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
            var camera=new GameObject("Receiver validation camera").AddComponent<Camera>();camera.enabled=false;camera.nearClipPlane=.05f;camera.farClipPlane=100;camera.fieldOfView=72;
            var sun=new GameObject("Receiver validation sun").AddComponent<Light>();sun.type=LightType.Directional;RenderSettings.sun=sun;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;RenderSettings.fog=false;
            ValidateCurvedReceiver(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Terrain/M_TerrainBase.mat"),camera,sun);EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }

    private static void ValidateNaturalNetworks(Material material,TerrainSkyAccessSettings settings,Camera camera,Light sun)
    {
        var caveSettings=TerrainCaveSettings.Default;caveSettings.enabled=true;caveSettings.probability=1;
        double[] Flat(IReadOnlyList<double2> points){var values=new double[points.Count];for(int i=0;i<values.Length;i++)values[i]=200;return values;}
        foreach(int seed in new[]{1,-98371,int.MaxValue})
        {
            var planner=new TerrainCavePlanner(seed,14.4,caveSettings,Flat,(x,z)=>200);
            var cave=planner.Query(new double3(383,100,383),new double3(385,200,385))[0];
            var surface=new TerrainCaveValidation.Surface((x,z)=>200);
            var generator=new TerrainCaveDensityGenerator(surface,(a,b)=>cave.Intersects(a,b)?new[]{cave}:Array.Empty<TerrainCaveLayout>());
            TerrainCaveSpan room=cave.Spans[0],passage=cave.Spans[0];foreach(var span in cave.Spans)if(span.Width>room.Width)room=span;
            double3 position=(room.Start+room.End)*.5;position.y=room.Floor(.5,0)+1.6;
            using var host=new TerrainCaveValidation.Host(surface,generator,(Vector3)(float3)(position-new double3(0,1.4,0)),material,settings);host.Ready(timeoutSeconds:150);
            host.Runtime.SkyAccess.YieldToTerrain();sun.intensity=0;
            var torch=new GameObject("Natural network preview torch");var source=torch.AddComponent<LocalFlameSource>();source.localEmitter=Vector3.zero;source.flicker=0;source.stationary=false;
            var lighting=LocalFlameLighting.Ensure();lighting.automaticRendering=false;lighting.targetCamera=camera;lighting.Register(source);
            try
            {
                void View(TerrainCaveSpan span,string suffix)
                {
                    double3 eye=(span.Start+span.End)*.5;eye.y=span.Floor(.5,0)+1.6;
                    Check(cave.Air(eye)>0,"Natural preview camera has standing clearance.");
                    camera.transform.SetPositionAndRotation((Vector3)(float3)eye,Quaternion.LookRotation((Vector3)(float3)(span.End-span.Start)));
                    source.transform.position=camera.transform.position+camera.transform.right*.4f+Vector3.up*.15f;
                    // ExecuteMethod does not run LateUpdate between these captures.
                    // Match the source's normal spatial-index migration explicitly.
                    typeof(LocalFlameSource).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(source,null);
                    for(int i=0;i<16;i++)lighting.RenderForCamera(camera,1/60f);
                    Check(lighting.ActiveLights==1,"Natural preview's carried source is spatially registered.");
                    using var capture=Capture(camera,$"Natural_{seed}_{suffix}.png");Check(Foreground(capture)>.001,"Natural network walking floor renders with the carried light.");
                }
                View(room,"Chamber");View(passage,"Passage");
                bool detailCaptured=false;
                foreach(var formation in cave.Formations)
                {
                    if(formation.Tip.y<formation.Base.y)continue;
                    for(int direction=0;direction<12&&!detailCaptured;direction++)
                    {
                        double a=direction*Math.PI/6;double3 eye=formation.Base+new double3(Math.Cos(a)*(formation.Radius+1.6),1.6,Math.Sin(a)*(formation.Radius+1.6));
                        if(cave.Air(eye)<.4)continue;
                        camera.transform.SetPositionAndRotation((Vector3)(float3)eye,Quaternion.LookRotation((Vector3)(float3)((formation.Base+formation.Tip)*.5-eye)));
                        source.transform.position=camera.transform.position+camera.transform.right*.35f;
                        typeof(LocalFlameSource).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(source,null);
                        for(int frame=0;frame<16;frame++)lighting.RenderForCamera(camera,1/60f);
                        using var detail=Capture(camera,$"Natural_{seed}_Formation.png");Check(lighting.ActiveLights==1&&Mean(detail)>.001,"Geological formation renders with the carried source.");detailCaptured=true;
                    }
                    if(detailCaptured)break;
                }
                Check(detailCaptured,"Natural seed has a viewable geological formation.");
                Debug.Log($"CAVE NATURAL GRAPHICS: seed {seed}, {cave.Spans.Count} spans, {cave.Formations.Count} geological formations; chamber and passage rendered.");
            }
            finally{Object.DestroyImmediate(torch);DestroyFixtureLighting(lighting);}
        }
        var generation=TerrainSculptingValidation.ReadSceneGeneration(Path.Combine(TerrainSculptingValidation.SourceRoot(),"Assets/Scenes/SmearScene.unity"));generation.Caves=TerrainCaveSettings.Default;generation.Caves.enabled=true;
        var baseline=new TerrainBaselineSampler(generation,32);var native=new TerrainCavePlanner(generation,baseline);
        Check(native.FindNearest(new double3(0,100,0),out var entrance),"Native entrance preview exists.");var layout=native.Query(entrance-1,entrance+1)[0];
        var nativeSurface=new TerrainCaveValidation.Surface(baseline.SampleHeight);
        var nativeGenerator=new TerrainCaveDensityGenerator(baseline,(a,b)=>layout.Intersects(a,b)?new[]{layout}:Array.Empty<TerrainCaveLayout>());
        using var nativeHost=new TerrainCaveValidation.Host(nativeSurface,nativeGenerator,(Vector3)(float3)entrance,material,settings);nativeHost.Ready(timeoutSeconds:150);nativeHost.Runtime.SkyAccess.YieldToTerrain();
        TerrainCaveSpan approach=layout.Spans[0];double nearest=double.PositiveInfinity;
        foreach(var span in layout.Spans)if(span.Entrance){double d=math.distancesq(span.End.xz,entrance.xz);if(d<nearest){nearest=d;approach=span;}}
        var outward=math.normalize(approach.End-approach.Start);outward.y=0;
        camera.transform.SetPositionAndRotation((Vector3)(float3)(entrance+outward*5+new double3(0,2,0)),Quaternion.LookRotation((Vector3)(float3)(-outward-new double3(0,.1,0))));
        sun.intensity=1.2f;using var mouth=Capture(camera,"Natural_NativeEntrance.png");Check(Mean(mouth)>.001,"Native curved hillside entrance renders.");
    }

    private static void DestroyFixtureLighting(LocalFlameLighting lighting)
    {
        // The fixture initializes this component in edit mode. Dispatch its cleanup
        // explicitly so the shadow buffers are released even without an Awake callback.
        typeof(LocalFlameLighting).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(lighting, null);
        Object.DestroyImmediate(lighting.gameObject);
    }

    private static void ValidateFallback(TerrainSkyAccessSettings settings)
    {
        var surface=new TerrainCaveValidation.Surface((x,z)=>54);var cave=TerrainCaveLayout.Representative(new double3(0,30,0));var caves=new[]{cave};
        using var world=new TerrainDensityWorld(TerrainDensityProfile.Default,new TerrainCaveDensityGenerator(surface,(a,b)=>caves));
        using var requests=new TerrainRequestManager(1,1,1,1,new TerrainWaterSettings(-100,200,.3f));
        using var sky=new TerrainSkyAccess(world,surface.SampleHeight,requests,settings);
        var fallback=new Dictionary<int3,float>{{new int3(0,0,0),54},{new int3(0,1,0),54}};
        sky.Update(new double3(12,31,12),0,true,true,fallback);
        Check(sky.ResidentPages==0&&Shader.GetGlobalFloat("_TerrainSkyEnabled")==1,"Natural fallback binds before any cached tile is traced.");
        Check(sky.SampleExposure(new double3(12,31,12))==0&&sky.SampleExposure(new double3(12,55,12))==1,"Fallback preserves exterior sky while darkening known underground space.");
        var shader=Shader.Find("Hidden/FirstSettlers/SkyAccessValidation");Check(shader!=null,"GPU sky diagnostic exists.");
        var probe=new Material(shader);var target=new RenderTexture(4,4,0,RenderTextureFormat.ARGBFloat);target.Create();var old=RenderTexture.active;var pixels=new Texture2D(4,4,TextureFormat.RGBAFloat,false,true);
        try
        {
            RenderTexture.active=target;probe.SetVector("_ProbePosition",new Vector4(12,31,12,0));probe.SetPass(0);Graphics.DrawProceduralNow(MeshTopology.Triangles,3);
            pixels.ReadPixels(new Rect(0,0,4,4),0,0);pixels.Apply();var value=pixels.GetPixel(2,2);
            Check(value.r>.99&&value.g<.01&&value.b<-.5,$"GPU consumes fallback with no atlas page: {value}.");
        }
        finally{RenderTexture.active=old;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);Object.DestroyImmediate(probe);}
    }
    private static Color LightProbe(Vector3 position)
    {
        var material=new Material(Shader.Find("Hidden/FirstSettlers/CaveLightValidation"));var target=new RenderTexture(4,4,0,RenderTextureFormat.ARGBFloat);target.Create();var old=RenderTexture.active;var pixels=new Texture2D(4,4,TextureFormat.RGBAFloat,false,true);
        try{RenderTexture.active=target;material.SetVector("_ProbePosition",position);material.SetPass(0);Graphics.DrawProceduralNow(MeshTopology.Triangles,3);pixels.ReadPixels(new Rect(0,0,4,4),0,0);pixels.Apply();return pixels.GetPixel(2,2);}
        finally{RenderTexture.active=old;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);Object.DestroyImmediate(material);}
    }
    private sealed class CaptureResult:IDisposable
    {
        internal readonly Texture2D Texture;
        internal CaptureResult(Texture2D texture){Texture=texture;}
        public void Dispose()=>Object.DestroyImmediate(Texture);
    }
    private static CaptureResult Capture(Camera camera,string name)
    {
        var target=new RenderTexture(1280,800,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);target.Create();var old=RenderTexture.active;
        try
        {
            camera.targetTexture=target;
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
            if (name != null)
            {
                var display = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                var colors = image.GetPixels();
                for (int i = 0; i < colors.Length; i++) colors[i] = colors[i].gamma;
                display.SetPixels(colors);
                display.Apply();
                string folder = Path.Combine(TerrainSculptingValidation.SourceRoot(), "Documentation/Images/ProceduralCaves");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name), display.EncodeToPNG());
                Object.DestroyImmediate(display);
            }
            return new CaptureResult(image);
        }
        finally{camera.targetTexture=null;RenderTexture.active=old;target.Release();Object.DestroyImmediate(target);}
    }
    private static double Mean(CaptureResult capture){var image=capture.Texture;double sum=0;int n=0;for(int y=image.height/4;y<image.height*3/4;y+=2)for(int x=image.width/4;x<image.width*3/4;x+=2){sum+=image.GetPixel(x,y).grayscale;n++;}return sum/n;}
    private static double Foreground(CaptureResult capture){var image=capture.Texture;double sum=0;int n=0;for(int y=40;y<240;y+=2)for(int x=320;x<960;x+=2){sum+=image.GetPixel(x,y).grayscale;n++;}return sum/n;}
    private static double Difference(CaptureResult left,CaptureResult right){var a=left.Texture;var b=right.Texture;double sum=0;int n=0;for(int y=100;y<700;y+=4)for(int x=100;x<1180;x+=4){sum+=Math.Abs(a.GetPixel(x,y).grayscale-b.GetPixel(x,y).grayscale);n++;}return sum/n;}
}
