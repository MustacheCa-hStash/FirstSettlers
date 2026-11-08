using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class WoodlandFloorAuthoring
{
    const string Textures = "Assets/Textures/Ground/WoodlandPilot/";
    const string Models = "Assets/Models/Foliage/";
    const string Prefabs = "Assets/Prefabs/";
    const string Materials = "Assets/Materials/M_Grass/";
    const float Cutoff = .42f;

    public static void BuildBatch()
    {
        try { Build(); Debug.Log("WOODLAND FLOOR AUTHORING PASS"); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void Build()
    {
        var ginger = Plant("WoodlandGinger", "Groundcover", false);
        var hazel = Plant("WoodlandHazel", "Hazel", true);
        var bark = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Trees/Woodland/WhiteOak_Bark.png");
        if (!bark) throw new InvalidOperationException("Woodland oak bark texture is required for deadwood.");
        var wood = ScatterMaterial("WoodlandDeadwood", bark, new Color(.88f,.83f,.74f), 0, 0);
        wood.SetFloat("_Cutoff", .02f);
        EditorUtility.SetDirty(wood);
        var deadwood = (Prefab("WoodlandDeadwood", BuildWood(false), wood), Prefab("WoodlandDeadwood_Far", BuildWood(true), wood));

        var terrain = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Terrain/M_TerrainPilotMeadow.mat"));
        AssignLitter(terrain, "_LeafLitter", "DryDuff");
        AssignLitter(terrain, "_MixedForestFloor", "DampDuff");
        terrain.SetTexture("_LeafLitterAO", Import("DryDuff_AO", linear: true));
        terrain.SetFloat("_LeafLitterTiling", .82f);
        terrain.SetFloat("_LeafLitterAOStrength", .24f);
        terrain.SetFloat("_LeafLitterNormalStrength", .58f);
        terrain.SetFloat("_LeafLitterNormalFadeStart", 9);
        terrain.SetFloat("_LeafLitterNormalFadeEnd", 32);
        terrain.SetFloat("_LeafLitterHeightStrength", .006f);
        terrain.SetFloat("_MixedForestFloorHeightStrength", .004f);
        terrain.SetColor("_LeafLitterColor", new Color(1f,1.06f,1.11f));
        terrain.SetFloat("_ForestFloorMacroScale", .075f);
        terrain.SetFloat("_ForestFloorMacroStrength", .22f);
        terrain.SetFloat("_ForestFloorMossStrength", .56f);
        terrain.SetColor("_MossColor", new Color(.63f,.70f,.48f));
        terrain.SetColor("_MossFillColor", new Color(.14f,.17f,.065f));
        terrain = Save(terrain, "Assets/Materials/M_Terrain/M_TerrainPilotWoodland.mat");

        var scene = EditorSceneManager.OpenScene(ForestVarietyWorldStudy.Pilot);
        var world = Object.FindAnyObjectByType<WorldManager>();
        var so = new SerializedObject(world);
        Configure(so.FindProperty("leafClusterSettings"), ginger, WoodlandScatterKind.Groundcover, .86f, .83f, 3.75f, 52, 13, 24,
            new Vector2(.67f, 1.38f), 47000);
        Configure(so.FindProperty("woodlandShrubs"), hazel, WoodlandScatterKind.Shrub, 3.15f, .55f, 1, 68, 20, 37,
            new Vector2(.68f,1.28f), 49300);
        Configure(so.FindProperty("woodlandDebris"), deadwood, WoodlandScatterKind.Deadwood, 4.8f, .49f, 1, 46, 15, 27,
            new Vector2(.56f,1.10f), 49700);
        so.FindProperty("fernSettings.gradualMossFalloff").boolValue = true;
        so.FindProperty("fernSettings.sizeMultiplier").floatValue = 1.52f;
        so.FindProperty("forestLighting.groundDirectShade").floatValue = .12f;
        so.FindProperty("terrainMaterial").objectReferenceValue = terrain;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the woodland floor pilot.");
        AssetDatabase.SaveAssets();
    }

    internal static void Configure(SerializedProperty settings, (GameObject near, GameObject far) assets,
        WoodlandScatterKind kind, float spacing, float density, float frequency, float range, float lodStart, float lodEnd,
        Vector2 scale, int seed)
    {
        settings.FindPropertyRelative("enabled").boolValue = true;
        settings.FindPropertyRelative("gpuIndirectRendering").boolValue = true;
        settings.FindPropertyRelative("woodlandKind").enumValueIndex = (int)kind;
        settings.FindPropertyRelative("prefab").objectReferenceValue = assets.near;
        settings.FindPropertyRelative("distantPrefab").objectReferenceValue = assets.far;
        settings.FindPropertyRelative("matchGrassRenderDistance").boolValue = false;
        settings.FindPropertyRelative("renderDistance").floatValue = range;
        settings.FindPropertyRelative("cellSize").floatValue = spacing;
        settings.FindPropertyRelative("density").floatValue = density;
        settings.FindPropertyRelative("placementMultiplier").floatValue = frequency;
        settings.FindPropertyRelative("useDistanceDensity").boolValue = kind == WoodlandScatterKind.Groundcover;
        settings.FindPropertyRelative("lodStart").floatValue = lodStart;
        settings.FindPropertyRelative("lodEnd").floatValue = lodEnd;
        settings.FindPropertyRelative("scaleRange").vector2Value = scale;
        settings.FindPropertyRelative("seedOffset").intValue = seed;
    }

    private static (GameObject near, GameObject far) Plant(string name, string map, bool shrub)
    {
        Texture2D albedo = Import(map, cutout: true);
        var material = ScatterMaterial(name, albedo, shrub ? new Color(.96f,1f,.93f) : new Color(.93f,1f,.92f), .64f, .14f);
        var pixels = new Texture2D(2,2);
        try
        {
            if (!pixels.LoadImage(File.ReadAllBytes(Textures + map + ".png"))) throw new InvalidOperationException("Could not read " + map);
            return (Prefab(name, BuildCards(pixels, shrub, false), material),
                Prefab(name + "_Far", BuildCards(pixels, shrub, true), material));
        }
        finally { Object.DestroyImmediate(pixels); }
    }

    internal static Material ScatterMaterial(string name, Texture2D texture, Color tint, float upward, float wrap)
    {
        var shader = Shader.Find("FirstSettlers/Leaf Cluster Instanced");
        if (!shader) throw new InvalidOperationException("Missing forest scatter shader.");
        var material = new Material(shader) { enableInstancing = true };
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Cutoff", Cutoff);
        material.SetFloat("_UseAtlasColor", 1);
        material.SetFloat("_WoodlandShade", 1);
        material.SetFloat("_UpwardNormal", upward);
        material.SetFloat("_DiffuseWrap", wrap);
        material.SetFloat("_AmbientStrength", .14f);
        return Save(material, Materials + "M_" + name + ".mat");
    }

    private static Mesh BuildCards(Texture2D atlas, bool shrub, bool far)
    {
        int strips = far ? 1 : 3;
        var pixels = atlas.GetPixels32();
        var mesh = new Geometry();
        void Card(float angle, float width, float height, float tilt, bool mirror)
        {
            Vector3 across = new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            Vector3 bend = Vector3.Cross(across, Vector3.up);
            int start = mesh.vertices.Count;
            for (int row=0; row<=strips; row++)
            {
                float t = row/(float)strips;
                int lo = atlas.width, hi = 0;
                int y0 = Mathf.Max(0, (row-1)*atlas.height/strips);
                int y1 = Mathf.Min(atlas.height-1, (row+1)*atlas.height/strips);
                for (int y=y0; y<=y1; y++) for (int x=0; x<atlas.width; x++)
                    if (pixels[y*atlas.width+x].a >= 88) { lo=Mathf.Min(lo,x); hi=Mathf.Max(hi,x); }
                float left = Mathf.Clamp01((lo-3f)/atlas.width), right = Mathf.Clamp01((hi+4f)/atlas.width);
                if (left >= right) throw new InvalidOperationException("Empty cutout card strip.");
                if (mirror) (left, right) = (1f - right, 1f - left);
                Vector3 center = Vector3.up * (-.015f + height*t) + bend * (tilt*t*t);
                mesh.Vertex(center+across*((left-.5f)*width),new Vector2(mirror?1-left:left,t),.76f+t*.24f);
                mesh.Vertex(center+across*((right-.5f)*width),new Vector2(mirror?1-right:right,t),.76f+t*.24f);
                if (row>0) mesh.Quad(start+row*2-2,start+row*2-1,start+row*2,start+row*2+1);
            }
        }
        Card(.16f, shrub ? .91f : .53f, shrub ? .88f : .20f, shrub ? .11f : .18f, false);
        Card(1.87f, shrub ? .78f : .49f, shrub ? .78f : .16f, shrub ? .16f : .24f, true);
        if (!far) Card(3.62f, shrub ? .65f : .39f, shrub ? .66f : .13f, shrub ? .21f : .25f, false);
        return mesh.Finish();
    }

    private static Mesh BuildWood(bool far)
    {
        var mesh = new Geometry();
        void Tube(Vector3[] points, float[] radius, int sides)
        {
            int start = mesh.vertices.Count;
            for (int row=0; row<points.Length; row++)
            {
                Vector3 axis = (points[Mathf.Min(row+1,points.Length-1)]-points[Mathf.Max(0,row-1)]).normalized;
                Vector3 side = Vector3.Cross(Vector3.up,axis).normalized;
                Vector3 up = Vector3.Cross(axis,side);
                for (int i=0;i<=sides;i++)
                {
                    float angle = Mathf.PI*2*i/sides;
                    float r = radius[row] * (1f + .12f*Mathf.Cos(3f*angle+row*.6f));
                    var offset = side*Mathf.Cos(angle) + up*Mathf.Sin(angle);
                    mesh.Vertex(points[row]+offset*r,new Vector2(i/(float)sides, points[row].z*1.68f), .74f+.24f*Mathf.Clamp01(offset.y+.35f));
                    if (row>0 && i<sides)
                    {
                        int a=start+(row-1)*(sides+1)+i;
                        mesh.Quad(a,a+sides+1,a+1,a+sides+2);
                    }
                }
            }
        }
        Vector3 a = new(-.06f,.075f,-.10f), b = new(.04f,.079f,.30f), c = new(-.08f,.045f,.75f), d = new(.13f,.016f,1.38f);
        Tube(far ? new[]{a,c,d} : new[]{a,b,c,d}, far ? new[]{.09f,.045f,.012f} : new[]{.09f,.071f,.045f,.012f}, far ? 4 : 6);
        Tube(new[]{b,new Vector3(.34f,.085f,.56f),new Vector3(.55f,.024f,.70f)},new[]{.052f,.028f,.006f},far ? 3 : 5);
        if (!far) Tube(new[]{c,new Vector3(-.29f,.067f,.88f),new Vector3(-.46f,.023f,1.07f)},new[]{.037f,.019f,.005f},4);
        return mesh.Finish();
    }

    internal sealed class Geometry
    {
        public readonly List<Vector3> vertices = new();
        readonly List<Vector2> uv = new();
        readonly List<Vector4> scatter = new();
        readonly List<Color> colors = new();
        readonly List<int> indices = new();
        public void Vertex(Vector3 position, Vector2 texcoord, float tone, int shape = 0)
        {
            vertices.Add(position);
            uv.Add(texcoord);
            // These meshes already have their complete shape; disable per-leaf variation.
            scatter.Add(new Vector4(shape, 0, 0, 0));
            colors.Add(new Color(tone,tone,tone,1));
        }
        public void Quad(int a,int b,int c,int d) => indices.AddRange(new[]{a,b,c,b,d,c});
        public Mesh Finish()
        {
            var mesh = new Mesh();
            mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetUVs(1,scatter);
            mesh.SetColors(colors); mesh.SetTriangles(indices,0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }

    internal static void AssignLitter(Material material, string prefix, string art)
    {
        material.SetTexture(prefix + "Albedo", Import(art + "_Albedo"));
        material.SetTexture(prefix + "Normal", Import(art + "_Normal", normal: true));
        material.SetTexture(prefix + "Height", Import(art + "_Height", linear: true));
    }

    internal static Texture2D Import(string name, bool linear = false, bool normal = false, bool cutout = false)
    {
        string path = Textures + name + ".png";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) throw new InvalidOperationException("Missing " + path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !normal && !linear;
        importer.wrapMode = cutout ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.alphaIsTransparency = cutout;
        importer.mipMapsPreserveCoverage = cutout;
        importer.alphaTestReferenceValue = Cutoff;
        importer.anisoLevel = cutout ? 2 : 8;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    internal static GameObject Prefab(string name, Mesh built, Material material)
    {
        var mesh = Save(built, Models + name + ".asset");
        var root = new GameObject(name);
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            Debug.Log($"WOODLAND FLOOR ASSET {name}: {mesh.GetIndexCount(0)/3} triangles, bounds {mesh.bounds.size}.");
            return PrefabUtility.SaveAsPrefabAsset(root, Prefabs + name + ".prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    internal static T Save<T>(T asset, string path) where T : Object
    {
        asset.name = Path.GetFileNameWithoutExtension(path);
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!existing) { AssetDatabase.CreateAsset(asset,path); return asset; }
        EditorUtility.CopySerialized(asset,existing);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(asset);
        return existing;
    }
}
