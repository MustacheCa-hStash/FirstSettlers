using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class ThatchRoofSetup
{
    public const string ModelFolder="Assets/Models/Buildings/Wood/Roofs";
    public const string TexturePath="Assets/Textures/Buildings/Wood/thatch-roof-atlas.png";
    public const string DetailPath="Assets/Textures/Buildings/Wood/thatch-roof-detail.png";
    public const string MaterialPath="Assets/Materials/Buildings/Wood/ThatchRoof.mat";
    public const string ContentId="build.wood.thatch-roof-panel";
    public const string ModelPath=ModelFolder+"/ThatchRoofPanel_4.0Wx2.0Hx2.0D.fbx";
    public const string AssetRoot=BuildingPrototypeSetup.Folder+"/thatch-roof-panel";

    [MenuItem("Tools/Building/Link or Rebuild Thatched Roof")]
    public static void Rebuild()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        if(!catalog) throw new InvalidOperationException("Building catalog missing.");
        var presets=new List<BuildDefinition>(catalog.presets);
        presets.RemoveAll(d=>d!=null && (d.contentId=="build.wood.thatch-roof-leftend" ||
            d.contentId=="build.wood.thatch-roof-rightend" || d.contentId=="build.wood.thatch-roof-single"));
        foreach(var definition in CreateIfModelsAvailable())
        {
            int index=presets.FindIndex(d=>d.contentId==definition.contentId);
            if(index<0) presets.Add(definition); else presets[index]=definition;
        }
        catalog.presets=presets.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
    }
    public static List<BuildDefinition> CreateIfModelsAvailable()
    {
        var result=new List<BuildDefinition>();
        if(!File.Exists(ModelPath)) return result;
        Directory.CreateDirectory(".utmp/building-prototype");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        WoodSurfaceSetup.UpdateRoofTimber(WoodSurfaceSetup.ActiveTexture);
        var textureImporter=(TextureImporter)AssetImporter.GetAtPath(TexturePath);
        textureImporter.sRGBTexture=true;textureImporter.alphaSource=TextureImporterAlphaSource.FromInput;
        textureImporter.alphaIsTransparency=true;textureImporter.mipmapEnabled=true;
        textureImporter.mipMapsPreserveCoverage=true;textureImporter.alphaTestReferenceValue=.5f;
        textureImporter.wrapMode=TextureWrapMode.Clamp;textureImporter.filterMode=FilterMode.Trilinear;
        textureImporter.maxTextureSize=2048;textureImporter.textureCompression=TextureImporterCompression.Uncompressed;
        textureImporter.SaveAndReimport();
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(!material)
        {
            material=new Material(AssetDatabase.LoadAssetAtPath<Material>(WattleWallSetup.MaterialPath));
            material.name="Matte thatch roof atlas";AssetDatabase.CreateAsset(material,MaterialPath);
        }
        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
        if(File.Exists(DetailPath))
        {
            var detailImporter=(TextureImporter)AssetImporter.GetAtPath(DetailPath);
            detailImporter.textureType=TextureImporterType.Default;detailImporter.sRGBTexture=false;
            detailImporter.mipmapEnabled=true;detailImporter.wrapMode=TextureWrapMode.Clamp;
            detailImporter.filterMode=FilterMode.Trilinear;detailImporter.anisoLevel=4;
            detailImporter.maxTextureSize=1024;detailImporter.textureCompression=TextureImporterCompression.Uncompressed;
            detailImporter.SaveAndReimport();
            material.SetTexture("_ThatchDetailMap",AssetDatabase.LoadAssetAtPath<Texture2D>(DetailPath));
            material.SetFloat("_ThatchBakeEnabled",1);material.SetFloat("_ThatchNormalStrength",1);material.SetFloat("_ThatchOcclusionStrength",.55f);
            material.EnableKeyword("_THATCH_BAKED");
        }
        material.enableInstancing=true;EditorUtility.SetDirty(material);
        {
            string path=AssetRoot;
            var importer=(ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if(!importer)throw new InvalidOperationException("Roof model missing: "+ModelPath);
            Mesh before=null;
            if(!importer.bakeAxisConversion)
            {
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(AssetRoot+"-mesh.asset");
                if(existing!=null)before=Object.Instantiate(existing);
            }
            importer.bakeAxisConversion=true;
            importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.isReadable=false;
            importer.importNormals=ModelImporterNormals.Import;importer.importTangents=ModelImporterTangents.None;
            importer.SaveAndReimport();
            var imported=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var generated=WattleWallSetup.BakeStaticMesh(imported,new Vector3(4,2,2),"Thatch roof",out string inspection,true);
            if(generated.uv2.Length!=generated.vertexCount)
            {
                material.DisableKeyword("_THATCH_BAKED");material.SetFloat("_ThatchBakeEnabled",0);
            }
            if(before!=null)
            {
                try
                {
                    var a=before.vertices;var b=generated.vertices;var au=before.uv;var bu=generated.uv;var an=before.normals;var bn=generated.normals;
                    if(a.Length!=b.Length)throw new InvalidOperationException("Axis bake changed vertex count.");
                    for(int i=0;i<a.Length;i++)
                        if((a[i]-b[i]).sqrMagnitude>.000001f || (an[i]-bn[i]).sqrMagnitude>.000001f || (au[i]-bu[i]).sqrMagnitude>.000001f)
                            throw new InvalidOperationException($"Axis bake comparison at vertex {i}: position {a[i]:F6} -> {b[i]:F6}, normal {an[i]:F6} -> {bn[i]:F6}, UV {au[i]:F6} -> {bu[i]:F6}; bounds {before.bounds} -> {generated.bounds}");
                    Debug.Log("ROOF AXIS CONVERSION PASS: enabled; runtime vertices, normals and UVs match the previous hierarchy bake.");
                }
                finally{Object.DestroyImmediate(before);}
            }
            var visual=SaveMesh(generated,path+"-mesh.asset");
            // Verify the structural origin from independently authored timber:
            // eave bottom at y=0; high covering at +Z, never reflected or shifted.
            if(Mathf.Abs(visual.bounds.max.z-2)>.001f || visual.bounds.min.z<-.55f || visual.bounds.min.z>-.25f || visual.bounds.max.y<2.4f || visual.bounds.max.y>2.55f || Mathf.Abs(visual.bounds.max.x-4)>.001f || Mathf.Abs(visual.bounds.min.x)>.001f)
                throw new InvalidOperationException("Roof FBX axes/origin disagree: "+inspection);
            bool eave=false,ridge=false;
            foreach(var p in visual.vertices) { eave|=Mathf.Abs(p.y)<.001f && p.z>=-.001f && p.z<=.251f;ridge|=p.y>2.3f && p.z>1.99f; }
            if(!eave || !ridge)throw new InvalidOperationException($"Roof bearing/ridge import is reversed. eave={eave}, ridge={ridge}\n"+inspection);
            var definition=AssetDatabase.LoadAssetAtPath<BuildDefinition>(path+".asset");
            if(!definition) { definition=ScriptableObject.CreateInstance<BuildDefinition>();AssetDatabase.CreateAsset(definition,path+".asset"); }
            definition.contentId=ContentId; definition.displayName="Thatch roof";
            definition.kind=BuildPartKind.Roof;definition.sizeUnits=new Vector3Int(16,8,8);definition.minimumUnits=Vector3Int.zero;
            definition.wallEndInsetUnits=0;definition.mesh=visual;definition.material=material;
            var continuation=ClipEave(visual);
            definition.roofContinuationMesh=SaveMesh(continuation,path+"-joined-mesh.asset");
            // One straight thin slope plus its eave bearing; no attic wedge.
            definition.occupiedVolumes=new[]{
                Prism(path,0,0,2,0,2,.12f+Mathf.Sqrt(2)*.30f,2.12f+Mathf.Sqrt(2)*.30f),
                Prism(path,1,0,.25f,0,0,.13f,.13f)};
            SavePrefab(definition,path+".prefab");EditorUtility.SetDirty(definition);result.Add(definition);
            Debug.Log(inspection);File.WriteAllText(".utmp/building-prototype/roof-Panel-import.txt",inspection);
        }
        AssetDatabase.SaveAssets();return result;
    }
    // Clip at the structural seam instead of flattening frayed geometry onto
    // z=0. Interpolate UVs/normals and discard removed or degenerate triangles.
    private static Mesh ClipEave(Mesh source)
    {
        var positions=source.vertices;var normals=source.normals;var uv=source.uv;var detail=source.uv2;bool hasDetail=detail.Length==positions.Length;
        var output=new List<Vector3>();var ns=new List<Vector3>();var us=new List<Vector2>();var ds=new List<Vector2>();var indices=new List<int>();
        var polygon=new List<(Vector3 p,Vector3 n,Vector2 u,Vector2 d)>();
        int[] triangles=source.triangles;
        for(int i=0;i<triangles.Length;i+=3)
        {
            polygon.Clear();
            for(int j=0;j<3;j++)
            {
                int a=triangles[i+j],b=triangles[i+(j+1)%3];bool inside=positions[a].z>=0,other=positions[b].z>=0;
                if(inside)polygon.Add((positions[a],normals[a],uv[a],hasDetail?detail[a]:default));
                if(inside!=other)
                {
                    float t=-positions[a].z/(positions[b].z-positions[a].z);
                    var p=Vector3.Lerp(positions[a],positions[b],t);p.z=0;
                    polygon.Add((p,Vector3.Lerp(normals[a],normals[b],t).normalized,Vector2.Lerp(uv[a],uv[b],t),hasDetail?Vector2.Lerp(detail[a],detail[b],t):default));
                }
            }
            for(int j=1;j+1<polygon.Count;j++)
            {
                var a=polygon[0];var b=polygon[j];var c=polygon[j+1];
                if(Vector3.Cross(b.p-a.p,c.p-a.p).sqrMagnitude<1e-12f)continue;
                foreach(var v in new[]{a,b,c}){indices.Add(output.Count);output.Add(v.p);ns.Add(v.n);us.Add(v.u);ds.Add(v.d);}
            }
        }
        var mesh=new Mesh{name="Thatch roof joined eave"};mesh.SetVertices(output);mesh.SetNormals(ns);mesh.SetUVs(0,us);if(hasDetail)mesh.SetUVs(1,ds);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
    }
    private static Mesh SaveMesh(Mesh generated,string path)
    {
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(!existing) { AssetDatabase.CreateAsset(generated,path);return generated; }
        EditorUtility.CopySerialized(generated,existing);Object.DestroyImmediate(generated);EditorUtility.SetDirty(existing);return existing;
    }
    private static BuildConvexVolume Prism(string path,int index,float z0,float z1,float lower0,float lower1,float upper0,float upper1)
    {
        var v=new[]{new Vector3(0,lower0,z0),new Vector3(0,lower1,z1),new Vector3(0,upper1,z1),new Vector3(0,upper0,z0),
            new Vector3(4,lower0,z0),new Vector3(4,lower1,z1),new Vector3(4,upper1,z1),new Vector3(4,upper0,z0)};
        int[] t={0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7};
        Vector3 centre=Vector3.zero;foreach(var p in v)centre+=p/v.Length;
        for(int i=0;i<t.Length;i+=3)
            if(Vector3.Dot(Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]),(v[t[i]]+v[t[i+1]]+v[t[i+2]])/3-centre)<0)
                (t[i+1],t[i+2])=(t[i+2],t[i+1]);
        var mesh=new Mesh{name="Roof thin occupied prism "+index};mesh.vertices=v;mesh.triangles=t;mesh.RecalculateNormals();mesh.RecalculateBounds();
        mesh=SaveMesh(mesh,path+"-volume-"+index+".asset");
        return new BuildConvexVolume{mesh=mesh,vertices=v,bounds=mesh.bounds,
            faceAxes=new[]{Vector3.right,Vector3.forward,new Vector3(0,z1-z0,-(lower1-lower0)).normalized,new Vector3(0,z1-z0,-(upper1-upper0)).normalized},
            edgeAxes=new[]{Vector3.right,Vector3.up,new Vector3(0,lower1-lower0,z1-z0).normalized,new Vector3(0,upper1-upper0,z1-z0).normalized}};
    }
    private static void SavePrefab(BuildDefinition d,string path)
    {
        var root=new GameObject(d.displayName){layer=GameplayLayers.WorldSolid};
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh=d.mesh;
            var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=d.material;renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
            foreach(var volume in d.occupiedVolumes)
            {
                var child=new GameObject("Thin roof solid"){layer=GameplayLayers.WorldSolid};child.transform.SetParent(root.transform,false);
                var collider=child.AddComponent<MeshCollider>();collider.convex=true;collider.sharedMesh=volume.mesh;
            }
            d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally { Object.DestroyImmediate(root); }
    }
    public static void RebuildBatch()
    {
        try { Rebuild();EditorApplication.Exit(0); }
        catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
}
