using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class LimestoneCourtyardSetup
{
    public const string Root="Assets/Models/Buildings/LimestoneCourtyard";
    public const string ManifestPath="ArtReferences/LimestoneBuildKit/Courtyard/courtyard-models.json";
    public const string Colour="Assets/Textures/Buildings/LimestoneCourtyard/Courtyard_ColourAO.png";
    public const string Normal="Assets/Textures/Buildings/LimestoneCourtyard/Courtyard_Normal.png";
    public const string MaterialPath="Assets/Materials/Buildings/LimestoneCourtyard/Courtyard.mat";
    public static readonly string[] Slugs={"l10-arched-limestone-doorway","l11-narrow-limestone-window","l12-arched-limestone-window","l13-low-courtyard-wall","l14-short-courtyard-wall","l15-chamfered-limestone-coping","l16-limestone-courtyard-pier","l17-limestone-gable-rising"};
    public static readonly string[] Ids=Array.ConvertAll(Slugs,s=>"build.stone."+s);
    public const string FallingId="build.stone.l17-limestone-gable-falling";
    public static bool Available=>File.Exists(Root+"/L17.fbx")&&File.Exists(ManifestPath);
    [Serializable]public class Manifest{public Part[] parts;}
    [Serializable]public class Part{public string tag,slug,name,file,slope;public float[] size,minimum,opening;public float spring;public int triangles;public Solid[] solids;}
    [Serializable]public class Solid{public Vector3[] vertices;public int[] triangles;}
    public static Vector3 V(float[] a)=>new(a[0],a[1],a[2]);
    [MenuItem("Tools/Building/Install L10-L17 Limestone Courtyard")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Install outside Play mode.");
        var c=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);if(c==null)throw new InvalidOperationException("Existing catalog required.");
        var list=new List<BuildDefinition>(c.presets);foreach(var d in Create()){int i=list.FindIndex(p=>p!=null&&p.contentId==d.contentId);if(i<0)list.Add(d);else list[i]=d;}
        c.presets=list.ToArray();EditorUtility.SetDirty(c);AssetDatabase.SaveAssets();
        foreach(var id in Ids){var d=c.Find(id);BuildStoneUiAuthoring.BakeThumbnail(d);if(d.flipVariant!=null)BuildStoneUiAuthoring.BakeThumbnail(d.flipVariant);}AssetDatabase.SaveAssets();
        Debug.Log("COURTYARD INSTALLED: L10-L17; eight entries, real openings, shared bake and two gable slopes.");
    }
    public static BuildDefinition[] Create()
    {
        Directory.CreateDirectory("Assets/Materials/Buildings/LimestoneCourtyard");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var material=BakedStoneWallSetup.CreateMaterial(MaterialPath,Colour,Normal,"Limestone openings and courtyard");
        material.SetColor("_BaseColor",Color.white);material.SetFloat("_Saturation",1);material.SetFloat("_Contrast",1);material.SetFloat("_Brightness",1);EditorUtility.SetDirty(material);
        var source=JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));var result=new List<BuildDefinition>();BuildDefinition rise=null,fall=null;
        foreach(var p in source.parts)
        {
            string path=BuildingPrototypeSetup.Folder+"/"+p.slug;var d=AssetDatabase.LoadAssetAtPath<BuildDefinition>(path+".asset");
            if(d==null){d=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(d,path+".asset");}
            d.contentId="build.stone."+p.slug;d.displayName=p.name;d.kind=p.tag=="L16"?BuildPartKind.Corner:BuildPartKind.Wall;
            d.sizeUnits=Vector3Int.RoundToInt(V(p.size)/BuildGeometry.Unit);d.boundsOffset=V(p.minimum);d.minimumUnits=Vector3Int.zero;
            bool gable=p.tag.StartsWith("L17",StringComparison.Ordinal);
            d.wallEndInsetUnits=0;d.stackRiseUnits=Mathf.RoundToInt(p.size[1]*4);d.wallPlacementMode=WallPlacementMode.Panel;
            d.preserveAuthoredMesh=true;d.maxHealth=100;d.material=material;d.requiresFullBearing=false;
            d.canGroundOnTerrain=p.tag=="L13"||p.tag=="L14"||p.tag=="L16";
            d.jointCoverVariants=Array.Empty<Mesh>();d.jointCoverMaterial=null;d.uvVariants=Array.Empty<Mesh>();
            d.gableSlope=!gable?GableSlope.None:p.slope=="falling"?GableSlope.Falling:GableSlope.Rising;
            d.roofAttachment=gable?RoofAttachmentMode.Gable:RoofAttachmentMode.None;d.flipVariant=null;d.gableRoofSeamMesh=null;
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
            if(p.opening!=null&&p.opening.Length==4)
            {
                var o=p.opening;d.wallOpenings=new[]{LimestoneKitExtensionSetup.Box(new Vector3(o[0],o[1],-.25f),new Vector3(o[2],o[3],.25f))};
                // Floor fitting uses these conservative per-solid sections; physical
                // collision and structural occupancy use the actual convex shapes.
                d.solidBoxes=Array.ConvertAll(d.occupiedVolumes,v=>v.bounds);
            }
            var sockets=new List<BuildJointSocket>();
            if(d.kind==BuildPartKind.Wall)for(int j=0;j<=Mathf.RoundToInt(p.size[0]*4);j++)
                if((!gable||j==0||j==8)&&(p.opening==null||p.opening.Length==0||p.opening[1]>0||j*.25f<p.opening[0]||j*.25f>p.opening[2]))
                    sockets.Add(new BuildJointSocket{localPosition=new Vector3(j*.25f,0,0),outward=j==0?Vector3.left:Vector3.right,interior=!gable&&j>0&&j<p.size[0]*4});
            d.jointSockets=sockets.ToArray();
            BuildKitDimensions.Apply(d);
            var root=new GameObject(p.name){layer=GameplayLayers.WorldSolid};
            try{root.AddComponent<MeshFilter>().sharedMesh=d.mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;foreach(var v in d.occupiedVolumes){var col=root.AddComponent<MeshCollider>();col.sharedMesh=v.mesh;col.convex=true;}d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,path+".prefab");}finally{Object.DestroyImmediate(root);}
            EditorUtility.SetDirty(d);if(gable){if(p.slope=="falling")fall=d;else rise=d;}if(p.slope!="falling")result.Add(d);
        }
        rise.flipVariant=fall;fall.flipVariant=rise;EditorUtility.SetDirty(rise);EditorUtility.SetDirty(fall);AssetDatabase.SaveAssets();return result.ToArray();
    }
    private static Mesh Save(Mesh generated,string path)
    {var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved==null){saved=generated;AssetDatabase.CreateAsset(saved,path);}else{EditorUtility.CopySerialized(generated,saved);Object.DestroyImmediate(generated);EditorUtility.SetDirty(saved);}return saved;}
}
