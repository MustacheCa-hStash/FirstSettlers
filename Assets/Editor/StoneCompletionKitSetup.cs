using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class StoneCompletionKitSetup
{
    public const string Models="Assets/Models/Buildings/StoneCompletion";
    public const string Art="ArtReferences/StoneBuildKit/Completion";
    public const string MaterialPath="Assets/Materials/Buildings/StoneCompletion/StoneCompletion.mat";
    public const string Colour="Assets/Textures/Buildings/StoneCompletion/StoneCompletion_ColourAO.png";
    public const string Normal="Assets/Textures/Buildings/StoneCompletion/StoneCompletion_Normal.png";
    public static bool Available=>File.Exists(Models+"/S24.fbx")&&File.Exists(Art+"/stone-completion-models.json");
    public static readonly string[] Slugs={"s01-rubble-wall","s05-rubble-footing","s08-narrow-rubble-wall","s09-timber-lintel-doorway","s11-open-window-wall","s10-static-plank-door","s12-static-yard-gate","s15-stone-gable-rising","s16-timber-wall-plate","s17-threshold-steps","s18-half-storey-stair","s19-full-storey-stair","s20-stone-landing","s21-stepped-stone-guard","s23-rubble-retaining-wall","s24-rubble-buttress"};
    public static string Id(string slug)=>"build."+(slug.StartsWith("s10-",StringComparison.Ordinal)||slug.StartsWith("s12-",StringComparison.Ordinal)||slug.StartsWith("s16-",StringComparison.Ordinal)?"wood.":"stone.")+slug;
    public static int PresentCount(BuildCatalog c){int n=0;foreach(var slug in Slugs)if(c.Find(Id(slug))!=null)n++;return n;}
    [Serializable]public class Manifest{public Part[] parts;}
    [Serializable]public class Part{public string tag,slug,name,file,slope;public float[] size,minimum,opening;public int triangles,steps;public bool staticLeaf,guard;public Solid[] solids;}
    [Serializable]public class Solid{public Vector3[] vertices;public int[] triangles;}
    public static Vector3 V(float[] a)=>new(a[0],a[1],a[2]);
    [MenuItem("Tools/Building/Install Remaining Ordinary Stone Kit")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Install outside Play mode.");
        var c=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);if(c==null)throw new InvalidOperationException("Existing catalog required.");
        var list=new List<BuildDefinition>(c.presets);foreach(var d in Create()){int i=list.FindIndex(x=>x!=null&&x.contentId==d.contentId);if(i<0)list.Add(d);else list[i]=d;}
        c.presets=list.ToArray();EditorUtility.SetDirty(c);AssetDatabase.SaveAssets();
        foreach(var slug in Slugs){var d=c.Find(Id(slug));BuildStoneUiAuthoring.BakeThumbnail(d);if(d.flipVariant!=null)BuildStoneUiAuthoring.BakeThumbnail(d.flipVariant);}AssetDatabase.SaveAssets();
        Debug.Log("STONE COMPLETION INSTALLED: 16 selectable pieces, opposite gable, static leaves and fixed openings.");
    }
    public static BuildDefinition[] Create()
    {
        Directory.CreateDirectory("Assets/Materials/Buildings/StoneCompletion");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var material=BakedStoneWallSetup.CreateMaterial(MaterialPath,Colour,Normal,"Ordinary rubble and fitted timber");
        material.SetColor("_BaseColor",Color.white);material.SetFloat("_Saturation",1);material.SetFloat("_Contrast",1);material.SetFloat("_Brightness",1);EditorUtility.SetDirty(material);
        var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Art+"/stone-completion-models.json"));var result=new List<BuildDefinition>();BuildDefinition rising=null,falling=null;
        foreach(var p in manifest.parts)
        {
            string path=BuildingPrototypeSetup.Folder+"/"+p.slug;var d=AssetDatabase.LoadAssetAtPath<BuildDefinition>(path+".asset");
            if(d==null){d=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(d,path+".asset");}
            d.contentId=Id(p.slug);d.displayName=p.name;d.kind=p.steps>0?BuildPartKind.Stair:p.staticLeaf?BuildPartKind.Furniture:p.guard?BuildPartKind.Handrail:p.tag=="S16"?BuildPartKind.Beam:p.tag=="S05"?BuildPartKind.Foundation:p.tag=="S20"?BuildPartKind.Floor:p.tag=="S24"?BuildPartKind.Corner:BuildPartKind.Wall;
            d.sizeUnits=Vector3Int.RoundToInt(V(p.size)/BuildGeometry.Unit);d.boundsOffset=V(p.minimum);d.minimumUnits=Vector3Int.zero;
            d.wallEndInsetUnits=0;d.stackRiseUnits=d.sizeUnits.y;d.wallPlacementMode=WallPlacementMode.Panel;
            d.preserveAuthoredMesh=true;d.material=material;d.maxHealth=100;d.requiresFullBearing=false;d.canGroundOnTerrain=p.tag is "S05" or "S23" or "S24";
            d.wallBaseAttachment=p.tag=="S24";d.topCourseDepth=p.tag=="S16"?.25f:0;
            d.furnitureWallMounted=false;d.relaxedWallMount=false;d.furnitureSupportPoints=Array.Empty<Vector3>();
            d.jointCoverVariants=Array.Empty<Mesh>();d.jointCoverMaterial=null;d.uvVariants=Array.Empty<Mesh>();d.wallOpenings=Array.Empty<Bounds>();d.solidBoxes=Array.Empty<Bounds>();
            d.gableSlope=p.slope=="rising"?GableSlope.Rising:p.slope=="falling"?GableSlope.Falling:GableSlope.None;d.roofAttachment=d.IsHalfGable?RoofAttachmentMode.Gable:RoofAttachmentMode.None;d.flipVariant=null;d.gableRoofSeamMesh=null;
            var mesh=WattleWallSetup.BakeStaticMesh(ModularWoodSetup.Import(Models+"/"+p.file),Vector3.zero,p.name,out _,true);mesh.RecalculateTangents();d.mesh=Save(mesh,path+"-mesh.asset");
            var volumes=new List<BuildConvexVolume>();int n=0;
            foreach(var solid in p.solids)
            {
                var t=(int[])solid.triangles.Clone();Vector3 centre=Vector3.zero;foreach(var v in solid.vertices)centre+=v/solid.vertices.Length;
                for(int i=0;i<t.Length;i+=3)if(Vector3.Dot(Vector3.Cross(solid.vertices[t[i+1]]-solid.vertices[t[i]],solid.vertices[t[i+2]]-solid.vertices[t[i]]),(solid.vertices[t[i]]+solid.vertices[t[i+1]]+solid.vertices[t[i+2]])/3-centre)<0)(t[i+1],t[i+2])=(t[i+2],t[i+1]);
                var hull=new Mesh{name=p.tag+" occupied solid "+n};hull.vertices=solid.vertices;hull.triangles=t;hull.RecalculateBounds();hull.RecalculateNormals();volumes.Add(BuildCollisionAuthoring.Volume(Save(hull,path+"-collision-"+(n++)+".asset")));
            }
            d.occupiedVolumes=volumes.ToArray();d.collisionMesh=p.steps>0?Save(W21StairSetup.MakeWalkingHull(p.size[0],p.size[1],p.size[2]),path+"-walking-hull.asset"):null;
            var sockets=new List<BuildJointSocket>();
            if(d.kind==BuildPartKind.Wall)
            {
                int ticks=d.sizeUnits.x;for(int i=0;i<=ticks;i++)if(!d.IsHalfGable||i==0||i==ticks)
                    if(p.opening==null||p.opening.Length==0||p.opening[1]>0||i*.25f<p.opening[0]||i*.25f>p.opening[2])sockets.Add(new BuildJointSocket{localPosition=new Vector3(i*.25f,0,d.LocalBounds.center.z),outward=i==0?Vector3.left:Vector3.right,interior=i>0&&i<ticks});
            }
            d.jointSockets=sockets.ToArray();
            if(p.opening?.Length==4)d.wallOpenings=new[]{new Bounds(new Vector3((p.opening[0]+p.opening[2])*.5f,(p.opening[1]+p.opening[3])*.5f,0),new Vector3(p.opening[2]-p.opening[0],p.opening[3]-p.opening[1],.5f))};
            if(p.slope=="rising")rising=d;if(p.slope=="falling")falling=d;
            var root=new GameObject(p.name){layer=GameplayLayers.WorldSolid};
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh=d.mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;
                if(p.steps>0){var col=root.AddComponent<MeshCollider>();col.sharedMesh=d.collisionMesh;col.convex=true;root.AddComponent<SmoothWalkSurface>();}
                else foreach(var v in d.occupiedVolumes){var col=root.AddComponent<MeshCollider>();col.sharedMesh=v.mesh;col.convex=true;}
                d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,path+".prefab");
            }finally{Object.DestroyImmediate(root);}
            EditorUtility.SetDirty(d);if(p.slope!="falling")result.Add(d);
        }
        if(rising==null||falling==null)throw new InvalidOperationException("Both stone gable slopes required.");rising.flipVariant=falling;falling.flipVariant=rising;EditorUtility.SetDirty(rising);EditorUtility.SetDirty(falling);
        AssetDatabase.SaveAssets();return result.ToArray();
    }
    private static Mesh Save(Mesh mesh,string path){var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved==null){saved=mesh;AssetDatabase.CreateAsset(saved,path);}else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}return saved;}
}
