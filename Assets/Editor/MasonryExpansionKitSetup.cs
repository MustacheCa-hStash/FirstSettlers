using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Scoped import of editable masonry expansion assets; existing parts and scenes are retained.</summary>
public static class MasonryExpansionKitSetup
{
    public const string Art="ArtReferences/MasonryExpansion",Models="Assets/Models/Buildings/MasonryExpansion";
    public const string MaterialPath="Assets/Materials/Buildings/MasonryExpansion/MasonryExpansion.mat";
    public static bool Available=>File.Exists(Art+"/masonry-expansion-models.json")&&File.Exists(Models+"/braced-timber-frame.fbx");
    [Serializable]public class Manifest{public Part[] parts;}
    [Serializable]public class Part{public string slug,name,description,file,style;public int kind,triangles;public float[] size,minimum,opening,arch;public bool infill;public Solid[] solids;}
    [Serializable]public class Solid{public Vector3[] vertices;public int[] triangles;}
    public static string Id(string slug)=>"build."+(slug=="braced-timber-frame"?"wood.":"stone.")+slug;
    public static Manifest Source()=>JsonUtility.FromJson<Manifest>(File.ReadAllText(Art+"/masonry-expansion-models.json"));
    public static int PresentCount(BuildCatalog catalog)
    {int count=0;if(File.Exists(Art+"/masonry-expansion-models.json"))foreach(var p in Source().parts)if(catalog.Find(Id(p.slug))!=null)count++;return count;}
    private static Vector3 V(float[] a)=>new(a[0],a[1],a[2]);
    [MenuItem("Tools/Building/Install Masonry Roofs Floors Arches and Infill")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Install outside Play Mode.");
        var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);if(catalog==null)throw new InvalidOperationException("Existing catalog required.");
        var definitions=new List<BuildDefinition>(catalog.presets);
        foreach(var d in Create()){int i=definitions.FindIndex(p=>p!=null&&p.contentId==d.contentId);if(i<0)definitions.Add(d);else definitions[i]=d;}
        catalog.presets=definitions.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        foreach(var part in Source().parts)BuildStoneUiAuthoring.BakeThumbnail(catalog.Find(Id(part.slug)));
        AssetDatabase.SaveAssets();Debug.Log("MASONRY EXPANSION INSTALLED: eleven pieces; flat roof decks, full-height broad arch walls and braced timber frame.");
    }
    public static BuildDefinition[] Create()
    {
        Directory.CreateDirectory("Assets/Materials/Buildings/MasonryExpansion");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var material=BakedStoneWallSetup.CreateMaterial(MaterialPath,"Assets/Textures/Buildings/MasonryExpansion/MasonryExpansion_ColourAO.png","Assets/Textures/Buildings/MasonryExpansion/MasonryExpansion_Normal.png","Masonry expansion");
        material.SetColor("_BaseColor",Color.white);material.SetFloat("_Saturation",1);material.SetFloat("_Contrast",1);material.SetFloat("_Brightness",1);EditorUtility.SetDirty(material);
        var result=new List<BuildDefinition>();
        foreach(var p in Source().parts)
        {
            string path=BuildingPrototypeSetup.Folder+"/mx-"+p.slug;var d=AssetDatabase.LoadAssetAtPath<BuildDefinition>(path+".asset");
            if(d==null){d=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(d,path+".asset");}
            d.contentId=Id(p.slug);d.displayName=p.name;d.kind=(BuildPartKind)p.kind;d.sizeUnits=Vector3Int.RoundToInt(V(p.size)/BuildGeometry.Unit);
            d.minimumUnits=Vector3Int.zero;d.boundsOffset=V(p.minimum);d.stackRiseUnits=d.kind==BuildPartKind.Wall?d.sizeUnits.y:0;d.wallEndInsetUnits=0;
            d.wallPlacementMode=p.infill?WallPlacementMode.Infill:WallPlacementMode.Panel;d.preserveAuthoredMesh=true;d.requiresFullBearing=false;d.canGroundOnTerrain=false;d.maxHealth=100;
            d.material=p.style=="wood"?SplitPlankWallSetup.TrimMaterial:material;d.gableSlope=GableSlope.None;d.flipVariant=null;d.roofAttachment=RoofAttachmentMode.None;d.topCourseDepth=0;d.wallBaseAttachment=false;
            d.uvVariants=Array.Empty<Mesh>();d.jointCoverVariants=Array.Empty<Mesh>();d.floorParts=Array.Empty<BuildTimberPart>();d.solidBoxes=Array.Empty<Bounds>();d.wallOpenings=Array.Empty<Bounds>();
            var mesh=WattleWallSetup.BakeStaticMesh(ModularWoodSetup.Import(Models+"/"+p.file),Vector3.zero,p.name,out _,true);mesh.RecalculateTangents();d.mesh=Save(mesh,path+"-mesh.asset");
            var volumes=new List<BuildConvexVolume>();
            for(int index=0;index<p.solids.Length;index++)
            {
                var solid=p.solids[index];var t=(int[])solid.triangles.Clone();Vector3 centre=Vector3.zero;foreach(var v in solid.vertices)centre+=v/solid.vertices.Length;
                for(int i=0;i<t.Length;i+=3)if(Vector3.Dot(Vector3.Cross(solid.vertices[t[i+1]]-solid.vertices[t[i]],solid.vertices[t[i+2]]-solid.vertices[t[i]]),(solid.vertices[t[i]]+solid.vertices[t[i+1]]+solid.vertices[t[i+2]])/3-centre)<0)(t[i+1],t[i+2])=(t[i+2],t[i+1]);
                var hull=new Mesh{name=p.name+" occupied solid "+index};hull.vertices=solid.vertices;hull.triangles=t;hull.RecalculateBounds();hull.RecalculateNormals();
                volumes.Add(BuildCollisionAuthoring.Volume(Save(hull,path+"-solid-"+index+".asset")));
            }
            d.occupiedVolumes=volumes.ToArray();
            if(p.opening?.Length==4)d.wallOpenings=new[]{new Bounds(new Vector3((p.opening[0]+p.opening[2])*.5f,(p.opening[1]+p.opening[3])*.5f,0),new Vector3(p.opening[2]-p.opening[0],p.opening[3]-p.opening[1],p.size[2]))};
            var sockets=new List<BuildJointSocket>();if(d.kind==BuildPartKind.Wall)
                for(int i=0;i<=d.sizeUnits.x;i++)if(p.opening==null||p.opening.Length==0||p.opening[1]>0||i*.25f<p.opening[0]||i*.25f>p.opening[2])
                    sockets.Add(new BuildJointSocket{localPosition=new Vector3(i*.25f,0,0),outward=i==0?Vector3.left:Vector3.right,interior=i>0&&i<d.sizeUnits.x});
            d.jointSockets=sockets.ToArray();
            var root=new GameObject(p.name){layer=GameplayLayers.WorldSolid};
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh=d.mesh;root.AddComponent<MeshRenderer>().sharedMaterial=d.material;
                if(d.kind==BuildPartKind.Floor){var col=root.AddComponent<BoxCollider>();col.center=d.LocalBounds.center;col.size=d.LocalBounds.size;}
                else foreach(var volume in d.occupiedVolumes){var col=root.AddComponent<MeshCollider>();col.convex=true;col.sharedMesh=volume.mesh;}
                d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,path+".prefab");
            }finally{Object.DestroyImmediate(root);}
            EditorUtility.SetDirty(d);result.Add(d);
        }
        AssetDatabase.SaveAssets();return result.ToArray();
    }
    private static Mesh Save(Mesh generated,string path)
    {
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved==null){saved=generated;AssetDatabase.CreateAsset(saved,path);}
        else
        {
            // Replacing arrays also refreshes buffers held by live preview renderers.
            saved.Clear();saved.name=generated.name;saved.indexFormat=generated.indexFormat;
            saved.vertices=generated.vertices;saved.normals=generated.normals;saved.uv=generated.uv;
            saved.tangents=generated.tangents;saved.triangles=generated.triangles;saved.bounds=generated.bounds;
            saved.UploadMeshData(false);Object.DestroyImmediate(generated);EditorUtility.SetDirty(saved);
        }
        return saved;
    }
}
