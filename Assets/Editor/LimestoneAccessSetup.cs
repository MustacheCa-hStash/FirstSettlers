using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class LimestoneAccessSetup
{
    public const string Root="Assets/Models/Buildings/LimestoneAccess";
    public const string ManifestPath="ArtReferences/LimestoneBuildKit/Access/access-models.json";
    public const string Colour="Assets/Textures/Buildings/LimestoneAccess/Access_ColourAO.png";
    public const string Normal="Assets/Textures/Buildings/LimestoneAccess/Access_Normal.png";
    public const string MaterialPath="Assets/Materials/Buildings/LimestoneAccess/Access.mat";
    public static readonly string[] Slugs={"l18-limestone-eaves-cornice","l19-threshold-steps","l20-half-storey-limestone-stair","l21-full-storey-limestone-stair","l22-solid-bedded-limestone-landing","l23-limestone-retaining-wall","l24-plain-limestone-buttress"};
    public static readonly string[] Ids=Array.ConvertAll(Slugs,s=>"build.stone."+s);
    public static bool Available=>File.Exists(Root+"/L24.fbx")&&File.Exists(ManifestPath);
    [Serializable]public class Manifest{public Part[] parts;}
    [Serializable]public class Part{public string tag,slug,name,file,slope;public float[] size,minimum,opening;public float spring;public int triangles,steps;public Solid[] solids;}
    [Serializable]public class Solid{public Vector3[] vertices;public int[] triangles;}
    public static Vector3 V(float[] a)=>new(a[0],a[1],a[2]);
    [MenuItem("Tools/Building/Install L18-L24 Limestone Access")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Install outside Play mode.");
        var c=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);if(c==null)throw new InvalidOperationException("Existing catalog required.");
        var list=new List<BuildDefinition>(c.presets);foreach(var d in Create()){int i=list.FindIndex(p=>p!=null&&p.contentId==d.contentId);if(i<0)list.Add(d);else list[i]=d;}
        c.presets=list.ToArray();EditorUtility.SetDirty(c);AssetDatabase.SaveAssets();
        foreach(var id in Ids){var d=c.Find(id);BuildStoneUiAuthoring.BakeThumbnail(d);if(d.flipVariant!=null)BuildStoneUiAuthoring.BakeThumbnail(d.flipVariant);}AssetDatabase.SaveAssets();
        Debug.Log("ACCESS INSTALLED: L18-L24; seven entries, solid steps, bedded landing, retaining wall, buttress and fitted cornice.");
    }
    public static BuildDefinition[] Create()
    {
        Directory.CreateDirectory("Assets/Materials/Buildings/LimestoneAccess");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        // A rebake can retain importer settings while changing atlas layout. Refresh
        // both GPU artifacts explicitly before baking icons or reviewing materials.
        AssetDatabase.ImportAsset(Colour,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(Normal,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
        var material=BakedStoneWallSetup.CreateMaterial(MaterialPath,Colour,Normal,"Limestone access and bearing");
        material.SetColor("_BaseColor",Color.white);material.SetFloat("_Saturation",1);material.SetFloat("_Contrast",1);material.SetFloat("_Brightness",1);EditorUtility.SetDirty(material);
        var source=JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));var result=new List<BuildDefinition>();
        foreach(var p in source.parts)
        {
            string path=BuildingPrototypeSetup.Folder+"/"+p.slug;var d=AssetDatabase.LoadAssetAtPath<BuildDefinition>(path+".asset");
            if(d==null){d=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(d,path+".asset");}
            d.contentId="build.stone."+p.slug;d.displayName=p.name;d.kind=p.steps>0?BuildPartKind.Stair:p.tag=="L22"?BuildPartKind.Floor:p.tag=="L24"?BuildPartKind.Corner:BuildPartKind.Wall;
            d.sizeUnits=Vector3Int.RoundToInt(V(p.size)/BuildGeometry.Unit);d.boundsOffset=V(p.minimum);d.minimumUnits=Vector3Int.zero;
            d.wallEndInsetUnits=0;d.stackRiseUnits=Mathf.RoundToInt(p.size[1]*4);d.wallPlacementMode=WallPlacementMode.Panel;
            d.preserveAuthoredMesh=true;d.maxHealth=100;d.material=material;d.requiresFullBearing=p.tag=="L22"||p.tag=="L24";d.topCourseDepth=p.tag=="L18"?.25f:0;d.wallBaseAttachment=p.tag=="L24";
            d.canGroundOnTerrain=p.tag=="L23"||p.tag=="L24";
            d.jointCoverVariants=Array.Empty<Mesh>();d.jointCoverMaterial=null;d.uvVariants=Array.Empty<Mesh>();
            d.gableSlope=GableSlope.None;d.roofAttachment=RoofAttachmentMode.None;d.flipVariant=null;d.gableRoofSeamMesh=null;
            var mesh=WattleWallSetup.BakeStaticMesh(ModularWoodSetup.Import(Root+"/"+p.file),Vector3.zero,p.name,out _,true);mesh.RecalculateTangents();
            if((mesh.bounds.min-d.LocalBounds.min).sqrMagnitude>.000001f||(mesh.bounds.max-d.LocalBounds.max).sqrMagnitude>.000001f)throw new InvalidOperationException("FBX bounds/origin: "+p.tag);
            d.mesh=Save(mesh,path+"-mesh.asset");
            var volumes=new List<BuildConvexVolume>();int n=0;
            foreach(var s in p.solids)
            {
                var t=(int[])s.triangles.Clone();Vector3 centre=Vector3.zero;foreach(var v in s.vertices)centre+=v/s.vertices.Length;
                for(int i=0;i<t.Length;i+=3)if(Vector3.Dot(Vector3.Cross(s.vertices[t[i+1]]-s.vertices[t[i]],s.vertices[t[i+2]]-s.vertices[t[i]]),(s.vertices[t[i]]+s.vertices[t[i+1]]+s.vertices[t[i+2]])/3-centre)<0)(t[i+1],t[i+2])=(t[i+2],t[i+1]);
                var hull=new Mesh{name=p.tag+" occupied solid "+n};hull.vertices=s.vertices;hull.triangles=t;hull.RecalculateBounds();hull.RecalculateNormals();
                volumes.Add(BuildCollisionAuthoring.Volume(Save(hull,path+"-collision-"+(n++)+".asset")));
            }
            d.occupiedVolumes=volumes.ToArray();d.solidBoxes=Array.Empty<Bounds>();d.wallOpenings=Array.Empty<Bounds>();
            d.collisionMesh=null;
            if(p.steps>0)d.collisionMesh=Save(W21StairSetup.MakeWalkingHull(1.5f,p.size[1],p.size[2]),path+"-walking-hull.asset");
            d.jointSockets=p.tag=="L23"?new[]{new BuildJointSocket{localPosition=Vector3.zero,outward=Vector3.left},new BuildJointSocket{localPosition=new Vector3(4,0,0),outward=Vector3.right}}:Array.Empty<BuildJointSocket>();
            BuildKitDimensions.Apply(d);
            var root=new GameObject(p.name){layer=GameplayLayers.WorldSolid};
            try{root.AddComponent<MeshFilter>().sharedMesh=d.mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;if(p.steps>0){var col=root.AddComponent<MeshCollider>();col.sharedMesh=d.collisionMesh;col.convex=true;root.AddComponent<SmoothWalkSurface>();}else foreach(var v in d.occupiedVolumes){var col=root.AddComponent<MeshCollider>();col.sharedMesh=v.mesh;col.convex=true;}d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,path+".prefab");}finally{Object.DestroyImmediate(root);}
            EditorUtility.SetDirty(d);result.Add(d);
        }
        AssetDatabase.SaveAssets();return result.ToArray();
    }
    private static Mesh Save(Mesh generated,string path)
    {var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved==null){saved=generated;AssetDatabase.CreateAsset(saved,path);}else{EditorUtility.CopySerialized(generated,saved);Object.DestroyImmediate(generated);EditorUtility.SetDirty(saved);}return saved;}
}
