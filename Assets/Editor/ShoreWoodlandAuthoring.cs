using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ShoreWoodlandAuthoring
{
    private const string SandFolder = "Assets/Textures/Ground/ShoreDesert/";
    private const string CattailPrefab = "Assets/Prefabs/cattail.prefab";
    private const string CattailSource = "Assets/Models/Plants+Bush/cattail.fbx";
    private const string CattailAtlas = "Assets/Textures/Flowers/T_CattailFiberAtlas.png";

    [MenuItem("Tools/Foliage/Apply Shore and Woodland Surfaces")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Author these surfaces in Edit mode.");
        var scene = EditorSceneManager.OpenScene(ForestVarietyWorldStudy.Pilot);
        var world = new SerializedObject(Object.FindAnyObjectByType<WorldManager>());
        var terrain = (Material)world.FindProperty("terrainMaterial").objectReferenceValue;
        ConfigureSurfaces(terrain);
        BuildCattails();

        var prefabs = WoodlandSurfaceAuthoring.BuildFerns();
        ForestFernPrefabBuilder.BindFallbacks(prefabs);
        var fern = world.FindProperty("fernSettings");
        fern.FindPropertyRelative("prefab").objectReferenceValue = prefabs.near;
        fern.FindPropertyRelative("distantPrefab").objectReferenceValue = prefabs.middle;
        fern.FindPropertyRelative("coarsePrefab").objectReferenceValue = prefabs.far;
        fern.FindPropertyRelative("sizeMultiplier").floatValue = 1.48f;

        world.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the pilot scene.");
        AssetDatabase.SaveAssets();
    }

    public static void BuildBatch()
    {
        try { Apply(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void ConfigureSurfaces(Material terrain)
    {
        var shore = Import(SandFolder + "T_ShoreSand_Albedo.png");
        var shoreNormal = Import(SandFolder + "T_ShoreSand_Normal.png", normal: true);
        var desert = Import(SandFolder + "T_DesertSand_Albedo.png");
        var desertNormal = Import(SandFolder + "T_DesertSand_Normal.png", normal: true);
        terrain.SetColor("_ForestGrassGroundTint", new Color(1.55f, .94f, .95f));
        terrain.SetTexture("_SandAlbedo", shore);
        terrain.SetTexture("_SandNormal", shoreNormal);
        terrain.SetTexture("_DesertSandAlbedo", desert);
        terrain.SetTexture("_DesertSandNormal", desertNormal);
        terrain.SetColor("_SandColor", new Color(.98f, .97f, .96f));
        terrain.SetFloat("_SplitSandSurfaces", 1);
        terrain.SetFloat("_SandTiling", 1f / 3f);
        terrain.SetFloat("_DesertSandTiling", .25f);
        terrain.SetFloat("_SandDetailStrength", .90f);
        terrain.SetFloat("_SandNormalStrength", .63f);
        terrain.SetFloat("_ShoreSandDampness", .22f);
        EditorUtility.SetDirty(terrain);

        var sedges = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Grass/M_PilotWoodlandSedges.mat");
        sedges.SetColor("_BaseColor", new Color(.67f, .85f, .75f));
        EditorUtility.SetDirty(sedges);
    }

    private static Texture2D Import(string path, bool normal = false, bool clamp = false)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            throw new InvalidOperationException("Missing source map " + path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !normal;
        importer.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = clamp ? 2 : 8;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void BuildCattails()
    {
        var original = AssetDatabase.LoadAssetAtPath<GameObject>(CattailSource);
        var originalFilter = original.GetComponentInChildren<MeshFilter>();
        Matrix4x4 originalLocal = original.transform.worldToLocalMatrix * originalFilter.transform.localToWorldMatrix;
        Bounds originalBounds = TransformBounds(originalFilter.sharedMesh.bounds, originalLocal);

        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Flowers/M_Cattail.mat");
        WriteCattailAtlas();
        material.SetTexture("_BaseMap", Import(CattailAtlas, clamp: true));
        material.SetFloat("_HeadAtlasSplit", .50f);
        material.SetColor("_LeafTint", Color.white);
        material.SetColor("_HeadTint", Color.white);
        material.SetFloat("_UpwardLighting", .44f);
        material.SetFloat("_AmbientFloor", .40f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);

        var root = PrefabUtility.LoadPrefabContents(CattailPrefab);
        try
        {
            var filter = root.GetComponentInChildren<MeshFilter>();
            Matrix4x4 local = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var mesh = MakeCattailMesh(originalBounds.size * .5f, local.inverse);
            filter.sharedMesh = WoodlandFloorAuthoring.Save(mesh, "Assets/Models/Plants+Bush/ShoreCattailClump.asset");
            PrefabUtility.SaveAsPrefabAsset(root, CattailPrefab, out bool saved);
            if (!saved) throw new InvalidOperationException("Could not update the cattail prefab.");
            Debug.Log($"CATTAIL ASSET PASS: original {originalBounds.size.ToString("F3")}, replacement " +
                $"{BoundsOfVertices(filter.sharedMesh.vertices, local).size.ToString("F3")}, {filter.sharedMesh.GetIndexCount(0) / 3} triangles.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
    {
        var corners = new Vector3[8];
        for (int i = 0; i < corners.Length; i++)
            corners[i] = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z);
        return BoundsOfVertices(corners, matrix);
    }

    private static Bounds BoundsOfVertices(IReadOnlyList<Vector3> vertices, Matrix4x4 matrix)
    {
        var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
        for (int i = 1; i < vertices.Count; i++) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[i]));
        return bounds;
    }

    private static Mesh MakeCattailMesh(Vector3 size, Matrix4x4 prefabToMesh)
    {
        var geometry = new WoodlandFloorAuthoring.Geometry();
        var roots = new[]
        {
            new Vector3(-.24f, 0, -.12f),
            new Vector3(.24f, 0, .045f),
            new Vector3(-.045f, 0, .255f)
        };
        for (int blade = 0; blade < 8; blade++)
        {
            int shoot = blade % roots.Length;
            float angle = blade * 2.39996f + .14f * Mathf.Sin(blade * 4.1f);
            Vector3 outward = new(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vector3 across = Vector3.Cross(Vector3.up, outward);
            float length = Mathf.Lerp(.55f, .93f, .5f + .5f * Mathf.Sin(blade * 4.23f));
            if (shoot == 2) length *= .66f;
            float spread = Mathf.Lerp(.13f, .45f, .5f + .5f * Mathf.Cos(blade * 3.57f));
            float width = Mathf.Lerp(.016f, .029f, .5f + .5f * Mathf.Sin(blade * 2.53f));
            int start = geometry.vertices.Count;
            const int sections = 5;
            for (int row = 0; row <= sections; row++)
            {
                float t = row / (float)sections;
                float bladeWidth = width * Mathf.Lerp(.5f, 1f, Mathf.Sin(t * Mathf.PI)) * (1f - .98f * Mathf.Pow(t, 2));
                Vector3 mid = roots[shoot] + outward * (.025f + spread * t * t) +
                    Vector3.up * (length * t - spread * .42f * Mathf.Pow(t, 5));
                Vector3 twist = Quaternion.AngleAxis(t * 26 * Mathf.Sin(blade), outward) * across * bladeWidth;
                geometry.Vertex(mid - twist, new Vector2(.55f, t), 1f);
                geometry.Vertex(mid + outward * bladeWidth * .29f, new Vector2(.75f, t), 1f);
                geometry.Vertex(mid + twist, new Vector2(.95f, t), 1f);
                if (row == 0) continue;
                int previous = start + (row - 1) * 3;
                geometry.Quad(previous, previous + 1, previous + 3, previous + 4);
                geometry.Quad(previous + 1, previous + 2, previous + 4, previous + 5);
            }
        }

        AddStalk(geometry, roots[0], roots[0] + new Vector3(-.04f, 1.12f, -.06f), .016f);
        AddStalk(geometry, roots[1], roots[1] + new Vector3(.065f, .87f, .075f), .018f);
        Bounds bounds = BoundsOfVertices(geometry.vertices, Matrix4x4.identity);
        Vector3 scale = new(size.x / bounds.size.x, size.y / bounds.size.y, size.z / bounds.size.z);
        Vector3 origin = new(bounds.center.x, bounds.min.y, bounds.center.z);
        for (int i = 0; i < geometry.vertices.Count; i++)
            geometry.vertices[i] = prefabToMesh.MultiplyPoint3x4(Vector3.Scale(geometry.vertices[i] - origin, scale));
        Mesh mesh = geometry.Finish();
        mesh.name = "ShoreCattailClump";
        return mesh;
    }

    private static void AddStalk(WoodlandFloorAuthoring.Geometry geometry, Vector3 root, Vector3 tip, float headRadius)
    {
        Tube(geometry, root, Vector3.Lerp(root, tip, .83f), 5, .69f, .81f, .07f, .83f, new[] {.008f, .006f, .004f});
        Tube(geometry, Vector3.Lerp(root, tip, .77f), Vector3.Lerp(root, tip, .94f), 7, .035f, .465f, .04f, .96f,
            new[] {headRadius * .38f, headRadius * .95f, headRadius, headRadius * .97f, headRadius * .82f, headRadius * .28f});
        Tube(geometry, Vector3.Lerp(root, tip, .93f), tip, 5, .69f, .81f, .85f, 1f, new[] {.004f, .0006f});
    }

    private static void Tube(WoodlandFloorAuthoring.Geometry geometry, Vector3 from, Vector3 to,
        int sides, float u0, float u1, float v0, float v1, float[] radii)
    {
        int start = geometry.vertices.Count;
        Vector3 axis = (to - from).normalized;
        Vector3 across = Vector3.Cross(axis, Vector3.forward).normalized;
        Vector3 depth = Vector3.Cross(across, axis);
        for (int row = 0; row < radii.Length; row++)
        {
            float t = row / (float)(radii.Length - 1);
            for (int side = 0; side <= sides; side++)
            {
                float angle = side * Mathf.PI * 2 / sides;
                Vector3 offset = across * Mathf.Cos(angle) + depth * Mathf.Sin(angle);
                geometry.Vertex(Vector3.Lerp(from, to, t) + offset * radii[row],
                    new Vector2(Mathf.Lerp(u0, u1, side / (float)sides), Mathf.Lerp(v0, v1, t)), 1f);
                if (row == 0 || side == sides) continue;
                int previous = start + (row - 1) * (sides + 1) + side;
                geometry.Quad(previous, previous + sides + 1, previous + 1, previous + sides + 2);
            }
        }
    }

    private static void WriteCattailAtlas()
    {
        const int size = 512;
        var pixels = new Color32[size * size];
        var random = new System.Random(70127);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (size - 1f);
            float v = y / (size - 1f);
            float grain = (float)random.NextDouble() - .5f;
            Color color;
            if (u < .5f)
            {
                float felt = .76f * Mathf.PerlinNoise(x * .37f, y * .19f) + .24f * (grain + .5f);
                color = Color.Lerp(new Color(.23f, .12f, .064f), new Color(.53f, .34f, .18f), felt);
            }
            else
            {
                float fiber = Mathf.PerlinNoise(x * .49f, y * .021f) - .5f;
                float ridge = Mathf.Exp(-Mathf.Pow((u - .75f) * 52f, 2f));
                color = new Color(.36f, .45f, .22f) * (1f + .24f * fiber + .14f * ridge + .11f * grain);
                color = Color.Lerp(color, new Color(.59f, .50f, .25f) * (1f + .17f * fiber),
                    Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.75f + .06f * fiber, .99f, v)));
            }
            color.a = 1;
            pixels[y * size + x] = color;
        }
        var atlas = new Texture2D(size, size, TextureFormat.RGB24, false);
        try
        {
            atlas.SetPixels32(pixels);
            atlas.Apply();
            File.WriteAllBytes(CattailAtlas, atlas.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(atlas); }
    }
}
