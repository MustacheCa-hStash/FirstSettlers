using System;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Two-metre rectangular infill and matching short junction framing.</summary>
public static class SquareInfillSetup
{
    public const string Id="build.wood.square-infill";
    public const string Model=ModularWoodSetup.Folder+"/SquareInfill_2x2x0.25.fbx";
    public const string PostModel=ModularWoodSetup.Folder+"/SquareInfill_JunctionPost_2m.fbx";
    public const string AssetPath=BuildingPrototypeSetup.Folder+"/square-infill";
    public static BuildDefinition Create(BuildDefinition wall)
    {
        var d=AssetDatabase.LoadAssetAtPath<BuildDefinition>(AssetPath+".asset");
        if(d==null){d=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(d,AssetPath+".asset");}
        ModularWoodSetup.Profile(d,true);d.contentId=Id;d.displayName="Square wall infill";
        d.wallPlacementMode=WallPlacementMode.Infill;d.sizeUnits=new Vector3Int(8,8,1);d.stackRiseUnits=8;
        d.material=wall.material;d.preserveAuthoredMesh=true;d.jointCoverMaterial=wall.material;
        d.jointSockets=new BuildJointSocket[9];
        for(int i=0;i<=8;i++)d.jointSockets[i]=new BuildJointSocket{localPosition=new Vector3(i*.25f,0,0),outward=i==0?Vector3.left:Vector3.right,interior=i>0 && i<8};
        d.jointCoverVariants=new[]{ModularWoodSetup.StructuralMesh(ModularWoodSetup.Import(PostModel),new Bounds(new Vector3(0,1.0015f,0),new Vector3(.26f,2.003f,.26f)))};
        d.mesh=ModularWoodSetup.StructuralMesh(ModularWoodSetup.Import(Model),d.LocalBounds);
        d.solidBoxes=Array.Empty<Bounds>();d.wallOpenings=Array.Empty<Bounds>();d.occupiedVolumes=Array.Empty<BuildConvexVolume>();
        var root=new GameObject(d.displayName){layer=GameplayLayers.WorldSolid};
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh=d.mesh;root.AddComponent<MeshRenderer>().sharedMaterial=d.material;
            var collider=root.AddComponent<BoxCollider>();collider.center=d.LocalBounds.center;collider.size=d.LocalBounds.size;
            d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,AssetPath+".prefab");
        }finally{Object.DestroyImmediate(root);}
        EditorUtility.SetDirty(d);return d;
    }
    // Targeted install preserves every existing definition, material choice and catalog entry.
    public static void InstallBatch()
    {
        try
        {
            var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            var d=Create(catalog.Find("build.prototype.wall"));var entries=new System.Collections.Generic.List<BuildDefinition>(catalog.presets);
            if(!entries.Contains(d))entries.Add(d);catalog.presets=entries.ToArray();EditorUtility.SetDirty(catalog);
            BuildStoneUiAuthoring.BakeThumbnail(d);AssetDatabase.SaveAssets();BuildStoneUiAuthoring.Validate();
            ModularWoodValidation.RunInstalledBatch();
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
}
