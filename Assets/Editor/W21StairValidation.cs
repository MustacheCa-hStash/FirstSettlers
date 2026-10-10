using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Uses the real CharacterMotor and pooled walking hull; never loads the saved scene.</summary>
public static class W21StairValidation
{
    private static int checks;
    private static void Check(bool ok,string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
    private static void Invoke(object target,string name) => target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);

    public static void RunBatch()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            BuildingPrototypeSetup.CreateAssets(); BuildingPrototypeValidation.Run();
            var stair = W21StairSetup.PrepareDefinition(); AssetDatabase.SaveAssets();
            checks = 0;
            Check(stair.kind == BuildPartKind.Stair && stair.sizeUnits == new Vector3Int(5,6,8) && stair.minimumUnits == Vector3Int.zero,"Stair grid contract changed.");
            Check(stair.collisionMesh.vertexCount == 6 && stair.collisionMesh.triangles.Length == 24,"Stair walking hull is not the continuous triangular prism.");
            Check((stair.collisionMesh.bounds.min).sqrMagnitude < .000001f && (stair.collisionMesh.bounds.max-W21StairSetup.Size).sqrMagnitude < .000001f,"Stair walking envelope changed.");
            var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            Check(stair.mesh != null || catalog.Find(W21StairSetup.ContentId) == null,"Unlinked stair was put in the live menu.");
            ValidatePlacement(stair,catalog);
            ValidateStarterWallClearance(stair,catalog);
            ValidatePool(stair,catalog.presets[0]);
            foreach (int yaw in new[] { 0,1,2,7 })
                foreach (float x in new[] { .43f,.625f,.82f })
                    foreach (bool down in new[] { false,true })
                        foreach (float delta in new[] { 1f/60,1f/120 })
                            foreach (int flights in new[] { 1,2 }) Walk(stair,catalog,yaw,x,down,delta,flights);
            Debug.Log($"W21 STAIR PASS: {checks} checks; 96 single/chained real-motor up/down walks, continuous rise through flight joins, near-side positions, 45-degree frames, toe/exit joins, stable starter snaps and advisory wall overlap, collider rays, support and pooled box/ramp transitions.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static void ValidateStarterWallClearance(BuildDefinition stair,BuildCatalog catalog)
    {
        foreach(int yaw in new[]{0,1,2,7})
        {
            var host=new GameObject("Permissive stair snap fixture");var w=host.AddComponent<BuildWorld>();if(w.Session==null)Invoke(w,"Awake");
            try
            {
                var f=w.Session.CreateFrame(new Vector3(26000+yaw*20,26000,26000),yaw);var q=BuildGeometry.Rotation(yaw);
                var floor=w.Session.Add(catalog.presets[2],f,default,0,true);
                var wall=w.Session.Add(catalog.presets[0],f,new Vector3Int(15,0,16),4,false);
                var raw=BuildPlacement.Solve(stair,f.Origin+q*new Vector3(.625f,0,1),Vector3.up,floor,w.Session.Frame(floor.OwnFrameId),0,0,default);
                Vector3 chosen=raw.Origin;w.Validate(ref raw);Check(raw.Valid && (raw.Origin-chosen).sqrMagnitude<.000001f,"Clearance warning shifted the chosen stair snap");
                w.Validate(ref raw);Check((raw.Origin-chosen).sqrMagnitude<.000001f,"Repeated validation drifted the stair snap");
                var first=w.Commit(raw);Check(first!=null,"Starter commit failed");
                var next=BuildPlacement.Solve(stair,first.Origin+q*new Vector3(.625f,1.5f,2),Vector3.up,first,w.Session.Frame(first.OwnFrameId),0,0,default);
                w.Validate(ref next);Check(next.Valid && next.Warning==BuildPlacementWarning.BuildOverlap && next.WarningPieceId==wall.Id,"Quarter-metre wall clash was not advisory");
                var second=w.Commit(next);Check(second!=null && second.Supported,"Overlapping continuation did not commit");
                w.Remove(first.Id);w.Remove(second.Id);
                var manual=BuildPlacement.Solve(stair,f.Origin+q*new Vector3(.625f,0,1),Vector3.up,floor,w.Session.Frame(floor.OwnFrameId),0,0,new Vector3Int(1,0,0));
                w.Validate(ref manual);Check((manual.Origin-(chosen+q*Vector3.right*.25f)).sqrMagnitude<.00002f,"Manual nudge was not exactly one unit");
            }finally{Object.DestroyImmediate(host);Physics.SyncTransforms();}
        }
    }

    private static void ValidatePlacement(BuildDefinition stair,BuildCatalog catalog)
    {
        var host = new GameObject("Stair placement fixture"); var world = host.AddComponent<BuildWorld>();
        if (world.Session == null) Invoke(world,"Awake");
        try
        {
            foreach (int yaw in new[] { 0,1,2,7 })
            {
                var frame = world.Session.CreateFrame(new Vector3(24000+yaw*20,24000,24000),yaw);
                var basePiece = world.Session.Add(catalog.presets[2],frame,default,0,true);
                var targetFrame = world.Session.Frame(basePiece.OwnFrameId);
                Vector3 aim = BuildGeometry.WorldPoint(targetFrame,new Vector3Int(2,0,4));
                var foot = BuildPlacement.Solve(stair,aim,Vector3.up,basePiece,targetFrame,0,0,default);
                var first = world.Commit(foot); Check(first != null && first.Supported,"Stair foot placement failed.");
                var firstFrame = world.Session.Frame(first.OwnFrameId);
                var secondPreview = BuildPlacement.Solve(stair,first.Origin+BuildGeometry.Rotation(yaw)*new Vector3(.625f,1.5f,2),Vector3.up,first,firstFrame,0,0,default);
                var second = world.Commit(secondPreview); Check(second != null && second.Supported,"Direct next stair flight lost its authored connector support.");
                Vector3 offset = Quaternion.Inverse(BuildGeometry.Rotation(yaw))*(second.Origin-first.Origin);
                Check((offset-new Vector3(0,1.5f,2)).sqrMagnitude < .000001f,"Stair continuation is not 1.5 m up and 2 m forward.");
                var landing = BuildPlacement.Solve(catalog.presets[1],second.Origin+BuildGeometry.Rotation(yaw)*new Vector3(.625f,1.5f,2),Vector3.up,second,world.Session.Frame(second.OwnFrameId),0,0,default);
                var topFloor = world.Commit(landing); Check(topFloor != null && topFloor.Supported,"Landing beyond the high end failed.");
                world.Remove(basePiece.Id); Check(!first.Supported && !second.Supported && !topFloor.Supported,"Detached stair chain retained support.");
                world.Session.Add(catalog.presets[2],frame,default,0,true); Check(first.Supported && second.Supported && topFloor.Supported,"Stair chain did not reconnect.");
                var highFrame = world.Session.Frame(topFloor.OwnFrameId);
                Vector3 sideAim = BuildGeometry.WorldPoint(highFrame,new Vector3Int(3,-1,0));
                var hanging = BuildPlacement.Solve(stair,sideAim,BuildGeometry.Rotation(yaw)*Vector3.back,topFloor,highFrame,0,0,default);
                float highY = (hanging.Origin+BuildGeometry.Rotation(hanging.WorldYaw)*new Vector3(0,1.5f,2)).y;
                Check(Mathf.Abs(highY-topFloor.Origin.y) < .00001f,"High-end attachment did not match floor walking elevation.");
                world.Validate(ref foot); Check(!foot.Valid,"Duplicate stair envelope was accepted.");
            }
        }
        finally { Object.DestroyImmediate(host); Physics.SyncTransforms(); }
    }

    private static void ValidatePool(BuildDefinition stair,BuildDefinition wall)
    {
        var session = new BuildSession(); var frame = session.CreateFrame(new Vector3(23000,23000,23000),0);
        var stairs = session.Add(stair,frame,default,0,true); var wood = session.Add(wall,frame,new Vector3Int(30,0,0),0,true);
        var root = new GameObject("Stair proxy reuse"); var proxy = root.AddComponent<BuildGameplayProxy>();
        try
        {
            proxy.Bind(wood); Check(proxy.Shape.enabled && proxy.ActiveShape == proxy.Shape,"Original box did not bind.");
            proxy.Bind(stairs); Physics.SyncTransforms();
            Check(!proxy.Shape.enabled && proxy.RampShape.enabled && proxy.RampShape.convex && proxy.ActiveShape == proxy.RampShape,"Pooled box remained active on a ramp.");
            foreach (float z in new[] { .05f,.5f,1.0f,1.70f,1.8f,1.99f })
            {
                var ray = new Ray(stairs.Origin+new Vector3(.625f,3,z),Vector3.down);
                Check(proxy.RampShape.Raycast(ray,out var hit,5),"Walking hull missed a vertical ray.");
                float expected = z/2*1.5f;
                Check(Mathf.Abs(hit.point.y-stairs.Origin.y-expected) < .02f,"Walking surface has a lip or wrong height.");
            }
            proxy.Unbind(); Check(!proxy.Shape.enabled && !proxy.RampShape.enabled && !proxy.TryGetInfo(out _),"Ramp release retained collision/query state.");
            proxy.Bind(wood); Check(proxy.Shape.enabled && !proxy.RampShape.enabled && proxy.ActiveShape == proxy.Shape,"Ramp remained active on a reused wall proxy.");
        }
        finally { Object.DestroyImmediate(root); Physics.SyncTransforms(); }
    }

    private static void Walk(BuildDefinition stair,BuildCatalog catalog,int yaw,float x,bool down,float dt,int flights)
    {
        using var session = new BuildSession(); var origin = new Vector3(100,10,100); var rotation = BuildGeometry.Rotation(yaw);
        var frame = session.CreateFrame(origin,yaw);
        session.Add(catalog.presets[2],frame,new Vector3Int(-8,0,-8),0,true);
        for (int flight = 0; flight < flights; ++flight) session.Add(stair,frame,new Vector3Int(0,6*flight,8*flight),0,false);
        session.Add(catalog.presets[1],frame,new Vector3Int(0,6*flights,8*flights),0,false);
        using var gameplay = new BuildGameplay(session); gameplay.Update(origin,32,40,8); Physics.SyncTransforms();
        var rig = new GameObject("W21 motor traversal"); rig.layer = GameplayLayers.Player;
        var controller = rig.AddComponent<CharacterController>();
        // Matches the saved player's CC configuration, read without opening that scene.
        controller.height = 1.8f; controller.center = new Vector3(0,.9f,0); controller.radius = .4f;
        controller.slopeLimit = 50; controller.stepOffset = .3f; controller.skinWidth = .08f; controller.minMoveDistance = 0;
        float endZ = 2*flights+.8f, topY = 1.5f*flights;
        rig.transform.SetPositionAndRotation(origin+rotation*new Vector3(x,down ? topY+.08f : .08f,down ? endZ : -.8f),rotation);
        var motor = rig.AddComponent<CharacterMotor>(); Invoke(motor,"Awake"); Physics.SyncTransforms();
        float previousZ = down ? endZ : -.8f, previousY = down ? topY+.08f : .08f;
        float minJoinSlope = float.PositiveInfinity, maxJoinSlope = 0; int stalled = 0, airFrames = 0, joinSamples = 0;
        try
        {
            for (int i = 0; i < 30; ++i) motor.Simulate(new CharacterMoveCommand(Vector2.zero,false,false,false),dt);
            int limit = Mathf.CeilToInt(4/dt);
            for (int i = 0; i < limit; ++i)
            {
                motor.Simulate(new CharacterMoveCommand(new Vector2(0,down ? -1 : 1),false,false,false),dt);
                Vector3 local = Quaternion.Inverse(rotation)*(rig.transform.position-origin);
                if (i > 12 && Mathf.Abs(local.z-previousZ) < .001f) stalled++; else stalled = 0;
                if (flights == 2 && local.z > 1.5f && local.z < 2.5f && Mathf.Abs(local.z-previousZ) > .005f)
                {
                    float ratio = (local.y-previousY)/(local.z-previousZ);
                    minJoinSlope = Mathf.Min(minJoinSlope,ratio); maxJoinSlope = Mathf.Max(maxJoinSlope,ratio); joinSamples++;
                }
                previousZ = local.z; previousY = local.y;
                if (local.z > .2f && local.z < 2*flights-.4f && !motor.IsGrounded) airFrames++;
                Check(stalled < 20,$"Stair walk got stuck: yaw={yaw}, x={x}, down={down}, position={local}.");
                Check(local.y > -.2f && local.y < topY+.6f,"Character fell through or rose above the stair.");
                if ((!down && local.z > endZ) || (down && local.z < -.65f))
                {
                    Check(Mathf.Abs(local.y-(down ? 0 : topY)) < .12f,$"Stair exit did not seat on the adjoining floor: {local}.");
                    Check(airFrames == 0,$"Stair movement lost smooth ramp contact: yaw={yaw}, x={x}, down={down}, airborne={airFrames}.");
                    if (flights == 2) Check(joinSamples > 4 && minJoinSlope > .3f && maxJoinSlope < 1.1f,$"Flight join has a vertical pause or jump: min slope={minJoinSlope}, max={maxJoinSlope}.");
                    Debug.Log($"W21 WALK: flights={flights}, yaw={yaw}, x={x:F3}, down={down}, dt={dt:F5}, finish={local:F4}, airborneRampFrames={airFrames}, joinSlope={minJoinSlope:F4}..{maxJoinSlope:F4}");
                    return;
                }
            }
            throw new InvalidOperationException($"Stair traversal timed out: yaw={yaw}, x={x}, down={down}, footZ={previousZ}.");
        }
        finally { Object.DestroyImmediate(rig); Physics.SyncTransforms(); }
    }
}
