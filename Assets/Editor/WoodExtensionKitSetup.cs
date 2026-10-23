using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class WoodExtensionKitSetup
{
    public const string Models="Assets/Models/Buildings/Wood/Extension";
    public const string Art="ArtReferences/IronAgeWood/Extension";
    public static readonly string[] Slugs={"w05-sleeper-footing","w06-horizontal-beam","w07-low-plank-wall","w08-narrow-wall","w12-wattle-fence"};
    public static bool Available=>File.Exists(Models+"/W12.fbx");
    [Serializable] public class Manifest {public Part[] parts;}
    [Serializable] public class Part {public string tag,slug,name,file;public float[] size,minimum;public int triangles;public Solid[] solids;}
    [Serializable] public class Solid {public float[] minimum,maximum;}
    public static Vector3 V(float[] p)=>new(p[0],p[1],p[2]);
    public static string Id(string slug)=>"build.wood."+slug;
    public static int PresentCount(BuildCatalog c){int count=0;foreach(var slug in Slugs)if(c.Find(Id(slug))!=null)count++;return count;}
    [MenuItem("Tools/Building/Install W05 W06 W07 W08 W12")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Install outside Play mode.");
        var c=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        if(c==null)throw new InvalidOperationException("Existing build catalog required.");
        var entries=new List<BuildDefinition>(c.presets);
        foreach(var d in Create()){int i=entries.FindIndex(x=>x!=null&&x.contentId==d.contentId);if(i<0)entries.Add(d);else entries[i]=d;}
        c.presets=entries.ToArray();EditorUtility.SetDirty(c);AssetDatabase.SaveAssets();
        foreach(var slug in Slugs)BuildStoneUiAuthoring.BakeThumbnail(c.Find(Id(slug)));
        AssetDatabase.SaveAssets();Debug.Log("WOOD EXTENSION INSTALLED: W05/W06/W07/W08/W12");
    }
    public static BuildDefinition[] Create()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var wood=AssetDatabase.LoadAssetAtPath<Material>(SplitPlankWallSetup.MaterialPath);
        var woven=AssetDatabase.LoadAssetAtPath<Material>(WattleWallSetup.MaterialPath);
        var post=AssetDatabase.LoadAssetAtPath<BuildDefinition>(BuildingPrototypeSetup.Folder+"/bay-post.asset");
        if(wood==null||woven==null||post==null)throw new InvalidOperationException("Shared wood, wattle and post assets required.");
        var lowCover=ModularWoodSetup.Save(Models+"/Cover.fbx","wood-extension-low-cover-mesh");
        var parts=JsonUtility.FromJson<Manifest>(File.ReadAllText(Art+"/wood-extension-models.json")).parts;
        var result=new List<BuildDefinition>();
        foreach(var p in parts)
        {
            if(p.tag=="Cover")continue;
            string path=BuildingPrototypeSetup.Folder+"/"+p.slug;
            var d=AssetDatabase.LoadAssetAtPath<BuildDefinition>(path+".asset");
            if(d==null){d=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(d,path+".asset");}
            d.contentId=Id(p.slug);d.displayName=p.name;
            d.kind=p.tag=="W05"?BuildPartKind.Foundation:p.tag=="W06"?BuildPartKind.Beam:BuildPartKind.Wall;
            d.sizeUnits=Vector3Int.RoundToInt(V(p.size)/BuildGeometry.Unit);d.minimumUnits=Vector3Int.zero;d.boundsOffset=V(p.minimum);
            d.wallEndInsetUnits=0;d.stackRiseUnits=d.sizeUnits.y;d.wallPlacementMode=WallPlacementMode.Panel;
            d.material=p.tag=="W12"?woven:wood;d.preserveAuthoredMesh=true;d.maxHealth=100;
            d.requiresFullBearing=false;d.canGroundOnTerrain=p.tag=="W07"||p.tag=="W12";
            var model=ModularWoodSetup.Import(Models+"/"+p.file);
            d.mesh=ModularWoodSetup.StructuralMesh(model,new Bounds(V(p.minimum)+V(p.size)*.5f,V(p.size)));
            d.uvVariants=Array.Empty<Mesh>();d.occupiedVolumes=Array.Empty<BuildConvexVolume>();
            d.solidBoxes=Array.ConvertAll(p.solids,s=>new Bounds((V(s.minimum)+V(s.maximum))*.5f,V(s.maximum)-V(s.minimum)));
            d.floorParts=p.tag=="W05"?new BuildTimberPart[d.solidBoxes.Length]:Array.Empty<BuildTimberPart>();
            for(int i=0;i<d.floorParts.Length;i++)d.floorParts[i]=new BuildTimberPart{bounds=d.solidBoxes[i],grainAxis=i<5?0:2,uvSeed=(i+1)*7919};
            d.wallOpenings=Array.Empty<Bounds>();d.jointSockets=Array.Empty<BuildJointSocket>();
            d.jointCoverVariants=Array.Empty<Mesh>();d.jointCoverMaterial=null;
            if(d.kind==BuildPartKind.Wall)
            {
                d.jointSockets=new BuildJointSocket[d.sizeUnits.x+1];
                for(int i=0;i<d.jointSockets.Length;i++)d.jointSockets[i]=new BuildJointSocket{localPosition=new Vector3(i*.25f,0,0),outward=i==0?Vector3.left:Vector3.right,interior=i>0&&i<d.sizeUnits.x};
                d.jointCoverVariants=p.tag=="W08"?post.uvVariants:new[]{lowCover};d.jointCoverMaterial=wood;
            }
            var root=new GameObject(p.name){layer=GameplayLayers.WorldSolid};
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh=d.mesh;var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=d.material;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.TwoSided;
                foreach(var b in d.solidBoxes.Length>0?d.solidBoxes:new[]{d.LocalBounds}){var col=root.AddComponent<BoxCollider>();col.center=b.center;col.size=b.size;}
                d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,path+".prefab");
            }finally{Object.DestroyImmediate(root);}
            EditorUtility.SetDirty(d);result.Add(d);
        }
        AssetDatabase.SaveAssets();return result.ToArray();
    }
}
