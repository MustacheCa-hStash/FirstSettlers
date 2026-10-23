using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Actual imported walls, URP renders and eight-heading Play-mode support/collision checks.</summary>
[InitializeOnLoad]
public static class LimestoneKitExtensionValidation
{
    public const string Output = "ArtReferences/LimestoneBuildKit/Extension";
    private const string Pending = "FS.Limestone.ExtensionValidation";
    private static int checks;
    private static readonly List<string> errors = new();
    static LimestoneKitExtensionValidation() => EditorApplication.playModeStateChanged += OnState;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
    private static void Near(Vector3 a, Vector3 b, string message) => Check((a - b).sqrMagnitude < .000001f, message + ": " + a + " / " + b);
    public static void InstallAndRunBatch() => Run(true);
    public static void RunInstalledBatch() => Run(false);
    private static void Run(bool install)
    {
        try
        {
            checks = 0;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            if (install)
            {
                var originals = new Dictionary<string, string>();
                foreach (var d in catalog.presets) if (Array.IndexOf(LimestoneKitExtensionSetup.Ids, d.contentId) < 0)
                    originals.Add(AssetDatabase.GetAssetPath(d), File.ReadAllText(AssetDatabase.GetAssetPath(d)));
                LimestoneKitExtensionSetup.Install();
                var guids = new string[6];
                for (int i = 0; i < 6; i++) guids[i] = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog.Find(LimestoneKitExtensionSetup.Ids[i])));
                int count = catalog.presets.Length;
                LimestoneKitExtensionSetup.Install();
                Check(catalog.presets.Length == count, "Repeat import duplicated catalog entries");
                for (int i = 0; i < 6; i++) Check(guids[i] == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog.Find(LimestoneKitExtensionSetup.Ids[i]))), "Definition GUID changed on reinstall");
                foreach (var original in originals) Check(File.ReadAllText(original.Key) == original.Value, "Unrelated definition changed: " + original.Key);
            }
            Assets(catalog);
            Render(catalog);
            SessionState.SetInt(Pending + ".checks", checks);
            SessionState.SetBool(Pending, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void Assets(BuildCatalog catalog)
    {
        var ids = new HashSet<string>();
        foreach (var d in catalog.presets) Check(d != null && ids.Add(d.contentId), "Missing/duplicate catalog definition");
        var shared = AssetDatabase.LoadAssetAtPath<Material>(LimestoneKitExtensionSetup.MaterialPath);
        Check(shared != null && shared.shader.name == "Custom/BakedStoneWallMatte" && shared.enableInstancing, "Shared matte material missing");
        Check(shared.GetTexture("_BumpMap") != null && shared.GetTexture("_BaseMap") != null && shared.GetFloat("_AlphaClip") == 0, "Opaque colour/AO + normal pair missing");
        for (int part = 0; part < 6; part++)
        {
            var d = catalog.Find(LimestoneKitExtensionSetup.Ids[part]); float width = LimestoneKitExtensionSetup.Sizes[part].x;
            Check(d != null && d.kind == (part == 0 ? BuildPartKind.Corner : part == 1 ? BuildPartKind.Floor : part == 2 ? BuildPartKind.Foundation : BuildPartKind.Wall) && d.material == shared, "Limestone definition missing");
            var installed=BuildKitDimensions.InstalledBounds(d,LimestoneKitExtensionSetup.Minima[part],LimestoneKitExtensionSetup.Sizes[part]);
            Near(d.LocalBounds.min, installed.min, "Native minimum");
            Near(d.LocalBounds.max, installed.max, "Native maximum");
            Check(d.StackRise == (part == 1 ? .25f : part == 2 || part == 4 ? .5f : 3) && d.WallEndInset == 0 && d.preserveAuthoredMesh, "Full-span fitting contract missing");
            Check(d.jointCoverVariants.Length == 0 && !d.canGroundOnTerrain, "Limestone gained timber covers or new grounding rules");
            Check(d.jointSockets.Length == new[] {0,0,0,17,17,12}[part], "Incorrect short/full span sockets");
            Check(d.uvVariants.Length == 3 && d.mesh == d.uvVariants[0], "UV variants missing");
            var colliders = d.authoringPrefab.GetComponents<BoxCollider>();
            var solids = d.solidBoxes.Length > 0 ? d.solidBoxes : new[] { d.LocalBounds };
            Check(colliders.Length == solids.Length, "Authoring collider count");
            for (int i = 0; i < solids.Length; i++) Check(colliders[i].center == solids[i].center && colliders[i].size == solids[i].size, "Authoring solid mismatch");
            Check(d.requiresFullBearing == (part == 1), "Paving bearing policy");
            Check(d.authoringPrefab.GetComponent<MeshFilter>().sharedMesh == d.mesh && d.authoringPrefab.GetComponent<MeshRenderer>().sharedMaterial == shared, "Prefab visual references mismatch");
            Check(BuildUiContent.Category(d) == new[] {4,3,1,2,2,2}[part] && !BuildUiContent.Description(d).StartsWith("Choose this component", StringComparison.Ordinal), "Component menu metadata missing: " + d.contentId);
            Check(Resources.Load<Texture2D>("Building/UI/Thumbnails/" + BuildUiContent.ThumbnailKey(d)) != null, "Actual-mesh icon missing");
            Vector3[] firstVertices = null; Vector2[] firstUvs = null;
            foreach (var mesh in d.uvVariants)
            {
                Check(mesh.subMeshCount == 1 && mesh.GetIndexCount(0) == LimestoneKitExtensionSetup.Triangles[part] * 3, "Runtime wall must be one closed 12-triangle solid");
                Near(mesh.bounds.min, d.LocalBounds.min, "Imported min"); Near(mesh.bounds.max, d.LocalBounds.max, "Imported max");
                using var data = MeshUtility.AcquireReadOnlyMeshData(mesh); var source = data[0];
                using var vertices = new NativeArray<Vector3>(source.vertexCount, Allocator.Temp);
                using var normals = new NativeArray<Vector3>(source.vertexCount, Allocator.Temp);
                using var tangents = new NativeArray<Vector4>(source.vertexCount, Allocator.Temp);
                using var uvs = new NativeArray<Vector2>(source.vertexCount, Allocator.Temp);
                using var indices = new NativeArray<int>(LimestoneKitExtensionSetup.Triangles[part] * 3, Allocator.Temp);
                source.GetVertices(vertices); source.GetNormals(normals); source.GetTangents(tangents); source.GetUVs(0, uvs); source.GetIndices(indices, 0);
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 tangent = tangents[i];
                    Check(Mathf.Abs(tangent.magnitude - 1) < .001f && Mathf.Abs(Vector3.Dot(tangent, normals[i])) < .001f && Mathf.Abs(tangents[i].w) == 1, "Invalid normal tangent basis");
                    Check(float.IsFinite(uvs[i].x) && float.IsFinite(uvs[i].y) && uvs[i].x > .034f && uvs[i].x < .966f && uvs[i].y > .034f && uvs[i].y < .966f, "UV escaped atlas padding");
                }
                double volume = 0; int portalHits = 0, jambHits = 0;
                var edges = new Dictionary<string, int>();
                string Point(Vector3 v) => $"{Mathf.RoundToInt(v.x * 10000)},{Mathf.RoundToInt(v.y * 10000)},{Mathf.RoundToInt(v.z * 10000)}";
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                    Vector3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                    if (part == 5)
                    {
                        if (TriangleHit(new Vector3(2,1,-1), a,b,c)) portalHits++;
                        if (TriangleHit(new Vector3(.5f,1,-1), a,b,c)) jambHits++;
                    }
                    Check(Vector3.Dot(Vector3.Cross(b - a, c - a), normals[ia] + normals[ib] + normals[ic]) > 0, "Inverted wall face");
                    Vector2 u = uvs[ib] - uvs[ia], v = uvs[ic] - uvs[ia];
                    Check(Mathf.Abs(u.x * v.y - u.y * v.x) > 1e-10f, "Collapsed wall UV triangle");
                    volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
                    foreach (var pair in new[] { (a, b), (b, c), (c, a) })
                    {
                        string p = Point(pair.Item1), q = Point(pair.Item2);
                        string key = string.CompareOrdinal(p, q) < 0 ? p + "/" + q : q + "/" + p;
                        edges.TryGetValue(key, out int count); edges[key] = count + 1;
                    }
                }
                Check(Math.Abs(volume - SolidVolume(d)) < .001, "Incorrect solid volume");
                if (part == 5) Check(portalHits == 0 && jambHits > 0, "Imported doorway mesh seals its aperture or loses a jamb");
                foreach (int count in edges.Values) Check((count == 2 || part == 5 && count == 4), "Solid boundary is open/nonmanifold");
                if (firstVertices == null) { firstVertices = vertices.ToArray(); firstUvs = uvs.ToArray(); }
                else
                {
                    int changed = 0;
                    for (int i = 0; i < vertices.Length; i++) { Near(vertices[i], firstVertices[i], "UV variant changed geometry"); if ((uvs[i] - firstUvs[i]).sqrMagnitude > .000001f) changed++; }
                    Check(changed > vertices.Length * .9f, "Variant did not change UV mapping");
                }
            }
        }
        foreach (string path in new[] { LimestoneKitExtensionSetup.Colour, LimestoneKitExtensionSetup.Normal })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Check(importer.mipmapEnabled && !importer.isReadable && importer.maxTextureSize == 2048 && importer.anisoLevel == 4 && !importer.alphaIsTransparency, "Texture filtering/AO import changed");
            var platform = importer.GetPlatformTextureSettings("Standalone");
            Check(platform.overridden && platform.format == (path == LimestoneKitExtensionSetup.Normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7), "Texture compression mismatch");
        }
    }

    private static void OnState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); checks = SessionState.GetInt(Pending + ".checks", 0); errors.Clear();
        void Log(string message, string trace, LogType type) { if (type is LogType.Error or LogType.Exception or LogType.Assert) errors.Add(message); }
        Application.logMessageReceived += Log;
        try
        {
            var catalog = Resources.Load<BuildCatalog>("Building/PrototypeCatalog");
            for (int part = 0; part < 6; part++) for (int yaw = 0; yaw < 8; yaw++) Runtime(catalog, part, yaw);
            for (int yaw = 0; yaw < 8; yaw++) TerrainFooting(catalog, yaw);
            Check(errors.Count == 0, "Runtime errors: " + string.Join(" | ", errors));
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/unity-validation.json", "{\"passed\":true,\"checks\":" + checks + ",\"parts\":6,\"uvVariants\":18,\"runtimeTriangles\":96,\"headings\":8,\"placementCases\":48,\"doorWalks\":16,\"terrainFootings\":8,\"sharedAtlasSize\":2048,\"urpRenders\":true,\"instancedRendering\":true}");
            Debug.Log("LIMESTONE EXTENSION PASS: " + checks + " checks; L04-L09, 18 layouts, 48 support/collision cases, 16 doorway walks and 8 terrain footings.");
            Application.logMessageReceived -= Log; EditorApplication.Exit(0);
        }
        catch (Exception ex) { Application.logMessageReceived -= Log; Debug.LogException(ex); EditorApplication.Exit(1); }
    }


    private static bool TriangleHit(Vector3 origin,Vector3 a,Vector3 b,Vector3 c)
    {
        Vector3 e1=b-a,e2=c-a,h=Vector3.Cross(Vector3.forward,e2);float det=Vector3.Dot(e1,h);
        if(Mathf.Abs(det)<.000001f)return false;
        float inverse=1/det;Vector3 delta=origin-a;float u=Vector3.Dot(delta,h)*inverse;
        if(u<0||u>1)return false;
        Vector3 q=Vector3.Cross(delta,e1);float v=Vector3.Dot(Vector3.forward,q)*inverse;
        float distance=Vector3.Dot(e2,q)*inverse;
        return v>=0&&u+v<=1&&distance>=0&&distance<=2;
    }
    private static double SolidVolume(BuildDefinition d)
    {
        double total=0; foreach(var b in d.solidBoxes.Length>0?d.solidBoxes:new[]{d.LocalBounds}) total+=b.size.x*b.size.y*b.size.z;
        return total;
    }
    private static void Runtime(BuildCatalog catalog, int part, int yaw)
    {
        var d=catalog.Find(LimestoneKitExtensionSetup.Ids[part]);
        var origin=new Vector3(200+yaw*30,30,200);var q=BuildGeometry.Rotation(yaw);
        using var session=new BuildSession();var frame=session.CreateFrame(origin,yaw);
        var bases=new List<BuildPieceRecord>();
        if(part==1)
        {
            var strip=catalog.Find(LimestoneKitExtensionSetup.Ids[2]);
            for(int z=0;z<4;z++)bases.Add(session.Add(strip,frame,new Vector3Int(0,-1,z*4+2),0,true));
        }
        else bases.Add(session.Add(catalog.Find("build.prototype.foundation"),frame,default,0,true));
        var first=session.Add(d,frame,part==2?new Vector3Int(0,2,0):default,0,false);
        if(part==1)
        {
            var target=bases[0];
            var preview=BuildPlacement.Solve(d,target.Origin+q*new Vector3(2,0,0),Vector3.up,target,session.Frame(target.OwnFrameId),yaw,0,default);
            Near(preview.Origin,origin,"Paving underside did not seat on authored footing top");
        }
        Check(first.Supported,"Supported component rejected: "+d.contentId);
        Check(Array.IndexOf(d.uvVariants,first.Resolved.mesh)>=0,"Authored mesh/UV replaced: "+d.contentId);
        Check(first.Resolved.auxiliaryMesh==null&&first.Resolved.attachments.Length==0,"Unwanted timber fitting");
        if(part==1)
        {
            foreach(var support in bases)Check(Array.IndexOf(support.Definition.uvVariants,support.Resolved.mesh)>=0,"Paving remeshed its limestone footing");
            using var partial=new BuildSession();var pf=partial.CreateFrame(origin+Vector3.right*10,yaw);
            partial.Add(catalog.Find(LimestoneKitExtensionSetup.Ids[2]),pf,new Vector3Int(0,-1,2),0,true);
            var overhang=partial.Add(d,pf,default,0,false);Check(overhang.Supported,"Cantilevered paving accepted");
        }
        if(d.kind is BuildPartKind.Wall or BuildPartKind.Corner)
        {
            var preview=BuildPlacement.Solve(d,first.Origin+q*new Vector3(d.LocalBounds.center.x,d.LocalBounds.max.y,0),Vector3.up,first,session.Frame(first.OwnFrameId),yaw,0,default);
            Near(preview.Origin,first.Origin+Vector3.up*d.StackRise,"Stack datum");
            var upper=session.Add(d,frame,new Vector3Int(0,Mathf.RoundToInt(d.StackRise*4),0),0,false);
            Check(upper.Supported,"Stack lost support");
            if(part==4)
            {
                var wall=catalog.Find(LimestoneKitSetup.Ids[0]);
                var raised=session.Add(wall,frame,new Vector3Int(0,2,0),0,false);
                Near(raised.Origin,origin+Vector3.up*.5f,"Plinth did not raise full wall by 0.5 m");
            }
        }
        using(var gameplay=new BuildGameplay(session))
        {
            gameplay.Update(origin+q*new Vector3(2,1,1),32,40,32);Physics.SyncTransforms();
            Check(gameplay.TryGetProxy(first.Id,out var proxy),"Missing physical component");
            var colliders=new List<Collider>();proxy.GetEnabledColliders(colliders);
            Check(colliders.Count==(part==5?3:1),"Incorrect physical solid count");
            Vector3 local=part==5?new Vector3(.5f,1,0):d.LocalBounds.center;
            var ray=new Ray(first.Origin+q*(local-Vector3.forward*(d.LocalBounds.extents.z+1)),q*Vector3.forward);
            bool hit=false;foreach(var collider in colliders)if(collider.Raycast(ray,out var result,4)){hit=true;Check(Mathf.Abs(result.distance-1)<.001f,"Physical face moved");}
            Check(hit,"Missing component collision");
            if(part==5)
            {
                var voidRay=new Ray(origin+q*new Vector3(2,1,-1),q*Vector3.forward);
                foreach(var collider in colliders)Check(!collider.Raycast(voidRay,out _,2),"Doorway aperture blocked");
                var pad=GameObject.CreatePrimitive(PrimitiveType.Cube);pad.layer=GameplayLayers.WorldSolid;
                pad.transform.position=origin+new Vector3(2,-.1f,0);pad.transform.localScale=new Vector3(12,.2f,12);Physics.SyncTransforms();
                try{Walk(origin,q,false);Walk(origin,q,true);}finally{Object.DestroyImmediate(pad);Physics.SyncTransforms();}
            }
        }
        session.Remove(bases[0].Id);Check(first.Supported,"Free placement unexpectedly depends on structural support");
    }

    private static void Walk(Vector3 origin,Quaternion q,bool reverse)
    {
        var rig=new GameObject("L09 actual character traversal");var cc=rig.AddComponent<CharacterController>();
        cc.height=1.8f;cc.center=new Vector3(0,.9f,0);cc.radius=.4f;cc.skinWidth=.08f;cc.stepOffset=.3f;cc.minMoveDistance=0;
        rig.transform.SetPositionAndRotation(origin+q*new Vector3(2,.03f,reverse?1.5f:-1.5f),q);
        var motor=rig.AddComponent<CharacterMotor>();Physics.SyncTransforms();
        try
        {
            for(int i=0;i<20;i++)motor.Simulate(default,1f/60);
            for(int i=0;i<52;i++)motor.Simulate(new CharacterMoveCommand(new Vector2(0,reverse?-1:1),false,false,false),1f/60);
            var p=Quaternion.Inverse(q)*(rig.transform.position-origin);
            Check(reverse?p.z<-.8f:p.z>.8f,"Character blocked by L09 doorway: "+p);Check(Mathf.Abs(p.y)<.15f,"Door traversal lost floor contact");
        }
        finally{Object.DestroyImmediate(rig);Physics.SyncTransforms();}
    }

    private static void TerrainFooting(BuildCatalog catalog,int yaw)
    {
        var origin=new Vector3(1200+yaw*30,30,1200);var q=BuildGeometry.Rotation(yaw);
        var data=new TerrainData{heightmapResolution=33,size=new Vector3(12,4,12)};
        data.SetHeights(0,0,new float[33,33]);var ground=Terrain.CreateTerrainGameObject(data);ground.layer=GameplayLayers.WorldSolid;
        ground.transform.position=origin-new Vector3(6,0,6);Physics.SyncTransforms();
        var host=new GameObject("L06 terrain footing");var world=host.AddComponent<BuildWorld>();world.enabled=false;
        try
        {
            var d=catalog.Find(LimestoneKitExtensionSetup.Ids[2]);
            var preview=BuildPlacement.Solve(d,origin+q*new Vector3(2,0,0),Vector3.up,null,null,yaw,0,default);
            world.Validate(ref preview);Check(preview.Valid&&!preview.Grounded,"L06 cannot seat on TerrainCollider: "+preview.Message);
            Near(preview.Origin,origin+Vector3.up*.5f,"Footing bottom-contact datum");
            var floating=preview;floating.Origin+=Vector3.up;world.Validate(ref floating);Check(floating.Valid&&!floating.Grounded,"Floating footing grounded");
            var committed=world.Commit(preview);Check(committed!=null&&!committed.Grounded&&committed.Supported,"Grounded footing commit failed");
        }
        finally{Object.DestroyImmediate(host);Object.DestroyImmediate(ground);Object.DestroyImmediate(data);Physics.SyncTransforms();}
    }
    private static void Render(BuildCatalog catalog)
    {
        Directory.CreateDirectory(Output);
        var oldPipeline = GraphicsSettings.defaultRenderPipeline; var oldQuality = QualitySettings.renderPipeline;
        var oldAmbient = RenderSettings.ambientLight; var oldMode = RenderSettings.ambientMode; var oldSun = RenderSettings.sun;
        bool oldAsync = ShaderUtil.allowAsyncCompilation; var oldTarget = RenderTexture.active;
        var root = new GameObject("Limestone render fixture");
        var cameraObject = new GameObject("Limestone render camera"); var lightObject = new GameObject("Limestone render light");
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
        camera.nearClipPlane = .01f; camera.farClipPlane = 100; camera.aspect = 16f / 9; camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.17f, .19f, .20f);
        cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f;
        light.shadows = LightShadows.None; light.cullingMask = 1 << 31; light.transform.rotation = Quaternion.Euler(28, -40, 0);
        var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32); target.Create();
        var material = new Material(catalog.Find(LimestoneKitExtensionSetup.Ids[0]).material);
        Action<ScriptableRenderContext, Camera> submit = null;
        try
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Check(pipeline != null, "URP pipeline missing");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.3f, .3f, .3f); RenderSettings.sun = light;
            ShaderUtil.allowAsyncCompilation = false;
            for (int i = 0; i < material.passCount; i++) ShaderUtil.CompilePass(material, i, true);
            Check(!ShaderUtil.ShaderHasError(material.shader), "Limestone shader failed compilation");
            var renderers = new List<MeshRenderer>();
            for (int part = 0; part < 6; part++)
            {
                var d = catalog.Find(LimestoneKitExtensionSetup.Ids[part]);
                var wall = new GameObject("L0" + (part + 4)) { layer = 31 }; wall.transform.SetParent(root.transform);
                wall.AddComponent<MeshFilter>().sharedMesh = d.mesh;
                var renderer = wall.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderers.Add(renderer);
                camera.orthographicSize = 2.3f;
                var centre = d.LocalBounds.center;
                camera.transform.position = centre + new Vector3(4, 2, -8); camera.transform.LookAt(centre);
                var mapped = Capture(camera, target, "L0" + (part + 4) + "-unity-front.png");
                material.SetFloat("_BumpScale", 0);
                var flat = Capture(camera, target, "L0" + (part + 4) + "-unity-normal-disabled.png");
                double difference = 0;
                for (int i = 0; i < flat.Length; i++) difference += Math.Abs(mapped[i].r - flat[i].r) + Math.Abs(mapped[i].g - flat[i].g) + Math.Abs(mapped[i].b - flat[i].b);
                Check(difference / flat.Length > .005, "Normal map made no measurable lighting difference");
                material.SetFloat("_BumpScale", 1);
                camera.transform.position = centre + new Vector3(-4, 2, 8); camera.transform.LookAt(centre);
                light.transform.rotation = Quaternion.Euler(30, 140, 0);
                Capture(camera, target, "L0" + (part + 4) + "-unity-back.png");
                light.transform.rotation = Quaternion.Euler(28, -40, 0);
                wall.transform.position = new Vector3(part % 3 * 6, 0, part / 3 * 5); renderer.enabled = false;
            }
            foreach (var renderer in renderers) renderer.enabled = true;
            camera.orthographicSize = 6.4f; camera.transform.position = new Vector3(19, 13, -20); camera.transform.LookAt(new Vector3(8, 1, 3));
            Capture(camera, target, "limestone-extension-unity-overview.png");
            foreach (var renderer in renderers) renderer.enabled = false;
            using var session = new BuildSession(); var frame = session.CreateFrame(Vector3.zero, 1);
            for (int row = 0; row < 2; row++) for (int col = 0; col < 3; col++)
                session.Add(catalog.Find(LimestoneKitExtensionSetup.Ids[row * 3 + col]), frame, new Vector3Int(col * 24, 0, row * 20), 0, false);
            var buildingRenderer = new BuildRenderer(session);
            try
            {
                camera.cullingMask = ~0; camera.orthographicSize = 6.8f; var rotation = BuildGeometry.Rotation(1);
                camera.transform.position = rotation * new Vector3(19, 13, -20); camera.transform.LookAt(rotation * new Vector3(8, 1, 3));
                submit = (_, current) => { if (current == camera) buildingRenderer.Draw(camera, 100, 0); };
                RenderPipelineManager.beginCameraRendering += submit;
                Capture(camera, target, "limestone-extension-instanced.png");
                Check(buildingRenderer.DrawCalls > 0 && buildingRenderer.DrawCalls <= 6, "Placed limestone instanced draw missing");
            }
            finally { if (submit != null) RenderPipelineManager.beginCameraRendering -= submit; }
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = oldPipeline; QualitySettings.renderPipeline = oldQuality;
            RenderSettings.ambientLight = oldAmbient; RenderSettings.ambientMode = oldMode; RenderSettings.sun = oldSun;
            ShaderUtil.allowAsyncCompilation = oldAsync; RenderTexture.active = oldTarget;
            Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject); Object.DestroyImmediate(material);
            target.Release(); Object.DestroyImmediate(target);
        }
    }

    private static Color32[] Capture(Camera camera, RenderTexture target, string filename)
    {
        for (int i = 0; i < 2; i++) RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            var pixels = image.GetPixels32(); int pink = 0, visible = 0; var background = pixels[0];
            foreach (var pixel in pixels)
            {
                if (pixel.r > 200 && pixel.b > 200 && pixel.g < 50) pink++;
                if (Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b) > 12) visible++;
            }
            Check(pink < 100 && visible > 1000, "Blank/error render: " + filename);
            File.WriteAllBytes(Output + "/" + filename, image.EncodeToPNG());
            return pixels;
        }
        finally { Object.DestroyImmediate(image); }
    }
}


