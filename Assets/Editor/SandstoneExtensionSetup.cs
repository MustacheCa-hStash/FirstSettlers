using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Scoped import of SS13 and SS15-SS24, including short lengths and a coping turn.</summary>
public static class SandstoneExtensionSetup
{
    public const string Art="ArtReferences/SandstoneBuildKit/Extension";
    public const string Models="Assets/Models/Buildings/SandstoneExtension";
    public const string Colour="Assets/Textures/Buildings/SandstoneExtension/SandstoneExtension_ColourAO.png";
    public const string Normal="Assets/Textures/Buildings/SandstoneExtension/SandstoneExtension_Normal.png";
    public const string MaterialPath="Assets/Materials/Buildings/SandstoneExtension/SandstoneExtension.mat";
    public static bool Available=>File.Exists(Art+"/sandstone-extension-models.json")&&File.Exists(Models+"/SS24.fbx");
    [Serializable]public class Manifest{public Part[] parts;public int trianglesSelectable;}
    [Serializable]public class Part
    {public string tag,slug,name,file,description;public int kind,triangles,steps;public float[] size,minimum;public bool ground,wallMounted;public SandstoneKitSetup.Solid[] solids;}
    public static Manifest Source()=>JsonUtility.FromJson<Manifest>(File.ReadAllText(Art+"/sandstone-extension-models.json"));
    public static string Id(Part p)=>(p.kind==7?"build.furniture.":"build.stone.")+p.slug;
    public static int PresentCount(BuildCatalog catalog)
    {int count=0;if(Available)foreach(var p in Source().parts)if(catalog.Find(Id(p))!=null)count++;return count;}
    private static Vector3 V(float[] a)=>new(a[0],a[1],a[2]);

    [MenuItem("Tools/Building/Install SS13 SS15-SS24 Sandstone")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Install outside Play mode.");
        var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        if(catalog==null)throw new InvalidOperationException("Existing catalog required.");
        var list=new List<BuildDefinition>(catalog.presets);
        foreach(var d in Create()){int i=list.FindIndex(p=>p!=null&&p.contentId==d.contentId);if(i<0)list.Add(d);else list[i]=d;}
        catalog.presets=list.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        foreach(var p in Source().parts)BuildStoneUiAuthoring.BakeThumbnail(catalog.Find(Id(p)));
        AssetDatabase.SaveAssets();Debug.Log("SANDSTONE EXTENSION INSTALLED: SS13 and SS15-SS24, short wall/coping and coping turn.");
    }

    public static BuildDefinition[] Create()
    {
        Directory.CreateDirectory("Assets/Materials/Buildings/SandstoneExtension");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(Colour,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(Normal,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
        var material=BakedStoneWallSetup.CreateMaterial(MaterialPath,Colour,Normal,"Apricot sandstone court and household");
        material.SetColor("_BaseColor",Color.white);material.SetFloat("_Saturation",1);material.SetFloat("_Contrast",1);material.SetFloat("_Brightness",1);EditorUtility.SetDirty(material);
        var result=new List<BuildDefinition>();
        foreach(var p in Source().parts)
        {
            string path=BuildingPrototypeSetup.Folder+"/"+p.slug;
            var d=AssetDatabase.LoadAssetAtPath<BuildDefinition>(path+".asset");
            if(d==null){d=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(d,path+".asset");}
            d.contentId=Id(p);d.displayName=p.name;d.kind=(BuildPartKind)p.kind;
            d.sizeUnits=Vector3Int.RoundToInt(V(p.size)/BuildGeometry.Unit);d.minimumUnits=Vector3Int.zero;d.boundsOffset=V(p.minimum);
            d.wallEndInsetUnits=0;d.stackRiseUnits=d.sizeUnits.y;d.wallPlacementMode=WallPlacementMode.Panel;
            d.material=material;d.preserveAuthoredMesh=true;d.maxHealth=100;
            d.canGroundOnTerrain=p.ground||d.kind==BuildPartKind.Furniture&&!p.wallMounted;
            d.furnitureWallMounted=p.wallMounted;
            d.topCourseDepth=0;d.wallBaseAttachment=false;d.gableSlope=GableSlope.None;d.roofAttachment=RoofAttachmentMode.None;d.flipVariant=null;
            d.jointCoverVariants=Array.Empty<Mesh>();d.jointCoverMaterial=null;d.floorParts=Array.Empty<BuildTimberPart>();
            d.wallOpenings=Array.Empty<Bounds>();d.floorOpeningMesh=null;d.floorOpeningUvVariants=Array.Empty<Mesh>();
            var mesh=WattleWallSetup.BakeStaticMesh(ModularWoodSetup.Import(Models+"/"+p.file),Vector3.zero,p.name,out _,true);
            mesh.RecalculateTangents();d.mesh=Save(mesh,path+"-mesh.asset");d.uvVariants=new[]{d.mesh};
            if((d.mesh.bounds.min-d.LocalBounds.min).sqrMagnitude>.000001f||(d.mesh.bounds.max-d.LocalBounds.max).sqrMagnitude>.000001f)
                throw new InvalidOperationException("Model bounds/origin mismatch: "+p.tag);
            bool convex=p.steps>0||p.tag is "SS16" or "SS21";
            var volumes=new List<BuildConvexVolume>();var boxes=new List<Bounds>();
            for(int i=0;i<p.solids.Length;i++)
            {
                var solid=p.solids[i];var triangles=(int[])solid.triangles.Clone();Vector3 centre=Vector3.zero;
                foreach(var v in solid.vertices)centre+=v/solid.vertices.Length;
                for(int t=0;t<triangles.Length;t+=3)
                    if(Vector3.Dot(Vector3.Cross(solid.vertices[triangles[t+1]]-solid.vertices[triangles[t]],solid.vertices[triangles[t+2]]-solid.vertices[triangles[t]]),
                        (solid.vertices[triangles[t]]+solid.vertices[triangles[t+1]]+solid.vertices[triangles[t+2]])/3-centre)<0)
                        (triangles[t+1],triangles[t+2])=(triangles[t+2],triangles[t+1]);
                var hull=new Mesh{name=p.tag+" solid "+i};hull.vertices=solid.vertices;hull.triangles=triangles;hull.RecalculateBounds();hull.RecalculateNormals();
                if(convex)volumes.Add(BuildCollisionAuthoring.Volume(Save(hull,path+"-solid-"+i+".asset")));
                else{boxes.Add(hull.bounds);Object.DestroyImmediate(hull);}
            }
            d.occupiedVolumes=volumes.ToArray();d.solidBoxes=boxes.ToArray();
            d.collisionMesh=p.steps>0?Save(W21StairSetup.MakeWalkingHull(p.size[0],p.size[1],p.size[2]),path+"-walking.asset"):null;
            var sockets=new List<BuildJointSocket>();
            if(d.kind==BuildPartKind.Wall&&p.tag!="SS15C")
                for(int i=0;i<=d.sizeUnits.x;i++)sockets.Add(new BuildJointSocket{localPosition=new Vector3(i*.25f,0,0),outward=i==0?Vector3.left:Vector3.right,interior=i>0&&i<d.sizeUnits.x});
            d.jointSockets=sockets.ToArray();
            var root=new GameObject(p.name){layer=GameplayLayers.WorldSolid};
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh=d.mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;
                if(p.steps>0){var collider=root.AddComponent<MeshCollider>();collider.sharedMesh=d.collisionMesh;collider.convex=true;root.AddComponent<SmoothWalkSurface>();}
                else
                {
                    foreach(var box in boxes){var c=root.AddComponent<BoxCollider>();c.center=box.center;c.size=box.size;}
                    foreach(var volume in volumes){var c=root.AddComponent<MeshCollider>();c.sharedMesh=volume.mesh;c.convex=true;}
                }
                d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,path+".prefab");
            }
            finally{Object.DestroyImmediate(root);}
            EditorUtility.SetDirty(d);result.Add(d);
        }
        AssetDatabase.SaveAssets();return result.ToArray();
    }
    private static Mesh Save(Mesh generated,string path)
    {var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved==null){saved=generated;AssetDatabase.CreateAsset(saved,path);}else{EditorUtility.CopySerialized(generated,saved);Object.DestroyImmediate(generated);EditorUtility.SetDirty(saved);}return saved;}
}
