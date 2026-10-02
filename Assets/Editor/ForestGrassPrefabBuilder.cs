using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ForestGrassPrefabBuilder
{
    public const string NearPath = "Assets/Resources/Foliage/ForestGrassTuft_LOD0.prefab";
    public const string FarPath = "Assets/Resources/Foliage/ForestGrassTuft_LOD1.prefab";
    public const string MaterialPath = "Assets/Materials/M_Grass/M_ForestGrassTuft.mat";
    private struct Blade { public Vector2 root; public float height, width, yaw, lean; public bool dry; }

    [MenuItem("Tools/Foliage/Build Forest Grass Tuft")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Author forest grass in Edit mode.");
        Shader shader = Shader.Find("FirstSettlers/Forest Grass Instanced");
        if (shader == null) throw new InvalidOperationException("Forest grass shader is missing.");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material,MaterialPath); }
        material.shader = shader;
        material.SetColor("_BaseColor",new Color(0.29f,0.40f,0.17f,1));
        material.SetColor("_DryColor",new Color(0.38f,0.33f,0.18f,1));
        material.SetColor("_RootColor",new Color(0.19f,0.16f,0.10f,1));
        material.SetFloat("_RootBlendHeight",0.08f);
        material.SetFloat("_InstanceVariation",0.06f);
        material.SetFloat("_AmbientStrength",0.20f);
        material.SetFloat("_UpwardNormalBlend",0.65f);
        material.SetFloat("_ReceiveShadows",1);
        material.SetFloat("_WindStrength",0.018f); material.SetFloat("_WindFlutterStrength",0.005f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        var blades = new List<Blade>();
        for (int i = 0; i < 14; i++)
        {
            // An offset fan rather than a ring/rosette. Each root remains separate.
            float angle = i * 2.399963f;
            float radius = 0.022f + Mathf.Sqrt((i + 0.5f)/14) * 0.075f;
            blades.Add(new Blade {
                root = new Vector2(Mathf.Cos(angle)*radius*1.15f,Mathf.Sin(angle)*radius*0.72f),
                height = Mathf.Lerp(0.09f,0.24f,Hash(i,17)),
                width = Mathf.Lerp(0.016f,0.028f,Hash(i,31)),
                yaw = angle * Mathf.Rad2Deg + Mathf.Lerp(-35,35,Hash(i,59)),
                lean = Mathf.Lerp(0.025f,0.095f,Hash(i,83)), dry = i == 3 || i == 11
            });
        }
        BuildOne("ForestGrassTuft_LOD0",blades,false,material,NearPath);
        BuildOne("ForestGrassTuft_LOD1",blades,true,material,FarPath);
        AssetDatabase.SaveAssets();
    }
    private static float Hash(int i,int salt) => Mathf.Repeat(Mathf.Sin(i*127.1f+salt*311.7f)*43758.5453f,1);
    private static void BuildOne(string name,List<Blade> blades,bool far,Material material,string path)
    {
        var vertices = new List<Vector3>(); var uv = new List<Vector2>();
        var colors = new List<Color>(); var triangles = new List<int>();
        for (int i = 0; i < blades.Count; i++)
        {
            // Retain one dry blade and the same roots/heights through the LOD switch.
            if (far && (i == 4 || (i % 2 != 0 && i != 3))) continue;
            Blade blade = blades[i];
            int start = vertices.Count;
            Quaternion yaw = Quaternion.Euler(0,blade.yaw,0);
            int rows = far ? 1 : 2;
            for (int row = 0; row < rows; row++)
            {
                float t = row * 0.60f;
                float width = blade.width * (1-t*0.7f);
                for (int side = 0; side < 2; side++)
                {
                    Vector3 p = yaw * new Vector3((side-0.5f)*width + blade.lean*t*t,blade.height*t-0.009f*(1-t),blade.lean*t*t*0.28f);
                    vertices.Add(p+new Vector3(blade.root.x,0,blade.root.y));
                    uv.Add(new Vector2(side,t));
                    float tone = Mathf.Lerp(0.94f,1.04f,Hash(i,127));
                    colors.Add(new Color(tone,tone,tone,blade.dry?1:0));
                }
            }
            vertices.Add(yaw * new Vector3(blade.lean,blade.height,blade.lean*0.28f)+new Vector3(blade.root.x,0,blade.root.y));
            uv.Add(new Vector2(0.5f,1));
            float tipTone = Mathf.Lerp(0.94f,1.04f,Hash(i,127));
            colors.Add(new Color(tipTone,tipTone,tipTone,blade.dry?1:0));
            for (int row = 0; row < rows-1; row++)
            {
                int a = start+row*2;
                triangles.Add(a); triangles.Add(a+2); triangles.Add(a+3);
                triangles.Add(a); triangles.Add(a+3); triangles.Add(a+1);
            }
            int last = start+(rows-1)*2;
            triangles.Add(last); triangles.Add(last+2); triangles.Add(last+1);
        }
        var built = new Mesh {name=name};
        built.SetVertices(vertices); built.SetUVs(0,uv); built.SetColors(colors); built.SetTriangles(triangles,0);
        built.RecalculateNormals(); built.RecalculateBounds();
        string meshPath = "Assets/Models/Foliage/"+name+".asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null) { AssetDatabase.CreateAsset(built,meshPath); mesh=built; }
        else
        {
            // Notify live renderer buffers when rebuilding an already-rendered mesh.
            mesh.Clear(); mesh.name=built.name; mesh.vertices=built.vertices; mesh.normals=built.normals;
            mesh.uv=built.uv; mesh.colors=built.colors; mesh.triangles=built.triangles; mesh.bounds=built.bounds;
            mesh.UploadMeshData(false); EditorUtility.SetDirty(mesh); UnityEngine.Object.DestroyImmediate(built);
        }
        var root = new GameObject(name);
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=root.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=true;
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Debug.Log($"FOREST GRASS BUILT: {path}, {mesh.triangles.Length/3} triangles, {mesh.vertexCount} vertices, bounds {mesh.bounds.size}.");
    }
}
