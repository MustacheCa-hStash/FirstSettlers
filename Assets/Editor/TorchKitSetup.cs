using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

/// <summary>Idempotent torch installation. Does not rebuild other catalog entries or save a game scene.</summary>
public static class TorchKitSetup
{
    public const string Id="build.lighting.wall-torch";
    const string Folder="Assets/Resources/Lighting";
    static int checks;
    static void Check(bool ok,string text){checks++;if(!ok)throw new InvalidOperationException(text);}
    [MenuItem("Tools/Building/Install Mounted Torch")]
    public static void Install()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var atlas=Texture("Assets/Textures/Buildings/Lighting/TorchAtlas.png",true,true);
        var fire=Texture(Folder+"/TorchFlameAtlas.png",true,false);
        var wood=Material(Folder+"/TorchWood.mat",Shader.Find("Custom/BuildingWoodMatte"));wood.SetTexture("_BaseMap",atlas);
        wood.SetFloat("_AmbientFloor",.08f);wood.SetFloat("_Brightness",1.1f);EditorUtility.SetDirty(wood);
        var flame=Material(Folder+"/TorchFlame.mat",Resources.Load<Shader>("Lighting/TorchFlame"));flame.SetTexture("_BaseMap",fire);EditorUtility.SetDirty(flame);
        var mounted=Model("Assets/Models/Buildings/Lighting/MountedTorch.fbx");
        var hand=Model("Assets/Models/Buildings/Lighting/HandTorch.fbx");
        var quad=Quad();
        var effect=Effect(quad,flame,false,null,wood);
        Effect(quad,flame,true,hand,wood);
        const string definitionPath="Assets/Resources/Building/wall-torch.asset";
        var d=AssetDatabase.LoadAssetAtPath<BuildDefinition>(definitionPath);
        if(d==null){d=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(d,definitionPath);}
        d.contentId=Id;d.displayName="Mounted torch";d.kind=BuildPartKind.Furniture;
        d.sizeUnits=new Vector3Int(1,3,2);d.minimumUnits=Vector3Int.zero;d.boundsOffset=new Vector3(-.125f,0,0);
        d.mesh=mounted;d.material=wood;d.preserveAuthoredMesh=true;d.canGroundOnTerrain=false;d.furnitureWallMounted=true;
        d.furnitureSupportPoints=new[]{new Vector3(-.035f,.22f,0),new Vector3(.035f,.22f,0),new Vector3(0,.43f,0)};
        d.solidBoxes=new[]{new Bounds(new Vector3(0,.315f,.019f),new Vector3(.16f,.35f,.038f)),
            new Bounds(new Vector3(0,.285f,.19f),new Vector3(.14f,.04f,.3f)),new Bounds(new Vector3(0,.38f,.32f),new Vector3(.11f,.73f,.11f))};
        d.jointSockets=Array.Empty<BuildJointSocket>();d.uvVariants=Array.Empty<Mesh>();d.jointCoverVariants=Array.Empty<Mesh>();d.wallOpenings=Array.Empty<Bounds>();d.occupiedVolumes=Array.Empty<BuildConvexVolume>();
        d.flamePrefab=effect;d.maxHealth=50;
        BakedFurnishingsSetup.Apply(d);
        var root=new GameObject("Mounted torch"){layer=GameplayLayers.WorldSolid};
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh=d.mesh;root.AddComponent<MeshRenderer>().sharedMaterial=d.material;
            foreach(var box in d.solidBoxes){var collider=root.AddComponent<BoxCollider>();collider.center=box.center;collider.size=box.size;}
            d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/Building/wall-torch.prefab");
        }finally{Object.DestroyImmediate(root);}
        var catalog=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");var presets=new List<BuildDefinition>(catalog.presets);
        int index=presets.FindIndex(p=>p!=null && p.contentId==Id);if(index<0)presets.Add(d);else presets[index]=d;
        catalog.presets=presets.ToArray();EditorUtility.SetDirty(catalog);EditorUtility.SetDirty(d);AssetDatabase.SaveAssets();
        BuildStoneUiAuthoring.BakeThumbnail(d);AssetDatabase.SaveAssets();
        Debug.Log("TORCH INSTALLED: mounted build piece, hand prop, shared flipbook, matte atlas and stable content ID.");
    }
    static Texture2D Texture(string path,bool srgb,bool mip)
    {
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);Check(importer!=null,"Missing texture "+path);
        importer.textureType=TextureImporterType.Default;importer.textureShape=TextureImporterShape.Texture2D;importer.sRGBTexture=srgb;importer.mipmapEnabled=mip;importer.alphaIsTransparency=true;
        importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=1024;importer.wrapMode=TextureWrapMode.Clamp;
        importer.filterMode=FilterMode.Bilinear;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.isReadable=false;importer.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);Check(texture!=null,"Texture failed 2D import: "+path);return texture;
    }
    static Material Material(string path,Shader shader)
    {
        Check(shader!=null,"Required torch shader missing.");var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}else material.shader=shader;
        material.enableInstancing=true;return material;
    }
    static Mesh Model(string path)
    {
        var model=ModularWoodSetup.Import(path);var filters=model.GetComponentsInChildren<MeshFilter>(true);
        Check(filters.Length==1 && filters[0].sharedMesh.subMeshCount==1,"Torch requires one static mesh/submesh.");
        return filters[0].sharedMesh;
    }
    static Mesh Quad()
    {
        const string path=Folder+"/FlameCard.asset";var quad=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(quad==null){quad=new Mesh{name="Anchored flame card"};AssetDatabase.CreateAsset(quad,path);}
        quad.vertices=new[]{new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(-.5f,1,0),new Vector3(.5f,1,0)};
        quad.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)};quad.triangles=new[]{0,2,1,1,2,3};
        quad.RecalculateNormals();quad.bounds=new Bounds(new Vector3(0,.5f,0),new Vector3(2,2,2));EditorUtility.SetDirty(quad);return quad;
    }
    static GameObject Effect(Mesh quad,Material flame,bool held,Mesh model,Material wood)
    {
        var root=new GameObject(held?"Hand torch":"Mounted torch flame");
        try
        {
            var source=root.AddComponent<LocalFlameSource>();source.stationary=!held;source.localEmitter=new Vector3(0,.91f,.32f);source.range=12;source.intensity=5;
            var card=new GameObject("Flame");card.transform.SetParent(root.transform,false);card.transform.localPosition=new Vector3(0,.715f,.32f);card.transform.localScale=new Vector3(.24f,.45f,.24f);
            card.AddComponent<MeshFilter>().sharedMesh=quad;var renderer=card.AddComponent<MeshRenderer>();renderer.sharedMaterial=flame;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            var effect=card.AddComponent<LocalFlameEffect>();effect.source=source;effect.flameRenderer=renderer;
            if(model!=null){root.AddComponent<MeshFilter>().sharedMesh=model;root.AddComponent<MeshRenderer>().sharedMaterial=wood;}
            return PrefabUtility.SaveAsPrefabAsset(root,Folder+(held?"/HandTorch.prefab":"/MountedTorchFlame.prefab"));
        }finally{Object.DestroyImmediate(root);}
    }
    public static void InstallBatch()
    {
        try{Install();Validate();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    static void Validate()
    {
        var catalog=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");var torch=catalog.Find(Id);Check(torch!=null && BuildUiContent.Category(torch)==8,"Lighting category/catalog missing.");
        Check(torch.mesh.vertexCount<5000,"Torch mesh unexpectedly heavy.");Check(torch.flamePrefab.GetComponent<LocalFlameSource>().stationary,"Mounted source must cache.");
        Check(torch.material.GetTexture("_BaseMap") is Texture2D,"Torch body atlas is not bound.");
        Check(torch.flamePrefab.GetComponentInChildren<LocalFlameEffect>().flameRenderer.sharedMaterial.GetTexture("_BaseMap") is Texture2D fire && fire.width==768 && fire.height==768,"Flame atlas is not bound at its authored resolution.");
        Check(!Resources.Load<GameObject>("Lighting/HandTorch").GetComponent<LocalFlameSource>().stationary,"Carried source must update dynamically.");
        Check(torch.furnitureWallMounted && !torch.canGroundOnTerrain,"Wall torch gained unsupported grounding.");
        using(var data=MeshUtility.AcquireReadOnlyMeshData(torch.mesh))
        {
            var mesh=data[0];using var uv=new NativeArray<Vector2>(mesh.vertexCount,Allocator.Temp);mesh.GetUVs(0,uv);
            foreach(var p in uv)Check(p.x>=0 && p.x<=1 && p.y>=0 && p.y<=1,"Torch UV outside atlas.");
        }
        // Actual placement and support on both faces at each of eight headings.
        var wall=catalog.Find("build.prototype.wall");
        for(int yaw=0;yaw<8;yaw++)foreach(int side in new[]{-1,1})
        {
            using var session=new BuildSession();var frame=session.CreateFrame(Vector3.zero,yaw);var record=session.Add(wall,frame,Vector3Int.zero,0,true);
            var rotation=BuildGeometry.Rotation(yaw);Vector3 hit=rotation*new Vector3(2,1,side*wall.LocalBounds.extents.z);
            var preview=BuildFurnishing.Solve(torch,hit,rotation*(Vector3.forward*side),record,session.Frame(record.OwnFrameId),0,0,Vector3Int.zero);
            Check(!preview.InvalidAttachment,"Mounted torch face attachment failed.");
            var p=session.Add(torch,session.CreateFrame(preview.Frame.Origin,preview.Frame.YawStep),preview.Anchor,preview.YawStep,false);
            Check(p.Supported,"Torch lost supported rear contacts.");
        }
        BuildStoneUiAuthoring.Validate();Capture(torch);
        Directory.CreateDirectory(".utmp/local-flame");File.WriteAllText(".utmp/local-flame/torch-validation.json","{\"checks\":"+checks+",\"vertices\":"+torch.mesh.vertexCount+",\"triangles\":"+(torch.mesh.GetIndexCount(0)/3)+"}");
        Debug.Log($"TORCH VALIDATION PASS: {checks} checks; {torch.mesh.vertexCount} vertices; {torch.mesh.GetIndexCount(0)/3} triangles.");
    }
    static void Capture(BuildDefinition torch)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);LocalFlameGeometry.Reset();LocalFlameValidation.Configure();
        var camera=new GameObject("Torch proof camera").AddComponent<Camera>();camera.tag="MainCamera";
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.012f,.014f,.02f);camera.fieldOfView=36;
        camera.transform.SetPositionAndRotation(new Vector3(1.7f,1.3f,2.5f),Quaternion.LookRotation(new Vector3(-1.7f,-.7f,-2.2f)));
        var wallMaterial=LocalFlameValidation.WhiteWood();wallMaterial.SetColor("_BaseColor",new Color(.23f,.19f,.15f));wallMaterial.SetFloat("_AmbientFloor",.015f);
        LocalFlameValidation.Box("Room wall",new Vector3(0,.65f,-.12f),new Vector3(3,2.3f,.24f),wallMaterial);
        LocalFlameValidation.Box("Room floor",new Vector3(0,-.1f,1),new Vector3(5,.2f,4),wallMaterial);
        var prop=(GameObject)PrefabUtility.InstantiatePrefab(torch.authoringPrefab);
        var effect=(GameObject)PrefabUtility.InstantiatePrefab(torch.flamePrefab);var source=effect.GetComponent<LocalFlameSource>();
        var manager=LocalFlameLighting.Ensure();manager.Register(source);manager.automaticRendering=false;manager.targetCamera=camera;
        var shadow=prop.AddComponent<LocalFlameOccluder>();shadow.GeometryChanged();
        source.ignoredGeometryOwner=shadow;
        for(int i=0;i<20;i++)manager.RenderForCamera(camera,1);
        var target=new RenderTexture(900,900,24);target.Create();camera.targetTexture=target;
        for(int i=0;i<4;i++)camera.Render();RenderTexture.active=target;
        var image=new Texture2D(900,900,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,900,900),0,0);image.Apply();
        Directory.CreateDirectory(".utmp/local-flame");File.WriteAllBytes(".utmp/local-flame/mounted-torch.png",image.EncodeToPNG());
        RenderTexture.active=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);
    }
}
