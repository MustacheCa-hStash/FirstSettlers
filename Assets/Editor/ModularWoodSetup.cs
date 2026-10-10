using System;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Imports the Blender-authored full-span profiles; retains component GUIDs and materials.</summary>
public static class ModularWoodSetup
{
    public const string Folder="Assets/Models/Buildings/Wood/Modular";
    public static GameObject Import(string path)
    {
        var importer=AssetImporter.GetAtPath(path) as ModelImporter;
        if(importer==null)throw new InvalidOperationException("Modular model missing: "+path);
        importer.bakeAxisConversion=true;importer.materialImportMode=ModelImporterMaterialImportMode.None;
        importer.importNormals=ModelImporterNormals.Import;importer.importTangents=ModelImporterTangents.None;importer.isReadable=false;
        importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }
    public static Mesh Save(string modelPath,string assetName)
    {
        var mesh=WattleWallSetup.BakeStaticMesh(Import(modelPath),Vector3.zero,assetName,out _,true);
        string path=BuildingPrototypeSetup.Folder+"/"+assetName+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved==null){saved=mesh;AssetDatabase.CreateAsset(saved,path);}else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
        return saved;
    }
    public static void Profile(BuildDefinition d,bool wall)
    {
        d.sizeUnits=wall?new Vector3Int(16,12,1):new Vector3Int(1,12,1);
        d.minimumUnits=Vector3Int.zero;d.boundsOffset=wall?new Vector3(0,0,-.125f):new Vector3(-.125f,0,-.125f);
        d.wallEndInsetUnits=0;d.stackRiseUnits=12;
        if(wall)
        {
            d.jointSockets=new BuildJointSocket[17];
            for(int i=0;i<=16;i++)d.jointSockets[i]=new BuildJointSocket{localPosition=new Vector3(i*.25f,0,0),outward=i==0?Vector3.left:Vector3.right,interior=i>0 && i<16};
        }
        else d.jointSockets=Array.Empty<BuildJointSocket>();
    }
    public static void Link(BuildDefinition wall,BuildDefinition wattle,BuildDefinition post)
    {
        post.uvVariants=new[]{post.mesh,Save(Folder+"/BayPost_0.26Wx3.00Hx0.26D_UV1.fbx","bay-post-uv1-mesh"),Save(Folder+"/BayPost_0.26Wx3.00Hx0.26D_UV2.fbx","bay-post-uv2-mesh")};
        foreach(var d in new[]{wall,wattle})
        {
            d.jointCoverVariants=post.uvVariants;d.jointCoverMaterial=wall.material;EditorUtility.SetDirty(d);
        }
        EditorUtility.SetDirty(post);
    }
    [MenuItem("Tools/Building/Rebuild Full-Span Modular Kit")]
    public static void Rebuild()=>BuildingPrototypeSetup.CreateAssets();
}
