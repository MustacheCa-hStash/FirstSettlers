using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Bounded direct lights with persistent radial-depth shadows and independent dynamic occluders.</summary>
[DefaultExecutionOrder(500)]
public sealed class LocalFlameLighting : MonoBehaviour
{
    public const int Capacity=64, Resolution=256, DynamicResolution=64, VisualCapacity=128;
    public const long AtlasBytes=(long)Capacity*6*(Resolution*Resolution+DynamicResolution*DynamicResolution)*4;
    public static LocalFlameLighting Instance { get; private set; }
    public bool cacheStationary=true;
    [Tooltip("Experimental surface irradiance cache. The spatial direct path is the measured default.")]
    public bool useSurfaceCache=false;
    public bool adaptiveSurfaceCache=true;
    [Range(1,8)] public int surfacePagesPerFrame=2;
    public LocalFlameSurfaceCache SurfaceCache { get; private set; }
    public int CoverageSuppressed { get; private set; }
    public long AllocatedGpuBytes=>initialized?AtlasBytes+(SurfaceCache!=null?LocalFlameSurfaceCache.Bytes:0):0;
    [Range(1,Capacity)] public int maxActiveLights=Capacity;
    [Range(1,Capacity)] public int staticRefreshesPerFrame=1;
    public Camera targetCamera;
    public bool automaticRendering=true;
    public int ActiveLights { get; private set; }
    public int StaticRefreshes { get; private set; }
    public int DynamicRefreshes { get; private set; }
    public int PendingRefreshes { get; private set; }
    public int ShadowDraws { get; private set; }
    public int TotalStaticRefreshes { get; private set; }
    public double LastCpuMs { get; private set; }
    public long LastAllocatedBytes { get; private set; }
    public int RegisteredSources { get; private set; }
    public int GeometryGroups=>LocalFlameGeometry.Count;
    public RenderTexture StaticAtlas=>staticAtlas;
    static readonly ProfilerMarker UpdateMarker=new("FS.Flame.Update"),StaticMarker=new("FS.Flame.StaticShadow"),DynamicMarker=new("FS.Flame.DynamicShadow");
    sealed class Slot
    {
        public LocalFlameSource Source;
        public bool Dirty=true,Valid,HadDynamic,Selected;
        public Vector3 Position;
        public float Range;
        public float Fade;
        public Vector4 Average;
    }
    readonly Slot[] slots=new Slot[Capacity];
    readonly Dictionary<Vector3Int,HashSet<LocalFlameSource>> sources=new();
    readonly List<LocalFlameSource> candidates=new(64);
    readonly List<float> scores=new(64);
    readonly List<LocalFlameGeometry.Group> stationary=new(128),dynamicGroups=new(64);
    readonly List<LocalFlameGeometry.Part> providerParts=new(256);
    readonly Plane[] planes=new Plane[6],facePlanes=new Plane[6];
    readonly Vector4[] positions=new Vector4[Capacity],colors=new Vector4[Capacity],averages=new Vector4[Capacity];
    readonly LocalFlameSpatialGrid grid=new();
    readonly Matrix4x4[] shadowMatrices=new Matrix4x4[Capacity*6];
    MaterialPropertyBlock casterProperties;
    static readonly Vector3[] Directions={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
    static readonly Vector3[] Ups={Vector3.down,Vector3.down,Vector3.forward,Vector3.back,Vector3.down,Vector3.down};
    Material depthMaterial;
    RenderTexture staticAtlas,dynamicAtlas;
    CommandBuffer commands;
    float nextSelection;
    int lastFrame=-1;
    int sourceState;
    bool initialized;
    static readonly int CountId=Shader.PropertyToID("_FSFlameCount"),PositionsId=Shader.PropertyToID("_FSFlamePositions"),ColorsId=Shader.PropertyToID("_FSFlameColors"),MatricesId=Shader.PropertyToID("_FSFlameShadowVP");
    static readonly int CaptureMatrixId=Shader.PropertyToID("_FSFlameCaptureVP"),CaptureLightId=Shader.PropertyToID("_FSFlameCaptureLight");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics(){Instance=null;LocalFlameGeometry.Reset();Shader.SetGlobalInt(CountId,0);Shader.SetGlobalInt("_FSSurfaceEnabled",0);}
    public static LocalFlameLighting Ensure()
    {
        if(Instance==null)new GameObject("Local flame lighting").AddComponent<LocalFlameLighting>().Initialize();
        return Instance;
    }
    void Awake()=>Initialize();
    void Initialize()
    {
        if(Instance==this)return;
        if(Instance!=null && Instance!=this){Destroy(this);return;}
        Instance=this;for(int i=0;i<Capacity;i++)slots[i]=new Slot();
        casterProperties=new MaterialPropertyBlock();
        LocalFlameGeometry.Changed+=Invalidate;
        LocalFlameGeometry.RefreshRequested+=Refresh;
        RenderPipelineManager.beginCameraRendering+=BeginCamera;
        SceneManager.sceneLoaded+=SceneLoaded;
    }
    void Start()=>DiscoverSceneOccluders();
    void SceneLoaded(Scene scene,LoadSceneMode mode){nextSelection=0;DiscoverSceneOccluders();}
    static void DiscoverSceneOccluders()
    {
        // One discovery per scene, never a per-frame renderer search. Runtime spawners use LocalFlameOccluder.
        foreach(var renderer in FindObjectsByType<Renderer>())
        {
            if(renderer is not (MeshRenderer or SkinnedMeshRenderer) || renderer.shadowCastingMode==ShadowCastingMode.Off ||
               renderer.sharedMaterial==null || renderer.sharedMaterial.renderQueue>2500 || renderer.GetComponent<LocalFlameOccluder>()!=null)continue;
            var occluder=renderer.gameObject.AddComponent<LocalFlameOccluder>();
            occluder.dynamicOccluder=renderer is SkinnedMeshRenderer || renderer.GetComponentInParent<Rigidbody>()!=null || renderer.GetComponentInParent<Animator>()!=null || renderer.GetComponentInParent<LocalFlameSource>()?.stationary==false;
            occluder.GeometryChanged();
        }
    }
    public void Register(LocalFlameSource source)
    {
        if(!sources.TryGetValue(source.Cell,out var bucket))sources.Add(source.Cell,bucket=new());
        if(bucket.Add(source))RegisteredSources++;
        nextSelection=0;
    }
    public void Move(LocalFlameSource source,Vector3Int oldCell,Vector3Int newCell)
    {
        if(sources.TryGetValue(oldCell,out var bucket)){bucket.Remove(source);if(bucket.Count==0)sources.Remove(oldCell);}
        if(!sources.TryGetValue(newCell,out bucket))sources.Add(newCell,bucket=new());bucket.Add(source);nextSelection=0;
    }
    public void Unregister(LocalFlameSource source)
    {
        if(sources.TryGetValue(source.Cell,out var bucket)){if(bucket.Remove(source))RegisteredSources--;if(bucket.Count==0)sources.Remove(source.Cell);}
        for(int i=0;i<Capacity;i++)if(slots[i].Source==source){if(slots[i].Average.w>.5f)SurfaceCache?.Invalidate(new Bounds(slots[i].Position,Vector3.one*slots[i].Range*2));slots[i].Source=null;slots[i].Valid=false;}
        nextSelection=0;
    }
    public void Invalidate(Bounds changed)
        => MarkDirty(changed,false);
    void Refresh(Bounds changed) => MarkDirty(changed,true);
    void MarkDirty(Bounds changed,bool retainCachedShadow)
    {
        SurfaceCache?.GeometryChanged(changed);
        for(int i=0;i<Capacity;i++)
        {
            var s=slots[i];if(s.Source==null)continue;
            if(changed.SqrDistance(s.Source.Position)<=s.Source.range*s.Source.range)
            {s.Dirty=true;s.Valid &= retainCachedShadow;SurfaceCache?.Invalidate(new Bounds(s.Source.Position,Vector3.one*s.Source.range*2));}
        }
    }
    public void InvalidateAll(){for(int i=0;i<Capacity;i++){slots[i].Dirty=true;slots[i].Valid=false;}SurfaceCache?.InvalidateAll();}
    public bool IsCached(LocalFlameSource source)
    {for(int i=0;i<Capacity;i++)if(slots[i].Source==source)return slots[i].Valid;return false;}
    void Select(Camera camera)
    {
        foreach(var source in candidates)if(source!=null)source.FlameVisible=false;
        candidates.Clear();scores.Clear();GeometryUtility.CalculateFrustumPlanes(camera,planes);
        Vector3 center=camera.transform.position;
        var min=LocalFlameGeometry.Cell(center-Vector3.one*112);var max=LocalFlameGeometry.Cell(center+Vector3.one*112);
        for(int x=min.x;x<=max.x;x++)for(int y=min.y;y<=max.y;y++)for(int z=min.z;z<=max.z;z++)
            if(sources.TryGetValue(new(x,y,z),out var bucket))foreach(var source in bucket)
            {
                if(source==null || !source.isActiveAndEnabled || !source.burning)continue;
                var p=source.Position;float distance=(p-center).sqrMagnitude;if(distance>112*112)continue;
                bool relevant=GeometryUtility.TestPlanesAABB(planes,new Bounds(p,Vector3.one*source.range*2));
                float score=distance+(relevant?0:2000);
                if(!source.stationary)score-=10000;
                for(int i=0;i<Capacity;i++)if(slots[i].Source==source){score-=4;break;}
                int insert=scores.BinarySearch(score);if(insert<0)insert=~insert;
                if(insert>=VisualCapacity)continue;
                candidates.Insert(insert,source);scores.Insert(insert,score);
                if(candidates.Count>VisualCapacity){candidates.RemoveAt(VisualCapacity);scores.RemoveAt(VisualCapacity);}
            }
        int count=Mathf.Min(maxActiveLights,candidates.Count);
        foreach(var source in candidates)source.FlameVisible=true;
        for(int i=0;i<Capacity;i++)
        {
            var s=slots[i];if(s.Source==null)continue;
            if(!s.Source.burning){if(s.Average.w>.5f)SurfaceCache?.Invalidate(new Bounds(s.Position,Vector3.one*s.Range*2));s.Source.HasLight=false;s.Source=null;s.Valid=false;s.Fade=0;continue;}
            int index=candidates.IndexOf(s.Source);
            s.Selected=index>=0 && index<count;
            if(!s.Selected){if(s.Average.w>.5f)SurfaceCache?.Invalidate(new Bounds(s.Position,Vector3.one*s.Range*2));s.Source.HasLight=false;s.Source=null;s.Valid=false;s.Fade=0;}
        }
        for(int i=0;i<count;i++)
        {
            var source=candidates[i];bool found=false;
            for(int j=0;j<Capacity;j++)if(slots[j].Source==source){found=true;break;}
            if(found)continue;
            for(int j=0;j<Capacity;j++)if(slots[j].Source==null)
            {slots[j].Source=source;slots[j].Dirty=true;slots[j].Valid=false;slots[j].HadDynamic=true;slots[j].Selected=true;slots[j].Fade=0;break;}
        }
    }
    bool Allocate()
    {
        if(initialized)return true;
        var shader=Resources.Load<Shader>("Lighting/FlameShadowDepth");
        if(shader==null || !shader.isSupported || !SystemInfo.supports2DArrayTextures ||
           !SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat,GraphicsFormatUsage.Blend))return false;
        depthMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
        staticAtlas=Atlas("Flame stationary depth",Resolution);dynamicAtlas=Atlas("Flame moving depth",DynamicResolution);
        commands=new CommandBuffer{name="FS.Flame.Shadows"};initialized=true;return true;
    }
    static RenderTexture Atlas(string name,int resolution)
    {
        var texture=new RenderTexture(new RenderTextureDescriptor(resolution,resolution)
        {graphicsFormat=GraphicsFormat.R32_SFloat,depthBufferBits=0,dimension=TextureDimension.Tex2DArray,volumeDepth=Capacity*6,msaaSamples=1,mipCount=1})
        {name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
        texture.Create();return texture;
    }
    void BeginCamera(ScriptableRenderContext context,Camera camera)
    {
        if(!automaticRendering)return;
        if(targetCamera==null)targetCamera=Camera.main;
        if(camera!=targetCamera || camera.cameraType==CameraType.Preview || lastFrame==Time.frameCount)return;
        lastFrame=Time.frameCount;RenderForCamera(camera);
    }
    /// <summary>Also used by the isolated render/benchmark fixture. Must run before drawing receivers.</summary>
    public void RenderForCamera(Camera camera,float deltaTime=-1)
    {
        long start=Stopwatch.GetTimestamp(),allocation=GC.GetAllocatedBytesForCurrentThread();
        using var scope=UpdateMarker.Auto();
        StaticRefreshes=DynamicRefreshes=PendingRefreshes=ShadowDraws=ActiveLights=CoverageSuppressed=0;
        int state=17;
        unchecked {foreach(var bucket in sources.Values)foreach(var source in bucket)if(source!=null)state=state*31+(source.burning?1:0);}
        if(state!=sourceState){sourceState=state;nextSelection=0;}
        if(Time.unscaledTime>=nextSelection){Select(camera);nextSelection=Time.unscaledTime+.1f;}
        if(RegisteredSources==0 || !Allocate())
        {Shader.SetGlobalInt(CountId,0);Shader.SetGlobalInt("_FSSurfaceEnabled",0);LastCpuMs=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;LastAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocation;return;}
        commands.Clear();
        int admission=staticRefreshesPerFrame;
        int averageOnlySources=0;
        for(int i=0;i<Capacity;i++)
        {
            var s=slots[i];positions[i]=colors[i]=averages[i]=Vector4.zero;
            if(s.Source==null)continue;
            if(!s.Source.burning){SurfaceCache?.Invalidate(new Bounds(s.Position,Vector3.one*s.Range*2));s.Source.HasLight=false;nextSelection=0;continue;}
            var source=s.Source;Vector3 position=source.Position;float range=Mathf.Clamp(source.range,.5f,12);
            if(source.coverageWorld!=null && source.coverageWorld.TerrainGuard is ITerrainLightGuard lightGuard)
            {
                if(!lightGuard.CanIlluminate(new Unity.Mathematics.double3(position.x,position.y,position.z),range))
                {SurfaceCache?.Invalidate(new Bounds(s.Position,Vector3.one*s.Range*2));s.Dirty=true;s.Valid=false;source.HasLight=false;PendingRefreshes++;CoverageSuppressed++;continue;}
            }
            if(position!=s.Position || range!=s.Range){if(source.stationary)SurfaceCache?.Invalidate(new Bounds(s.Position,Vector3.one*s.Range*2));s.Position=position;s.Range=range;s.Dirty=true;s.Valid=false;}
            Color average=source.color.linear*source.intensity;
            var nextAverage=new Vector4(average.r,average.g,average.b,source.stationary?1:0);
            if(s.Average!=nextAverage){if(s.Average.w>.5f||nextAverage.w>.5f)SurfaceCache?.Invalidate(new Bounds(position,Vector3.one*range*2));s.Average=nextAverage;}
            bool moving=!cacheStationary || !source.stationary;
            if(moving || s.Dirty)
            {
                if(moving || admission>0)
                {
                    using var marker=StaticMarker.Auto();
                    LocalFlameGeometry.Query(position,range,stationary,false);
                    LocalFlameGeometry.CollectProviders(position,range,providerParts);
                    Capture(i,staticAtlas,stationary,true);s.Dirty=false;s.Valid=true;
                    StaticRefreshes++;TotalStaticRefreshes++;if(!moving)admission--;
                    if(source.stationary)SurfaceCache?.Invalidate(new Bounds(position,Vector3.one*range*2));
                }
                else
                {
                    PendingRefreshes++;
                    // LOD-only changes keep the last valid shadow until this
                    // slot's turn. New geometry/edits still suppress stale light.
                    if(!s.Valid){source.HasLight=false;continue;}
                }
            }
            LocalFlameGeometry.Query(position,range,dynamicGroups,true);
            if(dynamicGroups.Count>0 || s.HadDynamic)
            {
                using var marker=DynamicMarker.Auto();Capture(i,dynamicAtlas,dynamicGroups,false);
                DynamicRefreshes++;s.HadDynamic=dynamicGroups.Count>0;
            }
            s.Fade=Mathf.MoveTowards(s.Fade,s.Selected?1:0,(deltaTime>=0?deltaTime:Time.unscaledDeltaTime>0?Time.unscaledDeltaTime:.016f)*5);
            if(s.Fade<=0 && !s.Selected){source.HasLight=false;s.Source=null;s.Valid=false;continue;}
            positions[i]=new Vector4(position.x,position.y,position.z,range);
            float flickerWeight=source.stationary?Mathf.Clamp01((20-Vector3.Distance(position,camera.transform.position))/8):1;
            Color color=average*Mathf.Lerp(1,source.FlameStrength(Time.time),flickerWeight)*s.Fade;
            colors[i]=new Vector4(color.r,color.g,color.b,s.Valid?1:0);
            averages[i]=new Vector4(average.r,average.g,average.b,source.stationary?1:0);
            if(!source.stationary || dynamicGroups.Count>0 || source.flicker>0 && flickerWeight>0)colors[i].w=2;
            if(dynamicGroups.Count>0 || !source.stationary)colors[i].w=3;
            if(colors[i].w<1.5f && source.stationary)averageOnlySources++;
            source.HasLight=s.Valid;ActiveLights++;
        }
        Shader.SetGlobalTexture("_FSFlameStaticDepth",staticAtlas);Shader.SetGlobalTexture("_FSFlameDynamicDepth",dynamicAtlas);
        Shader.SetGlobalVectorArray(PositionsId,positions);Shader.SetGlobalVectorArray(ColorsId,colors);
        Shader.SetGlobalVectorArray("_FSFlameAverageColors",averages);
        Shader.SetGlobalInt("_FSFlameAverageOnlyCount",averageOnlySources);Shader.SetGlobalInt("_FSSurfaceAdaptive",adaptiveSurfaceCache?1:0);
        Shader.SetGlobalMatrixArray(MatricesId,shadowMatrices);Shader.SetGlobalInt(CountId,Capacity);
        grid.Update(positions,colors);grid.Bind();
        Shader.SetGlobalInt("_FSSurfaceEnabled",0);
        if(useSurfaceCache && cacheStationary)
        {
            SurfaceCache??=new LocalFlameSurfaceCache();
            SurfaceCache.Update(camera,positions,colors,averages,commands,surfacePagesPerFrame);
        }
        Graphics.ExecuteCommandBuffer(commands);
        if(useSurfaceCache && cacheStationary)SurfaceCache.Bind();
        LastCpuMs=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
        LastAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocation;
    }
    void Capture(int slot,RenderTexture atlas,List<LocalFlameGeometry.Group> groups,bool matrices)
    {
        var s=slots[slot];var projection=Matrix4x4.Perspective(90,1,.015f,s.Range);
        commands.BeginSample(matrices?"FS.Flame.StaticShadowGPU":"FS.Flame.DynamicShadowGPU");
        for(int face=0;face<6;face++)
        {
            Matrix4x4 view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(s.Position,Quaternion.LookRotation(Directions[face],Ups[face]),Vector3.one).inverse;
            Matrix4x4 vp=GL.GetGPUProjectionMatrix(projection,true)*view;
            if(matrices)shadowMatrices[slot*6+face]=vp;
            GeometryUtility.CalculateFrustumPlanes(projection*view,facePlanes);
            commands.SetRenderTarget(atlas,0,CubemapFace.Unknown,slot*6+face);
            commands.SetViewport(new Rect(0,0,atlas.width,atlas.width));
            commands.ClearRenderTarget(false,true,Color.white);
            commands.SetGlobalMatrix(CaptureMatrixId,vp);
            commands.SetGlobalVector(CaptureLightId,new Vector4(s.Position.x,s.Position.y,s.Position.z,1/s.Range));
            foreach(var group in groups)
            {
                if(ReferenceEquals(group.Owner,s.Source.ignoredGeometryOwner) ||
                    s.Source.ignoredCasterRoot!=null && group.Owner is LocalFlameOccluder own && own.transform.IsChildOf(s.Source.ignoredCasterRoot))continue;
                foreach(var part in group.Parts)DrawPart(part);
            }
            if(matrices)foreach(var part in providerParts)DrawPart(part);
        }
        commands.EndSample(matrices?"FS.Flame.StaticShadowGPU":"FS.Flame.DynamicShadowGPU");
    }
    void DrawPart(LocalFlameGeometry.Part part)
    {
        if(part.Mesh==null || !GeometryUtility.TestPlanesAABB(facePlanes,part.Bounds))return;
        casterProperties.Clear();var material=part.Material;
        bool cutout=material!=null && (material.IsKeywordEnabled("_ALPHATEST_ON") || material.renderQueue>=2450 && material.HasProperty("_Cutoff"));
        casterProperties.SetFloat("_CasterCutoff",cutout?(material.HasProperty("_Cutoff")?material.GetFloat("_Cutoff"):.5f):0);
        casterProperties.SetTexture("_CasterBaseMap",cutout && material.HasProperty("_BaseMap")?material.GetTexture("_BaseMap")??Texture2D.whiteTexture:Texture2D.whiteTexture);
        casterProperties.SetVector("_CasterBaseMap_ST",material!=null && material.HasProperty("_BaseMap")?new Vector4(material.GetTextureScale("_BaseMap").x,material.GetTextureScale("_BaseMap").y,material.GetTextureOffset("_BaseMap").x,material.GetTextureOffset("_BaseMap").y):new Vector4(1,1,0,0));
        if(part.Renderer!=null)commands.DrawRenderer(part.Renderer,depthMaterial,part.Submesh,0);
        else commands.DrawMesh(part.Mesh,part.Matrix,depthMaterial,part.Submesh,0,casterProperties);
        ShadowDraws++;
    }
    void OnDestroy()
    {
        if(Instance!=this)return;
        RenderPipelineManager.beginCameraRendering-=BeginCamera;SceneManager.sceneLoaded-=SceneLoaded;
        LocalFlameGeometry.Changed-=Invalidate;Shader.SetGlobalInt(CountId,0);
        LocalFlameGeometry.RefreshRequested-=Refresh;
        commands?.Release();CoreUtils.Destroy(depthMaterial);
        SurfaceCache?.Dispose();grid.Dispose();Shader.SetGlobalInt("_FSSurfaceEnabled",0);
        if(staticAtlas!=null){staticAtlas.Release();CoreUtils.Destroy(staticAtlas);}
        if(dynamicAtlas!=null){dynamicAtlas.Release();CoreUtils.Destroy(dynamicAtlas);}
        Instance=null;
    }
}
