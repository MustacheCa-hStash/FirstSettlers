using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>World-aligned surface patches. Stores diffuse irradiance, not material colour or a volume of ambient light.</summary>
public sealed class LocalFlameSurfaceCache : IDisposable
{
    public const int Capacity=192,Resolution=32,TableSize=1024;
    public const float CellSize=8;
    public const long Bytes=2L*Capacity*6*(1024+256+64+16)*8+2L*(1024+256+64+16)*8+16*16*8+Resolution*Resolution*12+TableSize*16;
    public int ResidentPages { get; private set; }
    public int ReadyPages { get; private set; }
    public int UpdatedPages { get; private set; }
    public int TotalUpdates { get; private set; }
    public int GeometryCaptures { get; private set; }
    sealed class Page { public Vector3Int Cell;public bool Used,Dirty=true,GeometryDirty=true; }
    struct Candidate : IComparable<Candidate>
    { public Vector3Int Cell;public float Score;public int CompareTo(Candidate other){int c=Score.CompareTo(other.Score);if(c!=0)return c;c=Cell.x.CompareTo(other.Cell.x);if(c!=0)return c;c=Cell.y.CompareTo(other.Cell.y);return c!=0?c:Cell.z.CompareTo(other.Cell.z);} }
    readonly Page[] pages=new Page[Capacity];
    readonly Dictionary<Vector3Int,int> resident=new();
    readonly HashSet<Vector3Int> wanted=new();
    readonly List<Candidate> candidates=new();
    readonly List<LocalFlameGeometry.Group> geometry=new();
    readonly LocalFlameSpatialGrid.Entry[] table=new LocalFlameSpatialGrid.Entry[TableSize];
    readonly Vector3[] directions={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
    readonly Vector3[] ups={Vector3.down,Vector3.down,Vector3.forward,Vector3.back,Vector3.down,Vector3.down};
    readonly Material material;
    readonly RenderTexture normals,irradiance,capture,mipNormals,mipIrradiance,mipOutput;
    readonly ComputeBuffer lookup;
    float nextSelection;
    bool lookupDirty=true;
    public RenderTexture Irradiance=>irradiance;
    public RenderTexture NormalDepth=>normals;
    public LocalFlameSurfaceCache()
    {
        material=CoreUtils.CreateEngineMaterial(Resources.Load<Shader>("Lighting/FlameSurfaceCache"));
        normals=ArrayTexture("Flame surface normal + depth");irradiance=ArrayTexture("Flame surface irradiance");
        mipNormals=MipTexture("Flame surface mip normal scratch");mipIrradiance=MipTexture("Flame surface mip irradiance scratch");
        mipOutput=new RenderTexture(16,16,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);mipOutput.Create();
        capture=new RenderTexture(Resolution,Resolution,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){name="Flame surface capture",filterMode=FilterMode.Point};capture.Create();
        lookup=new ComputeBuffer(TableSize,16);
        for(int i=0;i<pages.Length;i++)pages[i]=new Page();
    }
    static RenderTexture ArrayTexture(string name)
    {
        var descriptor=new RenderTextureDescriptor(Resolution,Resolution,RenderTextureFormat.ARGBHalf,0){dimension=TextureDimension.Tex2DArray,volumeDepth=Capacity*6,useMipMap=true,autoGenerateMips=false,mipCount=4};
        var t=new RenderTexture(descriptor){name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};t.Create();return t;
    }
    static RenderTexture MipTexture(string name)
    {
        var t=new RenderTexture(new RenderTextureDescriptor(Resolution,Resolution,RenderTextureFormat.ARGBHalf,0){useMipMap=true,autoGenerateMips=false,mipCount=4}){name=name};t.Create();return t;
    }
    static Bounds Bounds(Vector3Int cell)=>new((Vector3)cell*CellSize+Vector3.one*(CellSize*.5f),Vector3.one*CellSize);
    public void Invalidate(Bounds influence)
    {nextSelection=0;foreach(var p in pages)if(p.Used&&Bounds(p.Cell).Intersects(influence)){p.Dirty=true;lookupDirty=true;}}
    public void GeometryChanged(Bounds changed)
    {foreach(var p in pages)if(p.Used&&Bounds(p.Cell).Intersects(changed)){p.Dirty=p.GeometryDirty=true;lookupDirty=true;}}
    public void InvalidateAll(){foreach(var p in pages)if(p.Used)p.Dirty=p.GeometryDirty=true;lookupDirty=true;nextSelection=0;}
    void Select(Camera camera,Vector4[] positions,Vector4[] colors,Vector4[] averages)
    {
        wanted.Clear();candidates.Clear();
        for(int i=0;i<positions.Length;i++)if(colors[i].w>.5f && averages[i].w>.5f)
        {
            Vector3 pos=positions[i];float radius=positions[i].w;
            LocalFlameGeometry.Query(pos,radius,geometry,false);
            foreach(var group in geometry)
            {
                Vector3 lo=Vector3.Max(group.Bounds.min,pos-Vector3.one*radius),hi=Vector3.Min(group.Bounds.max,pos+Vector3.one*radius);
                var min=LocalFlameSpatialGrid.Cell(lo);var max=LocalFlameSpatialGrid.Cell(hi);
                for(int z=min.z;z<=max.z;z++)for(int y=min.y;y<=max.y;y++)for(int x=min.x;x<=max.x;x++)
                {
                    var cell=new Vector3Int(x,y,z);var bounds=Bounds(cell);
                    if(bounds.SqrDistance(pos)>radius*radius || !wanted.Add(cell))continue;
                    candidates.Add(new Candidate{Cell=cell,Score=bounds.SqrDistance(camera.transform.position)});
                }
            }
        }
        candidates.Sort();wanted.Clear();int selected=Math.Min(Capacity,candidates.Count);
        for(int i=0;i<selected;i++)wanted.Add(candidates[i].Cell);
        foreach(var p in pages)if(p.Used&&!wanted.Contains(p.Cell)){resident.Remove(p.Cell);p.Used=false;lookupDirty=true;}
        for(int i=0;i<selected;i++)
        {
            var cell=candidates[i].Cell;if(resident.ContainsKey(cell))continue;
            for(int j=0;j<pages.Length;j++)if(!pages[j].Used)
            {var p=pages[j];p.Cell=cell;p.Used=p.Dirty=p.GeometryDirty=true;resident.Add(cell,j);lookupDirty=true;break;}
        }
        ResidentPages=resident.Count;
    }
    public void Update(Camera camera,Vector4[] positions,Vector4[] colors,Vector4[] averages,CommandBuffer commands,int budget)
    {
        UpdatedPages=GeometryCaptures=0;
        if(Time.unscaledTime>=nextSelection || ResidentPages==0){Select(camera,positions,colors,averages);nextSelection=Time.unscaledTime+.5f;}
        commands.SetGlobalTexture("_FSSurfaceNormals",normals);
        commands.BeginSample("FS.Flame.SurfaceCacheGPU");
        for(int index=0;index<pages.Length && UpdatedPages<budget;index++)
        {
            var p=pages[index];if(!p.Used || !p.Dirty)continue;
            var bounds=Bounds(p.Cell);
            commands.SetGlobalVector("_FSSurfaceCenter",bounds.center);
            LocalFlameGeometry.Query(bounds.center,CellSize*.867f,geometry,false);
            for(int face=0;face<6;face++)
            {
                Vector3 direction=directions[face],up=ups[face],right=Vector3.Cross(up,-direction);
                commands.SetGlobalVector("_FSSurfaceRight",right);commands.SetGlobalVector("_FSSurfaceUp",up);commands.SetGlobalVector("_FSSurfaceDirection",direction);
                int slice=index*6+face;commands.SetGlobalInt("_FSSurfaceSlice",slice);
                if(p.GeometryDirty)
                {
                    var projection=Matrix4x4.Ortho(-4,4,-4,4,.001f,8.002f);
                    var view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(bounds.center+direction*4.001f,Quaternion.LookRotation(-direction,up),Vector3.one).inverse;
                    commands.SetGlobalMatrix("_FSSurfaceVP",GL.GetGPUProjectionMatrix(projection,true)*view);
                    commands.SetRenderTarget(capture);commands.SetViewport(new Rect(0,0,Resolution,Resolution));commands.ClearRenderTarget(true,true,Color.clear);
                    foreach(var group in geometry)foreach(var part in group.Parts)
                    {
                        if(part.Mesh==null || part.Renderer!=null || part.Material!=null && part.Material.renderQueue>=2450)continue;
                        commands.DrawMesh(part.Mesh,part.Matrix,material,part.Submesh,0);
                    }
                    commands.CopyTexture(capture,0,0,normals,slice,0);GeometryCaptures++;
                }
                commands.SetRenderTarget(irradiance,0,CubemapFace.Unknown,slice);commands.SetViewport(new Rect(0,0,Resolution,Resolution));
                commands.DrawProcedural(Matrix4x4.identity,material,1,MeshTopology.Triangles,3);
                for(int mip=1;mip<4;mip++)
                {
                    commands.CopyTexture(normals,slice,mip-1,mipNormals,0,mip-1);commands.CopyTexture(irradiance,slice,mip-1,mipIrradiance,0,mip-1);
                    commands.SetGlobalTexture("_FSSurfaceMipNormals",mipNormals);commands.SetGlobalTexture("_FSSurfaceMipIrradiance",mipIrradiance);commands.SetGlobalInt("_FSSurfaceReadMip",mip-1);
                    int size=Resolution>>mip;
                    commands.SetRenderTarget(mipOutput);commands.SetViewport(new Rect(0,0,size,size));
                    commands.SetGlobalInt("_FSSurfaceMipOutput",0);commands.DrawProcedural(Matrix4x4.identity,material,2,MeshTopology.Triangles,3);
                    commands.CopyTexture(mipOutput,0,0,0,0,size,size,normals,slice,mip,0,0);
                    commands.SetGlobalInt("_FSSurfaceMipOutput",1);commands.DrawProcedural(Matrix4x4.identity,material,2,MeshTopology.Triangles,3);
                    commands.CopyTexture(mipOutput,0,0,0,0,size,size,irradiance,slice,mip,0,0);
                }
            }
            p.Dirty=p.GeometryDirty=false;lookupDirty=true;UpdatedPages++;TotalUpdates++;
        }
        commands.EndSample("FS.Flame.SurfaceCacheGPU");
        ReadyPages=0;foreach(var p in pages)if(p.Used&&!p.Dirty)ReadyPages++;
        if(lookupDirty)
        {
            Array.Clear(table,0,table.Length);
            for(int i=0;i<pages.Length;i++)if(pages[i].Used&&!pages[i].Dirty)
            {
                var cell=pages[i].Cell;int bucket=(int)(LocalFlameSpatialGrid.Hash(cell)&(TableSize-1));
                while(table[bucket].w!=0)bucket=(bucket+1)&(TableSize-1);
                table[bucket]=new LocalFlameSpatialGrid.Entry(cell,i+1);
            }
            lookup.SetData(table);lookupDirty=false;
        }
    }
    public void Bind()
    {Shader.SetGlobalTexture("_FSSurfaceNormals",normals);Shader.SetGlobalTexture("_FSSurfaceIrradiance",irradiance);Shader.SetGlobalBuffer("_FSSurfacePages",lookup);Shader.SetGlobalInt("_FSSurfaceEnabled",1);}
    public void Dispose()
    {lookup.Release();CoreUtils.Destroy(material);foreach(var rt in new[]{normals,irradiance,capture,mipNormals,mipIrradiance,mipOutput}){rt.Release();CoreUtils.Destroy(rt);}}
}
