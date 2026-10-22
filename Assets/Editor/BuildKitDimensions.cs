using System;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

/// <summary>Installed modular dimensions, derived from retained source art without changing source files or UVs.</summary>
public static class BuildKitDimensions
{
    public static Vector3 Scale(BuildDefinition d)=>Scale(d,d.LocalBounds);
    public static Bounds InstalledBounds(BuildDefinition d,Vector3 minimum,Vector3 size)
    {
        var scale=Scale(d,new Bounds(minimum+size*.5f,size));
        return new Bounds(Vector3.Scale(minimum+size*.5f,scale),Vector3.Scale(size,scale));
    }
    private static Vector3 Scale(BuildDefinition d,Bounds b)
    {
        string id=d.contentId??"";
        if(id.StartsWith("build.stone.l",StringComparison.Ordinal) && d.kind==BuildPartKind.Wall && b.size.z>.49f && b.size.z<.51f)
            return new Vector3(1,1,.5f);
        if(id=="build.stone.l18-limestone-eaves-cornice" && b.size.z>.74f)return new Vector3(1,1,2f/3);
        if(id is "build.stone.l19-threshold-steps" or "build.stone.l20-half-storey-limestone-stair" or "build.stone.l21-full-storey-limestone-stair")
            return new Vector3(2/b.size.x,1,1);
        if(id=="build.stone.l22-solid-bedded-limestone-landing")return new Vector3(2/b.size.x,1,2/b.size.z);
        return Vector3.one;
    }
    public static bool Apply(BuildDefinition d)
    {
        bool changed=false;
        if(d.contentId=="build.wood.l35-braced-door-canopy" && !d.relaxedWallMount){d.relaxedWallMount=true;changed=true;}
        var scale=Scale(d);
        if((scale-Vector3.one).sqrMagnitude<.000001f){if(changed)EditorUtility.SetDirty(d);return changed;}
        string path=AssetDatabase.GetAssetPath(d);path=path.Substring(0,path.Length-6)+"-modular";
        Mesh original=d.mesh;d.mesh=Mesh(original,scale,path+"-mesh.asset");
        if(d.uvVariants!=null)for(int i=0;i<d.uvVariants.Length;i++)
            d.uvVariants[i]=d.uvVariants[i]==original?d.mesh:Mesh(d.uvVariants[i],scale,path+"-uv"+i+".asset");
        if(d.collisionMesh!=null)d.collisionMesh=Mesh(d.collisionMesh,scale,path+"-walking.asset");
        if(d.occupiedVolumes!=null)for(int i=0;i<d.occupiedVolumes.Length;i++)d.occupiedVolumes[i]=BuildCollisionAuthoring.Volume(Mesh(d.occupiedVolumes[i].mesh,scale,path+"-solid"+i+".asset"));
        Bounds Scaled(Bounds b)=>new(Vector3.Scale(b.center,scale),Vector3.Scale(b.size,scale));
        if(d.solidBoxes!=null)for(int i=0;i<d.solidBoxes.Length;i++)d.solidBoxes[i]=Scaled(d.solidBoxes[i]);
        if(d.wallOpenings!=null)for(int i=0;i<d.wallOpenings.Length;i++)d.wallOpenings[i]=Scaled(d.wallOpenings[i]);
        d.boundsOffset=Vector3.Scale(d.boundsOffset,scale);d.minimumUnits=Vector3Int.RoundToInt(Vector3.Scale(d.minimumUnits,scale));
        d.sizeUnits=Vector3Int.RoundToInt(Vector3.Scale(d.sizeUnits,scale));
        EditorUtility.SetDirty(d);return true;
    }
    private static Mesh Mesh(Mesh source,Vector3 scale,string path)
    {
        using var snapshot=MeshUtility.AcquireReadOnlyMeshData(source);var data=snapshot[0];
        using var positions=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp);
        using var normals=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp);
        using var uv=new NativeArray<Vector2>(data.vertexCount,Allocator.Temp);
        data.GetVertices(positions);bool hasNormals=data.HasVertexAttribute(VertexAttribute.Normal);
        if(hasNormals)data.GetNormals(normals);
        var ps=positions.ToArray();var ns=normals.ToArray();
        for(int i=0;i<ps.Length;i++){ps[i]=Vector3.Scale(ps[i],scale);if(hasNormals){var n=ns[i];ns[i]=new Vector3(n.x/scale.x,n.y/scale.y,n.z/scale.z).normalized;}}
        var mesh=new Mesh{name=source.name+" modular dimensions",indexFormat=source.indexFormat};mesh.vertices=ps;if(hasNormals)mesh.normals=ns;
        for(int channel=0;channel<2;channel++)if(data.HasVertexAttribute(channel==0?VertexAttribute.TexCoord0:VertexAttribute.TexCoord1)){data.GetUVs(channel,uv);mesh.SetUVs(channel,uv);}
        mesh.subMeshCount=data.subMeshCount;
        for(int sub=0;sub<data.subMeshCount;sub++){using var indices=new NativeArray<int>(data.GetSubMesh(sub).indexCount,Allocator.Temp);data.GetIndices(indices,sub);mesh.SetIndices(indices.ToArray(),MeshTopology.Triangles,sub);}
        mesh.RecalculateBounds();if(!hasNormals)mesh.RecalculateNormals();if(data.HasVertexAttribute(VertexAttribute.TexCoord0))mesh.RecalculateTangents();
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved==null){saved=mesh;AssetDatabase.CreateAsset(saved,path);}else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}return saved;
    }
    public static void SyncPrefab(BuildDefinition d)
    {
        string path=AssetDatabase.GetAssetPath(d.authoringPrefab);if(string.IsNullOrEmpty(path))return;
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            root.GetComponent<MeshFilter>().sharedMesh=d.mesh;
            foreach(var old in root.GetComponents<Collider>())Object.DestroyImmediate(old);
            if(d.kind==BuildPartKind.Stair && d.collisionMesh!=null){var col=root.AddComponent<MeshCollider>();col.sharedMesh=d.collisionMesh;col.convex=true;}
            else if(d.occupiedVolumes?.Length>0)foreach(var volume in d.occupiedVolumes){var col=root.AddComponent<MeshCollider>();col.sharedMesh=volume.mesh;col.convex=true;}
            else foreach(var b in d.solidBoxes?.Length>0?d.solidBoxes:new[]{d.LocalBounds}){var col=root.AddComponent<BoxCollider>();col.center=b.center;col.size=b.size;}
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    [MenuItem("Tools/Building/Apply Interaction Dimensions")]
    public static void ApplyInstalled()
    {
        var catalog=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");int count=0;
        foreach(var d in catalog.presets)
        {
            if(Apply(d)){SyncPrefab(d);BuildStoneUiAuthoring.BakeThumbnail(d);count++;}
            if(d.flipVariant!=null && Apply(d.flipVariant)){SyncPrefab(d.flipVariant);BuildStoneUiAuthoring.BakeThumbnail(d.flipVariant);count++;}
        }
        AssetDatabase.SaveAssets();Debug.Log("INTERACTION DIMENSIONS APPLIED: "+count);
    }
    public static void ApplyBatch(){try{ApplyInstalled();EditorApplication.Exit(0);}catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}}
}
