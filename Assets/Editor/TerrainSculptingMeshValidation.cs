using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>Real reconstructed geometry, original collider masking, and actual motor traversal.</summary>
public static class TerrainSculptingMeshValidation
{
    private static readonly TerrainDensityProfile P = TerrainDensityProfile.Default;
    private static int checks, traversals;
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private sealed class HeightField : ITerrainDensityGenerator
    {
        internal readonly Func<double,double,double> Height;
        internal HeightField(Func<double,double,double> height){Height=height;}
        public double SampleDensity(double3 p)=>p.y-Height(p.x,p.z);
    }
    private sealed class BoundedFlat : ITerrainDensityGenerator,ITerrainDensityBounds
    {
        public double SampleDensity(double3 p)=>p.y-100;
        public bool TryGetDensityBounds(double3 min,double3 max,out double low,out double high)
        {low=min.y-100;high=max.y-100;return true;}
    }
    private static void Cut(TerrainDensityWorld world,TerrainDensityBrush brush)
    {using var edit=world.StageExcavation(brush);Check(world.TryCommit(edit),"Fixture cut failed.");}
    private static TerrainSculptFootprint Rect(int x0,int z0,int x1,int z1)=>new TerrainSculptFootprint(new int2(x0,z0),new int2(x1,z1));
    private static TerrainDensityCellBounds Bounds(int x0,int y0,int z0,int x1,int y1,int z1)=>new TerrainDensityCellBounds(new int3(x0,y0,z0),new int3(x1,y1,z1));

    [MenuItem("Tools/Terrain/Validate Sculpting Meshes and Collision")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run outside Play Mode in the isolated fixture.");
        checks=0;traversals=0;
        ValidateClosedAndStaged();ValidateSharedSeams();ValidateZeroPlane();ValidateDeepCavity();ValidateCollars();ValidateNativeCollars();ValidateTunnelAndMotor();
        Debug.Log($"TERRAIN SCULPTING P2 COMPLETE: {checks} assertions, {traversals} actual CharacterMotor traversals; meshing, explicit fine/coarse collars, roof retention and tunnel breakthrough passed.");
    }

    private static void ValidateClosedAndStaged()
    {
        using var world=new TerrainDensityWorld(P,new HeightField((x,z)=>100));
        using var edit=world.StageExcavation(TerrainDensityBrush.Sphere(new double3(.15,.15,.15),1.25));
        using var snapshot=world.CaptureMeshingSnapshot(edit);
        var bounds=Bounds(-8,-8,-8,9,9,9);
        var mesh=TerrainDensityMesher.Build(snapshot,bounds);
        Check(mesh.TriangleCount>0&&world.ModifiedBrickCount==0,"Pending geometry requires prematurely committed density.");
        Topology(new[]{mesh},null);
        double volume=0;
        for(int i=0;i<mesh.Triangles.Length;i+=3)
        {
            double3 a=mesh.Positions[mesh.Triangles[i]],b=mesh.Positions[mesh.Triangles[i+1]],c=mesh.Positions[mesh.Triangles[i+2]];
            Check(math.dot(math.cross(b-a,c-a),new double3(.15)-(a+b+c)/3)>0,"Cavity winding faces into stone.");
            volume+=math.dot(a,math.cross(b,c))/6;
        }
        Check(volume<0 && Math.Abs(volume)>7 && Math.Abs(volume)<8.3,"Reconstructed sphere volume or winding is implausible.");
        var again=TerrainDensityMesher.Build(snapshot,bounds);
        Check(mesh.Positions.Length==again.Positions.Length&&mesh.Triangles.Length==again.Triangles.Length,"Meshing is not deterministic.");
        for(int i=0;i<mesh.Positions.Length;i++)Check(math.all(mesh.Positions[i]==again.Positions[i])&&math.all(mesh.Normals[i]==again.Normals[i]),"Repeated extraction changed a vertex or normal.");
        for(int i=0;i<mesh.Triangles.Length;i++)Check(mesh.Triangles[i]==again.Triangles[i],"Repeated extraction changed topology.");
        using var boxWorld=new TerrainDensityWorld(P,new HeightField((x,z)=>100));
        Cut(boxWorld,TerrainDensityBrush.Box(new double3(.15),new double3(.5)));
        using var boxSnapshot=boxWorld.CaptureSnapshot();Topology(new[]{TerrainDensityMesher.Build(boxSnapshot,bounds)},null);
        Throws< TerrainDensityCapacityException>(()=>TerrainDensityMesher.Build(snapshot,bounds,1),"Cell admission did not reject oversized extraction.");
    }

    private static void ValidateSharedSeams()
    {
        foreach(int seam in new[]{0,128,-128})
        {
            using var world=new TerrainDensityWorld(P,new HeightField((x,z)=>100));
            Cut(world,TerrainDensityBrush.Sphere(new double3(seam*P.Spacing,0,0),1.25));
            using var snapshot=world.CaptureSnapshot();
            var left=TerrainDensityMesher.Build(snapshot,Bounds(seam-8,-8,-8,seam,8,8));
            var right=TerrainDensityMesher.Build(snapshot,Bounds(seam,-8,-8,seam+8,8,8));
            Topology(new[]{left,right},null);
            var normals=new Dictionary<Point,float3>();int matched=0;
            for(int i=0;i<left.Positions.Length;i++)if(Math.Abs(left.Positions[i].x-seam*P.Spacing)<1e-10)normals[new Point(left.Positions[i])]=left.Normals[i];
            for(int i=0;i<right.Positions.Length;i++)if(normals.TryGetValue(new Point(right.Positions[i]),out var n))
            {Check(math.length(n-right.Normals[i])<1e-6,"Normals disagree across a brick/source-chunk seam.");matched++;}
            Check(matched>8,"Shared seam fixture did not intersect the surface.");
        }
    }

    private static void ValidateZeroPlane()
    {
        using var world=new TerrainDensityWorld(P,new HeightField((x,z)=>0));
        using var snapshot=world.CaptureSnapshot();var domain=Rect(-8,-8,8,8);
        var mesh=TerrainDensityMesher.Build(snapshot,Bounds(-8,-8,-8,8,8,8));Topology(new[]{mesh},domain);
        foreach(var p in mesh.Positions)Check(p.y==0,"Exact-zero plane developed a geometric offset.");
        foreach(var n in mesh.Normals)Check(math.dot(n,new float3(0,1,0))>.999,"Exact-zero plane normal is inconsistent.");
    }

    private static void ValidateCollars()
    {
        Func<double,double,double> height=(x,z)=>3+.1*x+.05*z+.08*Math.Sin(x*.4)*Math.Sin(z*.4);
        using var world=new TerrainDensityWorld(P,new HeightField(height));
        Cut(world,TerrainDensityBrush.Sphere(new double3(0,3,0),.8));
        using var snapshot=world.CaptureSnapshot();var footprint=Rect(-32,-32,32,32);var domain=Rect(-64,-64,64,64);
        foreach(int step in new[]{1,8})
        {
            var patch=TerrainSculptPatchBuilder.BuildPatch(snapshot,footprint,-16,32,height,step);
            var baseline=TerrainSculptPatchBuilder.BuildBaseline(P,domain,height,step,new[]{footprint});
            Topology(new[]{patch,baseline},domain);
            Check(patch.TriangleCount>0&&baseline.TriangleCount>0,"Replacement or surviving baseline is empty.");
        }
        using var tooSmall=new TerrainDensityWorld(P,new HeightField(height));
        Cut(tooSmall,TerrainDensityBrush.Sphere(new double3(4.6,3,0),1));using var smallSnapshot=tooSmall.CaptureSnapshot();
        Throws<ArgumentException>(()=>TerrainSculptPatchBuilder.BuildPatch(smallSnapshot,Rect(-16,-16,16,16),-16,32,height,8),"Edited collar was silently capped.");
        Throws<ArgumentException>(()=>TerrainSculptPatchBuilder.BuildPatch(snapshot,footprint,8,32,height),"Air lower guard was accepted.");
        Throws<ArgumentException>(()=>TerrainSculptPatchBuilder.BuildBaseline(P,domain,(x,z)=>double.NaN),"Nonfinite baseline geometry was admitted.");
        Throws<ArgumentException>(()=>TerrainSculptPatchBuilder.BuildBaseline(P,default,height),"Unconstructed footprint was admitted.");
    }

    private static void ValidateDeepCavity()
    {
        using var world=new TerrainDensityWorld(P,new BoundedFlat());
        Cut(world,TerrainDensityBrush.Sphere(new double3(0),1.25));using var snapshot=world.CaptureSnapshot();
        var footprint=Rect(-16,-16,16,16);var domain=Rect(-32,-32,32,32);
        var patch=TerrainSculptPatchBuilder.BuildPatch(snapshot,footprint,-16,352,(x,z)=>100);
        var baseline=TerrainSculptPatchBuilder.BuildBaseline(P,domain,(x,z)=>100,1,new[]{footprint});
        Topology(new[]{patch,baseline},domain);
        Check(patch.SkippedBricks>patch.SampledBricks*2,"Solid span between deep cavity and surface was densely sampled/meshed.");
        Check(patch.SampledBricks>0&&patch.Positions.Length>0,"Deep cavity fixture skipped its actual boundaries.");
        Debug.Log($"SCULPT DEEP CAVITY PASS: 100 m below intact surface, {patch.SampledBricks} sampled bricks, {patch.SkippedBricks} conservatively skipped, no internal open edges.");
        using var air=new TerrainDensityWorld(P,new BoundedFlat());
        Cut(air,TerrainDensityBrush.Box(new double3(P.Spacing*7.5),new double3(3.6)));using var airSnapshot=air.CaptureSnapshot();
        Check(airSnapshot.IsSurfaceFree(Bounds(1,1,1,15,15,15)),"Uniform-air override was replaced by baseline stone during culling.");
    }

    private static void ValidateNativeCollars()
    {
        var g=TerrainSculptingValidation.ReadSceneGeneration(Path.Combine(TerrainSculptingValidation.SourceRoot(),"Assets/Scenes/SmearScene.unity"));
        var baseline=new TerrainBaselineSampler(g,16);Func<double,double,double> height=baseline.SampleHeight;
        using var world=new TerrainDensityWorld(P,baseline);using var snapshot=world.CaptureSnapshot();
        var footprint=Rect(16,16,48,48);var domain=Rect(0,0,128,128);double lo=double.MaxValue,hi=double.MinValue;
        for(int z=16;z<=48;z++)for(int x=16;x<=48;x++){double h=height(x*P.Spacing,z*P.Spacing);lo=Math.Min(lo,h);hi=Math.Max(hi,h);}
        int minY=(int)Math.Floor(lo/P.Spacing)-8,maxY=(int)Math.Ceiling(hi/P.Spacing)+8;
        foreach(int step in new[]{1,8})
        {
            var patch=TerrainSculptPatchBuilder.BuildPatch(snapshot,footprint,minY,maxY,height,step);
            var baseMesh=TerrainSculptPatchBuilder.BuildBaseline(P,domain,height,step,new[]{footprint});
            Topology(new[]{patch,baseMesh},domain);
        }
        Debug.Log("SCULPT NATIVE COLLAR PASS: actual SmearScene profile, original 0.3 m render grid and 2.4 m collider grid both join the reconstructed surface without internal boundary edges.");
    }

    private static void ValidateTunnelAndMotor()
    {
        double s=P.Spacing;
        Func<double,double,double> height=(x,z)=>s*Math.Max(0,Math.Min(14,.7*(x/s+20)));
        using var world=new TerrainDensityWorld(P,new HeightField(height));
        double cy=4.5*s;
        Cut(world,TerrainDensityBrush.Box(new double3(-3,cy,0),new double3(2.4,cy,4*s)));
        Cut(world,TerrainDensityBrush.Box(new double3(3,cy,0),new double3(2.4,cy,4*s)));
        var footprint=Rect(-48,-32,48,32);var domain=Rect(-64,-64,64,64);
        TerrainSculptMeshData separated;
        using(var snapshot=world.CaptureSnapshot())separated=TerrainSculptPatchBuilder.BuildPatch(snapshot,footprint,-16,32,height,8);
        var host=new GameObject("Sculpted terrain P2 fixture");host.transform.position=new Vector3(0,500,0);
        var meshes=new List<Mesh>();
        try
        {
            var oldData=TerrainSculptPatchBuilder.BuildBaseline(P,domain,height,8);
            var baseObject=ColliderObject(host,"Original baseline collider",oldData,meshes);
            Physics.SyncTransforms();
            Check(Physics.Raycast(host.transform.position+new Vector3(-5,1.35f,0),Vector3.right,out var oldHit,10,GameplayLayers.SolidSurfaceMask)&&oldHit.point.x<0,
                "Fixture did not reproduce the original heightfield blocking the tunnel.");
            var patchObject=ColliderObject(host,"Volume with separating wall",separated,meshes);
            var masked=TerrainSculptPatchBuilder.BuildBaseline(P,domain,height,8,new[]{footprint});
            baseObject.GetComponent<MeshCollider>().sharedMesh=masked.CreateMesh(new double3(0),"Masked source collision");meshes.Add(baseObject.GetComponent<MeshCollider>().sharedMesh);Physics.SyncTransforms();
            Check(Physics.Raycast(host.transform.position+new Vector3(-3,1.35f,0),Vector3.right,out var wall,6,GameplayLayers.SolidSurfaceMask)&&wall.point.x<.1f,"Separating wall was missing before breakthrough.");
            TerrainSculptMeshData joined;
            using(var pending=world.StageExcavation(TerrainDensityBrush.Box(new double3(0,cy,0),new double3(1,cy,4*s))))
            using(var staged=world.CaptureMeshingSnapshot(pending))
            {
                joined=TerrainSculptPatchBuilder.BuildPatch(staged,footprint,-16,32,height,8);
                Check(world.ReadEncoded(new int3(0,4,0))<0,"Mesh build published pending breakthrough density.");
                Check(world.TryCommit(pending),"Breakthrough could not commit.");
            }
            var joinedMesh=joined.CreateMesh(new double3(0),"Joined tunnel collision");meshes.Add(joinedMesh);patchObject.GetComponent<MeshCollider>().sharedMesh=joinedMesh;Physics.SyncTransforms();
            Check(Physics.Raycast(host.transform.position+new Vector3(-3,1.35f,0),Vector3.right,out var farWall,12,GameplayLayers.SolidSurfaceMask)&&farWall.point.x>5,"Old baseline/separating wall still blocks the opening.");
            var inside=host.transform.position+new Vector3(3,1.35f,0);
            Check(Physics.Raycast(inside,Vector3.up,out var ceiling,5,GameplayLayers.SolidSurfaceMask)&&Math.Abs(ceiling.point.y-500-2*cy)<.04,"Cavity ceiling was lost or faces the wrong way.");
            Check(ceiling.normal.y<-.9f,"Ceiling collision is not oriented into the air.");
            Check(Physics.Raycast(host.transform.position+new Vector3(3,8,0),Vector3.down,out var roof,8,GameplayLayers.SolidSurfaceMask)&&roof.point.y-500>4.1f,"Buried excavation removed the intact exterior roof.");
            Check(BuildWorld.IsGround(ceiling.collider)&&ceiling.collider.gameObject.layer==GameplayLayers.WorldSolid,"Patch collider is not recognized as world ground.");
            foreach(float dt in new[]{1/60f,1/120f})foreach(float lane in new[]{-.5f,0,.5f})foreach(bool reverse in new[]{false,true})
                Traverse(host.transform.position,reverse?4:-16,reverse?-16:4,lane,dt,false,height);
            foreach(float dt in new[]{1/60f,1/120f})foreach(bool reverse in new[]{false,true})
                Traverse(host.transform.position,reverse?16:-16,reverse?-16:16,5.4f,dt,true,height);
            using var final=world.CaptureSnapshot();
            var visible=TerrainSculptPatchBuilder.BuildPatch(final,footprint,-16,32,height,1);
            var visibleBase=TerrainSculptPatchBuilder.BuildBaseline(P,domain,height,1,new[]{footprint});
            Topology(new[]{visible,visibleBase},domain);Topology(new[]{joined,masked},domain);
            Export(visible,visibleBase);
            Debug.Log($"SCULPT TUNNEL PASS: exterior roof retained, separating wall removed, source collider masked; render patch {visible.TriangleCount} triangles, collider patch {joined.TriangleCount} triangles, {traversals} actual motor routes.");
        }
        finally{Object.DestroyImmediate(host);foreach(var mesh in meshes)Object.DestroyImmediate(mesh);Physics.SyncTransforms();}
    }

    private static GameObject ColliderObject(GameObject host,string name,TerrainSculptMeshData data,List<Mesh> meshes)
    {
        var obj=new GameObject(name);obj.transform.SetParent(host.transform,false);obj.layer=GameplayLayers.WorldSolid;obj.AddComponent<WorldGroundSurface>();
        var mesh=data.CreateMesh(new double3(0),name);meshes.Add(mesh);obj.AddComponent<MeshCollider>().sharedMesh=mesh;return obj;
    }

    private static void Traverse(Vector3 origin,float from,float to,float z,float dt,bool surface,Func<double,double,double> height)
    {
        var rig=new GameObject("Sculpt terrain actual motor route");var cc=rig.AddComponent<CharacterController>();
        cc.height=1.8f;cc.center=new Vector3(0,.9f,0);cc.radius=.4f;cc.slopeLimit=50;cc.stepOffset=.3f;cc.skinWidth=.08f;cc.minMoveDistance=0;
        float initialY=surface?(float)height(from,z):from< -6?(float)height(from,z):0;
        rig.transform.SetPositionAndRotation(origin+new Vector3(from,initialY+.08f,z),Quaternion.Euler(0,to>from?90:-90,0));
        var motor=rig.AddComponent<CharacterMotor>();typeof(CharacterMotor).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motor,null);Physics.SyncTransforms();
        try
        {
            for(int i=0;i<30;i++)motor.Simulate(default,dt);
            float direction=Math.Sign(to-from);int limit=(int)Math.Ceiling((Math.Abs(to-from)/2+4)/dt);bool arrived=false;
            for(int i=0;i<limit;i++)
            {
                motor.Simulate(new CharacterMoveCommand(new Vector2(0,1),false,false,false),dt);
                var p=rig.transform.position-origin;
                Check(p.y>-.2f&&p.y<6,"Motor fell through a seam or escaped into the roof.");
                if(direction*(p.x-to)>=-.15f){arrived=true;break;}
            }
            Check(arrived,$"Motor stalled on {(surface?"surface collar":"tunnel/entrance")} route {from}->{to}, lane {z}, dt={dt:R}, position={rig.transform.position-origin}.");traversals++;
        }
        finally{Object.DestroyImmediate(rig);Physics.SyncTransforms();}
    }

    private readonly struct Point:IEquatable<Point>
    {
        private readonly long x,y,z;
        internal Point(double3 p){x=(long)Math.Round(p.x*1e8);y=(long)Math.Round(p.y*1e8);z=(long)Math.Round(p.z*1e8);}
        public bool Equals(Point p)=>x==p.x&&y==p.y&&z==p.z;
        public override bool Equals(object obj)=>obj is Point p&&Equals(p);
        public override int GetHashCode()=>HashCode.Combine(x,y,z);
    }
    private static ulong Edge(int a,int b)=>((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
    private static void Topology(IReadOnlyList<TerrainSculptMeshData> meshes,TerrainSculptFootprint? outer)
    {
        var vertices=new Dictionary<Point,int>();var positions=new List<double3>();var counts=new Dictionary<ulong,int>();
        foreach(var mesh in meshes)
        {
            var remap=new int[mesh.Positions.Length];
            for(int i=0;i<remap.Length;i++)
            {
                Check(math.all(math.isfinite(mesh.Positions[i]))&&math.all(math.isfinite(mesh.Normals[i])),"Nonfinite mesh data.");
                var key=new Point(mesh.Positions[i]);if(!vertices.TryGetValue(key,out int index)){index=positions.Count;vertices.Add(key,index);positions.Add(mesh.Positions[i]);}remap[i]=index;
            }
            for(int i=0;i<mesh.Triangles.Length;i+=3)
            {
                int a=remap[mesh.Triangles[i]],b=remap[mesh.Triangles[i+1]],c=remap[mesh.Triangles[i+2]];
                Check(a!=b&&b!=c&&a!=c,"Degenerate triangle survived extraction.");
                foreach(ulong edge in new[]{Edge(a,b),Edge(b,c),Edge(c,a)}){counts.TryGetValue(edge,out int n);counts[edge]=n+1;}
            }
        }
        foreach(var pair in counts)
        {
            if(pair.Value==2){checks++;continue;}
            Check(pair.Value==1&&outer.HasValue,"Non-manifold, duplicate, or unpaired edge in the joined surface.");
            double3 a=positions[(int)(pair.Key>>32)],b=positions[(int)(pair.Key&uint.MaxValue)];var o=outer.Value;
            bool boundary=false;foreach(double x in new[]{o.Min.x*P.Spacing,o.Max.x*P.Spacing})boundary|=Math.Abs(a.x-x)<1e-8&&Math.Abs(b.x-x)<1e-8;
            foreach(double z in new[]{o.Min.y*P.Spacing,o.Max.y*P.Spacing})boundary|=Math.Abs(a.z-z)<1e-8&&Math.Abs(b.z-z)<1e-8;
            Check(boundary,"Internal T-junction or crack in volume/baseline collar.");
        }
    }

    private static void Throws<T>(Action action,string message)where T:Exception
    {try{action();}catch(T){checks++;return;}throw new InvalidOperationException(message);}
    private static void Export(TerrainSculptMeshData patch,TerrainSculptMeshData baseline)
    {
        string root=TerrainSculptingValidation.SourceRoot();string folder=Path.Combine(root,".utmp/terrain-sculpting/p2");Directory.CreateDirectory(folder);
        void Obj(TerrainSculptMeshData mesh,string name)
        {
            using var file=new StreamWriter(Path.Combine(folder,name));
            foreach(var p in mesh.Positions)file.WriteLine(string.Format(CultureInfo.InvariantCulture,"v {0:R} {1:R} {2:R}",p.x,p.y,p.z));
            for(int i=0;i<mesh.Triangles.Length;i+=3)file.WriteLine($"f {mesh.Triangles[i]+1} {mesh.Triangles[i+1]+1} {mesh.Triangles[i+2]+1}");
        }
        Obj(patch,"tunnel-patch.obj");Obj(baseline,"masked-baseline.obj");
    }
}
