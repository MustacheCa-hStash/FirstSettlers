using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class BakedFurnishingsValidation
{
    const string Output = "ArtReferences/BakedFurnishings";
    static int checks;
    static void Check(bool ok,string message) { checks++; if(!ok)throw new InvalidOperationException(message); }
    public static void InstallAndRunBatch()
    {
        try
        {
            BakedFurnishingsSetup.Install();
            Run();
            // Existing full furniture suite rebuilds the assets, then tests placement,
            // shaped collision, per-foot support and removals in actual Play mode.
            FurnitureKitValidation.RunBatch();
        }
        catch(Exception ex) { Debug.LogException(ex);EditorApplication.Exit(1); }
    }
    static void Run()
    {
        Directory.CreateDirectory(Output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var catalog=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
        foreach(string id in new[]{TorchKitSetup.Id,BakedFurnishingsSetup.BedId})
        {
            var d=catalog.Find(id);bool torch=id==TorchKitSetup.Id;
            Check(d.mesh.GetIndexCount(0)/3==(torch?280:320),"Triangle budget mismatch");
            Check(d.mesh.subMeshCount==1 && !d.mesh.isReadable && d.material.enableInstancing,"Runtime mesh/material settings");
            Check(d.material.shader.name=="Custom/BakedStoneWallMatte" && d.material.GetTexture("_BumpMap")!=null,"Baked material missing");
            Check(d.authoringPrefab.GetComponent<MeshFilter>().sharedMesh==d.mesh && d.authoringPrefab.GetComponent<MeshRenderer>().sharedMaterial==d.material,"Prefab mesh/material mismatch");
            var boxes=d.authoringPrefab.GetComponents<BoxCollider>();Check(boxes.Length==d.solidBoxes.Length,"Collision count changed");
            for(int i=0;i<boxes.Length;i++)Check(boxes[i].center==d.solidBoxes[i].center && boxes[i].size==d.solidBoxes[i].size,"Collision bounds changed");
            using(var data=MeshUtility.AcquireReadOnlyMeshData(d.mesh))
            {
                var m=data[0];using var n=new NativeArray<Vector3>(m.vertexCount,Allocator.Temp);using var t=new NativeArray<Vector4>(m.vertexCount,Allocator.Temp);
                using var v=new NativeArray<Vector3>(m.vertexCount,Allocator.Temp);using var uv=new NativeArray<Vector2>(m.vertexCount,Allocator.Temp);
                using var indices=new NativeArray<int>(m.GetSubMesh(0).indexCount,Allocator.Temp);
                m.GetNormals(n);m.GetTangents(t);m.GetVertices(v);m.GetUVs(0,uv);m.GetIndices(indices,0);
                for(int i=0;i<m.vertexCount;i++)Check(Mathf.Abs(((Vector3)t[i]).magnitude-1)<.001f && Mathf.Abs(Vector3.Dot(n[i],t[i]))<.001f && uv[i].x>=0 && uv[i].x<=1 && uv[i].y>=0 && uv[i].y<=1,"Invalid UV/tangent basis");
                for(int i=0;i<indices.Length;i+=3)
                {
                    int a=indices[i],b=indices[i+1],c=indices[i+2];
                    Check(Vector3.Dot(Vector3.Cross(v[b]-v[a],v[c]-v[a]),n[a]+n[b]+n[c])>0,"Face winding mismatch");
                }
                if(!torch)
                {
                    int up=0,down=0;for(int i=0;i<indices.Length;i+=3)
                    {int a=indices[i],b=indices[i+1],c=indices[i+2];var p=(v[a]+v[b]+v[c])/3;if(Mathf.Abs(p.x)<.89f && Mathf.Abs(p.z)<.44f){if(n[a].y>.9f)up++;if(n[a].y<-.9f)down++;}}
                    Check(up>=100 && down>=100,"Cord deck must retain both faces and physical openings");
                }
            }
            if(torch)
            {
                Check(d.furnitureWallMounted && !d.canGroundOnTerrain && d.flamePrefab!=null,"Mounted torch behaviour changed");
                var source=d.flamePrefab.GetComponent<LocalFlameSource>();
                Check(source.stationary && source.localEmitter==new Vector3(0,.91f,.32f) && source.range==12 && source.intensity==5,"Flame source changed");
            }
            Render(d,torch);
        }
        var audit=new List<string>();
        var seen=new HashSet<BuildDefinition>();
        void Entry(BuildDefinition d)
        {
            if(d==null || !seen.Add(d))return;
            void MeshRow(string suffix,Mesh mesh){if(mesh==null)return;ulong count=0;for(int i=0;i<mesh.subMeshCount;i++)count+=mesh.GetIndexCount(i)/3;
                audit.Add(d.displayName+suffix+"\t"+count+"\t"+d.contentId+"\t"+AssetDatabase.GetAssetPath(mesh));}
            MeshRow("",d.mesh);MeshRow(" (stairwell)",d.floorOpeningMesh);MeshRow(" (roof continuation)",d.roofContinuationMesh);
            MeshRow(" (roof seam)",d.gableRoofSeamMesh);Entry(d.flipVariant);
        }
        foreach(var d in catalog.presets)Entry(d);
        File.WriteAllLines(Output+"/catalog-triangles.tsv",new[]{"Piece\tTriangles\tContent ID\tMesh"});File.AppendAllLines(Output+"/catalog-triangles.tsv",audit);
        File.WriteAllText(Output+"/unity-validation.json","{\"passed\":true,\"checks\":"+checks+",\"torchTriangles\":280,\"platformTriangles\":320}");
        Debug.Log("BAKED FURNISHINGS PASS: "+checks+" checks; 280/320 triangles, both cord faces, baked normal lighting and catalog audit.");
    }
    static void Render(BuildDefinition d,bool torch)
    {
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;bool oldAsync=ShaderUtil.allowAsyncCompilation;
        var root=new GameObject("Baked furnishing proof");var camera=root.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;
        camera.orthographicSize=torch?.46f:.88f;camera.aspect=4f/3;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<31;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.19f,.20f);
        root.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
        var prop=new GameObject("Prop"){layer=31};prop.transform.SetParent(root.transform,false); // Camera transform must not move the prop.
        prop.transform.SetParent(null);var filter=prop.AddComponent<MeshFilter>();filter.sharedMesh=d.mesh;
        var material=new Material(d.material);prop.AddComponent<MeshRenderer>().sharedMaterial=material;
        var sun=new GameObject("Proof sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.4f;sun.cullingMask=1<<31;sun.shadows=LightShadows.None;
        var target=new RenderTexture(1200,900,24);target.Create();
        string name=torch?"torch":"platform";
        try
        {
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.gray*.6f;RenderSettings.sun=sun;
            ShaderUtil.allowAsyncCompilation=false;for(int i=0;i<material.passCount;i++)ShaderUtil.CompilePass(material,i,true);
            Check(!ShaderUtil.ShaderHasError(material.shader),"Baked shader compilation failed");
            sun.transform.rotation=Quaternion.Euler(35,-40,0);
            Vector3 aim=torch?new Vector3(0,.39f,.2f):new Vector3(0,.25f,0);
            camera.transform.position=torch?new Vector3(.9f,.8f,1.6f):new Vector3(2.5f,2,2.7f);camera.transform.LookAt(aim);
            var mapped=Capture(camera,target,name+"-after.png");material.SetFloat("_BumpScale",0);var flat=Capture(camera,target,name+"-normal-disabled.png");
            double difference=0;for(int i=0;i<mapped.Length;i++)difference+=Math.Abs(mapped[i].r-flat[i].r)+Math.Abs(mapped[i].g-flat[i].g)+Math.Abs(mapped[i].b-flat[i].b);
            Check(difference/mapped.Length>.01,"Normal map lighting missing");material.SetFloat("_BumpScale",1);
            string source=torch?"Assets/Models/Buildings/Lighting/MountedTorch.fbx":"Assets/Models/Buildings/Furniture/F05_sleeping_platform.fbx";
            var original=AssetDatabase.LoadAssetAtPath<GameObject>(source);Check(original!=null,"Original model missing");filter.sharedMesh=original.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            prop.GetComponent<MeshRenderer>().sharedMaterial=torch?AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Lighting/TorchWood.mat"):SplitPlankWallSetup.TrimMaterial;
            Capture(camera,target,name+"-before.png");filter.sharedMesh=d.mesh;prop.GetComponent<MeshRenderer>().sharedMaterial=material;
            camera.transform.position=torch?new Vector3(-.9f,.5f,-1.2f):new Vector3(-2,-1.3f,2);camera.transform.LookAt(aim);sun.transform.rotation=Quaternion.Euler(-30,140,0);
            Capture(camera,target,name+"-underside-back.png");
        }
        finally {GraphicsSettings.defaultRenderPipeline=oldPipeline;QualitySettings.renderPipeline=oldQuality;ShaderUtil.allowAsyncCompilation=oldAsync;RenderTexture.active=null;
            Object.DestroyImmediate(root);Object.DestroyImmediate(prop);Object.DestroyImmediate(sun.gameObject);Object.DestroyImmediate(material);target.Release();Object.DestroyImmediate(target);}
    }
    static Color32[] Capture(Camera camera,RenderTexture target,string file)
    {
        for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
        RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try {image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var pixels=image.GetPixels32();int visible=0,pink=0;var bg=pixels[0];
            foreach(var p in pixels){if(Math.Abs(p.r-bg.r)+Math.Abs(p.g-bg.g)+Math.Abs(p.b-bg.b)>15)visible++;if(p.r>200 && p.b>200 && p.g<50)pink++;}
            Check(visible>500 && pink<100,"Missing model/error shader: "+file);File.WriteAllBytes(Output+"/"+file,image.EncodeToPNG());return pixels;}
        finally{Object.DestroyImmediate(image);}
    }
}
