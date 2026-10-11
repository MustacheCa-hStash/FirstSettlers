using System;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class StairExtensionSetup
{
    public const string W22Id="build.wood.w22-full-storey-stair",W24Id="build.wood.w24-stair-handrail";
    public const string W22Model=W21StairSetup.ModelFolder+"/W22_FullStoreyStair_1.25Wx3Hx4D.fbx",W24Model=W21StairSetup.ModelFolder+"/W24_StairHandrail_0.25Wx2.5Hx2D.fbx";
    public static BuildDefinition[] Create()
    {
        var stair=Definition("w22-stair",W22Id,"W22 full-storey wood stair",BuildPartKind.Stair,new Vector3Int(5,12,16));
        stair.mesh=ModularWoodSetup.StructuralMesh(ModularWoodSetup.Import(W22Model),stair.LocalBounds);
        stair.collisionMesh=Save(W21StairSetup.MakeWalkingHull(1.25f,3,4),"w22-stair-collision");stair.occupiedVolumes=new[]{BuildCollisionAuthoring.Volume(stair.collisionMesh)};
        Prefab(stair,"w22-stair");
        var rail=Definition("w24-handrail",W24Id,"W24 stair handrail",BuildPartKind.Handrail,new Vector3Int(1,10,8));
        rail.mesh=ModularWoodSetup.StructuralMesh(ModularWoodSetup.Import(W24Model),new Bounds(new Vector3(.125f,1.25f,1),new Vector3(.125f,2.5f,2)));
        rail.occupiedVolumes=new[]{BuildCollisionAuthoring.Volume(Save(Box(new Vector3(.0625f,0,0),new Vector3(.1875f,1,.125f)),"w24-low-post-collision")),
            BuildCollisionAuthoring.Volume(Save(Box(new Vector3(.0625f,1.5f,1.875f),new Vector3(.1875f,2.5f,2)),"w24-high-post-collision")),
            BuildCollisionAuthoring.Volume(Save(Slope(),"w24-rail-collision"))};
        Prefab(rail,"w24-handrail");AssetDatabase.SaveAssets();return new[]{stair,rail};
    }
    private static BuildDefinition Definition(string file,string id,string title,BuildPartKind kind,Vector3Int size)
    {
        string path=BuildingPrototypeSetup.Folder+"/"+file+".asset";var d=AssetDatabase.LoadAssetAtPath<BuildDefinition>(path);
        if(d==null){d=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(d,path);}
        d.contentId=id;d.displayName=title;d.kind=kind;d.sizeUnits=size;d.minimumUnits=Vector3Int.zero;d.boundsOffset=Vector3.zero;d.wallEndInsetUnits=0;
        d.material=AssetDatabase.LoadAssetAtPath<Material>(SplitPlankWallSetup.MaterialPath);EditorUtility.SetDirty(d);return d;
    }
    private static Mesh Save(Mesh source,string name)
    {
        string path=BuildingPrototypeSetup.Folder+"/"+name+".asset";var d=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(d==null){d=source;AssetDatabase.CreateAsset(d,path);}else{EditorUtility.CopySerialized(source,d);Object.DestroyImmediate(source);EditorUtility.SetDirty(d);}return d;
    }
    private static Mesh Box(Vector3 lo,Vector3 hi)
    {
        var v=new Vector3[8];for(int i=0;i<8;i++)v[i]=new Vector3((i&1)==0?lo.x:hi.x,(i&2)==0?lo.y:hi.y,(i&4)==0?lo.z:hi.z);
        return Mesh(v);
    }
    private static Mesh Slope()
    {
        var v=new Vector3[8];for(int i=0;i<8;i++){float z=(i&4)==0?0:2;v[i]=new Vector3((i&1)==0?.0625f:.1875f,1+.75f*z+((i&2)==0?-.1f:0),z);}return Mesh(v);
    }
    private static Mesh Mesh(Vector3[] v)
    {
        var t=new[]{0,3,2,0,1,3,4,6,7,4,7,5,0,4,5,0,5,1,2,3,7,2,7,6,0,2,6,0,6,4,1,5,7,1,7,3};
        Vector3 centre=Vector3.zero;foreach(var p in v)centre+=p/8;
        for(int i=0;i<t.Length;i+=3)if(Vector3.Dot(Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]),(v[t[i]]+v[t[i+1]]+v[t[i+2]])/3-centre)<0)(t[i+1],t[i+2])=(t[i+2],t[i+1]);
        var m=new Mesh{name="W24 convex timber"};m.vertices=v;m.triangles=t;m.RecalculateNormals();m.RecalculateBounds();return m;
    }
    private static void Prefab(BuildDefinition d,string file)
    {
        string path=BuildingPrototypeSetup.Folder+"/"+file+".prefab";bool exists=AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null;var root=exists?PrefabUtility.LoadPrefabContents(path):new GameObject(d.displayName);
        try
        {
            root.layer=GameplayLayers.WorldSolid;root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.transform.localScale=Vector3.one;
            foreach(var collider in root.GetComponents<Collider>())Object.DestroyImmediate(collider);
            if(!root.TryGetComponent<MeshFilter>(out var f))f=root.AddComponent<MeshFilter>();f.sharedMesh=d.mesh;
            if(!root.TryGetComponent<MeshRenderer>(out var r))r=root.AddComponent<MeshRenderer>();r.sharedMaterial=d.material;
            foreach(var v in d.occupiedVolumes){var c=root.AddComponent<MeshCollider>();c.convex=true;c.sharedMesh=v.mesh;}
            if(d.kind==BuildPartKind.Stair && !root.TryGetComponent<SmoothWalkSurface>(out _))root.AddComponent<SmoothWalkSurface>();
            d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,path);EditorUtility.SetDirty(d);
        }finally{if(exists)PrefabUtility.UnloadPrefabContents(root);else Object.DestroyImmediate(root);}
    }
    [MenuItem("Tools/Building/Rebuild W22 W24 Kit")]
    public static void Rebuild()=>BuildingPrototypeSetup.CreateAssets();
}
