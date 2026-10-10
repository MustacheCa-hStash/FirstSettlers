using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Full-span aperture walls and one selectable half-gable with two slope representations.</summary>
public static class OpeningWallSetup
{
    public const string DoorId="build.wood.w09-doorway",WindowId="build.wood.w11-shutter-wall",GableId="build.wood.w15-gable-rising",FallingId="build.wood.w15-gable-falling";
    public static readonly string[] Files={"W09_DoorwayWall_4x3x0.25.fbx","W11_ShutterWall_4x3x0.25.fbx","W15_GableRising_2x2x0.25.fbx","W15_GableFalling_2x2x0.25.fbx"};
    private static readonly string[] Names={"w09-doorway-wall","w11-shutter-wall","w15-gable-rising","w15-gable-falling"};
    public static Bounds Box(Vector3 lo,Vector3 hi)=>new((lo+hi)*.5f,hi-lo);
    public static BuildDefinition[] Create(BuildDefinition wall,BuildDefinition post)
    {
        var door=Part(0,DoorId,"W09 doorway wall",wall,post,GableSlope.None,new Vector3(1.375f,0,-.125f),new Vector3(2.625f,2.25f,.125f));
        var window=Part(1,WindowId,"W11 shutter wall",wall,post,GableSlope.None,new Vector3(1.5f,1.25f,-.125f),new Vector3(2.5f,2,.125f));
        var rising=Part(2,GableId,"W15 half-gable · rising",wall,post,GableSlope.Rising,default,default);
        var falling=Part(3,FallingId,"W15 half-gable · falling",wall,post,GableSlope.Falling,default,default);
        rising.flipVariant=falling;falling.flipVariant=rising;EditorUtility.SetDirty(rising);EditorUtility.SetDirty(falling);
        return new[]{door,window,rising};
    }
    private static BuildDefinition Part(int index,string id,string title,BuildDefinition wall,BuildDefinition post,GableSlope slope,Vector3 lo,Vector3 hi)
    {
        string path=BuildingPrototypeSetup.Folder+"/"+Names[index];var d=AssetDatabase.LoadAssetAtPath<BuildDefinition>(path+".asset");
        if(d==null){d=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(d,path+".asset");}
        ModularWoodSetup.Profile(d,true);d.contentId=id;d.displayName=title;d.material=wall.material;d.gableSlope=slope;d.flipVariant=null;
        d.solidBoxes=Array.Empty<Bounds>();d.wallOpenings=Array.Empty<Bounds>();d.occupiedVolumes=Array.Empty<BuildConvexVolume>();
        d.jointCoverVariants=post.uvVariants;d.jointCoverMaterial=wall.material;
        d.gableRoofSeamMesh=null;
        if(slope==GableSlope.None)
        {
            d.wallOpenings=new[]{Box(lo,hi)};
            var solids=new List<Bounds>{Box(new Vector3(0,0,-.125f),new Vector3(lo.x,3,.125f)),Box(new Vector3(hi.x,0,-.125f),new Vector3(4,3,.125f)),
                Box(new Vector3(lo.x,hi.y,-.125f),new Vector3(hi.x,3,.125f))};
            if(lo.y>0)solids.Add(Box(new Vector3(lo.x,0,-.125f),new Vector3(hi.x,lo.y,.125f)));
            d.solidBoxes=solids.ToArray();
        }
        else
        {
            d.sizeUnits=new Vector3Int(8,8,1);d.stackRiseUnits=8;d.roofAttachment=RoofAttachmentMode.Gable;
            d.jointSockets=new[]{new BuildJointSocket{localPosition=Vector3.zero,outward=Vector3.left},new BuildJointSocket{localPosition=new Vector3(2,0,0),outward=Vector3.right}};
            d.jointCoverVariants=Array.Empty<Mesh>();d.occupiedVolumes=new[]{Triangle(path,slope)};
            d.gableRoofSeamMesh=ModularWoodSetup.StructuralMesh(ModularWoodSetup.Import(ModularWoodSetup.Folder+"/W15_Gable"+(slope==GableSlope.Rising?"Rising":"Falling")+"_RoofSeam.fbx"),new Bounds(new Vector3(1,1.0625f,0),new Vector3(2,2.125f,.25f)));
        }
        d.mesh=ModularWoodSetup.StructuralMesh(ModularWoodSetup.Import(ModularWoodSetup.Folder+"/"+Files[index]),d.LocalBounds);
        bool exists=AssetDatabase.LoadAssetAtPath<GameObject>(path+".prefab")!=null;
        var root=exists?PrefabUtility.LoadPrefabContents(path+".prefab"):new GameObject(title);
        try
        {
            root.name=title;root.layer=GameplayLayers.WorldSolid;root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.transform.localScale=Vector3.one;
            foreach(var collider in root.GetComponents<Collider>())Object.DestroyImmediate(collider);
            for(int i=root.transform.childCount-1;i>=0;i--)if(root.transform.GetChild(i).name.StartsWith("Authored solid "))Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            if(!root.TryGetComponent<MeshFilter>(out var filter))filter=root.AddComponent<MeshFilter>();filter.sharedMesh=d.mesh;
            if(!root.TryGetComponent<MeshRenderer>(out var renderer))renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=d.material;
            foreach(var solid in d.solidBoxes)
            {
                var child=new GameObject("Authored solid box"){layer=GameplayLayers.WorldSolid};child.transform.SetParent(root.transform,false);
                var collider=child.AddComponent<BoxCollider>();collider.center=solid.center;collider.size=solid.size;
            }
            foreach(var volume in d.occupiedVolumes){var collider=root.AddComponent<MeshCollider>();collider.convex=true;collider.sharedMesh=volume.mesh;}
            d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,path+".prefab");
        }
        finally{if(exists)PrefabUtility.UnloadPrefabContents(root);else Object.DestroyImmediate(root);}
        EditorUtility.SetDirty(d);return d;
    }
    private static BuildConvexVolume Triangle(string path,GableSlope slope)
    {
        var points=slope==GableSlope.Rising?new[]{new Vector2(0,0),new Vector2(2,0),new Vector2(2,2)}:new[]{new Vector2(0,0),new Vector2(2,0),new Vector2(0,2)};
        var v=new Vector3[6];for(int i=0;i<3;i++){v[i]=new Vector3(points[i].x,points[i].y,-.125f);v[i+3]=new Vector3(points[i].x,points[i].y,.125f);}
        var t=new[]{0,2,1,3,4,5,0,1,4,0,4,3,1,2,5,1,5,4,2,0,3,2,3,5};
        Vector3 centre=Vector3.zero;foreach(var p in v)centre+=p/6;
        for(int i=0;i<t.Length;i+=3)if(Vector3.Dot(Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]),(v[t[i]]+v[t[i+1]]+v[t[i+2]])/3-centre)<0)(t[i+1],t[i+2])=(t[i+2],t[i+1]);
        var generated=new Mesh{name="W15 triangular occupied prism"};generated.vertices=v;generated.triangles=t;generated.RecalculateBounds();generated.RecalculateNormals();
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path+"-collision.asset");
        if(saved==null){saved=generated;AssetDatabase.CreateAsset(saved,path+"-collision.asset");}else{EditorUtility.CopySerialized(generated,saved);Object.DestroyImmediate(generated);EditorUtility.SetDirty(saved);}
        return BuildCollisionAuthoring.Volume(saved);
    }
    [MenuItem("Tools/Building/Rebuild W09 W11 W15 Kit")]
    public static void Rebuild()=>BuildingPrototypeSetup.CreateAssets();
}
