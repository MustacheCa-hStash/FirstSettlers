using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class LeafClusterPrefabBuilder
{
    public const string TexturePath = "Assets/Textures/Foliage/T_LeafCluster_Atlas.png";
    public const string MeshPath = "Assets/Models/Foliage/LeafCluster.asset";
    public const string MaterialPath = "Assets/Materials/M_LeafCluster.mat";
    public const string PrefabPath = "Assets/Resources/Foliage/LeafCluster.prefab";
    public const string FarMeshPath = "Assets/Models/Foliage/LeafScatter_LOD1.asset";
    public const string FarPrefabPath = "Assets/Resources/Foliage/LeafScatter_LOD1.prefab";

    [MenuItem("Tools/Foliage/Build Forest Leaf Cluster")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Author leaves in Edit mode.");
        EnsureFolder("Assets/Models/Foliage"); EnsureFolder("Assets/Resources/Foliage");
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Painted leaf atlas is missing.");
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.45f;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.isReadable = true;
        importer.SaveAndReimport();
        Mesh built, farBuilt;
        try
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            built = BuildMesh(atlas, false); farBuilt = BuildMesh(atlas, true);
        }
        finally { importer.isReadable = false; importer.SaveAndReimport(); }
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null) { AssetDatabase.CreateAsset(built, MeshPath); mesh = built; }
        else { UpdateMesh(mesh,built); UnityEngine.Object.DestroyImmediate(built); }
        Shader shader = Shader.Find("FirstSettlers/Leaf Cluster Instanced");
        if (shader == null) throw new InvalidOperationException("Leaf cluster shader is missing.");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
        material.shader = shader;
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_UseAtlasColor", 0f);
        material.SetFloat("_Cutoff", 0.45f);
        material.SetFloat("_AmbientStrength", 0.12f);
        material.SetFloat("_FadeStart", 20f); material.SetFloat("_FadeEnd", 28f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        var root = new GameObject("LeafCluster");
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets();
        SaveFar(farBuilt, material);
        Debug.Log($"LEAF CLUSTER BUILT: {mesh.triangles.Length / 3} triangles, {mesh.vertexCount} vertices, bounds {mesh.bounds.size}; instanced material, no colliders.");
    }

    private static void SaveFar(Mesh built, Material material)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FarMeshPath);
        if (mesh == null) { AssetDatabase.CreateAsset(built, FarMeshPath); mesh = built; }
        else { UpdateMesh(mesh,built); UnityEngine.Object.DestroyImmediate(built); }
        var root = new GameObject("LeafScatter_LOD1");
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            PrefabUtility.SaveAsPrefabAsset(root, FarPrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets();
    }

    private static void UpdateMesh(Mesh mesh,Mesh built)
    {
        // CopySerialized alone can leave existing renderer GPU vertex buffers stale.
        mesh.Clear(); mesh.name=built.name; mesh.vertices=built.vertices; mesh.normals=built.normals;
        mesh.uv=built.uv; mesh.colors=built.colors; mesh.triangles=built.triangles; mesh.bounds=built.bounds;
        var scatter=new List<Vector4>();built.GetUVs(1,scatter);mesh.SetUVs(1,scatter);
        mesh.UploadMeshData(false); EditorUtility.SetDirty(mesh);
    }

    // Asymmetric drifts: independent headings, a few pairs, and empty space between leaves.
    private static Mesh BuildMesh(Texture2D atlas, bool far)
    {
        var vertices = new List<Vector3>(); var uv = new List<Vector2>();
        var triangles = new List<int>(); var colors = new List<Color>();
        var scatter = new List<Vector4>();int leafIndex=0;
        Color32[] pixels = atlas.GetPixels32();
        AddLeaf(0, new Vector2(-.40f, .12f), -19, .27f, .002f, .009f, .95f);
        AddLeaf(1, new Vector2(.08f, -.19f), 73, .28f, .003f, .007f, .92f);
        AddLeaf(2, new Vector2(.40f, .05f), 148, .25f, .002f, .011f, .98f);
        AddLeaf(3, new Vector2(-.14f, .23f), -74, .24f, .004f, .008f, .94f);
        if (!far)
        {
            AddLeaf(2, new Vector2(-.26f, -.15f), 34, .21f, .003f, .007f, .90f);
            AddLeaf(0, new Vector2(.28f, -.25f), -41, .22f, .002f, .006f, .93f);
            AddLeaf(1, new Vector2(-.31f, .09f), 126, .20f, .010f, .008f, .96f);
            AddLeaf(3, new Vector2(.26f, .18f), 12, .25f, .003f, .009f, .88f);
            AddLeaf(0, new Vector2(.02f, .04f), -118, .26f, .003f, .008f, 1f);
        }
        Mesh result = new Mesh { name = far ? "LeafScatter_LOD1" : "LeafScatter" };
        result.SetVertices(vertices); result.SetUVs(0, uv); result.SetColors(colors); result.SetTriangles(triangles, 0);
        result.SetUVs(1,scatter);
        result.RecalculateBounds();
        float baseY = result.bounds.min.y;
        for (int i = 0; i < vertices.Count; i++) vertices[i] -= Vector3.up * baseY;
        result.SetVertices(vertices);
        result.RecalculateNormals(); result.RecalculateBounds();
        return result;

        void AddLeaf(int tile, Vector2 center, float yaw, float length, float lift, float curl, float tone)
        {
            int identity=leafIndex++;
            int tileSizeX = atlas.width / 2, tileSizeY = atlas.height / 2;
            int ox = tile % 2 * tileSizeX, oy = (1 - tile / 2) * tileSizeY;
            int minX = ox + tileSizeX, minY = oy + tileSizeY, maxX = ox, maxY = oy;
            for (int y = oy; y < oy + tileSizeY; y++) for (int x = ox; x < ox + tileSizeX; x++)
                if (pixels[y * atlas.width + x].a >= 100)
                { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            if (maxX <= minX || maxY <= minY) throw new InvalidOperationException("Atlas quadrant contains no leaf silhouette.");
            float pixelsPerMeter = (maxY - minY + 1) / length;
            float centerX = (minX + maxX) * 0.5f;
            int strips = far ? 1 : 2;
            int start = vertices.Count;
            Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            for (int row = 0; row <= strips; row++)
            {
                float t = row / (float)strips;
                float y = Mathf.Lerp(minY - 2, maxY + 2, t);
                // Envelope covers this row's adjoining strips, avoiding chopped-off lobes.
                int y0 = Mathf.Clamp(Mathf.FloorToInt(y - (maxY - minY) / (float)strips), oy, oy + tileSizeY - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(y + (maxY - minY) / (float)strips), oy, oy + tileSizeY - 1);
                int left = maxX, right = minX;
                for (int py = y0; py <= y1; py++) for (int px = minX; px <= maxX; px++)
                    if (pixels[py * atlas.width + px].a >= 100) { left = Math.Min(left, px); right = Math.Max(right, px); }
                left = Math.Max(ox + 1, left - 3); right = Math.Min(ox + tileSizeX - 2, right + 3);
                for (int column = 0; column < 3; column++)
                {
                    float px = column == 0 ? left : column == 1 ? centerX : right;
                    float edge = column == 1 ? 0 : 1;
                    float height = lift + curl * (edge * 0.58f + (t - 0.45f) * (t - 0.45f) * 1.6f);
                    // A shallow fold follows the main vein; perimeter curls rise independently.
                    if (column == 1) height += curl * 0.22f * Mathf.Sin(t * Mathf.PI);
                    Vector3 p = rotation * new Vector3((px - centerX) / pixelsPerMeter, height, (y - (minY + maxY) * 0.5f) / pixelsPerMeter);
                    vertices.Add(p + new Vector3(center.x, 0, center.y));
                    scatter.Add(new Vector4(center.x,center.y,identity,1));
                    uv.Add(new Vector2((px + 0.5f) / atlas.width, (y + 0.5f) / atlas.height));
                    // Plain color per silhouette; the atlas contributes alpha only.
                    Color[] palette = { new Color(.43f,.29f,.13f), new Color(.34f,.24f,.14f),
                        new Color(.52f,.40f,.23f), new Color(.39f,.31f,.21f) };
                    colors.Add(palette[tile] * new Color(tone,tone,tone,1));
                }
            }
            for (int row = 0; row < strips; row++) for (int column = 0; column < 2; column++)
            {
                int a = start + row * 3 + column, b = a + 3, c = b + 1, d = a + 1;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
        }
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, split));
        AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
}
