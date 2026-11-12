using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class MeadowGrassReviewAuthoring
{
    private const string Root = "Assets/ArtReview/MeadowGrass/";
    private const string PilotMaterial = "Assets/Materials/M_Grass/M_PilotMeadowBlades.mat";
    private const string PilotMesh = "Assets/Models/Grass+Flowers/PilotMeadowGrass_Near.asset";

    [Serializable] private sealed class Variant { public float[] positions, uv; public int[] indices; }
    [Serializable] private sealed class Variants { public Variant[] near, shortNear, mid; }

    [MenuItem("Tools/Foliage/Build Meadow Grass Review")]
    public static void Build() => BuildScene();

    [MenuItem("Tools/Foliage/Install Meadow Grass in Forest Variety Pilot")]
    public static void InstallPilot()
    {
        const string scenePath = "Assets/Scenes/ForestVarietyPilot.unity";
        var atlas = ImportTexture("BladeAtlas.png", true);
        var turf = ImportTexture("PilotTurfSurface.png", false, linear: true);
        var variants = JsonUtility.FromJson<Variants>(File.ReadAllText(Root + "Variants.json"));
        var material = CreateBladeMaterial(atlas);
        material.SetColor("_BaseColor", new Color(.75f,.79f,.59f));
        material.SetFloat("_MeadowMeshVariants", 1);
        material.EnableKeyword("_MEADOW_MESH_VARIANTS");
        material.enableInstancing = true;
        var runtimeMaterial = Save(material, "PilotMeadowRuntime.mat");
        var near = MakePilotPrefab(variants.near, true, runtimeMaterial);
        var mid = MakePilotPrefab(variants.mid, false, runtimeMaterial);

        var scene = EditorSceneManager.OpenScene(scenePath);
        var world = new SerializedObject(Object.FindAnyObjectByType<WorldManager>());
        var settings = world.FindProperty("grassSettings");
        settings.FindPropertyRelative("grassPrefab").objectReferenceValue = near;
        settings.FindPropertyRelative("billboardGrassPrefab").objectReferenceValue = mid;
        var terrain = (Material)world.FindProperty("terrainMaterial").objectReferenceValue;
        if (!terrain || AssetDatabase.GetAssetPath(terrain) != "Assets/Materials/M_Terrain/M_TerrainPilotWoodland.mat")
            throw new InvalidOperationException("Unexpected pilot terrain material.");
        terrain.SetTexture("_GrassSurfaceMap", turf);
        terrain.SetTexture("_NearMeadowAlbedo", ImportTexture("NearTurfAlbedo.png", false));
        terrain.SetTexture("_NearMeadowRelief", ImportTexture("NearTurfRelief.png", false, linear: true));
        terrain.SetFloat("_GrassGroundTiling", 3f / 8);
        terrain.SetFloat("_GrassGroundFarTiling", .23f);
        terrain.SetFloat("_GrassGroundDetailContrast", 1.85f);
        terrain.SetFloat("_GrassGroundHeightDepth", .003f);
        terrain.SetColor("_MeadowTurfTint", new Color(1.28f,1.18f,.74f));
        EditorUtility.SetDirty(terrain);
        world.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    public static void InstallPilotBatch()
    {
        try
        {
            InstallPilot();
            Debug.Log("PILOT_MEADOW_RUNTIME_INSTALL_PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static GameObject MakePilotPrefab(Variant[] variants, bool near, Material material)
    {
        if (variants.Length != 3) throw new InvalidOperationException("Expected three meadow variants.");
        string name = near ? "PilotMeadowHybridNear" : "PilotMeadowHybridMid";
        var mesh = MakeMesh(variants[0]);
        var bounds = mesh.bounds;
        var accentVertices = new HashSet<int>();
        if (near)
        {
            // The accent blades sample the solid-color tile at UV (3/8, 3/4).
            var uv = mesh.uv;
            for (int i = 0; i < uv.Length; i++)
                if (uv[i].x >= .375f && uv[i].x < .5f && uv[i].y >= .75f)
                    accentVertices.Add(i);
            if (accentVertices.Count != 24) throw new InvalidOperationException("Unexpected tall meadow topology.");
        }
        // Unity supports a two-component UV stream for a scalar vertex flag.
        var flags = new List<Vector2>(mesh.vertexCount);
        for (int i = 0; i < mesh.vertexCount; i++)
            flags.Add(new Vector2(accentVertices.Contains(i) ? 1 : 0, 0));
        mesh.SetUVs(5, flags);
        for (int variant = 1; variant < variants.Length; variant++)
        {
            var source = MakeMesh(variants[variant]);
            try
            {
                if (source.vertexCount != mesh.vertexCount ||
                    !source.triangles.AsSpan().SequenceEqual(mesh.triangles))
                    throw new InvalidOperationException("Meadow variants must share vertex topology.");
                var position = source.vertices;
                var normal = source.normals;
                var uv = source.uv;
                var positions = new List<Vector4>(source.vertexCount);
                var normals = new List<Vector4>(source.vertexCount);
                for (int vertex = 0; vertex < source.vertexCount; vertex++)
                {
                    Vector3 p = position[vertex], n = normal[vertex];
                    positions.Add(new Vector4(p.x,p.y,p.z,uv[vertex].x));
                    normals.Add(new Vector4(n.x,n.y,n.z,uv[vertex].y));
                }
                mesh.SetUVs(variant*2-1, positions);
                mesh.SetUVs(variant*2, normals);
                bounds.Encapsulate(source.bounds);
            }
            finally { Object.DestroyImmediate(source); }
        }
        mesh.bounds = bounds;
        var root = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        try
        {
            root.GetComponent<MeshFilter>().sharedMesh = Save(mesh, name + ".asset");
            var renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return PrefabUtility.SaveAsPrefabAsset(root, Root + name + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    public static void RunBatch()
    {
        try
        {
            var review = BuildScene();
            Capture(review);
            Debug.Log("MEADOW_GRASS_REVIEW_PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static MeadowGrassReview BuildScene()
    {
        var atlas = ImportTexture("BladeAtlas.png", true);
        var ground = ImportTexture("ShortCover.png", false);
        var normal = ImportTexture("ShortCoverNormal.png", false, TextureImporterType.NormalMap);
        var occlusion = ImportTexture("ShortCoverAO.png", false, linear: true);
        var variants = JsonUtility.FromJson<Variants>(File.ReadAllText(Root + "Variants.json"));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var review = new GameObject("Meadow grass review").AddComponent<MeadowGrassReview>();
        review.pilotMaterial = AssetDatabase.LoadAssetAtPath<Material>(PilotMaterial);
        review.pilotMesh = AssetDatabase.LoadAssetAtPath<Mesh>(PilotMesh);
        review.prototypeMaterial = Save(CreateBladeMaterial(atlas), "ReviewBlades.mat");
        review.nearMeshes = MakeVariants(variants.near, "Near");
        review.shortNearMeshes = MakeVariants(variants.shortNear, "ShortNear");
        review.midMeshes = MakeVariants(variants.mid, "Mid");

        var terrainMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        terrainMaterial.SetTexture("_BaseMap", ground);
        terrainMaterial.SetTexture("_BumpMap", normal);
        terrainMaterial.SetFloat("_BumpScale", .48f);
        terrainMaterial.EnableKeyword("_NORMALMAP");
        terrainMaterial.SetTexture("_OcclusionMap", occlusion);
        terrainMaterial.SetFloat("_OcclusionStrength", .72f);
        terrainMaterial.EnableKeyword("_OCCLUSIONMAP");
        terrainMaterial.SetFloat("_Smoothness", .07f);
        terrainMaterial.SetColor("_BaseColor", new Color(.82f,.87f,.76f));
        var surface = new GameObject("Matched sloping ground", typeof(MeshFilter), typeof(MeshRenderer));
        surface.GetComponent<MeshFilter>().sharedMesh = Save(CreateGround(), "ReviewSlope.asset");
        surface.GetComponent<MeshRenderer>().sharedMaterial = Save(terrainMaterial, "ReviewGround.mat");

        var sun = new GameObject("Afternoon sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1,.975f,.91f);
        sun.intensity = 1.45f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.LookRotation(new Vector3(.55f,-.91f,.48f));
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.56f,.65f,.77f);
        RenderSettings.ambientEquatorColor = new Color(.4f,.44f,.5f);
        RenderSettings.ambientGroundColor = new Color(.23f,.218f,.167f);

        review.shadeCaster = GameObject.CreatePrimitive(PrimitiveType.Cube);
        review.shadeCaster.name = "Shade comparison";
        review.shadeCaster.transform.SetPositionAndRotation(new Vector3(-2.4f,4,2), Quaternion.identity);
        review.shadeCaster.transform.localScale = new Vector3(5,.3f,14);
        review.shadeCaster.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        review.shadeCaster.SetActive(false);
        review.reviewCamera = new GameObject("Review camera").AddComponent<Camera>();
        review.reviewCamera.clearFlags = CameraClearFlags.SolidColor;
        review.reviewCamera.backgroundColor = new Color(.38f,.54f,.68f);
        review.reviewCamera.fieldOfView = 40;
        review.SetView(0);
        EditorSceneManager.SaveScene(scene, Root + "MeadowGrassReview.unity");
        AssetDatabase.SaveAssets();
        return review;
    }

    private static Material CreateBladeMaterial(Texture2D atlas)
    {
        var blades = new Material(AssetDatabase.LoadAssetAtPath<Material>(PilotMaterial));
        blades.SetTexture("_BaseMap", atlas);
        blades.SetColor("_BaseColor", Color.white);
        blades.SetColor("_RootTint", new Color(.82f,.85f,.75f));
        blades.SetColor("_TipTint", Color.white);
        blades.SetFloat("_BladeMaxY", .56f);
        blades.SetFloat("_MeadowSeedStemCompression", 0);
        blades.SetFloat("_RootSeatFade", 0);
        blades.SetFloat("_Cutoff", .24f);
        blades.SetFloat("_AlphaClipThreshold", .24f);
        blades.SetFloat("_ReceiveShadows", 1);
        return blades;
    }

    private static Mesh[] MakeVariants(Variant[] variants, string prefix)
    {
        var meshes = new Mesh[variants.Length];
        for (int i = 0; i < meshes.Length; i++)
        {
            meshes[i] = Save(MakeMesh(variants[i]), prefix+i+".asset");
        }
        return meshes;
    }

    private static Mesh MakeMesh(Variant data)
    {
        var vertices = new Vector3[data.positions.Length / 3];
        var uv = new Vector2[vertices.Length];
        for (int j = 0; j < vertices.Length; j++)
        {
            vertices[j] = new Vector3(data.positions[j*3],data.positions[j*3+1],data.positions[j*3+2]);
            uv[j] = new Vector2(data.uv[j*2],data.uv[j*2+1]);
        }
        var mesh = new Mesh { vertices = vertices, uv = uv, triangles = data.indices };
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Mesh CreateGround()
    {
        const int width = 81, length = 101;
        var vertices = new Vector3[width*length];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[(width-1)*(length-1)*6];
        int next = 0;
        for (int z = 0; z < length; z++)
        for (int x = 0; x < width; x++)
        {
            int i = x + z*width;
            float px = (x-40)*.15f, pz = (z-12)*.15f;
            vertices[i] = new Vector3(px, MeadowGrassReview.GroundHeight(px,pz), pz);
            uv[i] = new Vector2(px,pz) / (8f/3);
            if (x == width-1 || z == length-1) continue;
            triangles[next++] = i;
            triangles[next++] = i+width;
            triangles[next++] = i+1;
            triangles[next++] = i+1;
            triangles[next++] = i+width;
            triangles[next++] = i+width+1;
        }
        var mesh = new Mesh { vertices = vertices, uv = uv, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static Texture2D ImportTexture(string name, bool atlas,
        TextureImporterType type = TextureImporterType.Default, bool linear = false)
    {
        string path = Root + name;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = type;
        importer.sRGBTexture = type == TextureImporterType.Default && !linear;
        importer.alphaIsTransparency = atlas;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = atlas;
        importer.alphaTestReferenceValue = .24f;
        importer.wrapMode = atlas ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = atlas ? 2 : 8;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static T Save<T>(T asset, string name) where T : Object
    {
        asset.name = Path.GetFileNameWithoutExtension(name);
        var prior = AssetDatabase.LoadAssetAtPath<T>(Root + name);
        if (!prior)
        {
            AssetDatabase.CreateAsset(asset, Root + name);
            return asset;
        }
        EditorUtility.CopySerialized(asset, prior);
        EditorUtility.SetDirty(prior);
        Object.DestroyImmediate(asset);
        return prior;
    }

    private static void Capture(MeadowGrassReview review)
    {
        ShaderUtil.allowAsyncCompilation = false;
        Shader.SetGlobalVector("_FSGroundDaylight", new Vector4(1,1,0,0));
        const string output = ".utmp/meadow-grass-unity/";
        Directory.CreateDirectory(output);
        var target = new RenderTexture(1060,640,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var pixels = new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        var previous = RenderTexture.active;
        try
        {
            for (int view = 0; view < 5; view++)
            {
                review.SetView(view);
                for (int mode = 0; mode < 4; mode++)
                {
                    review.SetMode(mode);
                    Write(view + "-" + mode);
                }
            }
            review.SetView(1);
            review.shadeCaster.SetActive(true);
            for (int mode = 0; mode < 3; mode++)
            {
                review.SetMode(mode);
                Write("shade-" + mode);
            }
        }
        finally
        {
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
        }

        void Write(string name)
        {
            // Let renderer visibility settle after swapping a whole patch.
            for (int frame = 0; frame < 3; frame++)
                RenderPipeline.SubmitRenderRequest(review.reviewCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
            File.WriteAllBytes(output + name + ".png",pixels.EncodeToPNG());
        }
    }
}
