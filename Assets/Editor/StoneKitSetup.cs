using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Imports authored stone meshes without rebuilding unrelated wooden assets.</summary>
public static class StoneKitSetup
{
    public const string Models="Assets/Models/Buildings/Stone";
    public const string Texture="Assets/Textures/Buildings/Stone/PaintedStone_Albedo.png";
    public const string MaterialPath="Assets/Materials/Buildings/Stone/PaintedStone.mat";
    public static readonly string[] Ids={"build.stone.s02-coursed-wall","build.stone.s03-pier","build.stone.s04-flagstone-floor","build.stone.s06-wall-cap","build.stone.s07-low-wall","build.stone.s12-gate-bay"};
    public static readonly string[] Files={"S02_CoursedRubbleWall_4x3x0.50","S03_StonePier_0.50x3x0.50","S04_FlagstoneFloor_4x0.25x4","S06_WallCap_4x0.25x0.50","S07_LowRubbleWall_4x1.25x0.50","S12_EmptyGateBay_4x1.25x0.50"};
    private static readonly string[] Names={"S02 coursed stone wall","S03 stone pier","S04 flagstone ground floor","S06 stone wall cap","S07 low rubble wall","S12 empty stone gate bay"};
    public static bool Available=>File.Exists(Models+"/"+Files[0]+".fbx");

    [MenuItem("Tools/Building/Rebuild S02 S03 S04 S06 Stone Kit")]
    [MenuItem("Tools/Building/Rebuild Stone Kit Including S07 S12")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Import stone assets outside Play mode.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        if(catalog==null)throw new InvalidOperationException("Install the existing building prototype first.");
        var presets=new List<BuildDefinition>(catalog.presets);
        foreach(var part in Create())
        {
            int index=presets.FindIndex(d=>d!=null && d.contentId==part.contentId);
            if(index>=0)presets[index]=part;else presets.Add(part);
        }
        catalog.presets=presets.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        foreach(var id in Ids)BuildStoneUiAuthoring.BakeThumbnail(catalog.Find(id));
        AssetDatabase.SaveAssets();
        Debug.Log("STONE KIT: S02/S03/S04/S06/S07/S12 installed, three UV layouts each, actual-mesh thumbnails; existing wooden assets and scene retained.");
    }

    public static BuildDefinition[] Create()
    {
        Directory.CreateDirectory("Assets/Materials/Buildings/Stone");AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(Texture,ImportAssetOptions.ForceSynchronousImport);
        var texture=AssetImporter.GetAtPath(Texture) as TextureImporter;
        if(texture==null)throw new InvalidOperationException("Approved painted stone texture missing.");
        texture.textureType=TextureImporterType.Default;texture.sRGBTexture=true;texture.mipmapEnabled=true;
        texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;texture.anisoLevel=4;
        texture.maxTextureSize=2048;texture.isReadable=false;texture.alphaSource=TextureImporterAlphaSource.None;texture.SaveAndReimport();
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material==null)
        {
            // The existing matte surface shader supplies the same fog, shadow and night response as timber.
            var wood=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Buildings/Wood/SplitPlankWood.mat");
            material=wood!=null?new Material(wood):new Material(Shader.Find("Custom/BuildingWoodMatte"));
            material.name="Ordinary painted stone";
            AssetDatabase.CreateAsset(material,MaterialPath);
        }
        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Texture));
        material.SetColor("_BaseColor",Color.white);material.SetFloat("_Saturation",1);material.SetFloat("_Contrast",1);material.SetFloat("_Brightness",1);
        material.SetFloat("_AlphaClip",0);material.DisableKeyword("_ALPHATEST_ON");material.enableInstancing=true;EditorUtility.SetDirty(material);
        var bakedMaterial=BakedStoneKitSetup.CreateMaterial();
        var result=new BuildDefinition[Ids.Length];
        for(int i=0;i<Ids.Length;i++)
        {
            string path=BuildingPrototypeSetup.Folder+"/"+Ids[i].Replace("build.stone.","");
            var d=AssetDatabase.LoadAssetAtPath<BuildDefinition>(path+".asset");
            if(d==null){d=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(d,path+".asset");}
            d.contentId=Ids[i];d.displayName=Names[i];d.material=material;
            d.kind=i==1?BuildPartKind.Corner:i==2?BuildPartKind.Floor:BuildPartKind.Wall;
            d.sizeUnits=i==0?new Vector3Int(16,12,2):i==1?new Vector3Int(2,12,2):i==2?new Vector3Int(16,1,16):new Vector3Int(16,i>=4?5:1,2);
            d.minimumUnits=i==2?new Vector3Int(0,-1,0):Vector3Int.zero;
            d.boundsOffset=i==1?new Vector3(-.25f,0,-.25f):i==2?Vector3.zero:new Vector3(0,0,-.25f);
            d.wallEndInsetUnits=0;d.stackRiseUnits=i==0||i==1?12:i>=4?5:1;d.wallPlacementMode=WallPlacementMode.Panel;
            d.preserveAuthoredMesh=true;d.requiresFullBearing=i==2;d.canGroundOnTerrain=i>=4;
            d.jointSockets=Array.Empty<BuildJointSocket>();d.jointCoverVariants=Array.Empty<Mesh>();d.jointCoverMaterial=null;
            d.solidBoxes=Array.Empty<Bounds>();d.wallOpenings=Array.Empty<Bounds>();d.occupiedVolumes=Array.Empty<BuildConvexVolume>();
            if(d.kind==BuildPartKind.Wall)
            {
                var sockets=new List<BuildJointSocket>();
                for(int j=0;j<=16;j++)
                    if(i!=5 || j*.25f<1.375f || j*.25f>2.625f)sockets.Add(new BuildJointSocket{localPosition=new Vector3(j*.25f,0,0),outward=j==0?Vector3.left:Vector3.right,interior=j>0&&j<16});
                d.jointSockets=sockets.ToArray();
            }
            if(i==5)
            {
                d.solidBoxes=new[]{new Bounds(new Vector3(.6875f,.625f,0),new Vector3(1.375f,1.25f,.5f)),new Bounds(new Vector3(3.3125f,.625f,0),new Vector3(1.375f,1.25f,.5f))};
                d.wallOpenings=new[]{new Bounds(new Vector3(2,.625f,0),new Vector3(1.25f,1.25f,.5f))};
            }
            if(i==0) BakedStoneWallSetup.Apply(d);
            else BakedStoneKitSetup.Apply(d,i-1,bakedMaterial);
            d.mesh=d.uvVariants[0];
            bool exists=AssetDatabase.LoadAssetAtPath<GameObject>(path+".prefab")!=null;
            var root=exists?PrefabUtility.LoadPrefabContents(path+".prefab"):new GameObject(Names[i]);
            try
            {
                root.name=Names[i];root.layer=GameplayLayers.WorldSolid;
                root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.transform.localScale=Vector3.one;
                if(!root.TryGetComponent<MeshFilter>(out var filter))filter=root.AddComponent<MeshFilter>();filter.sharedMesh=d.mesh;
                if(!root.TryGetComponent<MeshRenderer>(out var renderer))renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=d.material;
                foreach(var old in root.GetComponents<Collider>())Object.DestroyImmediate(old);
                foreach(var solid in d.solidBoxes.Length>0?d.solidBoxes:new[]{d.LocalBounds})
                {var collider=root.AddComponent<BoxCollider>();collider.center=solid.center;collider.size=solid.size;}
                d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,path+".prefab");
            }finally{if(exists)PrefabUtility.UnloadPrefabContents(root);else Object.DestroyImmediate(root);}
            EditorUtility.SetDirty(d);result[i]=d;
        }
        return result;
    }
}
