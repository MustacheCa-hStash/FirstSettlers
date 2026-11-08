using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class PilotLandscapeAuthoring
{
    private const string MeadowGround = "Assets/Textures/Ground/Grass/T_PilotMeadowGroundOblique.png";
    private const string Terrain = "Assets/Materials/M_Terrain/M_TerrainPilotWoodland.mat";

    public static void TerrainBatch() => Run(ConfigureTerrain);
    public static void UnderstoryBatch() => Run(ConfigureUnderstory);
    public static void StandBatch() => Run(ConfigureStands);
    public static void WoodlandPolishBatch() => Run(ConfigureWoodlandPolish);

    private static void Run(Action configure)
    {
        try
        {
            configure();
            AssetDatabase.SaveAssets();
            Debug.Log("PILOT LANDSCAPE AUTHORING PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void ConfigureTerrain()
    {
        AssetDatabase.ImportAsset(MeadowGround, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(MeadowGround) is not TextureImporter importer)
            throw new InvalidOperationException("Run Tools/BakePilotGrassGround.py before configuring the terrain.");
        importer.sRGBTexture = false;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 4;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "Standalone", overridden = true, maxTextureSize = 1024,
            format = TextureImporterFormat.BC7, compressionQuality = 80
        });
        importer.SaveAndReimport();
        var material = AssetDatabase.LoadAssetAtPath<Material>(Terrain);
        material.SetTexture("_GrassSurfaceMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MeadowGround));
        material.SetFloat("_GrassGroundTiling", 1f / 2.75f);
        material.SetFloat("_GrassGroundFarTiling", .092f);
        material.SetFloat("_GrassGroundScaleFadeStart", 38);
        material.SetFloat("_GrassGroundScaleFadeEnd", 107);
        material.SetVector("_GrassGroundSingleSample", new Vector4(132, 230, 0, 0));
        material.SetFloat("_GrassGroundDetailStrength", 1f);
        material.SetFloat("_GrassGroundDetailContrast", 2.6f);
        material.SetFloat("_GrassGroundDetailFadeStart", 520);
        material.SetFloat("_GrassGroundDetailFadeEnd", 1180);
        material.SetFloat("_GrassGroundHeightDepth", .007f);
        material.SetFloat("_GrassNormalStrength", .33f);
        material.SetColor("_GrassGroundTint", new Color(.89f,1.10f,.93f));
        material.SetColor("_GroundDarkGrassColor", new Color(.154f,.239f,.080f));
        material.SetColor("_GroundMidGrassColor", new Color(.224f,.324f,.114f));
        material.SetColor("_GroundLightGrassColor", new Color(.298f,.405f,.151f));
        material.SetVector("_RockSideTiling", new Vector4(.028f, .046f, 0, 0));
        EditorUtility.SetDirty(material);
    }

    private static void ConfigureWoodlandPolish()
    {
        ConfigureTerrain();
        WoodlandSurfaceAuthoring.ConfigureTerrainSurface(AssetDatabase.LoadAssetAtPath<Material>(Terrain));
        ConfigureRootSeating("M_PilotMeadowBlades", .19f);
        ConfigureRootSeating("M_PilotWoodlandSedges", .23f);
        var scene = EditorSceneManager.OpenScene(ForestVarietyWorldStudy.Pilot);
        var world = new SerializedObject(Object.FindAnyObjectByType<WorldManager>());
        var profile = (TreeDistributionProfile)world.FindProperty("treeSettings.distributionProfile").objectReferenceValue;
        profile.variedWoodlandEdges = true;
        EditorUtility.SetDirty(profile);
        world.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the woodland polish settings.");
    }

    private static void ConfigureRootSeating(string name, float amount)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Grass/" + name + ".mat");
        material.SetFloat("_RootSeatFade", amount);
        EditorUtility.SetDirty(material);
    }

    private static void ConfigureUnderstory()
    {
        var shrubs = BuildShrubs();

        const string grass = "Assets/Materials/M_Grass/M_Pilot";
        var forestGrass = new Material(AssetDatabase.LoadAssetAtPath<Material>(grass + "MeadowBlades.mat"));
        forestGrass.SetFloat("_BladeMaxY", .31f);
        forestGrass.SetFloat("_WindStrength", .045f);
        forestGrass.SetColor("_BaseColor", new Color(.68f,.83f,.72f));
        forestGrass = WoodlandFloorAuthoring.Save(forestGrass, grass + "WoodlandSedges.mat");
        var nearSedges = PilotMeadowAuthoring.BuildTuft("PilotWoodlandSedges", forestGrass, true, true);
        var farSedges = PilotMeadowAuthoring.BuildTuft("PilotWoodlandSedges_Far", forestGrass, false, true);

        var scene = EditorSceneManager.OpenScene(ForestVarietyWorldStudy.Pilot);
        var world = new SerializedObject(Object.FindAnyObjectByType<WorldManager>());
        ConfigureShrubs(world.FindProperty("woodlandShrubs"), shrubs);
        var cover = world.FindProperty("leafClusterSettings");
        cover.FindPropertyRelative("scaleRange").vector2Value = new Vector2(.89f,1.78f);
        cover.FindPropertyRelative("density").floatValue = .89f;
        world.FindProperty("fernSettings.sizeMultiplier").floatValue = 1.85f;
        world.FindProperty("fernSettings.minMoisture").floatValue = .31f;
        world.FindProperty("woodlandDebris.enabled").boolValue = false;
        world.FindProperty("treeSettings.disableBerryBushes").boolValue = true;
        world.FindProperty("grassSettings.forestGrassPrefab").objectReferenceValue = nearSedges;
        world.FindProperty("grassSettings.forestBillboardGrassPrefab").objectReferenceValue = farSedges;
        world.FindProperty("grassSettings.forestDarkGrassColor").colorValue = new Color(.13f,.22f,.10f);
        world.FindProperty("grassSettings.forestMidGrassColor").colorValue = new Color(.20f,.33f,.15f);
        world.FindProperty("grassSettings.forestLightGrassColor").colorValue = new Color(.28f,.43f,.18f);
        world.FindProperty("forestLighting.groundDarkness").floatValue = .70f;
        world.FindProperty("forestLighting.groundDirectShade").floatValue = .03f;
        world.FindProperty("forestLighting.groundSunStrength").floatValue = 1.24f;
        world.ApplyModifiedPropertiesWithoutUndo();
        var berries = Object.FindAnyObjectByType<BerryBushManager>();
        if (berries) berries.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save pilot understory.");
    }

    internal static (GameObject near, GameObject far) BuildShrubs()
    {
        var atlas = WoodlandFloorAuthoring.Import("ShrubSprays", cutout: true);
        var material = WoodlandFloorAuthoring.ScatterMaterial("PilotShrubs", atlas, new Color(.91f,.97f,.88f), .56f, .17f);
        material.SetFloat("_WoodlandShapes", 3);
        material.SetFloat("_WoodlandShade", .82f);
        material.SetFloat("_AmbientStrength", .17f);
        EditorUtility.SetDirty(material);
        return (WoodlandFloorAuthoring.Prefab("PilotShrubs", ShrubMesh(false), material),
                WoodlandFloorAuthoring.Prefab("PilotShrubs_Far", ShrubMesh(true), material));
    }

    internal static void ConfigureShrubs(SerializedProperty settings, (GameObject near, GameObject far) shrubs) =>
        WoodlandFloorAuthoring.Configure(settings, shrubs, WoodlandScatterKind.HabitatShrub,
            2.62f, .84f, 1f, 83f, 23f, 39f, new Vector2(.74f,1.35f), 49300);

    private static Mesh ShrubMesh(bool far)
    {
        var mesh = new WoodlandFloorAuthoring.Geometry();
        void Spray(int shape, int art, float angle, Vector3 root, float width, float height, float bend, bool flip)
        {
            Vector3 axis = new(Mathf.Cos(angle),0,Mathf.Sin(angle));
            Vector3 forward = Vector3.Cross(axis, Vector3.up);
            int start = mesh.vertices.Count;
            int segments = far ? 1 : 2;
            float stem = art == 0 ? .25f : art == 1 ? .39f : .52f;
            if (flip) stem = 1 - stem;
            float spread = shape == 0 ? 1.38f : shape == 1 ? 1.37f : 1.63f;
            float rise = shape == 0 ? 1.24f : shape == 1 ? 1.34f : 1.58f;
            var size = new Vector3(spread,rise,spread);
            Vector2 corner = new((art%2)*.5f+12f/1024, (1-art/2)*.5f+12f/1024);
            const float tile = 488f/1024;
            for (int row=0;row<=segments;row++)
            {
                float t = row/(float)segments;
                Vector3 point = root + Vector3.up * (height*t-.024f) + forward * (bend*t*t);
                float tone = Mathf.Lerp(.73f,1f,t);
                mesh.Vertex(Vector3.Scale(point-axis*(width*stem),size), corner+new Vector2(flip?tile:0,t*tile),tone,shape);
                mesh.Vertex(Vector3.Scale(point+axis*(width*(1-stem)),size), corner+new Vector2(flip?0:tile,t*tile),tone,shape);
                if (row>0) mesh.Quad(start+row*2-2,start+row*2-1,start+row*2,start+row*2+1);
            }
        }
        Spray(0,0,.13f,new Vector3(-.12f,0,0),1.03f,.65f,.29f,false);
        Spray(0,0,1.71f,new Vector3(.18f,0,.04f),.90f,.53f,.34f,true);
        Spray(1,1,.62f,new Vector3(-.10f,0,0),.82f,1.29f,.15f,false);
        Spray(1,1,2.18f,new Vector3(.13f,0,.06f),.71f,1.03f,.24f,true);
        Spray(2,2,.21f,Vector3.zero,1.16f,1.05f,.28f,false);
        Spray(2,2,1.86f,new Vector3(.17f,0,-.13f),1.05f,.97f,.29f,true);
        if (!far)
        {
            Spray(0,0,3.30f,new Vector3(-.15f,0,-.10f),.94f,.56f,.35f,true);
            Spray(0,2,4.95f,new Vector3(.08f,0,.08f),.73f,.48f,.30f,false);
            Spray(1,1,4.08f,new Vector3(-.14f,0,-.10f),.69f,.85f,.36f,false);
            Spray(1,0,5.38f,new Vector3(.04f,0,-.13f),.65f,.67f,.30f,true);
            Spray(2,2,2.92f,new Vector3(-.24f,0,.07f),.99f,.84f,.32f,true);
            Spray(2,0,4.58f,new Vector3(.21f,0,.17f),.99f,.74f,.41f,false);
            Spray(2,2,5.49f,new Vector3(-.12f,0,.19f),.83f,.94f,.25f,false);
            Spray(2,1,3.71f,new Vector3(.08f,0,-.09f),.89f,.77f,.38f,true);
        }
        return mesh.Finish();
    }

    private static void ConfigureStands()
    {
        const string original = "Assets/ScriptableObjects/ForestVarietyDistribution.asset";
        var scene = EditorSceneManager.OpenScene(ForestVarietyWorldStudy.Pilot);
        var baseline = AssetDatabase.LoadAssetAtPath<TreeDistributionProfile>(original);
        var denser = Object.Instantiate(baseline);
        var world = Object.FindAnyObjectByType<WorldManager>();
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        var config = (WorldConfiguration)typeof(WorldManager).GetMethod("BuildConfiguration", privateInstance).Invoke(world, null);
        var g = config.Generation;
        var treeSettings = config.Foliage.Trees;
        var reference = treeSettings.distributionProfile;
        TreeInstanceData[] Sample(ChunkCoord chunk, WorldFeatureGenerationSettings snapshot) =>
            DistantTreePlacement.Generate(chunk,g.ChunkSize,g.Seed,g.SampleScale,g.Octaves,g.Persistence,g.Lacunarity,
                g.WorldScale,g.MeshHeightMultiplier,g.Water.WaterLevel,g.MountainHorizontalScale,
                treeSettings.seedOffset,snapshot,g.Erosion);
        var counts = new List<(ChunkCoord chunk,int forest,int meadow)>();
        int Forest(TreeInstanceData[] trees) => trees.Count(tree => !TreeSpeciesCatalog.IsGrassland(tree.variant));
        try
        {
            treeSettings.distributionProfile = baseline;
            var options = TreeGenerationSnapshot.Create(treeSettings,g.WorldScale);
            // A fixed seed-4 window surrounds the forest interior used in the pilot's matched capture.
            for (int z=13;z<=21;z++)
            for (int x=-41;x<=-33;x++)
            {
                var chunk = new ChunkCoord(x,z);
                var trees = Sample(chunk,options);
                int forest = Forest(trees);
                counts.Add((chunk,forest,trees.Length-forest));
            }
            var interiors = counts.Where(c => c.forest>0).OrderByDescending(c => c.forest)
                .Take(Mathf.Max(1,counts.Count(c => c.forest>0)/3)).Select(c => c.chunk).ToHashSet();
            int beforeDense = counts.Where(c => interiors.Contains(c.chunk)).Sum(c => c.forest);
            if (beforeDense==0) throw new InvalidOperationException("No forest interiors in the seed-4 comparison window.");
            treeSettings.distributionProfile = denser;
            float bestError = float.MaxValue;
            float bestIntensity = 0;
            float bestSpacing = 1f;
            foreach(float spacing in new[]{1f,.93f,.86f,.80f})
            foreach(float intensity in new[]{104f,116f,128f,142f})
            {
                denser.forestStand = intensity;
                denser.forestCoreSpacing = spacing;
                options = TreeGenerationSnapshot.Create(treeSettings,g.WorldScale);
                int count = interiors.Sum(chunk => Forest(Sample(chunk,options)));
                float ratio = (float)count/beforeDense;
                float error = Mathf.Abs(ratio - 1.25f);
                Debug.Log($"PILOT STANDS trial intensity={intensity}, core spacing={spacing}: interior accepted {beforeDense} -> {count} ({ratio:P1})");
                if (error>bestError || (error==bestError && intensity>=bestIntensity)) continue;
                bestError=error; bestIntensity=intensity; bestSpacing=spacing;
            }
            denser.forestStand = bestIntensity;
            denser.forestCoreSpacing = bestSpacing;
            options = TreeGenerationSnapshot.Create(treeSettings,g.WorldScale);
            var output = new List<string>{"chunkX,chunkZ,denseInterior,forestBefore,forestAfter,meadowBefore,meadowAfter"};
            int afterDense = 0;
            foreach (var row in counts)
            {
                var trees = Sample(row.chunk,options);
                int forest = Forest(trees);
                bool interior = interiors.Contains(row.chunk);
                if (interior) afterDense += forest;
                output.Add($"{row.chunk.x},{row.chunk.z},{(interior ? 1 : 0)},{row.forest},{forest},{row.meadow},{trees.Length-forest}");
            }
            Directory.CreateDirectory(".utmp/environment-passes");
            File.WriteAllLines(".utmp/environment-passes/stand-counts.csv",output);
            Debug.Log($"PILOT STANDS accepted interiors: {beforeDense} -> {afterDense}; intensity {bestIntensity}, core spacing {bestSpacing}");
            if (afterDense < beforeDense*1.18f || afterDense > beforeDense*1.35f)
                throw new InvalidOperationException("Dense-stand calibration is outside the pilot's acceptance range.");
        }
        finally { treeSettings.distributionProfile = reference; }
        denser = WoodlandFloorAuthoring.Save(denser, "Assets/ScriptableObjects/ForestVarietyDistributionDense.asset");
        var serialized = new SerializedObject(world);
        serialized.FindProperty("treeSettings.distributionProfile").objectReferenceValue = denser;
        serialized.FindProperty("treeSettings.distantTreeThinningStartChunks").floatValue = 6.5f;
        serialized.FindProperty("treeSettings.distantTreeDensity").floatValue = .14f;
        serialized.FindProperty("treeSettings.distantTreeEdgeProtection").floatValue = .83f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the denser pilot stands.");
    }
}
