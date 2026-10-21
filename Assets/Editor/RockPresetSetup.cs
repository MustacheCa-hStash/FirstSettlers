using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class RockPresetSetup
{
    public const string Folder = "Assets/Resources/Rocks";
    public static readonly string[] Names = { "Pebble", "Fieldstone", "FlatStone", "RoundBoulder", "SplitBoulder", "LongBoulder" };
    [MenuItem("Tools/Terrain/Build Rock Preset Assets")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var materials=ApplySurfaceMaterials();
        string[] finishes={"Bare","Moss","Snow"};
        BuildGeometry(materials,finishes);
        AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        Debug.Log("ROCK_PRESETS_BUILT: six shapes, 18 assigned prefabs, three shared materials and independent hulls.");
    }
    [MenuItem("Tools/Terrain/Update Rock Surface Materials")]
    public static void UpdateSurfaces()
    {
        ApplySurfaceMaterials();AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        Debug.Log("ROCK_SURFACES_UPDATED: painted albedo, preserved snow mask, fuller moss; geometry and pooling unchanged.");
    }
    private static Material[] ApplySurfaceMaterials()
    {
        var texturePath="Assets/Textures/Rock/Presets/PaintedStone_Albedo.png";
        var textureImporter=(TextureImporter)AssetImporter.GetAtPath(texturePath);
        textureImporter.textureType=TextureImporterType.Default;textureImporter.textureShape=TextureImporterShape.Texture2D;
        textureImporter.sRGBTexture=true;textureImporter.anisoLevel=4;
        textureImporter.alphaSource=TextureImporterAlphaSource.FromInput;textureImporter.alphaIsTransparency=false;
        textureImporter.wrapMode=TextureWrapMode.Repeat;textureImporter.mipmapEnabled=true;
        textureImporter.maxTextureSize=512;textureImporter.textureCompression=TextureImporterCompression.Uncompressed;
        textureImporter.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if(texture==null)throw new Exception("Painted rock albedo did not import as Texture2D");
        var coating=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Rock/Presets/WeatheredStone_ColorHeight.png");
        if(coating==null)throw new Exception("Original rock coating mask is missing");
        var shader=Shader.Find("Custom/WeatheredRock");if(shader==null)throw new Exception("Rock shader missing");
        string[] finishes={"Bare","Moss","Snow"};
        var materials=new Material[3];
        for(int f=0;f<3;f++)
        {
            string path=Folder+"/Stone_"+finishes[f]+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}else m.shader=shader;
            m.SetTexture("_BaseMap",texture);m.SetFloat("_SurfaceMode",f);
            m.SetTexture("_CoatingMap",coating);
            m.SetFloat("_Coverage",f==2?1:f==1?.96f:.88f);m.SetFloat("_TextureScale",.65f);m.SetFloat("_Relief",0);
            m.SetColor("_CoatingColor",f==2?new Color(.82f,.86f,.89f):new Color(.24f,.30f,.11f));
            m.SetFloat("_Saturation",.85f);m.enableInstancing=true;EditorUtility.SetDirty(m);materials[f]=m;
        }
        return materials;
    }
    private static void BuildGeometry(Material[] materials,string[] finishes)
    {
        foreach(var name in Names)
        {
            Mesh mesh=Bake(name),hull=Bake(name+"_Hull"),lod=Bake(name+"_LOD1");
            if(mesh.bounds.min.y<-.001f || mesh.bounds.min.y>.001f)throw new Exception(name+" origin must sit at ground height");
            if(hull.GetIndexCount(0)/3>100)throw new Exception(name+" collision hull budget exceeded");
            for(int f=0;f<3;f++)
            {
                var root=new GameObject(name+"_"+finishes[f]){layer=GameplayLayers.WorldSolid};
                try
                {
                    root.AddComponent<MeshFilter>().sharedMesh=mesh;
                    root.AddComponent<MeshRenderer>().sharedMaterial=materials[f];
                    var c=root.AddComponent<MeshCollider>();c.sharedMesh=hull;c.convex=true;
                    var p=root.AddComponent<RockPreset>();p.collisionHull=hull;p.groundEmbed=name=="Pebble"?.035f:.08f;
                    p.distantMesh=lod;p.lodDistance=mesh.bounds.size.x>2?60:30;
                    p.shadowDistance=mesh.bounds.size.x>2?140:45;
                    root.AddComponent<RockQueryTarget>();
                    FoliageExclusionProfileAuthoring.AttachRock(root, name);
                    PrefabUtility.SaveAsPrefabAsset(root,Folder+"/"+name+"_"+finishes[f]+".prefab");
                }
                finally{Object.DestroyImmediate(root);}
            }
        }
    }
    private static Mesh Bake(string name)
    {
        string path="Assets/Models/Rocks/Presets/"+name+".fbx";
        var importer=(ModelImporter)AssetImporter.GetAtPath(path);
        importer.bakeAxisConversion=true;importer.materialImportMode=ModelImporterMaterialImportMode.None;
        importer.importNormals=ModelImporterNormals.Import;importer.isReadable=true;importer.SaveAndReimport();
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var filter=model.GetComponentInChildren<MeshFilter>();
        if(filter==null)throw new Exception(name+" has no static mesh");
        var clone=Object.Instantiate(filter.sharedMesh);clone.name=name;
        var matrix=filter.transform.localToWorldMatrix;
        var vertices=clone.vertices;var normals=clone.normals;
        for(int i=0;i<vertices.Length;i++){vertices[i]=matrix.MultiplyPoint3x4(vertices[i]);normals[i]=matrix.inverse.transpose.MultiplyVector(normals[i]).normalized;}
        clone.vertices=vertices;clone.normals=normals;clone.RecalculateBounds();
        string target=Folder+"/"+name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(target);
        if(saved==null){saved=clone;AssetDatabase.CreateAsset(saved,target);}
        else{EditorUtility.CopySerialized(clone,saved);Object.DestroyImmediate(clone);EditorUtility.SetDirty(saved);}
        return saved;
    }
    public static void BuildBatch()
    {try{Build();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    public static void SurfaceBatch()
    {try{UpdateSurfaces();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
