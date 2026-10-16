using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Resident geometry references, indexed in world space. Publication, not camera visibility, changes ownership.</summary>
public static class LocalFlameGeometry
{
    public interface IProvider { void CollectFlameGeometry(Vector3 position,float range,List<Part> output); }
    static readonly HashSet<IProvider> providers=new();
    public static void RegisterProvider(IProvider provider)=>providers.Add(provider);
    public static void RemoveProvider(IProvider provider)
    {if(providers.Remove(provider))Changed?.Invoke(new Bounds(Vector3.zero,Vector3.one*100000000));}
    public static void NotifyChanged(Bounds bounds)=>Changed?.Invoke(bounds);
    public static void CollectProviders(Vector3 position,float range,List<Part> output)
    {output.Clear();foreach(var provider in providers)provider.CollectFlameGeometry(position,range,output);}
    public readonly struct Part
    {
        public readonly Mesh Mesh;
        public readonly Matrix4x4 Matrix;
        public readonly Bounds Bounds;
        public readonly int Submesh;
        public readonly Renderer Renderer;
        public readonly Material Material;
        public Part(Mesh mesh, Matrix4x4 matrix, Material material=null, int submesh=0, Renderer renderer=null)
        { Mesh=mesh;Matrix=matrix;Material=material;Submesh=submesh;Renderer=renderer;
          Bounds=renderer!=null?renderer.bounds:BuildRenderer.TransformBounds(mesh.bounds,matrix); }
        public bool Same(Part p)=>Mesh==p.Mesh && Matrix==p.Matrix && Material==p.Material && Submesh==p.Submesh && Renderer==p.Renderer;
    }
    public sealed class Group
    {
        public object Owner;
        public Part[] Parts;
        public Bounds Bounds;
        public bool Dynamic;
        internal int QueryStamp;
    }
    const float CellSize=16;
    static readonly Dictionary<object,Group> groups=new();
    static readonly Dictionary<Vector3Int,HashSet<Group>> cells=new();
    static int queryStamp;
    public static event Action<Bounds> Changed;
    public static int Count=>groups.Count;
    public static Vector3Int Cell(Vector3 p)=>new(Mathf.FloorToInt(p.x/CellSize),Mathf.FloorToInt(p.y/CellSize),Mathf.FloorToInt(p.z/CellSize));

    public static void Set(object owner,IReadOnlyList<Part> parts,bool dynamic=false)
    {
        if(parts.Count==0){Remove(owner);return;}
        groups.TryGetValue(owner,out var group);
        if(group!=null && group.Dynamic==dynamic && group.Parts.Length==parts.Count)
        {
            bool same=true;
            for(int i=0;i<parts.Count;i++)if(!group.Parts[i].Same(parts[i])){same=false;break;}
            if(same)return;
        }
        Bounds bounds=parts[0].Bounds;for(int i=1;i<parts.Count;i++)bounds.Encapsulate(parts[i].Bounds);
        if(group!=null)
        {
            Index(group,false);
            if(!group.Dynamic)Changed?.Invoke(group.Bounds);
        }
        else {group=new Group{Owner=owner};groups.Add(owner,group);}
        if(group.Parts==null || group.Parts.Length!=parts.Count)group.Parts=new Part[parts.Count];
        for(int i=0;i<parts.Count;i++)group.Parts[i]=parts[i];
        group.Bounds=bounds;group.Dynamic=dynamic;Index(group,true);
        if(!dynamic)Changed?.Invoke(bounds);
    }
    public static void Remove(object owner)
    {
        if(owner==null || !groups.Remove(owner,out var group))return;
        Index(group,false);if(!group.Dynamic)Changed?.Invoke(group.Bounds);
    }
    static void Index(Group group,bool add)
    {
        var min=Cell(group.Bounds.min);var max=Cell(group.Bounds.max);
        for(int x=min.x;x<=max.x;x++)for(int y=min.y;y<=max.y;y++)for(int z=min.z;z<=max.z;z++)
        {
            var cell=new Vector3Int(x,y,z);
            if(add)
            {if(!cells.TryGetValue(cell,out var bucket))cells.Add(cell,bucket=new());bucket.Add(group);}
            else if(cells.TryGetValue(cell,out var bucket)){bucket.Remove(group);if(bucket.Count==0)cells.Remove(cell);}
        }
    }
    public static void Query(Vector3 position,float radius,List<Group> output,bool dynamic)
    {
        output.Clear();queryStamp++;
        var min=Cell(position-Vector3.one*radius);var max=Cell(position+Vector3.one*radius);
        for(int x=min.x;x<=max.x;x++)for(int y=min.y;y<=max.y;y++)for(int z=min.z;z<=max.z;z++)
            if(cells.TryGetValue(new(x,y,z),out var bucket))foreach(var group in bucket)
            {
                if(group.QueryStamp==queryStamp)continue;group.QueryStamp=queryStamp;
                if(group.Dynamic==dynamic && group.Bounds.SqrDistance(position)<=radius*radius)output.Add(group);
            }
    }
    public static void Reset(){groups.Clear();cells.Clear();providers.Clear();Changed=null;queryStamp=0;}
}

/// <summary>Add to spawned doors/actors/props. Static meshes notify only on geometry/pose changes; skinned actors remain dynamic.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(400)]
public sealed class LocalFlameOccluder : MonoBehaviour
{
    public bool dynamicOccluder;
    Renderer target;
    MeshFilter filter;
    Matrix4x4 lastMatrix;
    Mesh lastMesh;
    bool dirty=true,lastDynamic;
    MaterialPropertyBlock receiverProperties;
    bool receiverOverride;
    readonly List<LocalFlameGeometry.Part> parts=new();
    void OnEnable(){dirty=true;target=GetComponent<Renderer>();filter=GetComponent<MeshFilter>();Refresh();}
    void LateUpdate(){Refresh();}
    public void GeometryChanged(){LocalFlameGeometry.Remove(this);dirty=true;Refresh();}
    void Refresh()
    {
        if(target==null){target=GetComponent<Renderer>();filter=GetComponent<MeshFilter>();}
        if(target==null || !target.enabled || target.shadowCastingMode==ShadowCastingMode.Off){LocalFlameGeometry.Remove(this);dirty=true;return;}
        Mesh mesh=filter!=null?filter.sharedMesh:(target as SkinnedMeshRenderer)?.sharedMesh;
        if(mesh==null)return;
        var matrix=transform.localToWorldMatrix;bool dynamic=dynamicOccluder || target is SkinnedMeshRenderer;
        if(!dirty && lastMatrix==matrix && lastMesh==mesh && lastDynamic==dynamic)return;
        if((dynamic || receiverOverride) && (dirty || lastDynamic!=dynamic))
        {
            receiverProperties??=new MaterialPropertyBlock();target.GetPropertyBlock(receiverProperties);
            receiverProperties.SetFloat("_FSFlameDynamicReceiver",dynamic?1:0);target.SetPropertyBlock(receiverProperties);
            receiverOverride=dynamic;
        }
        lastMatrix=matrix;lastMesh=mesh;lastDynamic=dynamic;dirty=false;
        parts.Clear();
        // Cache material references; sharedMaterials would allocate every update.
        target.GetSharedMaterials(materials);
        for(int i=0;i<mesh.subMeshCount;i++)
        {
            var material=materials.Count>0?materials[Mathf.Min(i,materials.Count-1)]:null;
            if(material!=null && material.renderQueue>2500)continue;
            parts.Add(new(mesh,matrix,material,i,target is SkinnedMeshRenderer?target:null));
        }
        LocalFlameGeometry.Set(this,parts,dynamic);
    }
    readonly List<Material> materials=new();
    void OnDisable(){LocalFlameGeometry.Remove(this);dirty=true;}
}
