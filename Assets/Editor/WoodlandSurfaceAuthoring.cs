using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class WoodlandSurfaceAuthoring
{
    private const string FrondAtlas = "WoodlandFernFrond";

    public static void BuildBatch()
    {
        try
        {
            Build();
            AssetDatabase.SaveAssets();
            Debug.Log("WOODLAND SURFACE AUTHORING PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ForestVarietyWorldStudy.Pilot);
        var world = new SerializedObject(Object.FindAnyObjectByType<WorldManager>());
        var terrain = (Material)world.FindProperty("terrainMaterial").objectReferenceValue;
        ConfigureTerrainSurface(terrain);

        var fern = world.FindProperty("fernSettings");
        var fernPrefabs = BuildFerns();
        fern.FindPropertyRelative("prefab").objectReferenceValue = fernPrefabs.near;
        fern.FindPropertyRelative("distantPrefab").objectReferenceValue = fernPrefabs.middle;
        fern.FindPropertyRelative("coarsePrefab").objectReferenceValue = fernPrefabs.far;
        fern.FindPropertyRelative("sizeMultiplier").floatValue = 1.48f;
        PilotLandscapeAuthoring.ConfigureShrubs(world.FindProperty("woodlandShrubs"), PilotLandscapeAuthoring.BuildShrubs());
        world.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the woodland pilot.");
    }

    internal static void ConfigureTerrainSurface(Material terrain)
    {
        WoodlandFloorAuthoring.AssignLitter(terrain, "_LeafLitter", "WoodlandDuffDry");
        WoodlandFloorAuthoring.AssignLitter(terrain, "_MixedForestFloor", "WoodlandDuffDamp");
        terrain.SetTexture("_LeafLitterAO", WoodlandFloorAuthoring.Import("WoodlandDuffDry_AO", linear: true));
        terrain.SetFloat("_LeafLitterTiling", 1f/3.84f);
        terrain.SetFloat("_LeafLitterNormalStrength", .56f);
        terrain.SetFloat("_LeafLitterHeightStrength", .008f);
        terrain.SetFloat("_MixedForestFloorHeightStrength", .006f);
        terrain.SetFloat("_ForestFloorMossStrength", .51f);
        terrain.SetVector("_ForestFloorScatter", new Vector4(.20f,.67f,.49f,1f));
        EditorUtility.SetDirty(terrain);
    }

    internal static (GameObject near, GameObject middle, GameObject far) BuildFerns()
    {
        var atlas = WoodlandFloorAuthoring.Import(FrondAtlas, cutout: true);
        var shader = Shader.Find("FirstSettlers/Forest Fern Instanced");
        if (!shader) throw new InvalidOperationException("Forest fern shader is missing.");
        var material = new Material(shader) { enableInstancing = true, renderQueue = (int)RenderQueue.AlphaTest };
        material.SetTexture("_FrondTexture", atlas);
        material.SetFloat("_UseFrondTexture", 1);
        material.SetFloat("_Cutoff", .42f);
        material.SetFloat("_CanopyShade", .91f);
        material.SetFloat("_AmbientStrength", .16f);
        material.SetFloat("_WindStrength", .016f);
        material.SetColor("_BaseColor", new Color(.97f,1f,.92f));
        material = WoodlandFloorAuthoring.Save(material, "Assets/Materials/M_Grass/M_PilotWoodlandFern.mat");

        var read = new Texture2D(2,2);
        try
        {
            if (!read.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(atlas))))
                throw new InvalidOperationException("Could not read the baked fern atlas.");
            return (WoodlandFloorAuthoring.Prefab("PilotWoodlandFern", FernMesh(read, 10, new[]{0f,.13f,.30f,.51f,.71f,.88f,1f}), material),
                    WoodlandFloorAuthoring.Prefab("PilotWoodlandFern_Mid", FernMesh(read, 6, new[]{0f,.19f,.43f,.70f,1f}), material),
                    WoodlandFloorAuthoring.Prefab("PilotWoodlandFern_Far", FernMesh(read, 4, new[]{0f,.25f,.62f,1f}), material));
        }
        finally { Object.DestroyImmediate(read); }
    }

    private static Mesh FernMesh(Texture2D atlas, int fronds, float[] rows)
    {
        var colors = atlas.GetPixels32();
        const float bottom = .025f, top = .974f;
        var spans = new Vector2[rows.Length];
        for (int row=0; row<rows.Length; row++)
        {
            // Include both adjacent bands at each vertex row to keep leaflets
            // inside the tapered card throughout the section.
            int y0 = Mathf.FloorToInt(atlas.height*Mathf.Lerp(bottom,top,rows[Mathf.Max(0,row-1)]));
            int y1 = Mathf.CeilToInt(atlas.height*Mathf.Lerp(bottom,top,rows[Mathf.Min(rows.Length-1,row+1)]));
            int left=atlas.width, right=0;
            for(int y=y0; y<y1; y++)
            for(int x=0; x<atlas.width; x++)
            {
                if (colors[y*atlas.width+x].a<90) continue;
                left=Mathf.Min(left,x);
                right=Mathf.Max(right,x);
            }
            if (left>=right) throw new InvalidOperationException("A fern card has no visible texture.");
            spans[row] = new Vector2(Mathf.Clamp01((left-4f)/atlas.width),Mathf.Clamp01((right+5f)/atlas.width));
        }

        var mesh = new WoodlandFloorAuthoring.Geometry();
        for (int i=0; i<fronds; i++)
        {
            float angle = i*2.399963f+.19f*Mathf.Sin(i*3.1f);
            var outward = new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            var side = Vector3.Cross(Vector3.up,outward);
            float length = Mathf.Lerp(.49f,.73f,(1+Mathf.Sin(i*5.37f))*.5f);
            float lift = Mathf.Lerp(.26f,.49f,(1+Mathf.Sin(i*3.73f+.6f))*.5f);
            float arc = Mathf.Lerp(.81f,1f,(1+Mathf.Sin(i*7.33f))*.5f);
            float width = length*.84f;
            int start = mesh.vertices.Count;
            for (int row=0; row<rows.Length; row++)
            {
                float t = rows[row];
                float v = Mathf.Lerp(bottom,top,t);
                var point = outward*(.025f+length*t) + Vector3.up*(lift*Mathf.Sin(Mathf.PI*t*arc)-.018f) +
                    side*(.07f*Mathf.Sin(i*2f)*t*t);
                float tone = Mathf.Lerp(.77f,1f,Mathf.SmoothStep(0,1,t*1.7f))*(.95f+.06f*Mathf.Sin(i*2f));
                float spineU = .49f+.019f*t;
                float roll = Mathf.Sin(i*4.3f+t*.96f) * (.12f+.29f*t);
                var cross = side*Mathf.Cos(roll)+Vector3.up*Mathf.Sin(roll);
                mesh.Vertex(point+cross*((spans[row].x-spineU)*width), new Vector2(spans[row].x,v),tone);
                mesh.Vertex(point+cross*((spans[row].y-spineU)*width), new Vector2(spans[row].y,v),tone);
                if (row>0) mesh.Quad(start+row*2-2,start+row*2-1,start+row*2,start+row*2+1);
            }
        }
        return mesh.Finish();
    }
}
