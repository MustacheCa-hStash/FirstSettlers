using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Actual production-shader pixels: coverage, material isolation and normal selection.</summary>
public static class SnowRockSeparationValidation
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException(message); }

    public static void RunBatch()
    {
        bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        try
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-snowOutput");
            string output = index >= 0 ? args[index+1] : "Logs/SnowRockSeparation";
            Directory.CreateDirectory(output);
            TerrainMaterialInspectorValidation.Run();
            ValidatePixels(output);
            SurfaceBlendValidation.Run();
            MountainSnowValidation.Run();
            MountainSnowDistributionValidation.RenderSeparationComparisons(output);
            Debug.Log($"SNOW ROCK SEPARATION PASS: {checks} production GPU pixel/normal/variant checks; inspector, unchanged generation/seams, and identical-map mountain comparisons passed.");
            EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally { ShaderUtil.allowAsyncCompilation = async; }
    }

    private static void ValidatePixels(string output)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var oldPipeline=GraphicsSettings.defaultRenderPipeline; var oldQuality=QualitySettings.renderPipeline;
        GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
        RenderSettings.fog=false; RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=Color.black;
        var camera=new GameObject("Snow coverage pixel camera").AddComponent<Camera>();camera.enabled=false;
        camera.orthographic=true;camera.orthographicSize=.125f;camera.aspect=8;camera.nearClipPlane=.1f;camera.farClipPlane=20;
        camera.transform.SetPositionAndRotation(new Vector3(0,3,0),Quaternion.Euler(90,0,0));
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var sun=new GameObject("Snow normal test sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=0;
        sun.transform.rotation=Quaternion.Euler(45,90,0);RenderSettings.sun=sun;
        var mesh=new Mesh { vertices=new[]{new Vector3(-1,0,-1),new Vector3(-1,0,1),new Vector3(1,0,-1),new Vector3(1,0,1)},
            normals=new[]{Vector3.up,Vector3.up,Vector3.up,Vector3.up},
            uv=new[]{Vector2.zero,Vector2.up,Vector2.right,Vector2.one},
            colors=new[]{Color.white,Color.white,Color.white,Color.white},triangles=new[]{0,1,2,2,1,3} };
        var ground=new GameObject("Snow coverage reference plane");ground.AddComponent<MeshFilter>().sharedMesh=mesh;
        var material=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Terrain/M_TerrainBase.mat"));
        ground.AddComponent<MeshRenderer>().sharedMaterial=material;
        var a=new Texture2D(257,2,TextureFormat.RGBA32,false,true){wrapMode=TextureWrapMode.Clamp};
        var b=new Texture2D(257,2,TextureFormat.RGBA32,false,true){wrapMode=TextureWrapMode.Clamp};
        var empty=new Texture2D(1,1,TextureFormat.RGBA32,false,true);empty.SetPixel(0,0,Color.clear);empty.Apply();
        var normal=new Texture2D(1,1,TextureFormat.RGBA32,false,true);normal.SetPixel(0,0,new Color(.85f,.7f,1,.85f));normal.Apply();
        try
        {
            material.DisableKeyword("_ROCK_DETAIL");material.DisableKeyword("_GRASS_BLADE_GROUND");
            material.SetFloat("_RockDetail",0);material.SetFloat("_GrassBladeGround",0);
            material.SetFloat("_ReceiveShadows",0);material.SetFloat("_AmbientStrength",1);
            material.SetFloat("_GrassDetailStrength",0);material.SetFloat("_GrassNormalStrength",0);
            material.SetFloat("_SandDetailStrength",0);material.SetFloat("_SandNormalStrength",0);
            material.SetFloat("_SnowNormalStrength",0);material.SetFloat("_SurfaceBlendSharpness",1);
            material.SetFloat("_SnowCoverageThreshold",.5f);material.SetFloat("_SnowBoundaryWidth",.2f);
            material.SetTexture("_SnowAlbedo",Texture2D.whiteTexture);material.SetColor("_SnowTint",Color.white);
            material.SetColor("_SnowColor",Color.green);material.SetColor("_RockColor",Color.red);material.SetColor("_CliffColor",Color.blue);
            foreach(string property in new[]{"_DarkGrassColor","_MidGrassColor","_LightGrassColor"})material.SetColor(property,Color.blue);
            material.SetTexture("_ControlMap0",a);material.SetTexture("_ControlMap1",b);material.SetTexture("_ControlMap2",empty);
            material.SetVector("_TerrainHorizonParams",Vector4.zero);

            void Fill(float snow,float rock,float cliff=0,float grass=0)
            {
                var pixels0=new Color[a.width*a.height];var pixels1=new Color[pixels0.Length];
                for(int i=0;i<pixels0.Length;i++) {pixels0[i]=new Color(0,0,grass,rock);pixels1[i]=new Color(snow,cliff,0,0);}
                a.SetPixels(pixels0);a.Apply();b.SetPixels(pixels1);b.Apply();
            }
            Color[] Render(float strength)
            { material.SetFloat("_SnowRockSeparation",strength);return Capture(camera); }
            Color Center(Color[] pixels)=>pixels[32*512+256];
            float Difference(Color[] x,Color[] y)
            {
                float max=0;
                for(int z=8;z<56;z++)for(int col=8;col<504;col++)
                {var c=x[z*512+col]-y[z*512+col];max=Mathf.Max(max,Mathf.Abs(c.r),Mathf.Abs(c.g),Mathf.Abs(c.b));}
                return max;
            }
            for(int z=0;z<2;z++)for(int x=0;x<257;x++)
            {float snow=x/256f;a.SetPixel(x,z,new Color(0,0,0,1-snow));b.SetPixel(x,z,new Color(snow,0,0,0));}
            a.Apply();b.Apply();var before=Render(0);var after=Render(1);
            int oldMixed=0,newMixed=0;
            for(int x=4;x<508;x++)
            {
                var original=before[32*512+x];var selected=after[32*512+x];
                if(original.g>.05f&&original.g<.95f)oldMixed++;
                if(selected.g>.05f&&selected.g<.95f)newMixed++;
                Check(Mathf.Abs(selected.r+selected.g-1)<.015f,"Snow/rock transition changed total material weight.");
            }
            Check(oldMixed>350&&newMixed>8&&newMixed<oldMixed*.35f,"Coverage did not resolve to mostly pure materials with a small soft edge.");
            Check(after[32*512+100].r>.98f&&after[32*512+400].g>.98f,"Snow/rock interiors remain faded.");
            Save(before,Path.Combine(output,"coverage-blended.png"));Save(after,Path.Combine(output,"coverage-selected.png"));
            Debug.Log($"Snow gradient: mixed pixels {oldMixed} -> {newMixed} out of 504 interior pixels (production shader, .2 width).");
            material.SetFloat("_SnowCoverageThreshold",.65f);var shifted=Render(1);
            Check(shifted[32*512+282].r>.98f&&shifted[32*512+420].g>.98f,"Coverage threshold did not move the boundary.");
            material.SetFloat("_SnowCoverageThreshold",.5f);
            Fill(.25f,0,0,.75f);Check(Difference(Render(0),Render(1))<.004f,"Snow-on-grass dusting changed.");
            Fill(0,.3f,.2f,.5f);Check(Difference(Render(0),Render(1))<.004f,"Snow-free material blending changed.");
            Fill(.25f,.45f,.3f);var ratio=Center(Render(1));
            Check(Mathf.Abs(ratio.r/ratio.b-1.5f)<.04f&&ratio.g<.01f,"Exposed rock/cliff proportions changed.");
            Fill(.3f,.5f,0,.2f);var isolation=Center(Render(1));
            Check(Mathf.Abs(isolation.b-.2f)<.015f&&isolation.g<.01f,"Snow sharpening altered the grass share.");
            Fill(0,1);Check(Difference(Render(0),Render(1))<.004f,"Pure rock changed.");
            Fill(1,0);Check(Difference(Render(0),Render(1))<.004f,"Pure snow changed.");
            foreach(float threshold in new[]{.05f,.5f,.95f})foreach(float width in new[]{.01f,.2f,1f})
            {
                material.SetFloat("_SnowCoverageThreshold",threshold);material.SetFloat("_SnowBoundaryWidth",width);
                Fill(0,1);Check(Center(Render(1)).r>.98f,"An extreme threshold/width contaminated pure rock.");
                Fill(1,0);Check(Center(Render(1)).g>.98f,"An extreme threshold/width contaminated pure snow.");
            }
            material.SetFloat("_SnowCoverageThreshold",.5f);material.SetFloat("_SnowBoundaryWidth",.2f);

            // Equal white albedo isolates shading from normals. Resolved endpoint pixels
            // must match the corresponding single-material render, not the old mixed normal.
            material.SetColor("_SnowColor",Color.white);material.SetColor("_RockColor",Color.white);
            material.SetTexture("_SnowNormal",normal);material.SetFloat("_SnowNormalStrength",1.5f);
            material.SetFloat("_AmbientStrength",.02f);sun.intensity=.8f;
            Fill(0,1);var rock=Render(1);Fill(1,0);var snowOnly=Render(1);
            Check(Difference(rock,snowOnly)>.03f,"Normal fixture did not produce distinct snow/rock shading.");
            Fill(.2f,.8f);Check(Difference(rock,Render(1))<.006f,"Bare rock retained a blended snow normal.");
            Fill(.8f,.2f);Check(Difference(snowOnly,Render(1))<.006f,"Solid snow retained a blended rock normal.");
            CompileVariants(material.shader);
        }
        finally
        {
            Object.DestroyImmediate(ground);Object.DestroyImmediate(mesh);Object.DestroyImmediate(material);
            Object.DestroyImmediate(a);Object.DestroyImmediate(b);Object.DestroyImmediate(empty);Object.DestroyImmediate(normal);
            Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(sun.gameObject);
            GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;
        }
    }

    private static void CompileVariants(Shader shader)
    {
        var variants=new ShaderVariantCollection();
        try
        {
            foreach(bool grass in new[]{false,true})foreach(bool rock in new[]{false,true})
                foreach(string shadow in new[]{"","_MAIN_LIGHT_SHADOWS","_MAIN_LIGHT_SHADOWS_CASCADE"})
                {
                    var keywords=new List<string>();if(grass)keywords.Add("_GRASS_BLADE_GROUND");if(rock)keywords.Add("_ROCK_DETAIL");
                    if(shadow.Length>0)keywords.Add(shadow);
                    variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.ScriptableRenderPipeline,keywords.ToArray()));
                }
            variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.ShadowCaster));variants.WarmUp();
            foreach(var message in ShaderUtil.GetShaderMessages(shader))
                Check(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
            Check(!ShaderUtil.ShaderHasError(shader),"Terrain shader compilation failed.");
            Debug.Log("Snow separation shader variants warmed: "+variants.variantCount);
        }
        finally{Object.DestroyImmediate(variants);}
    }

    private static Color[] Capture(Camera camera)
    {
        var target=new RenderTexture(512,64,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var pixels=new Texture2D(512,64,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
        try
        {
            camera.targetTexture=target;camera.Render();camera.Render();RenderTexture.active=target;
            pixels.ReadPixels(new Rect(0,0,512,64),0,0);pixels.Apply();return pixels.GetPixels();
        }
        finally{camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);}
    }

    private static void Save(Color[] colors,string path)
    {
        var texture=new Texture2D(512,64,TextureFormat.RGBA32,false,true);
        try{texture.SetPixels(colors);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
        finally{Object.DestroyImmediate(texture);}
    }
}
