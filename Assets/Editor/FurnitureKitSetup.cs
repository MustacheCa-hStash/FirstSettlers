using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Imports F01-F06 and appends stable IDs without rebuilding existing assets or scenes.</summary>
public static class FurnitureKitSetup
{
    public const string Models="Assets/Models/Buildings/Furniture";
    public const string ManifestPath="ArtReferences/IronAgeFurniture/Models/furniture-models.json";
    public static bool Available=>File.Exists(Models+"/F01_three_leg_stool.fbx") && File.Exists(ManifestPath);
    [Serializable] public sealed class Manifest { public Part[] models; }
    [Serializable] public sealed class Part
    {
        public string tag,content_id,slug,name,file;
        public float[] size,minimum;
        public Vector3[] support_points;
        public Box[] solid_boxes;
        public bool wall_mounted;
    }
    [Serializable] public sealed class Box { public float[] center,size; }
    public static Vector3 V(float[] v)=>new(v[0],v[1],v[2]);
    [MenuItem("Tools/Building/Install F01-F06 Furniture")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Install furniture outside Play mode.");
        var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        if(catalog==null)throw new InvalidOperationException("Existing building catalog is required.");
        var presets=new List<BuildDefinition>(catalog.presets);
        foreach(var d in Create())
        {
            int index=presets.FindIndex(p=>p!=null && p.contentId==d.contentId);
            if(index<0)presets.Add(d);else presets[index]=d;
        }
        catalog.presets=presets.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        foreach(var d in catalog.presets)if(d!=null && d.kind==BuildPartKind.Furniture)BuildStoneUiAuthoring.BakeThumbnail(d);
        AssetDatabase.SaveAssets();
        Debug.Log("FURNITURE INSTALLED: six FBX meshes, prefabs, definitions and actual-mesh icons; existing material and scene retained.");
    }
    public static BuildDefinition[] Create()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var source=JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        if(source.models.Length!=6)throw new InvalidOperationException("Expected F01-F06 model manifest.");
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Buildings/Wood/SplitPlankWood.mat");
        if(material==null)throw new InvalidOperationException("Shared wood material is required.");
        var result=new List<BuildDefinition>();
        for(int i=0;i<source.models.Length;i++)
        {
            var part=source.models[i];string path=BuildingPrototypeSetup.Folder+"/"+part.slug;
            var d=AssetDatabase.LoadAssetAtPath<BuildDefinition>(path+".asset");
            if(d==null){d=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(d,path+".asset");}
            d.contentId=part.content_id;d.displayName=part.name;d.kind=BuildPartKind.Furniture;
            d.sizeUnits=Vector3Int.RoundToInt(V(part.size)/BuildGeometry.Unit);d.minimumUnits=Vector3Int.zero;d.boundsOffset=V(part.minimum);
            d.preserveAuthoredMesh=true;d.canGroundOnTerrain=!part.wall_mounted;d.furnitureWallMounted=part.wall_mounted;
            d.furnitureSupportPoints=part.support_points;d.material=material;d.maxHealth=100;
            d.solidBoxes=new Bounds[part.solid_boxes.Length];
            for(int j=0;j<d.solidBoxes.Length;j++)d.solidBoxes[j]=new Bounds(V(part.solid_boxes[j].center),V(part.solid_boxes[j].size));
            d.mesh=ModularWoodSetup.StructuralMesh(ModularWoodSetup.Import(Models+"/"+part.file),d.LocalBounds);
            d.uvVariants=Array.Empty<Mesh>();d.jointSockets=Array.Empty<BuildJointSocket>();d.jointCoverVariants=Array.Empty<Mesh>();
            d.occupiedVolumes=Array.Empty<BuildConvexVolume>();d.wallOpenings=Array.Empty<Bounds>();d.requiresFullBearing=false;
            var root=new GameObject(part.name){layer=GameplayLayers.WorldSolid};
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh=d.mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;
                foreach(var box in d.solidBoxes){var collider=root.AddComponent<BoxCollider>();collider.center=box.center;collider.size=box.size;}
                d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,path+".prefab");
            }finally{Object.DestroyImmediate(root);}
            EditorUtility.SetDirty(d);result.Add(d);
        }
        return result.ToArray();
    }
}
