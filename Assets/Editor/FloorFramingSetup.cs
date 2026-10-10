using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class FloorFramingSetup
{
    public const string Folder="Assets/Models/Buildings/Wood/Floors";
    public const string Full=Folder+"/TimberFloor_4.00Wx0.25Hx4.00D.fbx";
    public const string Opening=Folder+"/TimberFloorStairwell_4.00Wx0.25Hx4.00D.fbx";
    [Serializable]private class Point{public float[] min,max;public int grainAxis,uvSeed;}
    [Serializable]private class Blueprint{public Point[] parts;}
    public static void Apply(BuildDefinition floor)
    {
        if(!File.Exists(Full))throw new InvalidOperationException("Floor framing FBX is missing.");
        Directory.CreateDirectory(".utmp/framing");
        floor.material=AssetDatabase.LoadAssetAtPath<Material>(SplitPlankWallSetup.MaterialPath);
        var paths=new[]{Full,Opening,Folder+"/RimBeam_3.50Wx0.25Hx0.25D.fbx",Folder+"/CornerBearingCap_0.25Wx0.25Hx0.25D.fbx"};
        var names=new[]{"floor-mesh","floor-stairwell-mesh","rim-span-mesh","bearing-cap-mesh"};
        for(int i=0;i<paths.Length;i++)
        {
            var importer=(ModelImporter)AssetImporter.GetAtPath(paths[i]);importer.bakeAxisConversion=true;importer.materialImportMode=ModelImporterMaterialImportMode.None;
            importer.importNormals=ModelImporterNormals.Import;importer.isReadable=false;importer.SaveAndReimport();
            var mesh=WattleWallSetup.BakeStaticMesh(AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]),Vector3.zero,names[i],out string report,true);
            string target=BuildingPrototypeSetup.Folder+"/"+names[i]+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(target);
            if(!saved){saved=mesh;AssetDatabase.CreateAsset(saved,target);}else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
            if(i==0)floor.mesh=saved;if(i==1)floor.floorOpeningMesh=saved;
            if(i>=2)SaveReference(saved,floor.material,names[i].Replace("-mesh",""),new[]{saved.bounds});
            File.WriteAllText(".utmp/framing/"+names[i]+"-import.txt",report);
        }
        var blueprint=JsonUtility.FromJson<Blueprint>(File.ReadAllText(Folder+"/floor-framing-blueprint.json"));
        var parts=new List<BuildTimberPart>();foreach(var p in blueprint.parts)parts.Add(new BuildTimberPart{bounds=BuildResolution.Box(new Vector3(p.min[0],p.min[1],p.min[2]),new Vector3(p.max[0],p.max[1],p.max[2])),grainAxis=p.grainAxis,uvSeed=p.uvSeed});
        floor.floorParts=parts.ToArray();
        floor.uvVariants=new[]{floor.mesh,ModularWoodSetup.Save(Folder+"/TimberFloor_4.00Wx0.25Hx4.00D_UV1.fbx","floor-uv1-mesh"),ModularWoodSetup.Save(Folder+"/TimberFloor_4.00Wx0.25Hx4.00D_UV2.fbx","floor-uv2-mesh")};
        floor.floorOpeningUvVariants=new[]{floor.floorOpeningMesh,ModularWoodSetup.Save(Folder+"/TimberFloorStairwell_4.00Wx0.25Hx4.00D_UV1.fbx","floor-stairwell-uv1-mesh"),ModularWoodSetup.Save(Folder+"/TimberFloorStairwell_4.00Wx0.25Hx4.00D_UV2.fbx","floor-stairwell-uv2-mesh")};
        EditorUtility.SetDirty(floor);
        var opening=BuildResolution.Box(new Vector3(.75f,-.25f,.5f),new Vector3(3,0,4));
        SaveReference(floor.floorOpeningMesh,floor.material,"floor-stairwell",BuildResolution.Subtract(new[]{floor.LocalBounds},new[]{opening}).ToArray());
        var obj=new GameObject("Timber floor"){layer=GameplayLayers.WorldSolid};
        try{obj.AddComponent<MeshFilter>().sharedMesh=floor.mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=floor.material;
            var box=obj.AddComponent<BoxCollider>();box.center=floor.LocalBounds.center;box.size=floor.LocalBounds.size;
            floor.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(obj,BuildingPrototypeSetup.Folder+"/floor.prefab");}
        finally{Object.DestroyImmediate(obj);}
    }
    private static void SaveReference(Mesh mesh,Material material,string name,Bounds[] solids)
    {
        var root=new GameObject(name){layer=GameplayLayers.WorldSolid};
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;
            foreach(var b in solids){var collider=root.AddComponent<BoxCollider>();collider.center=b.center;collider.size=b.size;}
            PrefabUtility.SaveAsPrefabAsset(root,BuildingPrototypeSetup.Folder+"/"+name+".prefab");
        }
        finally{Object.DestroyImmediate(root);}
    }
}
