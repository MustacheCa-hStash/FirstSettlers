using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

/// <summary>Real URP images, roof shadow occlusion, and resident GPU vegetation.</summary>
public static class TerrainSculptingWorldGraphicsValidation
{
    private static int checks;
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Hidden).GetValue(target);
    private static void Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Hidden).Invoke(target,args);
    public static void RunBatch()
    {
        try{Run();EditorApplication.Exit(0);}
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    public static void RunLightingBatch()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;
            var camera=new GameObject("Sky validation camera").AddComponent<Camera>();camera.nearClipPlane=.05f;camera.farClipPlane=80;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var light=new GameObject("Sky validation sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=0;
            ValidateCaveLighting(camera,light,Shader.Find("Custom/StylizedTerrainURP"),Resources.Load<TerrainSculptSurfaceSettings>("TerrainSculpting/ExcavatedStone"));
            EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    [MenuItem("Tools/Terrain/Render Sculpting P6 Validation")]
    public static void Run()
    {
        checks=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/S_Terrain/CustomStylizedTerrain.shader");
        Check(shader!=null&&shader.isSupported,"Terrain shader unavailable.");
        CompileTerrainVariants(shader);
        TerrainMaterialInspectorValidation.Run();
        var appearance=Resources.Load<TerrainSculptSurfaceSettings>("TerrainSculpting/ExcavatedStone");
        Check(appearance!=null&&appearance.albedo!=null,"Replaceable stone settings/texture missing.");
        var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        Check(pipeline!=null,"Production URP pipeline missing.");
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;
        var oldMode=RenderSettings.ambientMode;var oldAmbient=RenderSettings.ambientLight;var oldSun=RenderSettings.sun;
        var cameraObject=new GameObject("P6 render camera");var lightObject=new GameObject("P6 directional sun");
        var control=new Texture2D(1,1,TextureFormat.RGBA32,false,true);control.SetPixel(0,0,new Color(0,0,1,0));control.Apply();
        var empty=new Texture2D(1,1,TextureFormat.RGBA32,false,true);empty.SetPixel(0,0,Color.clear);empty.Apply();
        var disabledSky=ScriptableObject.CreateInstance<TerrainSkyAccessSettings>();disabledSky.enabled=false;
        try
        {
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;
            var camera=cameraObject.AddComponent<Camera>();cameraObject.tag="MainCamera";
            camera.nearClipPlane=.05f;camera.farClipPlane=80;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.03f,.04f,.06f);
            camera.GetUniversalAdditionalCameraData().renderShadows=true;
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.shadows=LightShadows.Hard;
            light.shadowBias=.02f;light.shadowNormalBias=0;light.shadowCustomResolution=2048;light.transform.rotation=Quaternion.Euler(90,0,0);RenderSettings.sun=light;
            using var f=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>10,terrainShader:shader,appearance:appearance,receiveShadows:true,skyAccess:disabledSky);
            f.Material.SetTexture("_ControlMap0",control);f.Material.SetTexture("_ControlMap1",empty);f.Material.SetTexture("_ControlMap2",empty);
            f.Material.DisableKeyword("_GRASS_BLADE_GROUND");f.Material.DisableKeyword("_ROCK_DETAIL");f.Material.SetFloat("_GrassNormalStrength",0);
            foreach(string property in new[]{"_DarkGrassColor","_MidGrassColor","_LightGrassColor"})f.Material.SetColor(property,new Color(.15f,.55f,.12f));
            f.Viewer.transform.position=new Vector3(12,7,12);
            using(var edit=f.World.StageExcavation(TerrainDensityBrush.Box(new double3(12,7,12),new double3(3,2,3))))Check(f.World.TryCommit(edit),"Render cavity seed failed.");
            f.Ready();
            var renderers=new List<MeshRenderer>();
            foreach(var surface in Field<Dictionary<ChunkCoord,TerrainSculptChunkSurface>>(f.Runtime,"live").Values)
            {
                var renderer=Field<GameObject>(surface,"root").GetComponent<MeshRenderer>();if(renderer==null)continue;
                renderers.Add(renderer);Check(renderer.sharedMaterial.IsKeywordEnabled("_TERRAIN_EXCAVATION"),"Excavation variant not bound.");
                Check(renderer.sharedMaterial.FindPass("ShadowCaster")>=0,"Ceiling shadow pass missing.");
                var properties=new MaterialPropertyBlock();renderer.GetPropertyBlock(properties);
                Check(properties.GetTexture("_ExcavationAlbedo")==appearance.albedo,"Independent replacement texture not bound.");
            }
            camera.orthographic=true;camera.orthographicSize=1.3f;camera.transform.SetPositionAndRotation(new Vector3(12,8,12),Quaternion.Euler(90,0,0));
            foreach(var renderer in renderers)renderer.shadowCastingMode=ShadowCastingMode.Off;
            using var lit=Capture(camera,"TerrainSculptingP6RoofCasterOff.png");
            foreach(var renderer in renderers)renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
            using var shadowed=Capture(camera,"TerrainSculptingP6RoofCasterOn.png");
            double a=Mean(lit.Texture),b=Mean(shadowed.Texture);
            Check(a>b*1.3,$"Roof did not occlude direct sunlight: without={a:F4}, with={b:F4}.");
            var chunk=f.Find(new ChunkCoord(0,0));chunk.SetRenderVisible(false);
            foreach(var renderer in renderers)Check(renderer.enabled&&renderer.shadowCastingMode==ShadowCastingMode.ShadowsOnly,"Offscreen roof stopped casting its shadow.");
            chunk.SetTerrainHandoffHidden(true);
            foreach(var renderer in renderers)Check(!renderer.enabled,"Outgoing handoff left a duplicate shadow caster.");
            chunk.SetTerrainHandoffHidden(false);chunk.SetRenderVisible(true);
            camera.transform.SetPositionAndRotation(new Vector3(29,13,29),Quaternion.Euler(90,0,0));
            var retained=Field<Dictionary<ChunkCoord,TerrainSculptChunkSurface>>(f.Runtime,"live")[new ChunkCoord(0,0)];
            chunk.SetSculptSurface(null);
            using var original=Capture(camera,"TerrainSculptingP6ExteriorOriginal.png");
            chunk.SetSculptSurface(retained);
            using var preserved=Capture(camera,"TerrainSculptingP6ExteriorPreserved.png");
            var before=original.Texture.GetPixels32();var after=preserved.Texture.GetPixels32();double difference=0;
            for(int i=0;i<before.Length;i++)difference+=Math.Abs(before[i].r-after[i].r)+Math.Abs(before[i].g-after[i].g)+Math.Abs(before[i].b-after[i].b);
            Check(difference/(before.Length*3)<1,"Replacement changed untouched exterior appearance.");
            camera.orthographic=false;camera.fieldOfView=78;camera.transform.position=new Vector3(10.8f,6.8f,10);
            camera.transform.LookAt(new Vector3(13.8f,6.6f,14.5f));
            using var cave=Capture(camera,"TerrainSculptingP6Cave.png");
            Check(Mean(cave.Texture)>.015,"Cave rendered black/missing.");
            Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(12,9.7,9),2)),"Entrance excavation failed.");f.Ready();
            camera.transform.position=new Vector3(12,12,5.5f);camera.transform.LookAt(new Vector3(12,7.5f,12));camera.fieldOfView=68;
            using var entrance=Capture(camera,"TerrainSculptingP6Entrance.png");
            // A patterned vertical wall exercises actual Y projection independently of the source texture.
            var quad=new GameObject("P6 vertical mapping reference");var quadMesh=new Mesh();var wallMaterial=new Material(f.Material);
            var stripes=new Texture2D(8,8,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Point};
            try
            {
                for(int y=0;y<8;y++)for(int x=0;x<8;x++)stripes.SetPixel(x,y,y<4?Color.white:new Color(.1f,.1f,.1f));stripes.Apply();
                quadMesh.vertices=new[]{new Vector3(-1,0,0),new Vector3(-1,2,0),new Vector3(1,2,0),new Vector3(1,0,0)};
                quadMesh.normals=new[]{Vector3.back,Vector3.back,Vector3.back,Vector3.back};quadMesh.triangles=new[]{0,1,2,0,2,3};
                quadMesh.uv=new[]{Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero};quadMesh.uv2=new[]{Vector2.right,Vector2.right,Vector2.right,Vector2.right};
                quad.AddComponent<MeshFilter>().sharedMesh=quadMesh;var renderer=quad.AddComponent<MeshRenderer>();renderer.sharedMaterial=wallMaterial;
                wallMaterial.EnableKeyword("_TERRAIN_EXCAVATION");wallMaterial.SetTexture("_ExcavationAlbedo",stripes);wallMaterial.SetFloat("_ExcavationTiling",1);wallMaterial.SetFloat("_ExcavationNormalStrength",0);
                camera.orthographic=true;camera.orthographicSize=1;camera.transform.SetPositionAndRotation(new Vector3(0,1,-3),Quaternion.identity);
                using var mapped=Capture(camera,"TerrainSculptingP6VerticalMapping.png");
                float low=1,high=0;
                for(int y=20;y<mapped.Texture.height-20;y++)
                {float value=mapped.Texture.GetPixel(mapped.Texture.width/2,y).grayscale;low=Math.Min(low,value);high=Math.Max(high,value);}
                Check(high-low>.15,"Vertical wall has no texture variation with height.");
            }
            finally{Object.DestroyImmediate(quad);Object.DestroyImmediate(quadMesh);Object.DestroyImmediate(wallMaterial);Object.DestroyImmediate(stripes);}
            ValidateGpuVegetation(f);
            ValidateCaveLighting(camera,light,shader,appearance);
            foreach(var message in ShaderUtil.GetShaderMessages(shader))Check(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,"Terrain shader compilation failed: "+message.message);
            Debug.Log($"TERRAIN SCULPTING P6 GRAPHICS PASS: {checks} checks; real URP cave/stone, independent texture, vertical projection, two-sided roof shadow occlusion ({a:F4}->{b:F4}), resident GPU grass/fern, cached cave ambient, live brightness, exterior preservation and building shading.");
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;
            RenderSettings.ambientMode=oldMode;RenderSettings.ambientLight=oldAmbient;RenderSettings.sun=oldSun;
            Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);Object.DestroyImmediate(control);Object.DestroyImmediate(empty);
            Object.DestroyImmediate(disabledSky);
        }
    }
    private static void ValidateCaveLighting(Camera camera,Light light,Shader shader,TerrainSculptSurfaceSettings appearance)
    {
        var settings=ScriptableObject.CreateInstance<TerrainSkyAccessSettings>();settings.blendSeconds=0;settings.enabled=false;
        var wood=new Material(Shader.Find("Custom/BuildingWoodMatte"));
        GameObject panel=null;
        try
        {
            using var f=new TerrainSculptingIntegrationValidation.Fixture((x,z)=>10,terrainShader:shader,appearance:appearance,receiveShadows:true,skyAccess:settings);
            f.Viewer.transform.position=new Vector3(1008,7,1008);f.Ready();
            Check(f.Runtime.TryExcavate(TerrainDensityBrush.Box(new double3(1008,7,1008),new double3(4,2,4))),"Lighting chamber rejected.");f.Ready();
            light.intensity=0;camera.orthographic=true;camera.orthographicSize=1.3f;
            camera.transform.SetPositionAndRotation(new Vector3(1008,8,1008),Quaternion.Euler(90,0,0));
            using var ordinary=Capture(camera,"CaveSkyAccessDisabled.png");double outsideAmbient=Mean(ordinary.Texture);
            settings.enabled=true;TerrainSkyAccessValidation.Ready(f);
            var probeMaterial=new Material(Shader.Find("Hidden/FirstSettlers/SkyAccessValidation"));
            var probeTarget=new RenderTexture(4,4,0,RenderTextureFormat.ARGBFloat);probeTarget.Create();var oldTarget=RenderTexture.active;
            try
            {
                RenderTexture.active=probeTarget;probeMaterial.SetVector("_ProbePosition",new Vector4(1008,5.15f,1008,0));probeMaterial.SetPass(0);Graphics.DrawProceduralNow(MeshTopology.Triangles,3);
                var readback=new Texture2D(4,4,TextureFormat.RGBAFloat,false,true);readback.ReadPixels(new Rect(0,0,4,4),0,0);readback.Apply();
                Color pixel=readback.GetPixel(2,2);
                Check(pixel.r>.99&&pixel.g<.01&&pixel.b>=0&&pixel.b<128,"GPU page lookup disagrees with sealed-cave CPU visibility: "+pixel);Object.DestroyImmediate(readback);
            }
            finally{RenderTexture.active=oldTarget;probeTarget.Release();Object.DestroyImmediate(probeTarget);Object.DestroyImmediate(probeMaterial);}
            using var dark=Capture(camera,"CaveSkyAccessEnabled.png");double darkAmbient=Mean(dark.Texture);
            Check(outsideAmbient>.01&&darkAmbient<outsideAmbient*.4,$"Cached field did not darken terrain: {outsideAmbient:F4}->{darkAmbient:F4}.");
            double trace=f.Runtime.SkyAccess.LastTraceMs;
            settings.caveAmbientMultiplier=.25f;f.Tick();using var middle=Capture(camera,"CaveSkyAccessQuarterAmbient.png");
            Check(Mean(middle.Texture)>darkAmbient*1.5&&Mean(middle.Texture)<outsideAmbient*.85,"Cave brightness slider did not produce an intermediate image.");
            settings.caveAmbientMultiplier=1;f.Tick();using var full=Capture(camera,"CaveSkyAccessFullAmbient.png");
            Check(Math.Abs(Mean(full.Texture)-outsideAmbient)<.005,"Multiplier 1 failed to restore ordinary terrain ambient.");
            Check(f.Runtime.SkyAccess.LastTraceMs==trace&&!f.Runtime.SkyAccess.Busy,"Brightness slider retraced probes.");
            panel=GameObject.CreatePrimitive(PrimitiveType.Cube);panel.transform.position=new Vector3(1008,6,1008);panel.transform.localScale=new Vector3(2.5f,.25f,2.5f);
            panel.GetComponent<MeshRenderer>().sharedMaterial=wood;wood.SetFloat("_AmbientFloor",.4f);wood.SetColor("_BaseColor",Color.white);
            using var woodBright=Capture(camera,"CaveSkyAccessBuildingBright.png");
            settings.caveAmbientMultiplier=.025f;f.Tick();using var woodDark=Capture(camera,"CaveSkyAccessBuildingDark.png");
            Check(Mean(woodBright.Texture)>.02&&Mean(woodDark.Texture)<Mean(woodBright.Texture)*.4,"Building/stone shared material ignored cave ambient.");
            Object.DestroyImmediate(panel);panel=null;
            camera.transform.position=new Vector3(1008,12,1008);
            using var exteriorDark=Capture(camera,"CaveSkyAccessExteriorLow.png");settings.caveAmbientMultiplier=1;f.Tick();
            using var exteriorBright=Capture(camera,"CaveSkyAccessExteriorHigh.png");
            Check(Math.Abs(Mean(exteriorDark.Texture)-Mean(exteriorBright.Texture))<.002,"Cave brightness altered the retained exterior.");
            settings.caveAmbientMultiplier=.025f;f.Tick();
            Check(f.Runtime.TryExcavate(TerrainDensityBrush.Sphere(new double3(1008,9.5,1008),2)),"Lighting skylight rejected.");TerrainSkyAccessValidation.Ready(f);
            camera.transform.position=new Vector3(1008,8,1008);
            using var shaft=Capture(camera,"CaveSkyAccessOpenedShaft.png");
            Check(Mean(shaft.Texture)>darkAmbient*1.3,"Excavated skylight did not brighten the rendered cave floor.");
            foreach(var message in ShaderUtil.GetShaderMessages(wood.shader))Check(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,"Building shader compilation failed: "+message.message);
            Debug.Log($"CACHED SKY GRAPHICS PASS: terrain ambient {outsideAmbient:F4}->{darkAmbient:F4}; opened shaft {Mean(shaft.Texture):F4}; wood, live sliders, exterior and atlas coordinates passed.");
        }
        finally{if(panel!=null)Object.DestroyImmediate(panel);Object.DestroyImmediate(wood);Object.DestroyImmediate(settings);}
    }
    private sealed class CaptureResult:IDisposable
    {
        internal readonly Texture2D Texture;
        internal CaptureResult(Texture2D texture){Texture=texture;}
        public void Dispose()=>Object.DestroyImmediate(Texture);
    }
    private static void CompileTerrainVariants(Shader shader)
    {
        var variants=new ShaderVariantCollection();bool asynchronous=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
        try
        {
            foreach(bool rock in new[]{false,true})foreach(bool grass in new[]{false,true})foreach(bool excavation in new[]{false,true})
            foreach(bool cluster in new[]{false,true})foreach(string shadow in new[]{"","_MAIN_LIGHT_SHADOWS_CASCADE","_MAIN_LIGHT_SHADOWS_SCREEN"})foreach(string fog in new[]{"","FOG_LINEAR"})
            {
                var keywords=new List<string>();if(rock)keywords.Add("_ROCK_DETAIL");if(grass)keywords.Add("_GRASS_BLADE_GROUND");
                if(excavation)keywords.Add("_TERRAIN_EXCAVATION");if(cluster)keywords.Add("_CLUSTER_LIGHT_LOOP");
                if(shadow.Length>0){keywords.Add(shadow);keywords.Add("_SHADOWS_SOFT_LOW");}if(fog.Length>0)keywords.Add(fog);
                variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.ScriptableRenderPipeline,keywords.ToArray()));
            }
            variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.ShadowCaster));
            variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.ShadowCaster,"_CASTING_PUNCTUAL_LIGHT_SHADOW"));
            variants.WarmUp();
            Check(!ShaderUtil.ShaderHasError(shader),"Terrain variant compilation failed.");
            Debug.Log($"SCULPT P6 SHADER VARIANTS PASS: {variants.variantCount} forward/shadow variants, landscape/excavation, rock/grass modes, Forward/Forward+, shadows and fog.");
        }
        finally{ShaderUtil.allowAsyncCompilation=asynchronous;Object.DestroyImmediate(variants);}
    }
    private static CaptureResult Capture(Camera camera,string filename)
    {
        var target=new RenderTexture(960,640,24,RenderTextureFormat.ARGB32);target.Create();var old=RenderTexture.active;
        try
        {
            camera.targetTexture=target;
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;
            var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();
            var folder=Path.Combine(TerrainSculptingValidation.SourceRoot(),"Documentation","Images");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,filename),texture.EncodeToPNG());
            return new CaptureResult(texture);
        }
        finally{camera.targetTexture=null;RenderTexture.active=old;target.Release();Object.DestroyImmediate(target);}
    }
    private static double Mean(Texture2D image)
    {
        double sum=0;int n=0;
        for(int y=image.height/3;y<image.height*2/3;y++)for(int x=image.width/3;x<image.width*2/3;x++){sum+=image.GetPixel(x,y).grayscale;n++;}
        return sum/n;
    }
    private static void ValidateGpuVegetation(TerrainSculptingIntegrationValidation.Fixture f)
    {
        var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/GrassCompact.compute");
        var material=new Material(Shader.Find("Custom/GrassInstancedTerrainTint")){enableInstancing=true};
        var mesh=new Mesh{vertices=new[]{Vector3.zero,Vector3.up,Vector3.right},triangles=new[]{0,1,2}};
        try
        {
            Check(GrassRenderUtility.IsSupported(shader,material),"GPU grass fixture unsupported.");
            var candidates=new List<FoliageInstanceData>{new FoliageInstanceData(new Vector3(12,10,9),Quaternion.identity,Vector3.one,1,0),
                new FoliageInstanceData(new Vector3(12,10,12),Quaternion.identity,Vector3.one,2,1),new FoliageInstanceData(new Vector3(20,10,20),Quaternion.identity,Vector3.one,3,1)};
            using var renderer=new ResidentGrassRenderer(1,8,1);
            renderer.Upload(0,candidates,Matrix4x4.identity,mesh,mesh);
            var settings=new GrassSettings{grassCompactShader=shader};var meshes=new[]{mesh,mesh,mesh,mesh};var materials=new[]{material,material,material,material};
            renderer.CullAllForValidation(settings,meshes,materials,null,new Vector3(12,10,12),3,100,5,Vector4.one,1);
            Check(renderer.ReadVisibleForValidation(0).Length+renderer.ReadVisibleForValidation(2).Length==3,"Initial resident GPU grass missing.");
            renderer.ApplySuppression(f.Runtime.Suppression);
            renderer.CullAllForValidation(settings,meshes,materials,null,new Vector3(12,10,12),3,100,5,Vector4.one,1);
            Check(renderer.ReadVisibleForValidation(0).Length+renderer.ReadVisibleForValidation(2).Length==2,"GPU grass removed an intact-roof plant or kept an opening plant.");
            var slot=renderer.ReadSlotForValidation(0);Check(slot.Length==2&&slot[0].ObjectToWorld.m23==12&&slot[1].ObjectToWorld.m03==20,"GPU grass did not preserve the intact roof/unrelated source.");
            var generation=new LeafClusterGeneration(f.Find(new ChunkCoord(0,0)).ChunkRecord,new FernSettings(),1,128,.3f,60);
            generation.Instances.Add(new LeafClusterInstance{position=new Vector3(12,10,9),rotation=Quaternion.identity,scale=1});
            generation.Instances.Add(new LeafClusterInstance{position=new Vector3(12,10,12),rotation=Quaternion.identity,scale=1});
            generation.Instances.Add(new LeafClusterInstance{position=new Vector3(20,10,20),rotation=Quaternion.identity,scale=1});
            var compute=Resources.Load<ComputeShader>("Foliage/ForestScatterCompact");Check(compute!=null,"Fern compute missing.");
            foreach(var message in ShaderUtil.GetComputeShaderMessages(compute))Check(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,"Forest compute compilation failed: "+message.message);
            using var scatter=new ForestScatterGpuRenderer(compute);
            var fernShader=Shader.Find("FirstSettlers/Forest Fern Instanced");Check(fernShader!=null,"Fern material shader missing.");
            var fernMaterial=new Material(fernShader){enableInstancing=true};
            try
            {
            scatter.Sync(new ChunkCoord(0,0),generation,true,1,true);
            scatter.Draw(mesh,fernMaterial,Matrix4x4.identity,null,null,Matrix4x4.identity,null,new Plane[6],new Vector3(12,10,12),100,5,18,30);
            Check(scatter.ReadVisible(0).Length==3,"Initial GPU fern visibility missing.");
            Call(generation,"ApplySuppression",f.Runtime.Suppression);scatter.Sync(new ChunkCoord(0,0),generation,true,1,true);
            scatter.Draw(mesh,fernMaterial,Matrix4x4.identity,null,null,Matrix4x4.identity,null,new Plane[6],new Vector3(12,10,12),100,5,18,30);
            var visible=scatter.ReadVisible(0);
            bool roof=false,unrelated=false;
            foreach(uint index in visible){var p=scatter.ReadSource(index).Sphere;roof|=p.x==12&&p.z==12;unrelated|=p.x==20;}
            Check(scatter.ResidentInstances==2&&visible.Length==2&&roof&&unrelated,"GPU fern failed to remove only the entrance plant while preserving its intact roof.");
            }
            finally{Object.DestroyImmediate(fernMaterial);}
            ResidentGrassCullingValidation.Run();
        }
        finally{Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);}
    }
}
