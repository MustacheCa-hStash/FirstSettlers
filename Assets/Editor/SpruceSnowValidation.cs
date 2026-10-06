using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class SpruceSnowValidation
{
    private const int Size = 128, N = Size + 3;
    private const string PrefabPath = "Assets/Prefabs/Spruce_LOD0_v06.prefab";

    [MenuItem("Tools/Terrain/Validate Snowy Spruce")]
    public static void Run()
    {
        ValidateHabitatAndParity();
        ValidateNearInstancesAndPooling();
        ValidateRendering();
        Debug.Log("SPRUCE SNOW PASS: habitat exclusions, dense/sparse parity, near snow surface acceptance, pooled snow reset, shader compilation, preserved bare needles and connected white snow caps.");
    }

    public static void RunBatch()
    {
        try { ShaderUtil.allowAsyncCompilation = false; Run(); DistantTreeValidation.Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    public static void RenderBatch()
    {
        try { ShaderUtil.allowAsyncCompilation = false; ValidateRendering(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); }

    private static void ValidateHabitatAndParity()
    {
        int accepted = 0;
        foreach (string habitat in new[] { "snow", "tundra", "summit", "cold", "dry", "steep", "river", "water" })
        {
            var b = new BiomeType[N, N]; var s = new SurfaceType[N, N];
            var m = new float[N, N]; var t = new float[N, N]; var h = new float[N, N];
            var slope = new float[N, N]; var river = new float[N, N];
            for (int x = 0; x < N; x++) for (int z = 0; z < N; z++)
            {
                b[x, z] = habitat == "tundra" ? BiomeType.Tundra : BiomeType.Snow;
                s[x, z] = SurfaceType.Snow;
                m[x, z] = habitat == "dry" ? .2f : .8f;
                t[x, z] = habitat == "cold" ? .05f : .17f;
                h[x, z] = habitat == "summit" ? 1f : habitat == "water" ? .1f : .5f;
                slope[x, z] = habitat == "steep" ? 40f : 8f;
                river[x, z] = habitat == "river" ? .8f : 0f;
            }
            for (int index = 0; index < 8; index++)
            {
                var coord = new ChunkCoord(index - 4, 2 - index);
                var settings = WorldFeatureGenerationSettings.Default;
                var full = WorldFeaturePlanGenerator.Generate(coord, Size, 1937, b, s, m, t, slope, river, settings, h, .24f);
                var sparse = WorldFeaturePlanGenerator.GenerateTreePlacements(coord, Size, 1937, b, s, m, t, slope, river,
                    settings, (x, z) => { }, sampleHeight: (x, z) => h[x, z], waterLevel: .24f);
                var trees = full.Placements.Where(p => p.featureType == WorldFeatureType.Tree).ToArray();
                Check(trees.Length == sparse.Placements.Count, "Snow dense/sparse count differs: " + habitat);
                Check(trees.Length <= 8, "Snow stand exceeds sparse habitat budget.");
                for (int i = 0; i < trees.Length; i++)
                {
                    var a = trees[i]; var c = sparse.Placements[i];
                    Check(a.variant == WorldFeatureVariant.SpruceTree && a.snowCoverage >= .8f, "Snow stand contains an unsuitable tree.");
                    Check(a.sampleX == c.sampleX && a.sampleZ == c.sampleZ && a.rotation == c.rotation &&
                        a.scale == c.scale && a.snowCoverage == c.snowCoverage, "Snow dense/sparse identity differs.");
                    accepted++;
                }
                if (habitat != "snow") Check(trees.Length == 0, "Spruce accepted unsuitable habitat: " + habitat);
            }
        }
        Check(accepted > 0, "Snow fixtures produced no spruce.");
        Debug.Log("Validated " + accepted + " snow spruce placements across suitable and excluded habitats.");
    }

    private static void ValidateNearInstancesAndPooling()
    {
        var record = new ChunkRecord(new ChunkCoord(0, 0));
        var h = new float[N, N]; var s = new SurfaceType[N, N];
        for (int x = 0; x < N; x++) for (int z = 0; z < N; z++) { h[x, z] = .5f; s[x, z] = SurfaceType.Snow; }
        var plan = new WorldFeaturePlan(N, N);
        plan.Placements.Add(new WorldFeaturePlacement(WorldFeatureType.Tree, WorldFeatureVariant.SpruceTree,
            64f, 64f, Quaternion.identity, Vector3.one, 2f, 3f, snowCoverage: .9f));
        int version = record.BeginTerrainDataRequest();
        record.TryCompleteTerrainDataRequest(version, h, new float[N, N], new float[N, N], new float[N, N],
            new BiomeType[N, N], s, new WaterState[N, N], new GroundCoverType[N, N], plan,
            new float[N, N], null);
        var root = new GameObject("Snow spruce validation root");
        try
        {
            FoliageGenerator.GenerateTreeCubesForChunk(record, new TreeSettings(), 1937, Size, 1f, 10f);
            Check(record.FoliageData.treeCubeInstances.Count == 1, "Near foliage dropped a spruce on snow.");
            var tree = record.FoliageData.treeCubeInstances[0];
            Check(tree.snowCoverage == .9f && tree.localPosition.y == 5f, "Near instance lost snow or terrain seating.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Check(prefab != null, "Active spruce prefab missing.");
            using var renderer = new StandingTreeRenderer(new TreeSettings { spruceTreePrefab = prefab });
            renderer.BeginFrame(null); renderer.Submit(tree, Matrix4x4.identity, 0f, 1f);
            var batches = (System.Collections.IEnumerable)typeof(StandingTreeRenderer).GetField("batches",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(renderer);
            foreach (var batch in batches)
            {
                if ((int)batch.GetType().GetField("Count").GetValue(batch) == 0) continue;
                var appearance = (Vector4[])batch.GetType().GetField("Appearance").GetValue(batch);
                Check(appearance[0].x == .9f && appearance[0].y == 1f, "Standing tree lost snow or alpha shadows.");
            }
            tree.snowCoverage = 0f; renderer.Submit(tree, Matrix4x4.identity, 0f, 1f);
            foreach (var batch in batches)
            {
                if ((int)batch.GetType().GetField("Count").GetValue(batch) == 0) continue;
                var appearance = (Vector4[])batch.GetType().GetField("Appearance").GetValue(batch);
                Check(appearance[1].x == 0f, "Standing tree retained snow on the next instance.");
            }
        }
        finally { Object.DestroyImmediate(root); record.Dispose(); }
    }

    private static void ValidateRendering()
    {
        var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Trees/Spruce/SpruceTreeLeaf_M.mat");
        Check(source.GetTexture("_SnowMap") != null, "Snow card is not assigned to the active leaf material.");
        Check(AssetDatabase.GetAssetPath(source.GetTexture("_BaseMap")) == "Assets/Textures/Trees/leafCard_Spruce_03.png",
            "Original needle alpha texture changed.");
        var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        var previousPipeline = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        var cameraObject = new GameObject("Snow spruce validation camera");
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 0;
        var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(512, 512, TextureFormat.RGBA32, false);
        var material = new Material(source);
        var card = new Mesh();
        card.vertices = new[] { new Vector3(-1,-1,0), new Vector3(-1,1,0), new Vector3(1,1,0), new Vector3(1,-1,0) };
        card.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        card.triangles = new[] { 0,1,2,0,2,3 }; card.RecalculateNormals(); card.RecalculateBounds();
        var command = new CommandBuffer(); var previousTarget = RenderTexture.active;
        try
        {
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            camera.targetTexture = target; camera.Render();
            ShaderUtil.CompilePass(material, 0, true); ShaderUtil.CompilePass(material, 1, true);
            foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                Check(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            camera.orthographic = true; camera.orthographicSize = 1.05f; camera.aspect = 1f;
            camera.transform.position = new Vector3(0,0,-5); camera.transform.LookAt(Vector3.zero);
            material.SetFloat("_AmbientStrength", 1f); material.SetFloat("_BacklightStrength", 0f);
            material.SetFloat("_WindStrength", 0f); material.SetFloat("_WindFlutterStrength", 0f);
            Color32[] Render(float snow)
            {
                var block = new MaterialPropertyBlock(); block.SetFloat("_SnowCoverage", snow);
                command.Clear(); command.SetRenderTarget(target); command.ClearRenderTarget(true, true, Color.clear);
                command.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                command.SetGlobalMatrix("unity_MatrixVP", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix);
                command.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);
                command.SetGlobalVector("_MainLightPosition", new Vector4(0,1,0,0));
                command.SetGlobalVector("_MainLightColor", Vector4.one);
                command.DrawMesh(card, Matrix4x4.identity, material, 0, 0, block);
                Graphics.ExecuteCommandBuffer(command); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,512,512),0,0); pixels.Apply(); return pixels.GetPixels32();
            }
            var dry = Render(0f); var snow = Render(1f);
            int covered = 0, white = 0, added = 0;
            for (int i = 0; i < dry.Length; i++)
            {
                Check(dry[i].a == 0 || snow[i].a > 0, "Snow removed an original needle.");
                if (dry[i].a == 0 && snow[i].a > 0) added++;
                if (snow[i].a == 0) continue;
                covered++;
                if (snow[i].r > dry[i].r + 35 && snow[i].r > 150 && Math.Abs(snow[i].r - snow[i].g) < 35) white++;
            }
            Check(covered > 10000 && white > covered * .35f, "Broad snow caps are missing or tinted green.");
            Check(added > 8000 && covered < snow.Length * .85f, "Snow must bridge needle gaps while keeping major twig gaps clear.");
            Directory.CreateDirectory("Logs/SpruceSnowValidation");
            File.WriteAllBytes("Logs/SpruceSnowValidation/leaf-card.png", pixels.EncodeToPNG());
            Debug.Log($"Snow card render: {covered} foliage pixels, {white} visibly pale snow pixels, {added} cap pixels bridging needle gaps, original needles retained.");

            // Render the exact active instance prefab to inspect snow placement on its radial cards.
            camera.orthographicSize = 7f;
            camera.transform.position = new Vector3(12f, 9f, -16f);
            camera.transform.LookAt(new Vector3(0f, 6f, 0f));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            foreach (float amount in new[] { 0f, 1f })
            {
                command.Clear(); command.SetRenderTarget(target);
                command.ClearRenderTarget(true, true, new Color(.04f, .08f, .15f, 1f));
                command.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                command.SetGlobalMatrix("unity_MatrixVP", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix);
                command.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);
                var block = new MaterialPropertyBlock(); block.SetFloat("_SnowCoverage", amount);
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<Renderer>();
                    if (renderer == null) continue;
                    var materials = renderer.sharedMaterials;
                    for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount && submesh < materials.Length; submesh++)
                    {
                        ShaderUtil.CompilePass(materials[submesh], 0, true);
                        command.DrawMesh(filter.sharedMesh, Matrix4x4.Scale(Vector3.one * 2f) * filter.transform.localToWorldMatrix,
                            materials[submesh], submesh, 0, block);
                    }
                }
                Graphics.ExecuteCommandBuffer(command); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,512,512),0,0); pixels.Apply();
                File.WriteAllBytes("Logs/SpruceSnowValidation/" + (amount > 0 ? "snow-tree.png" : "dry-tree.png"), pixels.EncodeToPNG());
            }
        }
        finally
        {
            RenderTexture.active = previousTarget; command.Dispose(); target.Release();
            GraphicsSettings.defaultRenderPipeline = previousPipeline; QualitySettings.renderPipeline = previousQuality;
            Object.DestroyImmediate(card); Object.DestroyImmediate(material); Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(target); Object.DestroyImmediate(cameraObject);
        }
    }
}
