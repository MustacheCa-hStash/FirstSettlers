using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

/// <summary>Full-resolver parity and bounded mutation/render work in isolated fixtures.</summary>
public static class BuildIncrementalValidation
{
    private static int checks;
    private static readonly HashSet<(Mesh,Mesh)> compared=new();
    private static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static void Near(Vector3 a,Vector3 b,string message)=>Check((a-b).sqrMagnitude<.0000001f,message+": "+a+" / "+b);
    public static void RunBatch()
    {
        checks=0;compared.Clear();
        try
        {
            var catalog=Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            Check(catalog!=null,"Installed catalog missing");
            Parity(catalog);ConnectedLocality(catalog);Scaling(catalog);RenderGroups();
            Debug.Log("BUILD INCREMENTAL PASS: "+checks+" checks; full geometry/UV/normal/tangent/collision/joint/connection/flame parity, bounded work in connected construction and with 1024 distant pieces, partial render groups, 500-instance splits, preview restoration, pending mutations and multiple renderer lifetimes.");
            EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        finally{compared.Clear();}
    }
    private static void MeshEqual(Mesh a,Mesh b,string label)
    {
        if(a==b)return;
        Check(a!=null&&b!=null,label+" mesh presence");
        if(!compared.Add((a,b)))return;
        Check(a.vertexCount==b.vertexCount&&a.subMeshCount==b.subMeshCount,label+" topology");
        Near(a.bounds.center,b.bounds.center,label+" mesh bounds centre");Near(a.bounds.size,b.bounds.size,label+" mesh bounds size");
        using var da=MeshUtility.AcquireReadOnlyMeshData(a);using var db=MeshUtility.AcquireReadOnlyMeshData(b);
        using var va=new NativeArray<Vector3>(a.vertexCount,Allocator.Temp);using var vb=new NativeArray<Vector3>(b.vertexCount,Allocator.Temp);
        da[0].GetVertices(va);db[0].GetVertices(vb);
        for(int i=0;i<va.Length;i++)Near(va[i],vb[i],label+" vertex");
        foreach(var attribute in new[]{VertexAttribute.Normal,VertexAttribute.Tangent,VertexAttribute.TexCoord0})
        {
            Check(a.HasVertexAttribute(attribute)==b.HasVertexAttribute(attribute),label+" attributes");
            if(!a.HasVertexAttribute(attribute))continue;
            if(attribute==VertexAttribute.Normal)
            {
                da[0].GetNormals(va);db[0].GetNormals(vb);for(int i=0;i<va.Length;i++)Near(va[i],vb[i],label+" normal");
            }
            else
            {
                using var aa=new NativeArray<Vector4>(a.vertexCount,Allocator.Temp);using var ab=new NativeArray<Vector4>(b.vertexCount,Allocator.Temp);
                if(attribute==VertexAttribute.Tangent){da[0].GetTangents(aa);db[0].GetTangents(ab);}
                else{da[0].GetUVs(0,aa);db[0].GetUVs(0,ab);}
                for(int i=0;i<aa.Length;i++)Check((aa[i]-ab[i]).sqrMagnitude<.0000001f,label+" UV/tangent");
            }
        }
        for(int s=0;s<a.subMeshCount;s++)
        {
            Check(a.GetIndexCount(s)==b.GetIndexCount(s),label+" index count");
            using var ia=new NativeArray<int>((int)a.GetIndexCount(s),Allocator.Temp);using var ib=new NativeArray<int>(ia.Length,Allocator.Temp);
            da[0].GetIndices(ia,s);db[0].GetIndices(ib,s);for(int i=0;i<ia.Length;i++)Check(ia[i]==ib[i],label+" index");
        }
    }
    private static void BoundsEqual(Bounds[] a,Bounds[] b,string label)
    {
        Check(a.Length==b.Length,label+" count");for(int i=0;i<a.Length;i++){Near(a[i].center,b[i].center,label+" centre");Near(a[i].size,b[i].size,label+" size");}
    }
    private static void Compare(BuildSession session,BuildResolution reference)
    {
        var pieces=session.Pieces.Values.OrderBy(p=>p.Id).ToList();
        var all=reference.Resolve(pieces,p=>session.NeighboursFor(p));
        foreach(var p in pieces)
        {
            var a=p.Resolved;var b=all[p.Id];string label=p.Definition.contentId+" #"+p.Id;
            Check(a.key==b.key&&a.invalidJoint==b.invalidJoint,label+" key/policy");
            BoundsEqual(a.boxes,b.boxes,label+" boxes");BoundsEqual(a.cuts,b.cuts,label+" cuts");
            MeshEqual(a.mesh,b.mesh,label);MeshEqual(a.auxiliaryMesh,b.auxiliaryMesh,label+" bearing");
            Check(a.volumes.Length==b.volumes.Length,label+" hull count");
            for(int i=0;i<a.volumes.Length;i++)
            {
                var x=a.volumes[i];var y=b.volumes[i];MeshEqual(x.mesh,y.mesh,label+" hull");Near(x.bounds.center,y.bounds.center,label+" hull centre");Near(x.bounds.size,y.bounds.size,label+" hull bounds");
                foreach(var arrays in new[]{(x.vertices,y.vertices),(x.faceAxes,y.faceAxes),(x.edgeAxes,y.edgeAxes)})
                {Check(arrays.Item1.Length==arrays.Item2.Length,label+" SAT count");for(int k=0;k<arrays.Item1.Length;k++)Near(arrays.Item1[k],arrays.Item2[k],label+" SAT data");}
            }
            Check(a.attachments.Length==b.attachments.Length&&a.joints.Length==b.joints.Length,label+" attachment/contact counts");
            for(int i=0;i<a.attachments.Length;i++)
            {Check(a.attachments[i].material==b.attachments[i].material&&a.attachments[i].localMatrix==b.attachments[i].localMatrix,label+" attachment pose/material");MeshEqual(a.attachments[i].mesh,b.attachments[i].mesh,label+" attachment");}
            for(int i=0;i<a.joints.Length;i++)
            {var x=a.joints[i];var y=b.joints[i];Check(x.OtherPieceId==y.OtherPieceId&&x.PrimaryOwner==y.PrimaryOwner&&x.Kind==y.Kind,label+" joint identity");Near(x.Position,y.Position,label+" joint position");Near(x.LocalPosition,y.LocalPosition,label+" local joint");}
            var edges=new HashSet<ulong>();
            foreach(var other in session.NeighboursFor(p))
            {
                if(other.Id==p.Id)continue;
                var x=p.Id<other.Id?p:other;var y=p.Id<other.Id?other:p;
                if(BuildResolvedGeometry.Connects(x,all[x.Id],y,all[y.Id]))edges.Add(other.Id);
            }
            Check(p.Connections.SetEquals(edges),label+" connection graph");
            var flame=new List<LocalFlameGeometry.Group>();
            LocalFlameGeometry.Query(p.WorldBounds.center,p.WorldBounds.extents.magnitude+1,flame,false);
            var registered=flame.SingleOrDefault(g=>ReferenceEquals(g.Owner,p));
            var expected=new List<LocalFlameGeometry.Part>();
            var matrix=Matrix4x4.TRS(p.Origin,BuildGeometry.Rotation(p.WorldYawStep),Vector3.one);
            if(a.mesh!=null)expected.Add(new(a.mesh,matrix,p.Definition.material));
            if(a.auxiliaryMesh!=null)expected.Add(new(a.auxiliaryMesh,matrix,p.Definition.material));
            foreach(var part in a.attachments)if(part.mesh!=null)expected.Add(new(part.mesh,matrix*part.localMatrix,part.material));
            Check(expected.Count==0?registered==null:registered!=null&&registered.Parts.Length==expected.Count,label+" flame geometry count");
            for(int i=0;i<expected.Count;i++)Check(expected[i].Same(registered.Parts[i]),label+" flame geometry/pose/material");
        }
    }
    private static void Parity(BuildCatalog catalog)
    {
        using var s=new BuildSession();using var reference=new BuildResolution();
        var frame=s.CreateFrame(new Vector3(-24,200,-24),0);var placed=new List<BuildPieceRecord>();
        BuildDefinition D(string id){var d=catalog.Find(id);Check(d!=null,"Missing parity definition "+id);return d;}
        // Installed floors, stairs, corner covers, top courses, gables and reversible shelf pockets.
        var definitions=new[]{catalog.presets[0],catalog.presets[1],catalog.presets[2],catalog.presets[3],
            D(W21StairSetup.ContentId),D(ThatchRoofSetup.ContentId),D("build.stone.l01-coursed-limestone"),
            D("build.stone.l18-limestone-eaves-cornice"),D("build.stone.ss01-bedded-sandstone"),D(BuildCorbelPocket.ShelfId)};
        for(int heading=0;heading<8;heading+=2)
        {
            var f=s.CreateFrame(frame.Origin+Vector3.right*(heading*16),heading);
            foreach(var entry in new[]{(2,new Vector3Int(0,0,0)),(0,new Vector3Int(0,0,0)),(0,new Vector3Int(16,0,0)),
                (0,new Vector3Int(16,0,0)),(0,new Vector3Int(0,12,0)),(1,new Vector3Int(0,12,0)),(4,new Vector3Int(2,0,6)),
                (3,new Vector3Int(16,0,0)),(5,new Vector3Int(0,24,0)),(6,new Vector3Int(0,0,20)),(7,new Vector3Int(0,11,20)),
                (8,new Vector3Int(0,0,40)),(9,new Vector3Int(1,4,42))})
            {placed.Add(s.Add(definitions[entry.Item1],f,entry.Item2,0));Compare(s,reference);}
        }
        var random=new System.Random(7249);
        for(int i=0;i<48;i++)
        {
            var d=definitions[random.Next(definitions.Length)];
            placed.Add(s.Add(d,frame,new Vector3Int(random.Next(4)*8,random.Next(4)*4,random.Next(4)*8),random.Next(8)));
            Compare(s,reference);
        }
        while(placed.Count>0)
        {
            int index=random.Next(placed.Count);var p=placed[index];placed.RemoveAt(index);
            Check(s.Remove(p.Id),"Parity removal failed");Compare(s,reference);
            if(placed.Count%7==0)
            {var added=s.Add(definitions[0],frame,new Vector3Int(placed.Count*4,0,-20),2);Compare(s,reference);Check(s.Damage(added.Id,added.Health),"Parity destruction failed");Compare(s,reference);}
        }
    }
    private static void ConnectedLocality(BuildCatalog c)
    {
        using var s=new BuildSession();using var reference=new BuildResolution();
        var frame=s.CreateFrame(new Vector3(0,350,-100),0);
        var walls=new List<BuildPieceRecord>();var floors=new List<BuildPieceRecord>();
        for(int i=0;i<32;i++)
        {
            walls.Add(s.Add(c.presets[0],frame,new Vector3Int(i*16,0,0),0));
            floors.Add(s.Add(c.presets[1],frame,new Vector3Int(i*16,12,0),0));
        }
        var remote=walls[31].Resolved;
        s.Remove(walls[8].Id);int work=s.LastResolutionCount;
        Check(work<24&&ReferenceEquals(remote,walls[31].Resolved),"Connected construction propagated fitting to distant pieces");Compare(s,reference);
        walls[8]=s.Add(c.presets[0],frame,new Vector3Int(8*16,0,0),0);Compare(s,reference);
        var post=s.Add(c.presets[3],frame,new Vector3Int(8*16,0,0),0);Compare(s,reference);
        s.Remove(post.Id);Compare(s,reference);s.Remove(floors[8].Id);Compare(s,reference);
        s.Add(c.presets[1],frame,new Vector3Int(8*16,12,0),0);Compare(s,reference);
        Debug.Log("BUILD CONNECTED WORK PASS: 64 linked wall/floor records; local removal resolved "+work+" records.");
    }
    private static void Scaling(BuildCatalog c)
    {
        using var s=new BuildSession();var f=s.CreateFrame(new Vector3(0,400,0),0);
        var first=s.Add(c.presets[2],f,default,0);s.Add(c.presets[0],f,default,0);
        var probe=s.Add(c.presets[1],f,new Vector3Int(0,12,0),0);int small=s.LastResolutionCount;s.Remove(probe.Id);
        for(int i=0;i<1024;i++)s.Add(c.presets[0],s.CreateFrame(new Vector3(100+i%32*8,400,100+i/32*8),0),default,0);
        var remote=s.Pieces.Values.Last();var remoteState=remote.Resolved;int remoteRevision=remote.ResolvedRevision;
        probe=s.Add(c.presets[1],f,new Vector3Int(0,12,0),0);
        Check(s.LastResolutionCount==small&&small<8,"Distant population increased fitting work");
        Check(ReferenceEquals(remote.Resolved,remoteState)&&remote.ResolvedRevision==remoteRevision,"Distant geometry was revisited");
        s.Remove(first.Id);Check(s.LastResolutionCount<8&&s.TryGet(probe.Id,out _),"Local removal visited the settlement or removed independent construction");
        using var reference=new BuildResolution();Compare(s,reference);
        Debug.Log("BUILD LOCAL WORK PASS: "+s.Pieces.Count+" resident pieces; local addition resolved "+small+" records.");
    }
    private static IDictionary Groups(BuildRenderer renderer)=>(IDictionary)typeof(BuildRenderer).GetField("groups",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);
    private static object Field(object obj,string name)=>obj.GetType().GetField(name).GetValue(obj);
    private static void RenderGroups()
    {
        var d=ScriptableObject.CreateInstance<BuildDefinition>();d.kind=BuildPartKind.Beam;d.contentId="validation.incremental";d.preserveAuthoredMesh=true;d.sizeUnits=new Vector3Int(4,4,4);
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);d.mesh=cube.GetComponent<MeshFilter>().sharedMesh;d.material=new Material(Shader.Find("Custom/BuildingWoodMatte")) { enableInstancing=true };Object.DestroyImmediate(cube);
        var cameraObject=new GameObject("Incremental rendering fixture");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.transform.position=new Vector3(0,601,-8);camera.transform.LookAt(new Vector3(0,600,0));camera.farClipPlane=3000;
        try
        {
            using var s=new BuildSession();var f=s.CreateFrame(new Vector3(0,600,0),0);
            var p=s.Add(d,f,default,0);var distant=s.Add(d,f,new Vector3Int(160,0,0),0);
            using var renderer=new BuildRenderer(s);using var second=new BuildRenderer(s);
            renderer.Draw(camera,3000,100);int count=renderer.CommittedInstanceCount;
            var stable=Groups(renderer).Values.Cast<object>().ToArray();var batches=stable.ToDictionary(g=>g,g=>((IList)Field(g,"Batches"))[0]);
            var originalBounds=stable.ToDictionary(g=>g,g=>(Bounds)Field(batches[g],"Bounds"));
            var added=s.Add(d,f,new Vector3Int(0,0,8),0);renderer.Draw(camera,3000,100);
            Check(renderer.LastUpdatedPieces==1&&renderer.LastRebuiltGroups==1,"One-piece edit rebuilt unrelated groups");
            foreach(var g in stable)Check(ReferenceEquals(batches[g],((IList)Field(g,"Batches"))[0]),"Existing batch arrays were replaced");
            var preview=new BuildResolvedState { mesh=d.mesh,auxiliaryMesh=d.mesh };
            renderer.SetPreview(new Dictionary<ulong,BuildResolvedState>{{p.Id,preview}});renderer.Draw(camera,3000,100);
            Check(renderer.CommittedInstanceCount==count+1&&renderer.PreviewInstanceCount==2,"Preview leaked into committed batches");
            renderer.SetPreview(null);renderer.Draw(camera,3000,100);Check(renderer.PreviewInstanceCount==0&&renderer.CommittedInstanceCount==count+1&&renderer.LastRebuiltGroups==0,"Clearing a preview retained geometry or rebuilt stable groups");
            s.Remove(added.Id);second.Draw(camera,3000,100);renderer.Draw(camera,3000,100);
            Check(renderer.CommittedInstanceCount==count&&second.CommittedInstanceCount==count,"Delayed renderer lost pending mutations");
            foreach(var g in stable)Check((Bounds)Field(batches[g],"Bounds")==originalBounds[g],"Removed extremity retained stale culling bounds");
            // One visual can contribute more than 500 attachments to the same spatial/material group.
            var attachments=new BuildRenderAttachment[600];for(int i=0;i<attachments.Length;i++)attachments[i]=new BuildRenderAttachment { mesh=d.mesh,material=d.material,localMatrix=Matrix4x4.identity };
            p.Resolved=new BuildResolvedState { mesh=d.mesh,boxes=p.Resolved.boxes,attachments=attachments };
            using var large=new BuildRenderer(s);large.Draw(camera,3000,0);
            Check(large.CommittedInstanceCount==602,"Large group lost attachments");
            Check(Groups(large).Values.Cast<object>().Any(g=>((IList)Field(g,"Batches")).Count==2),"500-instance group did not split");
            large.SetPreview(new Dictionary<ulong,BuildResolvedState>{{p.Id,preview}});large.Draw(camera,3000,0);
            Check(large.CommittedInstanceCount==602&&large.PreviewInstanceCount==2,"Split preview changed committed instances");
            s.Remove(p.Id);large.Draw(camera,3000,0);Check(large.CommittedInstanceCount==1&&large.PreviewInstanceCount==0,"Removing a previewed piece left attachments or overlays");
            large.Dispose();s.Remove(distant.Id);large.Draw(camera,3000,0);Check(large.LastUpdatedPieces==0,"Disposed renderer retained mutation subscription");
        }
        finally {Object.DestroyImmediate(d.material);Object.DestroyImmediate(d);Object.DestroyImmediate(cameraObject);}
    }
}
