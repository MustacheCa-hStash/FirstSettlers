using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Cursor = UnityEngine.Cursor;

public static class BuildingPrototypeValidation
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Near(Vector3 actual, Vector3 expected, string message) => Check((actual - expected).sqrMagnitude < .000001f, message + $" ({actual} != {expected})");
    private static void Call(object obj, string method) => obj.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, null);
    private static void Set(Object obj, string field, Object value)
    { var serialized = new SerializedObject(obj); serialized.FindProperty(field).objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }

    [MenuItem("Tools/Building/Validate Prototype (synthetic)")]
    public static void Run()
    {
        checks = 0;
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        bool stairLinked = AssetDatabase.LoadAssetAtPath<GameObject>(W21StairSetup.ModelPath) != null;
        int roofCount=System.IO.File.Exists(ThatchRoofSetup.ModelPath)?1:0;
        Check(catalog != null && catalog.presets.Length == (stairLinked ? 7 : 6)+roofCount, "Building catalog is missing or a ready wood option is absent.");
        Check(stairLinked == (catalog.Find(W21StairSetup.ContentId) != null),"W21 readiness/catalog registration disagrees.");
        ValidateAssets(catalog); ValidateGeometry(catalog); ValidateAimIntent(catalog); ValidateSkyGuidance(catalog); ValidateGuideLifecycle(catalog);
        ValidateCornerKit(catalog); ValidateCompleteRoom(catalog); ValidateStoreys(catalog); ValidateSession(catalog); ValidatePhysicsAndUI(catalog);
        ValidateInfill(catalog); ValidateInfillDefaults(catalog); ValidatePillarSkyGuidance(catalog);
        ValidateWattle(catalog); ValidateBottomAttachments(catalog); ValidateCornerJointAim(catalog);
        Debug.Log("BUILDING PROTOTYPE PASS: " + checks + " checks; dimensions/colliders, rotated discrete frames, face connections, cross-cell records, " +
            "alternate support and detached cycles, foundation placement, blockers, pooled query identity and UI lifecycle.");
    }
    public static void RunBatch()
    {
        try { BuildingPrototypeSetup.CreateAssets(); Run(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    private static void ValidateAssets(BuildCatalog catalog)
    {
        foreach (var definition in catalog.presets)
        {
            if (definition.kind == BuildPartKind.Roof)
            {
                Check(definition.mesh.subMeshCount==1 && definition.material.enableInstancing && BuildOccupancy.Custom(definition),"Roof geometry/material/occupied shape missing.");
                Check(definition.authoringPrefab.GetComponentsInChildren<MeshCollider>().Length==2 && definition.authoringPrefab.GetComponent<BoxCollider>()==null,"Roof occupies attic with a box.");
                continue;
            }
            Near(definition.mesh.bounds.center, definition.LocalBounds.center, "Mesh pivot/bounds disagree.");
            Near(definition.mesh.bounds.size, definition.LocalBounds.size, "Mesh dimensions disagree.");
            if (definition.kind == BuildPartKind.Stair)
            {
                var ramp = definition.authoringPrefab.GetComponent<MeshCollider>();
                Check(ramp != null && ramp.convex && !ramp.isTrigger && ramp.sharedMesh == definition.collisionMesh,"Stair walking hull is missing.");
                Near(definition.collisionMesh.bounds.size,definition.LocalBounds.size,"Stair walking bounds changed.");
            }
            else
            {
                var collider = definition.authoringPrefab.GetComponent<BoxCollider>();
                Near(collider.center, definition.LocalBounds.center, "Collider centre is wrong."); Near(collider.size, definition.LocalBounds.size, "Collider size is wrong.");
                Check(!collider.isTrigger,"Solid collider is disabled.");
            }
            Check(definition.material.enableInstancing,"Instancing is disabled.");
            if (definition.kind == BuildPartKind.Wall)
            {
                string name = definition.contentId == WattleWallSetup.ContentId ? "Wattle wall" :
                    definition.contentId == SplitPlankWallSetup.InfillContentId ? "Two-plank infill wall" : "Split-plank wood wall";
                Check(definition.displayName == name, "The authored wood wall picker name is wrong.");
                Check(definition.material.shader.name == SplitPlankWallSetup.ShaderName, "The wall does not use matte wood lighting.");
                Check(definition.material.GetTexture("_BaseMap") == AssetDatabase.LoadAssetAtPath<Texture2D>(SplitPlankWallSetup.TexturePath), "The wood atlas is missing.");
                Check(definition.mesh.subMeshCount == 1 && definition.mesh.vertexCount > 24, "The imported wall geometry is missing.");
                Check(definition.authoringPrefab.GetComponent<MeshFilter>().sharedMesh == definition.mesh, "The authoring and runtime wall meshes differ.");
            }
            else if (definition.kind == BuildPartKind.Corner)
            {
                Check(definition.displayName == "Bay post" && definition.contentId == "build.prototype.corner", "Bay post replacement lost its name or stable ID.");
                Check(definition.material == catalog.presets[0].material && definition.material.GetTexture("_BaseMap") != null,
                    "Bay post does not share the wood atlas/material.");
                Check(definition.mesh.name.StartsWith("Bay post") && definition.mesh.subMeshCount == 1, "Bay post still references placeholder visuals.");
                Check(AssetDatabase.GetAssetPath(definition.authoringPrefab) == BayPostSetup.PrefabPath && definition.authoringPrefab.GetComponent<MeshFilter>().sharedMesh == definition.mesh,
                    "Bay post prefab references stale corner visuals.");
                Check(definition.material.GetShaderPassEnabled("ShadowCaster") && definition.authoringPrefab.GetComponent<MeshRenderer>().shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On,
                    "Bay post casting is disabled.");
                var baked = WattleWallSetup.BakeStaticMesh(AssetDatabase.LoadAssetAtPath<GameObject>(BayPostSetup.ModelPath), BayPostSetup.Size, "Bay post", out _);
                try
                {
                    var expected = baked.uv; var actual = definition.mesh.uv;
                    Check(expected.Length == actual.Length, "Bay post UV0 was lost.");
                    for (int i=0; i<expected.Length; i++) Check(expected[i] == actual[i], "Bay post authored UVs changed.");
                    var vertices = definition.mesh.vertices; var importedVertices = baked.vertices;
                    Check(vertices.Length == importedVertices.Length, "Bay post imported vertex count changed.");
                    for (int i=0; i<vertices.Length; i++) Near(vertices[i], importedVertices[i], "Bay post geometry differs from the uploaded FBX.");
                }
                finally { Object.DestroyImmediate(baked); }
            }
            else if (definition.kind == BuildPartKind.Stair)
                Check(definition.material == catalog.presets[0].material && definition.mesh.subMeshCount == 1,"Stair does not use the single opaque wood material.");
            else Check(definition.material.GetTexture("_BaseMap") == null, "An unrelated prototype material changed.");
        }
        Near(catalog.presets[0].LocalBounds.size, new Vector3(3.5f, 2.75f, .25f), "Wall contract changed.");
        Near(catalog.presets[3].LocalBounds.size, new Vector3(.25f, 2.75f, .25f), "Flush corner contract changed.");
        Check(catalog.panel != null && catalog.layout != null, "Build UI assets are absent.");
    }
    private static void ValidateWattle(BuildCatalog catalog)
    {
        var wattle = catalog.Find(WattleWallSetup.ContentId);
        Check(wattle != null && catalog.presets[5] == wattle, "Wattle was not appended after the existing five options.");
        string[] order = { "build.prototype.wall", "build.prototype.floor", "build.prototype.foundation", "build.prototype.corner", SplitPlankWallSetup.InfillContentId, WattleWallSetup.ContentId };
        for (int i = 0; i < order.Length; ++i) Check(catalog.presets[i].contentId == order[i], "Catalog order changed.");
        Check(wattle.kind == BuildPartKind.Wall && wattle.IsSurfaceWall && !wattle.IsWallInfill && wattle.wallEndInsetUnits == 1, "Wattle uses the wrong surface placement rules.");
        Check(wattle.sizeUnits == new Vector3Int(14,11,1) && wattle.minimumUnits == Vector3Int.zero, "Wattle grid envelope changed.");
        Near(wattle.mesh.bounds.min, Vector3.zero, "Wattle runtime origin is wrong.");
        Near(wattle.mesh.bounds.max, new Vector3(3.5f,2.75f,.25f), "Wattle runtime dimensions are wrong.");
        Check(wattle.material != catalog.presets[0].material && wattle.material.GetFloat("_AlphaClip") == 1 &&
            Mathf.Abs(wattle.material.GetFloat("_Cutoff")-.5f) < .00001f && wattle.material.GetFloat("_Cull") == 0 &&
            wattle.material.IsKeywordEnabled("_ALPHATEST_ON"), "Wattle is not a dedicated two-sided cutout material.");
        Check(wattle.material.renderQueue == 2450 && wattle.material.GetTag("RenderType", false) == "TransparentCutout", "Wattle is not in the cutout queue.");
        foreach (int index in new[] { 0, 4 })
            Check(catalog.presets[index].material.GetFloat("_AlphaClip") == 0 && catalog.presets[index].material.GetFloat("_Cull") == 2 &&
                !catalog.presets[index].material.IsKeywordEnabled("_ALPHATEST_ON"), "Existing wood stopped being opaque/back-face culled.");
        var model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(WattleWallSetup.ModelPath);
        var rebaked = WattleWallSetup.Bake(model, out _);
        try
        {
            var expected = rebaked.uv; var actual = wattle.mesh.uv;
            Check(expected.Length == actual.Length && expected.Length == wattle.mesh.vertexCount, "Wattle UV0 was lost.");
            for (int i = 0; i < expected.Length; ++i) Check(expected[i] == actual[i], "Wattle UV0 changed while baking transforms.");
            var vertices = wattle.mesh.vertices; var triangles = wattle.mesh.triangles;
            var atlas = new Texture2D(2,2,TextureFormat.RGBA32,false);
            atlas.LoadImage(System.IO.File.ReadAllBytes(SplitPlankWallSetup.TexturePath));
            int lower = 0, upper = 0;
            int railTriangles = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]]; var b = vertices[triangles[i+1]]; var c = vertices[triangles[i+2]];
                float xSpan = Mathf.Max(a.x,b.x,c.x)-Mathf.Min(a.x,b.x,c.x);
                float ySpan = Mathf.Max(a.y,b.y,c.y)-Mathf.Min(a.y,b.y,c.y);
                var u0 = actual[triangles[i]]; var u1 = actual[triangles[i+1]]; var u2 = actual[triangles[i+2]];
                if (Mathf.Abs(xSpan-1.75f) > .0001f || (Mathf.Abs(ySpan-1.75f) > .0001f && Mathf.Abs(ySpan-1) > .0001f))
                {
                    Vector2 sample = (u0+u1+u2)/3;
                    Check(atlas.GetPixelBilinear(sample.x,sample.y).a > .99f,"A timber rail samples the transparent weave region."); railTriangles++; continue;
                }
                Check(Mathf.Abs(Mathf.Min(u0.x,u1.x,u2.x)-.50805664f) < .0001f && Mathf.Abs(Mathf.Max(u0.x,u1.x,u2.x)-.99194336f) < .0001f,
                    "Wattle U rectangle changed or rotated.");
                float vMin = Mathf.Min(u0.y,u1.y,u2.y), vMax = Mathf.Max(u0.y,u1.y,u2.y);
                if (Mathf.Abs(ySpan-1.75f) < .0001f)
                {
                    Check(Mathf.Abs(vMin-.00805664f) < .0001f && Mathf.Abs(vMax-.49194336f) < .0001f,"Lower wattle V rectangle changed."); lower++;
                }
                else
                {
                    // The supplied FBX's upper islands are shifted upward in V by
                    // 0.0131767 versus the notes, but keep the exact 4/7 span.
                    // Preserve the authored UVs, verified against the source above.
                    Check(vMin >= .00805664f-.0001f && vMax <= .49194336f+.0001f &&
                        Mathf.Abs((vMax-vMin)-(.49194336f-.00805664f)*4/7) < .0001f,"Upper wattle lost its 4/7 span or left the weave patch.");
                    Debug.Log($"WATTLE AUTHORED UPPER UV: V={vMin:F8}..{vMax:F8}; preserved from FBX (notes specify 0.00805664..0.28456334)."); upper++;
                }
            }
            Check(lower == 4 && upper == 4, "The four authored weave faces are missing.");
            Check(railTriangles == 24,"The two solid timber rails are missing.");
            Object.DestroyImmediate(atlas);
        }
        finally { Object.DestroyImmediate(rebaked); }
        var host = new GameObject("Wattle placement fixture"); BuildGameplay gameplay = null;
        try
        {
            var world = host.AddComponent<BuildWorld>(); if (world.Session == null) Call(world,"Awake");
            foreach (int yaw in new[] { 0,1,7 })
            {
                var frame = world.Session.CreateFrame(new Vector3(30000+yaw*20,30000,30000),yaw);
                var foundation = world.Session.Add(catalog.presets[2],frame,default,0,true);
                var ownFrame = world.Session.Frame(foundation.OwnFrameId);
                Vector3 aim = BuildGeometry.WorldPoint(ownFrame,new Vector3Int(8,0,0));
                var preview = BuildPlacement.Solve(wattle,aim,Vector3.up,foundation,ownFrame,0,0,default);
                var opaque = BuildPlacement.Solve(catalog.presets[0],aim,Vector3.up,foundation,ownFrame,0,0,default);
                Near(preview.Origin,opaque.Origin,"Wattle wall did not use the existing full-panel snap.");
                Check(preview.WorldYaw == opaque.WorldYaw,"Wattle heading differs from a full wood panel.");
                foreach (float x in new[] { .25f, 2f, 3.75f })
                foreach (float z in new[] { .25f, 2f, 3.75f })
                {
                    var surface = BuildPlacement.Solve(wattle, ownFrame.Origin + BuildGeometry.Rotation(yaw) * new Vector3(x,0,z),
                        Vector3.up, foundation, ownFrame, 0, 0, default);
                    Vector3 local = BuildGeometry.LocalPoint(ownFrame, surface.Origin);
                    Check(Mathf.Abs(local.y + wattle.mesh.bounds.min.y) < .001f && surface.Hint == "Wall on surface",
                        "Wattle mesh bottom left the foundation's top plane.");
                    Check(Mathf.Abs(local.z - z) < .26f, "Wattle interior placement was forced to a perimeter edge.");
                    world.Validate(ref surface); Check(surface.Valid, "Wattle surface-grid placement failed support/collision: " + surface.Message);
                }
                var placed = world.Commit(preview); Check(placed != null && placed.Supported,"Wattle placement/support rejected.");
                world.Validate(ref preview); Check(!preview.Valid,"Overlapping wattle was accepted.");
                gameplay?.Dispose(); gameplay = new BuildGameplay(world.Session); gameplay.Update(placed.Origin,32,40,8);
                BuildGameplayProxy proxy = null;
                foreach (var candidate in Object.FindObjectsByType<BuildGameplayProxy>()) if (candidate.Record == placed) proxy = candidate;
                Check(proxy != null && proxy.Shape is BoxCollider,"Wattle did not get a full pooled wall box.");
                Near(proxy.Shape.center,new Vector3(1.75f,1.375f,.125f),"Wattle collider center changed.");
                Near(proxy.Shape.size,new Vector3(3.5f,2.75f,.25f),"Wattle collider size changed.");
                Physics.SyncTransforms();
                var rotation = BuildGeometry.Rotation(placed.WorldYawStep);
                var ray = new Ray(placed.Origin + rotation*new Vector3(1.5f,1,-1),rotation*Vector3.forward);
                Check(proxy.Shape.Raycast(ray,out _,3),"Visual weave gaps incorrectly opened the physics wall.");
                world.Remove(foundation.Id); Check(!placed.Supported,"Wattle retained disconnected support.");
                world.Session.Add(catalog.presets[2],frame,default,0,true); Check(placed.Supported,"Wattle did not reconnect to replacement support.");
            }
        }
        finally { gameplay?.Dispose(); Object.DestroyImmediate(host); Physics.SyncTransforms(); }
    }
    private static void ValidateBottomAttachments(BuildCatalog catalog)
    {
        foreach (int yaw in new[] { 0, 1, 3, 7 })
        foreach (var source in catalog.presets)
        foreach (var selected in catalog.presets)
        {
            // These assertions describe flat box bottom faces. Sloped roofs
            // use the real underside sockets tested by ThatchRoofValidation.
            if(source.kind==BuildPartKind.Roof || selected.kind==BuildPartKind.Roof) continue;
            var frame = new BuildGridFrame { Origin = new Vector3(-60,10,56), YawStep = (byte)yaw };
            var target = new BuildPieceRecord { Definition = source, Origin = frame.Origin, WorldYawStep = (byte)yaw };
            Vector3 aim = source.LocalBounds.center; aim.y = source.LocalBounds.min.y;
            Vector3 hit = frame.Origin + BuildGeometry.Rotation(yaw) * aim;
            var preview = BuildPlacement.Solve(selected, hit, Vector3.down, target, frame, 0, 0, default,
                frame.Origin + BuildGeometry.Rotation(yaw) * new Vector3(2,-2,3));
            Check(Mathf.Abs(preview.Origin.y + selected.LocalBounds.max.y - (frame.Origin.y + source.LocalBounds.min.y)) < .001f,
                "Bottom-face attachment used the top elevation: " + selected.displayName + " under " + source.displayName);
            Check(!preview.TopAttachment && !BuildGeometry.Overlaps(selected.LocalBounds, preview.Origin, preview.WorldYaw,
                source.LocalBounds, target.Origin, target.WorldYawStep), "Bottom-face attachment overlaps its source.");
            Check(BuildGeometry.Connects(selected.LocalBounds, preview.Origin, preview.WorldYaw,
                source.LocalBounds, target.Origin, target.WorldYawStep), "Bottom-face attachment lost face support.");
        }
        var host = new GameObject("Roof underside attachment fixture");
        try
        {
            var world = host.AddComponent<BuildWorld>(); if (world.Session == null) Call(world,"Awake");
            var frame = world.Session.CreateFrame(new Vector3(34000,34000,34000),0);
            world.Session.Add(catalog.presets[2],frame,default,0,true);
            world.Session.Add(catalog.presets[0],frame,new Vector3Int(1,0,0),0,false);
            var roof = world.Session.Add(catalog.presets[1],frame,new Vector3Int(0,12,0),0,false);
            Check(roof.Supported,"Underside roof fixture has no foundation path.");
            var own = world.Session.Frame(roof.OwnFrameId);
            var wall = BuildPlacement.Solve(catalog.presets[0], own.Origin + new Vector3(2,-.25f,4), Vector3.down,
                roof, own,0,0,default);
            Near(BuildGeometry.LocalPoint(frame,wall.Origin),new Vector3(3.75f,0,4),"Wall beneath a roof failed to bridge its underside to foundation height.");
            var placed = world.Commit(wall); Check(placed != null && placed.Supported,"Wall attached beneath a roof was rejected.");
        }
        finally { Object.DestroyImmediate(host); Physics.SyncTransforms(); }
    }
    private static void ValidateCornerJointAim(BuildCatalog catalog)
    {
        var host = new GameObject("Enclosed corner aim fixture"); BuildGameplay gameplay = null;
        var oldLock = Cursor.lockState; bool oldVisible = Cursor.visible;
        try
        {
            var controller = host.AddComponent<BuildingController>(); var world = host.GetComponent<BuildWorld>();
            if (world.Session == null) Call(world,"Awake"); Call(controller,"Start"); controller.Toggle();
            var resolve = typeof(BuildingController).GetMethod("TryResolveAim",BindingFlags.Instance|BindingFlags.NonPublic);
            var targetField = typeof(BuildingController).GetField("target",BindingFlags.Instance|BindingFlags.NonPublic);
            foreach (int yaw in new[] { 0,1,3,7 })
            {
                var frame = world.Session.CreateFrame(new Vector3(32000+yaw*20,32000,32000),yaw);
                Quaternion rotation = BuildGeometry.Rotation(yaw);
                Vector3 At(Vector3 p) => frame.Origin + rotation * p;
                world.Session.Add(catalog.presets[2],frame,default,0,true);
                var firstWall = world.Session.Add(catalog.presets[0],frame,new Vector3Int(1,0,0),0,false);
                world.Session.Add(catalog.presets[0],frame,new Vector3Int(0,0,15),2,false);
                var corner = world.Session.Add(catalog.presets[3],frame,default,0,false);
                gameplay?.Dispose(); gameplay = new BuildGameplay(world.Session); gameplay.Update(frame.Origin,32,40,8);
                controller.Select(catalog.presets[3]);
                Vector3 viewer = At(new Vector3(.5f,1.6f,2));
                Ray Aim(Vector3 p) => new(viewer,(At(p)-viewer).normalized);
                object[] Resolve(Vector3 p)
                {
                    var args = new object[] { Aim(p),null,null,null,null,false };
                    Check((bool)resolve.Invoke(controller,args),"Enclosed-corner aim failed to resolve."); return args;
                }
                var joint = Resolve(new Vector3(.26f,2.6f,.125f));
                Check(joint[3] == firstWall && joint[4] == corner,"Adjacent wall stole the near-joint pillar target.");
                var stack = BuildPlacement.Solve(catalog.presets[3],(Vector3)joint[1],(Vector3)joint[2],corner,
                    world.Session.Frame(corner.OwnFrameId),0,0,default,viewer);
                Near(BuildGeometry.LocalPoint(frame,stack.Origin),new Vector3(0,2.75f,0),"Enclosed post stack shifted onto the wall end.");
                world.Validate(ref stack); Check(stack.Valid,"Two enclosing walls blocked a valid post stack: " + stack.Message);
                targetField.SetValue(controller,corner);
                var sky = Resolve(new Vector3(.125f,3.25f,.25f));
                Check(sky[4] == corner && (bool)sky[5],"Enclosed corner lost sky continuation through its adjacent wall tops.");
                var far = Resolve(new Vector3(2,2.6f,.125f));
                Check(far[4] == firstWall,"A distant wall hit incorrectly attracted a corner stack.");
                var blocker = new GameObject("Corner aim obstruction") { layer = GameplayLayers.WorldSolid };
                blocker.transform.SetParent(host.transform); blocker.transform.position = (viewer+At(new Vector3(.26f,2.6f,.125f)))*.5f;
                blocker.AddComponent<BoxCollider>().size = Vector3.one * .1f; Physics.SyncTransforms();
                var blocked = Resolve(new Vector3(.26f,2.6f,.125f));
                Check(blocked[4] == null,"Corner joint acquisition bypassed an unrelated obstacle.");
                Object.DestroyImmediate(blocker); Physics.SyncTransforms();
                var hiddenBlocker = new GameObject("Obstruction behind the corner's adjacent wall") { layer = GameplayLayers.WorldSolid };
                hiddenBlocker.transform.SetParent(host.transform);
                hiddenBlocker.transform.position = Vector3.Lerp(viewer,At(new Vector3(.125f,3.25f,.25f)),.85f);
                hiddenBlocker.AddComponent<BoxCollider>().size = Vector3.one * .1f; Physics.SyncTransforms();
                var hidden = Resolve(new Vector3(.125f,3.25f,.25f));
                Check(hidden[4] != corner,"Corner guide skipped an unrelated obstacle behind its connected wall.");
                Object.DestroyImmediate(hiddenBlocker); Physics.SyncTransforms();
            }
        }
        finally { gameplay?.Dispose(); Object.DestroyImmediate(host); Cursor.lockState=oldLock; Cursor.visible=oldVisible; Physics.SyncTransforms(); }
    }
    private static void ValidateInfill(BuildCatalog catalog)
    {
        var wall = catalog.presets[0]; var foundation = catalog.presets[2];
        var infill = catalog.Find(SplitPlankWallSetup.InfillContentId);
        Check(infill != null && Array.FindAll(catalog.presets, p => p != null && p.contentId == infill.contentId).Length == 1,
            "Infill must be present exactly once in the picker.");
        Near(infill.LocalBounds.size, new Vector3(.5f, 2.75f, .25f), "Infill logical box changed.");
        Check(infill.wallEndInsetUnits == 0 && Mathf.Abs(infill.WallBaySpan - .5f) < .00001f,
            "Infill reserves unwanted end posts.");
        Check(infill.material == wall.material, "Infill does not reuse the matte wood material.");
        Check(infill.IsWallInfill && !wall.IsWallInfill, "Infill placement mode is missing or affected the full panel.");
        foreach (int yaw in new[] { 0, 1, 3, 7 })
        {
            var session = new BuildSession(); var frame = session.CreateFrame(new Vector3(-12, 10, 20), yaw);
            Vector3 At(Vector3 p) => BuildGeometry.WorldPoint(frame, BuildGeometry.Ticks(p));
            Vector3 normal = BuildGeometry.Rotation(yaw) * Vector3.back;
            session.Add(foundation, frame, default, 0, true);
            var left = session.Add(wall, frame, new Vector3Int(1, 0, 0), 0, false);
            var right = session.Add(wall, frame, new Vector3Int(17, 0, 0), 0, false);
            Check(left.Supported && !right.Supported, "Bridge fixture should have one anchored wall and one disconnected wall.");
            var fromLeft = BuildPlacement.Solve(infill, At(new Vector3(3.74f, .5f, .125f)), normal,
                left, session.Frame(left.OwnFrameId), yaw, 0, default);
            Near(fromLeft.Origin, At(new Vector3(3.75f, 0, 0)), "Infill did not snap directly after the full panel.");
            var fromRight = BuildPlacement.Solve(infill, At(new Vector3(4.26f, .5f, .125f)), normal,
                right, session.Frame(right.OwnFrameId), yaw, 0, default);
            Near(fromRight.Origin, fromLeft.Origin, "Infill does not fill the same gap from both wall ends.");
            foreach (float height in new[] { .5f, 1.6f, 2.5f })
            {
                var upperEnd = BuildPlacement.Solve(infill, At(new Vector3(3.74f,height,.125f)), normal,
                    left, session.Frame(left.OwnFrameId), yaw, 0, default);
                Near(upperEnd.Origin, fromLeft.Origin, "Eye-height infill aim selected stacking instead of the seam.");
                Check(upperEnd.WorldYaw == left.WorldYawStep, "Wall-end infill turned perpendicular to the wall.");
            }
            Check(!BuildGeometry.Overlaps(infill.LocalBounds, fromLeft.Origin, fromLeft.WorldYaw, wall.LocalBounds, left.Origin, left.WorldYawStep) &&
                !BuildGeometry.Overlaps(infill.LocalBounds, fromLeft.Origin, fromLeft.WorldYaw, wall.LocalBounds, right.Origin, right.WorldYawStep),
                "Infill overlaps a neighbouring full wall.");
            var bridge = session.Add(infill, fromLeft.Frame, fromLeft.Anchor, fromLeft.YawStep, false);
            Check(bridge.Connections.Contains(left.Id) && bridge.Connections.Contains(right.Id) && bridge.Supported && right.Supported,
                "Infill did not connect and support both adjacent wall panels.");
            session.Remove(bridge.Id);
            Check(!right.Supported && left.Supported, "Removing the infill did not revoke the disconnected wall's support.");
        }
    }
    private static void ValidateInfillDefaults(BuildCatalog catalog)
    {
        var infill = catalog.Find(SplitPlankWallSetup.InfillContentId);
        var foundation = catalog.presets[2]; var pillar = catalog.presets[3];
        for (int yaw = 0; yaw < 8; yaw++)
        {
            var frame = new BuildGridFrame { Origin = new Vector3(-40,10,32), YawStep = (byte)yaw };
            Quaternion rotation = BuildGeometry.Rotation(yaw);
            Vector3 At(Vector3 p) => frame.Origin + rotation * p;
            var surface = new BuildPieceRecord { Definition = foundation, Origin = frame.Origin, WorldYawStep = (byte)yaw };
            var post = new BuildPieceRecord { Definition = pillar, Origin = frame.Origin, WorldYawStep = (byte)yaw };
            foreach (int face in new[] { 0, 2, 4, 6 })
            {
                Vector3 direction = BuildGeometry.Rotation(face) * Vector3.forward;
                Vector3 viewer = At(new Vector3(2,1.6f,2) + direction * 3);
                var preview = BuildPlacement.Solve(infill, At(new Vector3(2,0,2)), Vector3.up, surface, frame, 0, 0, default, viewer);
                Check(preview.WorldYaw == BuildGeometry.Turn(yaw + face), "Surface infill presents its edge instead of its broad face to the viewer.");
                Check(BuildGeometry.Connects(infill.LocalBounds, preview.Origin, preview.WorldYaw,
                    foundation.LocalBounds, surface.Origin, surface.WorldYawStep), "Grid infill lost surface support.");
                var turned = BuildPlacement.Solve(infill, At(new Vector3(2,0,2)), Vector3.up, surface, frame, 0, 1, new Vector3Int(1,0,0), viewer);
                Check(turned.WorldYaw == BuildGeometry.Turn(preview.WorldYaw + 1), "Infill default overrode manual rotation.");
                Vector3 postHit = face % 4 == 0 ? new Vector3(.2f,1.6f,face == 0 ? .25f : 0) :
                    new Vector3(face == 2 ? .25f : 0,1.6f,.2f);
                Vector3 postViewer = At(new Vector3(.125f,1.6f,.125f) + direction * 3);
                var beside = BuildPlacement.Solve(infill, At(postHit), rotation * direction, post, frame, 0, 0, default, postViewer);
                Check(beside.WorldYaw == BuildGeometry.Turn(yaw + face), "Infill inherited the pillar's perpendicular corner extension.");
                Check(BuildGeometry.Connects(infill.LocalBounds, beside.Origin, beside.WorldYaw,
                    pillar.LocalBounds, post.Origin, post.WorldYawStep), "Post-aligned infill lost end support.");
                Check(!BuildGeometry.Overlaps(infill.LocalBounds, beside.Origin, beside.WorldYaw,
                    pillar.LocalBounds, post.Origin, post.WorldYawStep), "Post-aligned infill penetrated its post.");
                Bounds relative = BuildGeometry.WorldBounds(infill.LocalBounds, BuildGeometry.LocalPoint(frame, beside.Origin), face);
                Near(relative.center, face % 4 == 0 ? new Vector3(.5f,1.375f,.125f) : new Vector3(.125f,1.375f,.5f),
                    "Post infill did not extend across the viewed face.");
            }
            var seam = BuildPlacement.Solve(infill, At(new Vector3(3.99f,0,.125f)), Vector3.up, surface, frame, 0, 0, default,
                At(new Vector3(4,1.6f,3)));
            Near(BuildGeometry.LocalPoint(frame, seam.Origin), new Vector3(3.75f,0,0), "Surface infill was clamped away from a seam across two bays.");
        }
    }
    private static void ValidatePillarSkyGuidance(BuildCatalog catalog)
    {
        var pillar = catalog.presets[3];
        for (int yaw = 0; yaw < 8; yaw++)
        foreach (int face in new[] { 0, 2, 4, 6 })
        {
            var frame = new BuildGridFrame { Origin = new Vector3(-48,10,40), YawStep = (byte)yaw };
            var target = new BuildPieceRecord { Definition = pillar, Origin = frame.Origin, WorldYawStep = (byte)yaw };
            Quaternion rotation = BuildGeometry.Rotation(yaw);
            Vector3 At(Vector3 p) => frame.Origin + rotation * p;
            Vector3 normal = BuildGeometry.Rotation(face) * Vector3.forward;
            Vector3 centre = new(.125f,0,.125f), facePoint = centre + normal * .125f;
            Vector3 viewer = At(centre + new Vector3(0,1.6f,0) + normal * 3);
            Ray Aim(float y, float tangent = 0) => new(viewer, (At(facePoint + Vector3.up * y +
                Vector3.Cross(Vector3.up, normal) * tangent) - viewer).normalized);
            var guide = new BuildSurfaceAimGuide(); guide.Capture(target, frame, rotation * normal, viewer);
            Check(guide.TryContinue(Aim(3.25f), 8, pillar.LocalBounds.size.y, .15f, out var hit, out var hitNormal),
                "Pillar sky guidance failed on a side face.");
            Near(hitNormal, rotation * normal, "Pillar sky guidance switched faces.");
            var stack = BuildPlacement.Solve(pillar, hit, hitNormal, target, frame, 0, 0, default, viewer);
            Near(BuildGeometry.LocalPoint(frame, stack.Origin), new Vector3(0,2.75f,0), "Sky pillar did not stack directly above.");
            Check(stack.Hint == "Stack above" && BuildGeometry.Connects(pillar.LocalBounds, target.Origin, target.WorldYawStep,
                pillar.LocalBounds, stack.Origin, stack.WorldYaw), "Sky pillar stack lost its hint or support.");
            Check(!BuildGeometry.Overlaps(pillar.LocalBounds, target.Origin, target.WorldYawStep,
                pillar.LocalBounds, stack.Origin, stack.WorldYaw), "Sky pillar stack penetrated its source.");
            Check(!guide.TryContinue(Aim(3.25f,.3f), 8, 2.75f, .15f, out _, out _), "Pillar sky guide retained sideways aim too far away.");
            Check(!guide.TryContinue(Aim(6.2f), 8, 2.75f, .15f, out _, out _), "Pillar sky guide extended indefinitely upwards.");
            Check(!guide.TryContinue(Aim(3.25f), 1, 2.75f, .15f, out _, out _), "Pillar sky guide ignored reach.");
            guide.Capture(target, frame, Vector3.up, viewer);
            Check(guide.TryContinue(Aim(3.25f), 8, 2.75f, .15f, out _, out _), "Pillar top hit could not seed its viewer-side guide.");
        }
    }
    private static void ValidateGeometry(BuildCatalog catalog)
    {
        Check(BuildGeometry.Tick(-.375f) == -2 && BuildGeometry.Tick(.375f) == 2, "Negative-coordinate snapping is asymmetric.");
        Check(BuildGeometry.Turn(-1) == 7 && BuildGeometry.Turn(8) == 0, "Rotation wrapping failed.");
        var frame = new BuildGridFrame { Origin = new Vector3(12, 6, -7), YawStep = 1 };
        Vector3 world = BuildGeometry.WorldPoint(frame, new Vector3Int(16, 0, 0));
        Near(BuildGeometry.LocalPoint(frame, world), new Vector3(4, 0, 0), "Diagonal grid roundtrip failed.");
        Check(Mathf.Abs(Vector3.Distance(frame.Origin, world) - 4) < .00001f, "Diagonal placement changed component length.");
        var wall = catalog.presets[0]; var floor = catalog.presets[1]; var foundation = catalog.presets[2];
        var ground = BuildPlacement.Solve(foundation, new Vector3(-1.11f, 5.13f, 2.91f), Vector3.up, null, null, 1, 0, default);
        Check(ground.WorldYaw == 1 && Mathf.Abs(ground.Origin.y - 5.5f) < .001f, "Ground foundation did not quantize footing/elevation.");
        var target = new BuildPieceRecord { Definition = foundation, WorldYawStep = 1 };
        var attached = BuildPlacement.Solve(wall, frame.Origin + BuildGeometry.Rotation(1) * new Vector3(2, 0, 0), Vector3.up, target, frame, 0, 0, default);
        Check(attached.WorldYaw == 1, "A diagonal floor failed to align the wall.");
        Near(attached.Origin, frame.Origin + BuildGeometry.Rotation(1) * new Vector3(.25f, 0, 0), "Wall did not reserve the corner slot.");
        var nudged = BuildPlacement.Solve(wall, frame.Origin + BuildGeometry.Rotation(1) * new Vector3(2, 0, 0), Vector3.up, target, frame, 0, 1, new Vector3Int(1, 1, 0));
        Check(nudged.WorldYaw == 2, "Relative 45-degree rotation failed.");
        Near(BuildGeometry.LocalPoint(frame, nudged.Origin), new Vector3(.5f, .25f, 0), "Nudges left the local lattice.");
        Check(BuildGeometry.Connects(foundation.LocalBounds, frame.Origin, 1, wall.LocalBounds, frame.Origin, 1), "A wall on a diagonal foundation has no face connection.");
        Check(!BuildGeometry.Overlaps(foundation.LocalBounds, frame.Origin, 1, wall.LocalBounds, frame.Origin, 1), "Allowed face contact counts as penetration.");
        Check(BuildGeometry.Overlaps(wall.LocalBounds, Vector3.zero, 0, wall.LocalBounds, Vector3.zero, 1), "Rotated penetration was missed.");
        Check(!BuildGeometry.Connects(floor.LocalBounds, Vector3.zero, 0, floor.LocalBounds, new Vector3(4, 0, 4), 0), "Corner-only floor contact transmits support.");
        Check(BuildGeometry.Distance(floor.LocalBounds, Vector3.zero, 1, new Vector3(.1f, 0, 2.5f)) > 1,
            "Rotated empty AABB corners captured snapping.");
        var onTop = BuildPlacement.Solve(floor, frame.Origin, Vector3.up, target, frame, 0, 0, default);
        Check(!BuildGeometry.Overlaps(foundation.LocalBounds, frame.Origin, 1, floor.LocalBounds, onTop.Origin, onTop.WorldYaw), "Floor placement intersects foundation.");
    }
    private static void ValidateCornerKit(BuildCatalog catalog)
    {
        var wall = catalog.presets[0]; var corner = catalog.presets[3]; var foundation = catalog.presets[2];
        foreach (int yaw in new[] { 0, 1, 3, 7 })
        {
            var session = new BuildSession(); var frame = session.CreateFrame(new Vector3(-12, 10, 20), yaw);
            var basePiece = session.Add(foundation, frame, default, 0, true);
            var baseFrame = session.Frame(basePiece.OwnFrameId);
            Vector3 At(Vector3 p) => BuildGeometry.WorldPoint(baseFrame, BuildGeometry.Ticks(p));
            var pieces = new List<BuildPieceRecord>();
            var aims = new[] { new Vector3(2,0,0), new Vector3(0,0,2), new Vector3(2,0,4), new Vector3(4,0,2) };
            foreach (var aim in aims)
            {
                var preview = BuildPlacement.Solve(wall, At(aim), Vector3.up, basePiece, baseFrame, 0, 0, default);
                foreach (var other in pieces) Check(!BuildGeometry.Overlaps(wall.LocalBounds, preview.Origin, preview.WorldYaw,
                    other.Definition.LocalBounds, other.Origin, other.WorldYawStep), "Perpendicular walls intersect.");
                pieces.Add(session.Add(wall, baseFrame, preview.Anchor, preview.YawStep, false));
            }
            foreach (var aim in new[] { Vector3.zero, new Vector3(4,0,0), new Vector3(4,0,4), new Vector3(0,0,4) })
            {
                var preview = BuildPlacement.Solve(corner, At(aim), Vector3.up, basePiece, baseFrame, 0, 0, default);
                foreach (var other in pieces) Check(!BuildGeometry.Overlaps(corner.LocalBounds, preview.Origin, preview.WorldYaw,
                    other.Definition.LocalBounds, other.Origin, other.WorldYawStep), "Corner plug intersects another piece.");
                var plug = session.Add(corner, baseFrame, preview.Anchor, preview.YawStep, false); pieces.Add(plug);
                Check(plug.Supported && plug.Connections.Count == 3, "Corner did not touch its foundation and two walls.");
            }
            foreach (var piece in pieces)
            {
                Bounds b = piece.Definition.LocalBounds;
                for (int x = 0; x < 2; x++) for (int z = 0; z < 2; z++)
                {
                    Vector3 vertex = piece.Origin + BuildGeometry.Rotation(piece.WorldYawStep) * new Vector3(x == 0 ? b.min.x : b.max.x, 0, z == 0 ? b.min.z : b.max.z);
                    Vector3 local = BuildGeometry.LocalPoint(baseFrame, vertex);
                    Check(local.x >= -.001f && local.x <= 4.001f && local.z >= -.001f && local.z <= 4.001f, "Piece protrudes outside foundation footprint.");
                }
            }
            var firstWall = pieces[0]; var wallFrame = session.Frame(firstWall.OwnFrameId);
            var endCap = BuildPlacement.Solve(corner, firstWall.Origin + BuildGeometry.Rotation(firstWall.WorldYawStep) * new Vector3(3.49f,1,0),
                BuildGeometry.Rotation(firstWall.WorldYawStep) * Vector3.back, firstWall, wallFrame, 0, 0, default);
            Near(BuildGeometry.LocalPoint(baseFrame, endCap.Origin), new Vector3(3.75f,0,0), "Wall-end corner snapping missed the reserved slot.");
            var startCap = BuildPlacement.Solve(corner, firstWall.Origin, BuildGeometry.Rotation(firstWall.WorldYawStep) * Vector3.back, firstWall, wallFrame, 0, 0, default);
            Near(BuildGeometry.LocalPoint(baseFrame, startCap.Origin), Vector3.zero, "Wall-start corner snapping missed the reserved slot.");
            var extension = BuildPlacement.Solve(wall, firstWall.Origin + BuildGeometry.Rotation(firstWall.WorldYawStep) * new Vector3(3.49f,1,0),
                Vector3.forward, firstWall, wallFrame, 0, 0, default);
            Near(BuildGeometry.LocalPoint(baseFrame, extension.Origin), new Vector3(3.75f,0,0), "Consecutive panels do not touch directly.");
            Check(BuildGeometry.Connects(wall.LocalBounds, firstWall.Origin, firstWall.WorldYawStep,
                wall.LocalBounds, extension.Origin, extension.WorldYaw), "Direct wall extension has no supporting end contact.");
            var leftExtension = BuildPlacement.Solve(wall, firstWall.Origin, Vector3.forward, firstWall, wallFrame, 0, 0, default);
            Near(BuildGeometry.LocalPoint(baseFrame, leftExtension.Origin), new Vector3(-3.25f,0,0), "Reverse wall extension does not touch directly.");
            var seamPlug = pieces[5]; var plugFrame = session.Frame(seamPlug.OwnFrameId);
            var adjacent = BuildPlacement.Solve(corner, seamPlug.Origin + BuildGeometry.Rotation(yaw) * new Vector3(.25f, 1, .125f),
                BuildGeometry.Rotation(yaw) * Vector3.right, seamPlug, plugFrame, 0, 0, default);
            Near(BuildGeometry.LocalPoint(baseFrame, adjacent.Origin), new Vector3(4,0,0), "Adjacent quarter plugs could not fill a straight seam.");
        }
    }
    private static void ValidateAimIntent(BuildCatalog catalog)
    {
        var wall = catalog.presets[0]; var floor = catalog.presets[1]; var pillar = catalog.presets[3]; var foundation = catalog.presets[2];
        foreach (int yaw in new[] { 0, 1, 3, 7 })
        {
            var session = new BuildSession(); var frame = session.CreateFrame(new Vector3(-20,10,12), yaw);
            var basePiece = session.Add(foundation, frame, default, 0, true); var baseFrame = session.Frame(basePiece.OwnFrameId);
            Vector3 World(Vector3 p) => baseFrame.Origin + BuildGeometry.Rotation(yaw) * p;
            var allWalls = new List<BuildPieceRecord>();
            foreach (var edge in new[] { new Vector3(2,0,0), new Vector3(0,0,2), new Vector3(2,0,4), new Vector3(4,0,2) })
            {
                var initial = BuildPlacement.Solve(wall, World(edge), Vector3.up, basePiece, baseFrame, 0, 0, default);
                allWalls.Add(session.Add(wall, baseFrame, initial.Anchor, initial.YawStep, false));
            }
            foreach (var placed in allWalls)
            {
                var local = session.Frame(placed.OwnFrameId); Quaternion rotation = BuildGeometry.Rotation(local.YawStep);
                Vector3 At(Vector3 p) => local.Origin + rotation * p;
                var stack = BuildPlacement.Solve(wall, At(new Vector3(.5f,2,.25f)), rotation * Vector3.forward, placed, local, 0, 0, default);
                Near(BuildGeometry.LocalPoint(local, stack.Origin), new Vector3(0,2.75f,0), "Upper side hit did not stack a wall.");
                Check(BuildGeometry.Connects(wall.LocalBounds, placed.Origin, placed.WorldYawStep, wall.LocalBounds, stack.Origin, stack.WorldYaw), "Stacked wall has no support contact.");
                Check(!BuildGeometry.Overlaps(wall.LocalBounds, placed.Origin, placed.WorldYawStep, wall.LocalBounds, stack.Origin, stack.WorldYaw), "Stacked wall intersects its parent.");
                var lower = BuildPlacement.Solve(wall, At(new Vector3(3, .5f, .25f)), rotation * Vector3.forward, placed, local, 0, 0, default);
                Near(BuildGeometry.LocalPoint(local, lower.Origin), new Vector3(3.5f,0,0), "Lower hit stopped extending a wall directly.");
                // Hit X varies deliberately: ceilings must centre on the whole bay, not the cursor position.
                foreach (float x in new[] { .5f, 3f })
                {
                    var roof = BuildPlacement.Solve(floor, At(new Vector3(x,3,.25f)), rotation * Vector3.forward, placed, local, 0, 0, default, World(new Vector3(2,2,2)));
                    Near(BuildGeometry.LocalPoint(local, roof.Origin), new Vector3(-.25f,3,0), "Ceiling is not centred and fully seated on the wall.");
                    Bounds roofBounds = BuildGeometry.WorldBounds(floor.LocalBounds, roof.Origin, roof.WorldYaw);
                    Near(roofBounds.center, World(new Vector3(2,2.875f,2)), "Different enclosing walls selected different room ceilings.");
                    foreach (var other in allWalls) Check(!BuildGeometry.Overlaps(floor.LocalBounds, roof.Origin, roof.WorldYaw,
                        other.Definition.LocalBounds, other.Origin, other.WorldYawStep), "Ceiling intersects an enclosing wall.");
                    Check(BuildGeometry.Connects(floor.LocalBounds, roof.Origin, roof.WorldYaw, wall.LocalBounds, placed.Origin, placed.WorldYawStep), "Ceiling lost wall support.");
                }
                var outside = BuildPlacement.Solve(floor, At(new Vector3(2,3,0)), rotation * Vector3.back, placed, local, 0, 0, default);
                Near(BuildGeometry.LocalPoint(local, outside.Origin), new Vector3(-.25f,3,-3.75f), "Exterior wall face did not select an outward platform.");
                var top = BuildPlacement.Solve(floor, At(new Vector3(1,2.75f,.125f)), Vector3.up, placed, local, 0, 0, default, World(new Vector3(2,2,2)));
                Near(BuildGeometry.LocalPoint(local, top.Origin), new Vector3(-.25f,3,0), "Top-face hit ignored the viewer's side.");
                var shelf = BuildPlacement.Solve(floor, At(new Vector3(2,.75f,.25f)), rotation * Vector3.forward, placed, local, 0, 0, default);
                Near(BuildGeometry.LocalPoint(local, shelf.Origin), new Vector3(-.25f,.75f,.25f), "Lower wall face did not select a side platform.");
                Check(!BuildGeometry.Overlaps(floor.LocalBounds, shelf.Origin, shelf.WorldYaw, wall.LocalBounds, placed.Origin, placed.WorldYawStep), "Side platform penetrates the wall.");
                foreach (float y in new[] { .25f, .5f, .75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.25f, 2.5f, 2.75f })
                foreach (bool positive in new[] { false, true })
                {
                    var platform = BuildPlacement.Solve(floor, At(new Vector3(2,y,positive ? .25f : 0)),
                        rotation * (positive ? Vector3.forward : Vector3.back), placed, local, 0, 0, default);
                    Near(BuildGeometry.LocalPoint(local, platform.Origin), new Vector3(-.25f,y,positive ? .25f : -4), "Side platform lost a wall height tick.");
                    Check(!platform.TopAttachment && !BuildGeometry.Overlaps(floor.LocalBounds, platform.Origin, platform.WorldYaw,
                        wall.LocalBounds, placed.Origin, placed.WorldYawStep), "A side height tick was forced into a seated slab.");
                    Check(BuildGeometry.Connects(floor.LocalBounds, platform.Origin, platform.WorldYaw,
                        wall.LocalBounds, placed.Origin, placed.WorldYawStep), "Side height tick lost support contact.");
                }
                var retained = BuildPlacement.Solve(floor, At(new Vector3(2,2.74f,.25f)), rotation * Vector3.forward,
                    placed, local, 0, 0, default, null, true);
                Check(retained.TopAttachment, "Top-edge hysteresis flickers on small downward motion.");
                var released = BuildPlacement.Solve(floor, At(new Vector3(2,2.7f,.25f)), rotation * Vector3.forward,
                    placed, local, 0, 0, default, null, true);
                Check(!released.TopAttachment, "Top-edge hysteresis swallowed the final side-platform tick.");
            }
            foreach (var p in new[] { new Vector3(2.125f,0,2.125f), new Vector3(.625f,0,1.875f), new Vector3(3.375f,0,.875f) })
            {
                var candidate = BuildPlacement.Solve(pillar, World(p), Vector3.up, basePiece, baseFrame, 0, 0, default);
                Vector3 expected = new(BuildGeometry.Tick(p.x - .125f) * .25f,0,BuildGeometry.Tick(p.z - .125f) * .25f);
                Near(BuildGeometry.LocalPoint(baseFrame, candidate.Origin), expected, "Pillar was forced to a foundation corner.");
                Check(BuildGeometry.Connects(pillar.LocalBounds, candidate.Origin, candidate.WorldYaw, foundation.LocalBounds, basePiece.Origin, basePiece.WorldYawStep), "Interior pillar has no foundation support.");
            }
        }
    }
    private static void ValidateSkyGuidance(BuildCatalog catalog)
    {
        var wall = catalog.presets[0]; var floor = catalog.presets[1];
        var empty = new BuildSurfaceAimGuide();
        Check(!empty.TryContinue(new Ray(Vector3.zero, Vector3.up), 8, 1, .15f, out _, out _), "Sky guidance invented an unacquired wall.");
        foreach (int yaw in new[] { 0, 1, 3, 7 })
        foreach (bool positive in new[] { false, true })
        {
            var frame = new BuildGridFrame { Origin = new Vector3(-20,10,12), YawStep = (byte)yaw };
            var target = new BuildPieceRecord { Definition = wall, Origin = frame.Origin, WorldYawStep = (byte)yaw };
            Quaternion rotation = BuildGeometry.Rotation(yaw);
            Vector3 At(Vector3 p) => frame.Origin + rotation * p;
            float face = positive ? .25f : 0;
            Vector3 viewer = At(new Vector3(1.75f,1.6f,positive ? 3 : -3));
            Vector3 normal = rotation * (positive ? Vector3.forward : Vector3.back);
            Ray Aim(float x, float y) => new(viewer, (At(new Vector3(x,y,face)) - viewer).normalized);
            var guide = new BuildSurfaceAimGuide(); guide.Capture(target, frame, normal, viewer);
            Check(guide.TryContinue(Aim(1.75f,3.25f), 8, 1, .15f, out var hit, out var hitNormal), "Bounded sky continuation failed.");
            Near(hit, At(new Vector3(1.75f,3.25f,face)), "Sky ray left the acquired wall plane.");
            Near(hitNormal, normal, "Sky guide switched the viewed wall face.");
            var roof = BuildPlacement.Solve(floor, hit, hitNormal, target, frame, 0, 0, default, viewer);
            Near(BuildGeometry.LocalPoint(frame, roof.Origin), new Vector3(-.25f,3,positive ? 0 : -3.75f), "Sky roof is not seated toward the viewer.");
            Check(roof.TopAttachment, "Sky roof lost its top attachment.");
            Check(guide.TryContinue(Aim(1.75f,3.6f), 8, 1, .15f, out hit, out hitNormal), "Roof guidance ended too early.");
            var higherAim = BuildPlacement.Solve(floor, hit, hitNormal, target, frame, 0, 0, default, viewer);
            Near(higherAim.Origin, roof.Origin, "Looking higher made the roof float away from its wall.");
            Check(!guide.TryContinue(Aim(1.75f,3.8f), 8, 1, .15f, out _, out _), "Roof guidance continued beyond its finite region.");
            Check(!guide.TryContinue(Aim(1.75f,2.5f), 8, 1, .15f, out _, out _), "Sky guide inferred below the wall top.");
            Check(!guide.TryContinue(Aim(3.7f,3.25f), 8, 1, .15f, out _, out _), "Sky guide retained aim beyond the wall ends.");
            Check(guide.TryContinue(Aim(3.6f,3.25f), 8, 1, .15f, out _, out _), "Wall-end acquisition padding was lost.");
            Check(!guide.TryContinue(Aim(1.75f,3.25f), 1, 1, .15f, out _, out _), "Sky guide ignored ray reach.");
            Check(!guide.TryContinue(new Ray(viewer, Vector3.up), 8, 1, .15f, out _, out _), "Parallel sky ray produced an intersection.");
            Check(!guide.TryContinue(new Ray(viewer, normal), 8, 1, .15f, out _, out _), "Backward sky intersection was accepted.");
            Check(guide.TryContinue(Aim(1.75f,5.25f), 8, wall.LocalBounds.size.y, .15f, out hit, out hitNormal), "Stacked-wall selection region is too short.");
            var stack = BuildPlacement.Solve(wall, hit, hitNormal, target, frame, 0, 0, default, viewer);
            Near(BuildGeometry.LocalPoint(frame, stack.Origin), new Vector3(0,2.75f,0), "Sky wall did not select direct stacking.");
            guide.Capture(target, frame, Vector3.up, viewer);
            Check(guide.TryContinue(Aim(1.75f,3.25f), 8, 1, .15f, out _, out hitNormal), "Top face could not seed a viewer-side guide.");
            Near(hitNormal, normal, "Top-face guidance picked the wrong viewer side.");
        }
    }
    private static void ValidateGuideLifecycle(BuildCatalog catalog)
    {
        var host = new GameObject("Sky guide lifecycle fixture"); BuildGameplay gameplay = null;
        var oldLock = Cursor.lockState; bool oldVisible = Cursor.visible;
        try
        {
            var controller = host.AddComponent<BuildingController>(); var world = host.GetComponent<BuildWorld>();
            if (world.Session == null) Call(world, "Awake"); Call(controller, "Start"); controller.Toggle(); controller.Select(catalog.presets[1]);
            var frame = world.Session.CreateFrame(new Vector3(26000,26000,26000), 0);
            var wall = world.Session.Add(catalog.presets[0], frame, default, 0, true);
            gameplay = new BuildGameplay(world.Session); gameplay.Update(frame.Origin, 32, 40, 8);
            var resolve = typeof(BuildingController).GetMethod("TryResolveAim", BindingFlags.Instance | BindingFlags.NonPublic);
            var targetField = typeof(BuildingController).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector3 viewer = frame.Origin + new Vector3(1.75f,1.6f,3);
            Ray Aim(float x, float y) => new(viewer, (frame.Origin + new Vector3(x,y,.25f) - viewer).normalized);
            object[] Resolve(Ray ray, bool expected)
            {
                var args = new object[] { ray, null, null, null, null, false };
                Check((bool)resolve.Invoke(controller, args) == expected, "Controller sky resolution result disagrees."); return args;
            }
            void Seed()
            {
                var hit = Resolve(Aim(1.75f,2), true);
                Check(hit[3] == wall && hit[4] == wall && !(bool)hit[5], "Real wall hit did not acquire its guide.");
                targetField.SetValue(controller, wall);
            }
            Resolve(Aim(1.75f,3.25f), false); Seed();
            var sky = Resolve(Aim(1.75f,3.25f), true);
            Check(sky[3] == null && sky[4] == wall && (bool)sky[5], "Sky guide lost its target or offered removal of an inferred piece.");
            var blocker = new GameObject("Sky guide obstruction") { layer = GameplayLayers.WorldSolid };
            blocker.transform.SetParent(host.transform); blocker.transform.position = viewer + (frame.Origin + new Vector3(1.75f,3.25f,.25f) - viewer) * .5f;
            blocker.AddComponent<BoxCollider>().size = Vector3.one * .2f; Physics.SyncTransforms();
            var blocked = Resolve(Aim(1.75f,3.25f), true);
            Check(blocked[4] == null && !(bool)blocked[5], "Cached wall guidance passed through a real obstruction.");
            Object.DestroyImmediate(blocker); Physics.SyncTransforms();
            Resolve(Aim(1.75f,3.25f), false);
            Seed(); Resolve(Aim(4,3.25f), false); Resolve(Aim(1.75f,3.25f), false);
            Seed(); controller.OpenMenu(); Resolve(Aim(1.75f,3.25f), false);
            controller.Select(catalog.presets[1]); Seed(); controller.Select(catalog.presets[0]); Resolve(Aim(1.75f,3.25f), false);
            Seed(); var stacked = Resolve(Aim(1.75f,5.25f), true);
            Check((bool)stacked[5] && stacked[4] == wall, "Wall preset could not use its taller sky guide.");
            controller.Select(catalog.presets[1]); Seed();
            world.Remove(wall.Id); Resolve(Aim(1.75f,3.25f), false);
            var post = world.Session.Add(catalog.presets[3], frame, default, 0, true);
            gameplay.Update(frame.Origin, 32, 40, 8);
            viewer = frame.Origin + new Vector3(.125f,1.6f,3);
            Ray PillarAim(float y) => new(viewer, (frame.Origin + new Vector3(.125f,y,.25f) - viewer).normalized);
            controller.Select(catalog.presets[3]);
            var postHit = Resolve(PillarAim(2), true);
            Check(postHit[3] == post && !(bool)postHit[5], "Real pillar hit did not acquire the source piece.");
            targetField.SetValue(controller, post);
            var postSky = Resolve(PillarAim(3.25f), true);
            Check(postSky[3] == null && postSky[4] == post && (bool)postSky[5], "Controller did not continue the pillar into the sky.");
            var stackedPost = BuildPlacement.Solve(catalog.presets[3], (Vector3)postSky[1], (Vector3)postSky[2], post,
                world.Session.Frame(post.OwnFrameId), 0, 0, default, viewer);
            world.Validate(ref stackedPost); Check(stackedPost.Valid, "Guided pillar failed ordinary collision/support validation.");
            controller.Select(catalog.presets[1]); Resolve(PillarAim(2), true); targetField.SetValue(controller, post);
            Resolve(PillarAim(3.25f), false);
            controller.Select(catalog.presets[3]); Resolve(PillarAim(2), true); targetField.SetValue(controller, post);
            world.Remove(post.Id); Resolve(PillarAim(3.25f), false);
            var cameraObject = new GameObject("Infill heading fixture"); cameraObject.transform.SetParent(host.transform);
            var camera = cameraObject.AddComponent<Camera>(); camera.transform.rotation = Quaternion.Euler(0,90,0);
            Set(controller, "viewCamera", camera);
            controller.Select(catalog.Find(SplitPlankWallSetup.InfillContentId));
            int heading = (int)typeof(BuildingController).GetField("worldHeading", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            Check(heading == 6, "Selecting infill retained the edge-on global heading instead of facing the viewer.");
            var groundInfill = BuildPlacement.Solve(catalog.Find(SplitPlankWallSetup.InfillContentId), frame.Origin, Vector3.up,
                null, null, BuildGeometry.Turn(heading + 1), 0, default);
            Check(groundInfill.WorldYaw == 7, "Infill ground default overrode a subsequent manual turn.");
        }
        finally
        {
            gameplay?.Dispose(); Object.DestroyImmediate(host); Cursor.lockState = oldLock; Cursor.visible = oldVisible; Physics.SyncTransforms();
        }
    }
    private static void ValidateStoreys(BuildCatalog catalog)
    {
        var host = new GameObject("Floor-bearing storey fixture");
        try
        {
            var world = host.AddComponent<BuildWorld>(); if (world.Session == null) Call(world, "Awake");
            foreach (int yaw in new[] { 0, 1 })
            {
                var frame = world.Session.CreateFrame(new Vector3(24000 + yaw * 20,24000,24000), yaw);
                var foundation = world.Session.Add(catalog.presets[2], frame, default, 0, true);
                var baseFrame = world.Session.Frame(foundation.OwnFrameId);
                var initial = BuildPlacement.Solve(catalog.presets[0], BuildGeometry.WorldPoint(baseFrame, new Vector3Int(8,0,0)),
                    Vector3.up, foundation, baseFrame, 0, 0, default);
                var lower = world.Commit(initial); Check(lower != null, "Storey fixture wall rejected.");
                var wallFrame = world.Session.Frame(lower.OwnFrameId); Quaternion rotation = BuildGeometry.Rotation(yaw);
                Vector3 At(Vector3 p) => wallFrame.Origin + rotation * p;
                var stack = BuildPlacement.Solve(catalog.presets[0], At(new Vector3(2,2,.25f)), rotation * Vector3.forward, lower, wallFrame, 0, 0, default);
                var upper = world.Commit(stack); Check(upper != null, "Direct upper wall rejected.");
                var roof = BuildPlacement.Solve(catalog.presets[1], At(new Vector3(2,3,.25f)), rotation * Vector3.forward, lower, wallFrame, 0, 0, default);
                world.Validate(ref roof);
                Check(!roof.Valid && roof.Message.StartsWith("Wall occupies the floor edge"), "A slab was inserted into an occupied wall joint.");
                var side = BuildPlacement.Solve(catalog.presets[1], At(new Vector3(2,2.5f,.25f)), rotation * Vector3.forward, lower, wallFrame, 0, 0, default);
                world.Validate(ref side); Check(side.Valid, "Side platform was incorrectly blocked by a directly stacked wall: " + side.Message);
                world.Remove(upper.Id);
                var slab = world.Commit(roof); Check(slab != null && slab.Supported, "Seated floor failed after removing the upper wall.");
                var floorFrame = world.Session.Frame(slab.OwnFrameId);
                var onFloor = BuildPlacement.Solve(catalog.presets[0], BuildGeometry.WorldPoint(floorFrame, new Vector3Int(8,0,0)),
                    Vector3.up, slab, floorFrame, 0, 0, default);
                Near(BuildGeometry.LocalPoint(wallFrame, onFloor.Origin), new Vector3(0,3,0), "Wall on a floor ignored slab thickness.");
                var nextWall = world.Commit(onFloor); Check(nextWall != null && nextWall.Supported, "Floor-bearing upper wall failed support/commit.");
            }
        }
        finally { Object.DestroyImmediate(host); Physics.SyncTransforms(); }
    }
    private static void ValidateCompleteRoom(BuildCatalog catalog)
    {
        var host = new GameObject("Complete room placement fixture");
        var groundObject = new GameObject("Room terrain") { layer = GameplayLayers.WorldSolid };
        Vector3 origin = new(22000, 22000, 22000);
        groundObject.transform.position = origin + Vector3.down * .5f;
        groundObject.AddComponent<BoxCollider>().size = new Vector3(40,1,40); groundObject.AddComponent<WorldGroundSurface>();
        try
        {
            var world = host.AddComponent<BuildWorld>(); if (world.Session == null) Call(world, "Awake"); Physics.SyncTransforms();
            foreach (int yaw in new[] { 0, 1 })
            {
                Vector3 offset = yaw == 0 ? Vector3.zero : Vector3.right * 12;
                var foundation = BuildPlacement.Solve(catalog.presets[2], origin + offset, Vector3.up, null, null, yaw, 0, default);
                var basePiece = world.Commit(foundation); Check(basePiece != null, "Room foundation rejected.");
                var frame = world.Session.Frame(basePiece.OwnFrameId);
                Vector3 At(Vector3 p) => frame.Origin + BuildGeometry.Rotation(yaw) * p;
                // Reverse the order between fixtures: posts must never be prerequisites for placing walls.
                void Walls()
                {
                    foreach (var aim in new[] { new Vector3(2,0,0), new Vector3(0,0,2), new Vector3(2,0,4), new Vector3(4,0,2) })
                    {
                        var preview = BuildPlacement.Solve(catalog.presets[0], At(aim), Vector3.up, basePiece, frame, 0, 0, default);
                        world.Validate(ref preview); Check(preview.Valid, "Four-sided wall placement rejected: " + preview.Message);
                        Check(world.Commit(preview) != null, "Wall failed commit.");
                    }
                }
                void Corners()
                {
                    foreach (var aim in new[] { Vector3.zero, new Vector3(4,0,0), new Vector3(4,0,4), new Vector3(0,0,4) })
                    {
                        var preview = BuildPlacement.Solve(catalog.presets[3], At(aim), Vector3.up, basePiece, frame, 0, 0, default);
                        world.Validate(ref preview); Check(preview.Valid, "Flush corner placement rejected: " + preview.Message);
                        var piece = world.Commit(preview); Check(piece != null && piece.Supported, "Corner failed support/commit.");
                        world.Validate(ref preview); Check(!preview.Valid, "Duplicate corner was accepted.");
                    }
                }
                if (yaw == 0) { Walls(); Corners(); } else { Corners(); Walls(); }
                var pillar = BuildPlacement.Solve(catalog.presets[3], At(new Vector3(2.125f,0,2.125f)), Vector3.up,
                    basePiece, frame, 0, 0, default);
                world.Validate(ref pillar); Check(pillar.Valid, "Interior pillar placement rejected: " + pillar.Message);
                Check(world.Commit(pillar) != null, "Interior pillar failed commit.");
                var enclosingWalls = new List<BuildPieceRecord>();
                foreach (var piece in world.Session.Pieces.Values)
                    if (piece.Definition.kind == BuildPartKind.Wall && Vector3.Distance(piece.Origin, basePiece.Origin) < 6) enclosingWalls.Add(piece);
                BuildPreview ceiling = default;
                foreach (var placed in enclosingWalls)
                {
                    var wallFrame = world.Session.Frame(placed.OwnFrameId); Quaternion rotation = BuildGeometry.Rotation(placed.WorldYawStep);
                    Vector3 aim = placed.Origin + rotation * new Vector3(.5f,2,.25f);
                    var stack = BuildPlacement.Solve(catalog.presets[0], aim, rotation * Vector3.forward, placed, wallFrame, 0, 0, default);
                    world.Validate(ref stack); Check(stack.Valid, "Upper-half wall stack rejected: " + stack.Message);
                    ceiling = BuildPlacement.Solve(catalog.presets[1], placed.Origin + rotation * new Vector3(.5f,3,.25f),
                        rotation * Vector3.forward, placed, wallFrame, 0, 0, default, At(new Vector3(2,1.6f,2)));
                    world.Validate(ref ceiling); Check(ceiling.Valid, "Inward ceiling in a closed room rejected: " + ceiling.Message);
                }
                Check(enclosingWalls.Count == 4 && world.Commit(ceiling) != null, "Closed-room ceiling failed commit.");
            }
        }
        finally { Object.DestroyImmediate(host); Object.DestroyImmediate(groundObject); Physics.SyncTransforms(); }
    }
    private static void ValidateSession(BuildCatalog catalog)
    {
        var session = new BuildSession(); var floor = catalog.presets[1]; var foundation = catalog.presets[2];
        var frame = session.CreateFrame(new Vector3(-4, 10, -4), 0);
        var left = session.Add(foundation, frame, default, 0, true);
        var middle = session.Add(floor, frame, new Vector3Int(16, 0, 0), 0, false);
        var right = session.Add(foundation, frame, new Vector3Int(32, 0, 0), 0, true);
        Check(middle.Supported && middle.Connections.Count == 2, "Bridge support connections are missing.");
        session.Remove(left.Id); Check(middle.Supported, "Removing one of two roots collapsed a supported bridge.");
        session.Remove(right.Id); Check(!middle.Supported, "Detached bridge falsely supports itself.");
        Near(session.Frame(left.OwnFrameId).Origin, left.Origin, "Removing an anchor discarded its grid frame.");
        var restored = session.Add(foundation, frame, default, 0, true); Check(middle.Supported, "Replacement support did not restore a detached piece.");
        var result = new List<BuildPieceRecord>();
        session.Query(new Bounds(new Vector3(.01f, 9.9f, -2), new Vector3(.02f, .1f, .1f)), result);
        Check(result.Contains(middle), "Cross-cell piece indexing lost geometry at a boundary.");
        var loopFrame = session.CreateFrame(new Vector3(40, 10, 40), 0);
        var loopA = session.Add(floor, loopFrame, default, 0, false);
        var loopB = session.Add(floor, loopFrame, new Vector3Int(16, 0, 0), 0, false);
        var loopC = session.Add(floor, loopFrame, new Vector3Int(16, 0, 16), 0, false);
        var loopD = session.Add(floor, loopFrame, new Vector3Int(0, 0, 16), 0, false);
        Check(!loopA.Supported && !loopB.Supported && !loopC.Supported && !loopD.Supported, "Disconnected cycle acquired support.");
        Check(session.Damage(restored.Id, 100) && !session.TryGet(restored.Id, out _) && !middle.Supported, "Damage did not update support and identity.");
    }
    private static void ValidatePhysicsAndUI(BuildCatalog catalog)
    {
        Vector3 origin = new(20000, 20000, 20000);
        var root = new GameObject("Building synthetic fixture"); root.transform.position = origin;
        var host = new GameObject("Building host"); host.transform.SetParent(root.transform, false);
        var groundObject = new GameObject("Fixture terrain") { layer = GameplayLayers.WorldSolid };
        groundObject.transform.position = origin + Vector3.down * .5f;
        var ground = groundObject.AddComponent<BoxCollider>(); ground.size = new Vector3(30, 1, 30); groundObject.AddComponent<WorldGroundSurface>();
        BuildMenuView view = null; BuildGameplay gameplay = null;
        var oldLock = Cursor.lockState; bool oldVisible = Cursor.visible;
        try
        {
            var world = host.AddComponent<BuildWorld>(); if (world.Session == null) Call(world, "Awake"); Physics.SyncTransforms();
            var foundation = BuildPlacement.Solve(catalog.presets[2], origin, Vector3.up, null, null, 1, 0, default);
            world.Validate(ref foundation); Check(foundation.Valid && foundation.Grounded, "Ground foundation was rejected: " + foundation.Message);
            var first = world.Commit(foundation); Check(first != null && first.Supported, "Foundation commit failed.");
            var unsupported = BuildPlacement.Solve(catalog.presets[0], origin + Vector3.right * 10, Vector3.up, null, null, 0, 0, default);
            world.Validate(ref unsupported); Check(!unsupported.Valid, "Unsupported wall was accepted.");
            var wall = BuildPlacement.Solve(catalog.presets[0], first.Origin + BuildGeometry.Rotation(first.WorldYawStep) * new Vector3(2, 0, 0), Vector3.up,
                first, world.Session.Frame(first.OwnFrameId), 0, 0, default);
            world.Validate(ref wall); Check(wall.Valid, "Supported wall was rejected: " + wall.Message);
            var placedWall = world.Commit(wall); Check(placedWall != null && placedWall.Supported, "Wall commit failed.");
            world.Validate(ref wall); Check(!wall.Valid, "Duplicate wall placement was accepted.");
            var blocked = BuildPlacement.Solve(catalog.presets[0], first.Origin + BuildGeometry.Rotation(first.WorldYawStep) * new Vector3(2, 0, 4), Vector3.up,
                first, world.Session.Frame(first.OwnFrameId), 0, 0, default);
            var blocker = new GameObject("Fixture player") { layer = GameplayLayers.Player };
            blocker.transform.SetParent(root.transform, true); blocker.transform.position = blocked.Origin + BuildGeometry.Rotation(blocked.WorldYaw) * blocked.Definition.LocalBounds.center;
            blocker.AddComponent<BoxCollider>().size = Vector3.one * .1f; Physics.SyncTransforms();
            world.Validate(ref blocked); Check(!blocked.Valid && blocked.Message == "Move clear of the preview", "Player penetration is not blocked.");
            Object.DestroyImmediate(blocker);
            world.Remove(first.Id); Check(!placedWall.Supported, "Wall retained support after its last foundation was removed.");
            var repair = BuildPlacement.Solve(catalog.presets[2], placedWall.Origin, Vector3.up, placedWall,
                world.Session.Frame(placedWall.OwnFrameId), 0, 0, default);
            world.Validate(ref repair); Check(repair.Valid && repair.Grounded, "Aiming at the detached wall could not replace its footing.");
            first = world.Commit(repair); Check(first != null && placedWall.Supported, "Replacing the footing did not restore support.");
            gameplay = new BuildGameplay(world.Session); gameplay.Update(first.Origin, 32, 40, 8);
            Check(gameplay.ActiveCount == 2, "Nearby pieces did not activate pooled colliders.");
            var proxies = Object.FindObjectsByType<BuildGameplayProxy>();
            BuildGameplayProxy proxy = null;
            foreach (var candidate in proxies) if (candidate.Record == placedWall) proxy = candidate;
            Check(proxy != null && proxy.GetComponent<MeshRenderer>() == null && proxy.TryGetInfo(out _), "Gameplay proxy owns rendering or lacks identity.");
            proxy.TryGetInfo(out var identity);
            gameplay.Update(origin + Vector3.one * 100, 32, 40, 8);
            Check(gameplay.ActiveCount == 0 && !identity.IsValid && world.Session.Pieces.Count == 2, "Collider release lost records or retained stale identity.");
            BuildDefinition clicked = null; view = new BuildMenuView(catalog, root.transform, item => clicked = item);
            view.SetState(true, true);
            var document = root.GetComponentInChildren<UIDocument>();
            Check(document.rootVisualElement.Q<VisualElement>("build-menu").style.display.value == DisplayStyle.Flex &&
                document.rootVisualElement.Query<Button>().ToList().Count == catalog.presets.Length, "Picker layout/preset population failed.");
            view.SetState(true, false); view.Show("Plain wall", "Ready", true);
            Check(document.rootVisualElement.Q<Label>("build-status").text == "Ready" &&
                document.rootVisualElement.Q<VisualElement>("build-hud").pickingMode == PickingMode.Ignore, "Placement HUD failed or captures input.");
            view.Dispose(); view = null;
            var controller = host.AddComponent<BuildingController>(); Call(controller, "Start"); controller.Toggle();
            var proximity = typeof(BuildingController).GetMethod("NearbyTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector3 At(Vector3 local) => first.Origin + BuildGeometry.Rotation(first.WorldYawStep) * local;
            Check(proximity.Invoke(controller, new object[] { At(new Vector3(2, -.3f, -.05f)) }) == first, "Small ground margin did not acquire the local frame.");
            Check(proximity.Invoke(controller, new object[] { At(new Vector3(2, -.3f, -.25f)) }) == null, "Distant ground retained a structure's bias.");
            typeof(BuildingController).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, first);
            Check(proximity.Invoke(controller, new object[] { At(new Vector3(2, -.3f, -.12f)) }) == first, "Snap hysteresis released too soon.");
            Check(proximity.Invoke(controller, new object[] { At(new Vector3(2, -.3f, -.16f)) }) == null, "Snap hysteresis retained a distant target.");
            Check(controller.Active && controller.MenuOpen && Cursor.lockState == CursorLockMode.None, "B mode did not release the menu cursor.");
            controller.Select(catalog.presets[0]); Check(controller.Active && !controller.MenuOpen, "Selection did not enter placement.");
            Check(controller.GameplayCursorRequested && !Cursor.visible, "Selection did not request gameplay capture.");
            controller.OpenMenu(); Check(!controller.GameplayCursorRequested && Cursor.visible, "Picker retained gameplay capture.");
            controller.Exit(); Check(!controller.Active && controller.GameplayCursorRequested && !Cursor.visible, "Exit restored a free cursor instead of gameplay.");
            typeof(BuildingController).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, new object[] { true });
            Check(controller.GameplayCursorRequested && !Cursor.visible, "Focus regain did not request capture.");
        }
        finally
        {
            view?.Dispose(); gameplay?.Dispose();
            foreach (var build in root.GetComponentsInChildren<BuildWorld>(true)) Call(build, "OnDestroy");
            Object.DestroyImmediate(root); Object.DestroyImmediate(groundObject); Cursor.lockState = oldLock; Cursor.visible = oldVisible; Physics.SyncTransforms();
        }
    }
}
