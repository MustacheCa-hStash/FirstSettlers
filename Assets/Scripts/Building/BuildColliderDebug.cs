using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum BuildPlacementFailure { None, Solid, Headroom, Joint, Support, External, Crowded, Reach }
public enum BuildPlacementWarning { None, RoofStairHeadroom, RoofStairOverlap, UnresolvedJoint, StairHeadroom, BuildOverlap }
public enum BuildColliderDebugMode { Off, Solids, SolidsAndClearance }

/// <summary>Game-view diagnostics. Draws physical proxies and the exact prospective placement shapes.</summary>
[DefaultExecutionOrder(300)]
[DisallowMultipleComponent]
public sealed class BuildColliderDebug : MonoBehaviour
{
    [SerializeField] private BuildColliderDebugMode mode;
    [SerializeField, Min(4)] private float range=24;
    private BuildWorld world;
    private Material material;
    private Mesh wireMesh;
    private readonly BuildDebugLines lines=new();
    private readonly List<BuildPieceRecord> pieces=new();
    private readonly List<Collider> colliders=new();
    private BuildPreview snapshot;
    private BuildColliderDebugMode capturedMode;
    private int revision=-1,activeCount=-1;
    private Vector3 capturedFocus;
    private Bounds capturedBlockerBounds;
    private Vector3? blockerPosition;
    private string blockerLabel;
    private GUIStyle panelStyle,labelStyle;
    public bool Frozen { get; private set; }
    public BuildColliderDebugMode Mode
    {
        get=>mode;
        set { mode=value;Frozen=false;revision=-1; }
    }
    public Mesh WireMesh=>wireMesh;
    public static readonly Color PhysicalColor=new(.1f,.75f,1,1);
    public static readonly Color InactiveColor=new(.45f,.5f,.55f,.6f);
    public static readonly Color FittedColor=new(1,.85f,.2f,1);
    public static readonly Color BlockerColor=new(1,.45f,.05f,1);
    public static readonly Color ValidColor=new(.2f,1,.3f,1);
    public static readonly Color InvalidColor=new(1,.15f,.2f,1);
    public static readonly Color ClearanceColor=new(.85f,.3f,1,.8f);
    public void CycleMode()=>Mode=(BuildColliderDebugMode)(((int)mode+1)%3);
    public void ToggleFreeze()
    {
        if(mode==BuildColliderDebugMode.Off)Mode=BuildColliderDebugMode.SolidsAndClearance;
        if(!Frozen)Rebuild();
        Frozen=!Frozen;
    }
    private void Awake()=>world=GetComponent<BuildWorld>();
    private void OnValidate(){Frozen=false;revision=-1;range=Mathf.Max(4,range);}
    private void LateUpdate()
    {
        world??=GetComponent<BuildWorld>();
        if(mode==BuildColliderDebugMode.Off || world==null || world.Session==null)return;
        var p=world.DebugPreview;Vector3 focus=world.Focus!=null?world.Focus.position:world.Camera!=null?world.Camera.transform.position:transform.position;
        if(!Frozen && (revision!=world.Session.Revision || activeCount!=world.ActiveColliders || capturedMode!=mode ||
            (capturedFocus-focus).sqrMagnitude>1 || snapshot.Definition!=p.Definition || snapshot.Origin!=p.Origin ||
            snapshot.WorldYaw!=p.WorldYaw || snapshot.Valid!=p.Valid || snapshot.Failure!=p.Failure ||
            snapshot.BlockingPieceId!=p.BlockingPieceId || snapshot.BlockingCollider!=p.BlockingCollider || snapshot.Message!=p.Message ||
            snapshot.Warning!=p.Warning || snapshot.WarningPieceId!=p.WarningPieceId || snapshot.WarningMessage!=p.WarningMessage ||
            p.BlockingCollider!=null && p.BlockingCollider.bounds!=capturedBlockerBounds))Rebuild();
        var camera=world.Camera!=null?world.Camera:Camera.main;
        if(wireMesh==null || wireMesh.vertexCount==0 || camera==null)return;
        if(material==null)
        {
            var shader=Resources.Load<Shader>("Building/BuildColliderDebug");
            if(shader==null)return;
            material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
        }
        Graphics.RenderMesh(new RenderParams(material){camera=camera,worldBounds=wireMesh.bounds,
            shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false},wireMesh,0,Matrix4x4.identity);
    }
    public void Rebuild()
    {
        world??=GetComponent<BuildWorld>();
        if(world==null || world.Session==null)return;
        snapshot=world.DebugPreview;revision=world.Session.Revision;activeCount=world.ActiveColliders;capturedMode=mode;
        capturedFocus=world.Focus!=null?world.Focus.position:world.Camera!=null?world.Camera.transform.position:transform.position;
        capturedBlockerBounds=snapshot.BlockingCollider!=null?snapshot.BlockingCollider.bounds:default;
        lines.Clear();blockerPosition=null;blockerLabel=null;
        world.Session.Query(new Bounds(capturedFocus,Vector3.one*range*2),pieces);
        // A distant blocker stays visible even when it is outside the normal debug range.
        if(snapshot.BlockingPieceId!=0 && world.Session.TryGet(snapshot.BlockingPieceId,out var blocked) && !pieces.Contains(blocked))pieces.Add(blocked);
        if(snapshot.WarningPieceId!=0 && world.Session.TryGet(snapshot.WarningPieceId,out var warned) && !pieces.Contains(warned))pieces.Add(warned);
        foreach(var piece in pieces)
        {
            bool blocker=piece.Id==snapshot.BlockingPieceId || snapshot.BlockingPieceId==0 && piece.Id==snapshot.WarningPieceId;
            if(world.TryGetCollisionProxy(piece.Id,out var proxy))
            {
                proxy.GetEnabledColliders(colliders);
                foreach(var collider in colliders)lines.Collider(collider,blocker?BlockerColor:PhysicalColor);
            }
            else lines.State(piece.Resolved,piece.Origin,piece.WorldYawStep,blocker?BlockerColor:InactiveColor);
            if(world.DebugStates!=null && world.DebugStates.TryGetValue(piece.Id,out var fitted) && fitted!=piece.Resolved && fitted.key!=piece.Resolved.key)
                lines.State(fitted,piece.Origin,piece.WorldYawStep,blocker?BlockerColor:FittedColor);
            if(mode==BuildColliderDebugMode.SolidsAndClearance && piece.Definition.kind==BuildPartKind.Stair)
                lines.Passage(piece.Definition,piece.Origin,piece.WorldYawStep,ClearanceColor);
            if(blocker){blockerPosition=piece.Origin+BuildGeometry.Rotation(piece.WorldYawStep)*piece.Definition.LocalBounds.center;blockerLabel=piece.Definition.displayName+" #"+piece.Id;}
        }
        if(snapshot.Definition!=null)
        {
            Color color=snapshot.Valid?(snapshot.Warning!=BuildPlacementWarning.None?FittedColor:ValidColor):InvalidColor;
            lines.State(snapshot.Resolved,snapshot.Origin,snapshot.WorldYaw,color);
            if(snapshot.Resolved==null || snapshot.Resolved.boxes.Length+snapshot.Resolved.volumes.Length==0)
                lines.Box(snapshot.Definition.LocalBounds,Matrix4x4.TRS(snapshot.Origin,BuildGeometry.Rotation(snapshot.WorldYaw),Vector3.one),color);
            if(mode==BuildColliderDebugMode.SolidsAndClearance)
            {
                if(snapshot.Definition.kind==BuildPartKind.Stair)lines.Passage(snapshot.Definition,snapshot.Origin,snapshot.WorldYaw,ClearanceColor);
                if(snapshot.Resolved!=null)foreach(var cut in snapshot.Resolved.cuts)
                    lines.Box(cut,Matrix4x4.TRS(snapshot.Origin,BuildGeometry.Rotation(snapshot.WorldYaw),Vector3.one),FittedColor);
            }
        }
        if(snapshot.BlockingCollider!=null)
        {
            lines.Collider(snapshot.BlockingCollider,BlockerColor);
            blockerPosition=snapshot.BlockingCollider.bounds.center;blockerLabel=snapshot.BlockingCollider.name;
        }
        wireMesh??=new Mesh{name="Building collider debug wires",hideFlags=HideFlags.HideAndDontSave};
        lines.Upload(wireMesh);
    }
    private void OnGUI()
    {
        if(mode==BuildColliderDebugMode.Off)return;
        panelStyle??=new GUIStyle(GUI.skin.box){alignment=TextAnchor.UpperLeft,fontSize=13,wordWrap=true,padding=new RectOffset(10,10,8,8)};
        labelStyle??=new GUIStyle(GUI.skin.box){fontSize=13};
        string text="BUILD COLLIDERS · F8: "+mode+" · F9: "+(Frozen?"frozen (press to resume)":"freeze")+
            "\nBlue: active physics   Grey: unstreamed solids\nGreen/red: proposed solids   Orange: blocker / warning piece\nYellow: fitted neighbor / opening / accepted warning";
        if(mode==BuildColliderDebugMode.SolidsAndClearance)text+="   Purple: stair headroom";
        if(snapshot.Definition!=null)
        {
            var size=snapshot.Definition.LocalBounds.size;
            text+=$"\n{snapshot.Definition.displayName} · {size.x:0.##}w × {size.y:0.##}h × {size.z:0.##}d m · {snapshot.WorldYaw*45}°"+
                $"\nOrigin {snapshot.Origin.x:0.##}, {snapshot.Origin.y:0.##}, {snapshot.Origin.z:0.##}"+
                "\n"+snapshot.Hint+"\n"+(snapshot.Valid?"Accepted":snapshot.Failure.ToString())+": "+snapshot.Message;
            if(snapshot.Warning!=BuildPlacementWarning.None)text+="\nWarning: "+snapshot.WarningMessage;
        }
        else text+="\nAim at a piece to inspect placement. Debug remains available outside build mode.";
        float width=Mathf.Min(650,Screen.width-24);float height=panelStyle.CalcHeight(new GUIContent(text),width);
        GUI.Box(new Rect(12,12,width,height),text,panelStyle);
        var camera=world!=null?world.Camera:null;
        if(camera!=null && blockerPosition.HasValue)
        {
            var p=camera.WorldToScreenPoint(blockerPosition.Value);
            if(p.z>0){GUI.color=BlockerColor;GUI.Box(new Rect(p.x-130,Screen.height-p.y,260,25),blockerLabel,labelStyle);GUI.color=Color.white;}
        }
    }
    private void OnDestroy(){BuildLifetime.Destroy(wireMesh);BuildLifetime.Destroy(material);}
}

/// <summary>Wire data is rebuilt on diagnostic changes. No per-frame collider creation or physics mutation.</summary>
public sealed class BuildDebugLines
{
    private readonly List<Vector3> points=new();
    private readonly List<Color> colors=new();
    private readonly List<int> indices=new();
    private readonly Dictionary<Mesh,(Vector3[] vertices,int[] triangles)> meshes=new();
    private static readonly int[] BoxEdges={0,1,1,3,3,2,2,0,4,5,5,7,7,6,6,4,0,4,1,5,2,6,3,7};
    private static readonly int[] PassageEdges={0,1,1,2,2,3,3,0,4,5,5,6,6,7,7,4,0,4,1,5,2,6,3,7};
    public int VertexCount=>points.Count;
    public void Clear(){points.Clear();colors.Clear();indices.Clear();}
    private void Edge(Vector3 a,Vector3 b,Color color)
    {indices.Add(points.Count);indices.Add(points.Count+1);points.Add(a);points.Add(b);colors.Add(color);colors.Add(color);}
    public void Box(Bounds bounds,Matrix4x4 pose,Color color)
    {
        var v=new Vector3[8];
        for(int i=0;i<8;i++)v[i]=pose.MultiplyPoint3x4(new Vector3((i&1)==0?bounds.min.x:bounds.max.x,(i&2)==0?bounds.min.y:bounds.max.y,(i&4)==0?bounds.min.z:bounds.max.z));
        for(int i=0;i<BoxEdges.Length;i+=2)Edge(v[BoxEdges[i]],v[BoxEdges[i+1]],color);
    }
    public void Mesh(Mesh mesh,Matrix4x4 pose,Color color)
    {
        if(mesh==null || !mesh.isReadable)return;
        if(!meshes.TryGetValue(mesh,out var data)){data=(mesh.vertices,mesh.triangles);meshes.Add(mesh,data);}
        var edges=new HashSet<ulong>();
        for(int i=0;i<data.triangles.Length;i+=3)for(int j=0;j<3;j++)
        {
            int a=data.triangles[i+j],b=data.triangles[i+(j+1)%3];ulong key=((ulong)(uint)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
            if(edges.Add(key))Edge(pose.MultiplyPoint3x4(data.vertices[a]),pose.MultiplyPoint3x4(data.vertices[b]),color);
        }
    }
    public void State(BuildResolvedState state,Vector3 origin,int yaw,Color color)
    {
        if(state==null)return;
        var pose=Matrix4x4.TRS(origin,BuildGeometry.Rotation(yaw),Vector3.one);
        foreach(var b in state.boxes)Box(b,pose,color);
        foreach(var v in state.volumes)Mesh(v.mesh,pose,color);
    }
    public void Passage(BuildDefinition definition,Vector3 origin,int yaw,Color color)
    {
        var v=BuildResolvedGeometry.StairPassage(definition).vertices;var q=BuildGeometry.Rotation(yaw);
        for(int i=0;i<PassageEdges.Length;i+=2)Edge(origin+q*v[PassageEdges[i]],origin+q*v[PassageEdges[i+1]],color);
    }
    public void Collider(Collider collider,Color color)
    {
        if(collider is BoxCollider box)Box(new Bounds(box.center,box.size),box.transform.localToWorldMatrix,color);
        else if(collider is MeshCollider mesh)Mesh(mesh.sharedMesh,mesh.transform.localToWorldMatrix,color);
        else if(collider is CharacterController character)Capsule(character.center,character.radius,character.height,1,character.transform.localToWorldMatrix,color);
        else if(collider is CapsuleCollider capsule)Capsule(capsule.center,capsule.radius,capsule.height,capsule.direction,capsule.transform.localToWorldMatrix,color);
        else if(collider is SphereCollider sphere)Capsule(sphere.center,sphere.radius,sphere.radius*2,1,sphere.transform.localToWorldMatrix,color);
        else Box(collider.bounds,Matrix4x4.identity,color); // Terrain/unknown solid: broad bounds only.
    }
    private void Capsule(Vector3 centre,float radius,float height,int direction,Matrix4x4 pose,Color color)
    {
        Vector3 axis=direction==0?Vector3.right:direction==2?Vector3.forward:Vector3.up;
        Vector3 u=direction==0?Vector3.up:Vector3.right,v=Vector3.Cross(axis,u);
        float half=Mathf.Max(0,height*.5f-radius);
        for(int ring=0;ring<2;ring++)for(int i=0;i<32;i++)
        {
            float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;Vector3 c=centre+axis*(ring==0?-half:half);
            Edge(pose.MultiplyPoint3x4(c+radius*(u*Mathf.Cos(a)+v*Mathf.Sin(a))),pose.MultiplyPoint3x4(c+radius*(u*Mathf.Cos(b)+v*Mathf.Sin(b))),color);
        }
        for(int plane=0;plane<2;plane++)for(int i=0;i<32;i++)
        {
            Vector3 radial=plane==0?u:v;float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;
            Vector3 At(float angle)=>centre+axis*(Mathf.Sin(angle)*radius+(Mathf.Sin(angle)>=0?half:-half))+radial*Mathf.Cos(angle)*radius;
            Edge(pose.MultiplyPoint3x4(At(a)),pose.MultiplyPoint3x4(At(b)),color);
        }
        for(int i=0;i<4;i++){Vector3 r=radius*(u*Mathf.Cos(i*Mathf.PI/2)+v*Mathf.Sin(i*Mathf.PI/2));Edge(pose.MultiplyPoint3x4(centre+r-axis*half),pose.MultiplyPoint3x4(centre+r+axis*half),color);}
    }
    public void Upload(Mesh mesh)
    {
        mesh.Clear();mesh.indexFormat=points.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
        mesh.SetVertices(points);mesh.SetColors(colors);mesh.SetIndices(indices,MeshTopology.Lines,0);mesh.RecalculateBounds();
    }
}
