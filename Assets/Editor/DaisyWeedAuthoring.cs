using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class DaisyWeedAuthoring
{
    public const string PrefabPath="Assets/Prefabs/daisyWeeds.prefab";
    public const string MaterialPath="Assets/Materials/M_Flowers/M_DaisyWeeds.mat";
    public const string TexturePath="Assets/Textures/Flowers/T_DaisyWeeds_Painted_Atlas.png";
    public const string MeshPath="Assets/Models/Grass+Flowers/DaisyWeeds_Painted_Runtime.asset";
    [Serializable]private sealed class Blueprint{public Vector3[] positions,normals;public Vector2[] uvs;public Color[] colors;public int[] triangles;}
    [MenuItem("Tools/Foliage/Daisy Weeds/Build Replacement")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Daisy authoring requires Edit mode");
        AssetDatabase.Refresh();Directory.CreateDirectory(".utmp/daisy");
        var old=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        long oldId=0;string oldGuid=null;
        if(old!=null)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(old,out oldGuid,out oldId);
            var oldMesh=old.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            Debug.Log($"DAISY_LEGACY: root id {oldId}, {oldMesh?.vertexCount} vertices, {oldMesh?.GetIndexCount(0)/3} triangles, raw mesh bounds {oldMesh?.bounds}, prefab scale {old.transform.localScale}");
        }
        var importer=(TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.textureType=TextureImporterType.Default;importer.textureShape=TextureImporterShape.Texture2D;
        importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;
        importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=true;importer.alphaTestReferenceValue=.45f;
        importer.maxTextureSize=512;importer.textureCompression=TextureImporterCompression.CompressedHQ;
        importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;importer.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if(texture==null)throw new Exception("Daisy atlas must import as a 2D texture");
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        var shader=Shader.Find("Custom/DaisyWeedsPainted");if(shader==null)throw new Exception("Painted daisy shader missing");
        if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,MaterialPath);}else material.shader=shader;
        material.SetTexture("_BaseMap",texture);material.SetTextureScale("_BaseMap",Vector2.one);material.SetTextureOffset("_BaseMap",Vector2.zero);
        material.SetColor("_BaseColor",Color.white);material.SetColor("_StemColor",new Color(.28f,.43f,.09f));
        material.SetFloat("_Cutoff",.45f);material.SetFloat("_AmbientStrength",.18f);material.SetFloat("_NormalUpBlend",.2f);
        material.SetFloat("_InstanceTintStrength",.12f);material.SetFloat("_WindStrength",.018f);material.SetVector("_FlowerPetalColor",Vector4.one);
        material.enableInstancing=true;material.doubleSidedGI=true;material.renderQueue=2450;EditorUtility.SetDirty(material);
        var data=JsonUtility.FromJson<Blueprint>(File.ReadAllText("Assets/Models/Grass+Flowers/DaisyWeeds_Painted.json"));
        if(data.positions.Length!=data.normals.Length || data.positions.Length!=data.uvs.Length || data.positions.Length!=data.colors.Length)throw new Exception("Daisy mesh streams mismatch");
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if(mesh==null){mesh=new Mesh{name="DaisyWeeds_Painted_Runtime"};AssetDatabase.CreateAsset(mesh,MeshPath);}
        else mesh.Clear();
        mesh.vertices=data.positions;mesh.normals=data.normals;mesh.uv=data.uvs;mesh.colors=data.colors;mesh.triangles=data.triangles;mesh.RecalculateBounds();
        var bounds=mesh.bounds;bounds.Expand(.045f);mesh.bounds=bounds;mesh.UploadMeshData(false);EditorUtility.SetDirty(mesh);
        var root=new GameObject("daisyWeeds");
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.TwoSided;renderer.receiveShadows=true;renderer.lightProbeUsage=LightProbeUsage.Off;
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally{Object.DestroyImmediate(root);}
        AssetDatabase.SaveAssets();AssetDatabase.ImportAsset(PrefabPath,ImportAssetOptions.ForceSynchronousImport);
        var replacement=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(replacement,out string guid,out long id);
        // Converting a model variant to a standalone prefab must preserve every existing scene/recovery reference.
        if(oldId!=0 && id!=oldId)
        {
            var yaml=File.ReadAllText(PrefabPath);
            if(!yaml.Contains("--- !u!1 &"+id))throw new Exception("Replacement is not a standalone root prefab");
            yaml=Regex.Replace(yaml,@"(?<!\d)"+id+@"(?!\d)",oldId.ToString());File.WriteAllText(PrefabPath,yaml);
            AssetDatabase.ImportAsset(PrefabPath,ImportAssetOptions.ForceSynchronousImport);
            replacement=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);AssetDatabase.TryGetGUIDAndLocalFileIdentifier(replacement,out guid,out id);
        }
        if(oldId!=0 && (id!=oldId || guid!=oldGuid))throw new Exception("Daisy prefab identity changed");
        File.WriteAllText(".utmp/daisy/identity.txt",$"guid={guid}\nroot={id}\nvertices={mesh.vertexCount}\ntriangles={mesh.GetIndexCount(0)/3}\nbounds={mesh.bounds}\n");
        Debug.Log($"DAISY_REPLACED: {mesh.vertexCount} vertices / {mesh.GetIndexCount(0)/3} triangles, one mesh/material, existing prefab identity {guid}:{id}");
    }
    public static void BuildBatch(){try{Build();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}
