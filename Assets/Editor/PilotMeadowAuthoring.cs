using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class PilotMeadowAuthoring
{
    private const string Texture = "Assets/Textures/Grass/T_PilotMeadowBlades.png";
    private const string Prefabs = "Assets/Prefabs/";
    private const string Meshes = "Assets/Models/Grass+Flowers/";
    private const float Cutoff = .42f;

    public static void BuildBatch()
    {
        try
        {
            Build();
            Debug.Log("PILOT MEADOW AUTHORING PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void Build()
    {
        AssetDatabase.ImportAsset(Texture, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(Texture);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = Cutoff;
        importer.filterMode = FilterMode.Trilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();

        var meadow = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Grass/M_GrassTuftBroad.mat"));
        meadow.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Texture));
        meadow.EnableKeyword("_MEADOW_BLADE_ALBEDO");
        meadow.SetFloat("_MeadowBladeAlbedo", 1);
        meadow.SetFloat("_Cutoff", Cutoff);
        meadow.SetFloat("_BladeMinY", -.035f);
        meadow.SetFloat("_BladeMaxY", .76f);
        meadow.SetFloat("_HeightGradientPower", .65f);
        meadow.SetFloat("_BladeTransmission", .16f);
        meadow.SetFloat("_WindStrength", .075f);
        meadow.SetFloat("_WindGustScale", .32f);
        meadow.SetFloat("_WindSpeed", 1.6f);
        meadow.SetFloat("_WindFlutterStrength", .012f);
        meadow.SetFloat("_NoiseScale", .055f);
        meadow.SetFloat("_NoiseStrength", 1.25f);
        meadow.SetColor("_BaseColor", new Color(.60f,.85f,.77f));
        meadow.SetColor("_RootTint", new Color(.68f,.71f,.56f));
        meadow.SetColor("_TipTint", new Color(1.00f,1.02f,.96f));
        var material = Save(meadow, "Assets/Materials/M_Grass/M_PilotMeadowBlades.mat");
        var near = BuildTuft("PilotMeadowGrass_Near", material, true);
        var far = BuildTuft("PilotMeadowGrass_Far", material, false);

        var ground = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Terrain/M_TerrainBase.mat"));
        ground.SetColor("_DarkGrassColor", new Color(.20f,.265f,.11f));
        ground.SetColor("_MidGrassColor", new Color(.27f,.34f,.145f));
        ground.SetColor("_LightGrassColor", new Color(.36f,.43f,.185f));
        ground.SetColor("_GrassGroundTint", new Color(.80f,.98f,.89f));
        ground.SetFloat("_GrassGroundDetailStrength", .54f);
        var terrain = Save(ground, "Assets/Materials/M_Terrain/M_TerrainPilotMeadow.mat");

        var scene = EditorSceneManager.OpenScene(ForestVarietyWorldStudy.Pilot);
        var world = new SerializedObject(Object.FindAnyObjectByType<WorldManager>());
        var grass = world.FindProperty("grassSettings");
        grass.FindPropertyRelative("grassPrefab").objectReferenceValue = near;
        grass.FindPropertyRelative("billboardGrassPrefab").objectReferenceValue = far;
        world.FindProperty("terrainMaterial").objectReferenceValue = terrain;
        world.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    internal static GameObject BuildTuft(string name, Material material, bool detailed, bool woodland = false)
    {
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        var rows = detailed ? new[] {0f, .32f, .68f, 1f} : new[] {0f, .50f, 1f};

        void Card(float angle, float width, float height, float lean, bool mirror, bool seedStems = false)
        {
            if (woodland)
            {
                width *= .83f;
                height *= .59f;
                lean *= 1.35f;
            }
            var side = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            var bend = Vector3.Cross(side, Vector3.up);
            int start = vertices.Count;
            const float inset = 12f / 512f;
            foreach (float t in rows)
            {
                var center = bend * (lean * t * t) + Vector3.up * Mathf.Lerp(-.035f, height, t);
                float halfWidth = width * .5f * (.84f + .16f * Mathf.Sin(t * Mathf.PI));
                vertices.Add(center - side * halfWidth);
                vertices.Add(center + side * halfWidth);
                float u = seedStems ? .5f : 0f;
                float left = u + inset * .5f, right = u + (1 - inset) * .5f;
                float v = Mathf.Lerp(inset, 1 - inset, t);
                uv.Add(new Vector2(mirror ? right : left, v));
                uv.Add(new Vector2(mirror ? left : right, v));
            }
            for (int row = 0; row < rows.Length - 1; row++)
            {
                int a = start + row * 2;
                triangles.AddRange(new[] {a, a + 1, a + 2, a + 1, a + 3, a + 2});
            }
        }

        Card(.15f, .49f, detailed ? .51f : .46f, .10f, false);
        Card(detailed ? 1.99f : 1.73f, .46f, detailed ? .47f : .42f, .09f, true);
        if (detailed)
            Card(4.17f, .45f, woodland ? .42f : .76f, .12f, false, !woodland);
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var runtime = Save(mesh, Meshes + name + ".asset");

        var root = new GameObject(name);
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = runtime;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return PrefabUtility.SaveAsPrefabAsset(root, Prefabs + name + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static T Save<T>(T asset, string path) where T : Object
    {
        asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!existing)
        {
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
        EditorUtility.CopySerialized(asset, existing);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(asset);
        return existing;
    }
}
