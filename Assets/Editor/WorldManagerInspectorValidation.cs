using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WorldManagerInspectorValidation
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    [MenuItem("Tools/Terrain/Validate World Manager Inspector")]
    public static void Run()
    {
        var root = new GameObject("World Manager inspector validation");
        root.SetActive(false);
        var world = root.AddComponent<WorldManager>();
        EditorJsonUtility.FromJsonOverwrite("{\"grassSettings\":{},\"treeSettings\":{}}", world);
        try
        {
            using var so = new SerializedObject(world);
            var sectionPaths = new HashSet<string>();
            foreach (var section in WorldManagerEditor.Sections)
                for (int i = 1; i < section.Length; i++)
                {
                    Check(so.FindProperty(section[i]) != null, "Missing grouped property: " + section[i]);
                    Check(sectionPaths.Add(section[i]), "Duplicate grouped property: " + section[i]);
                }
            var iterator = so.GetIterator();
            bool enter = true;
            while (iterator.NextVisible(enter))
            {
                enter = false;
                Check(iterator.propertyPath == "m_Script" || sectionPaths.Contains(iterator.propertyPath),
                    "Ungrouped world property: " + iterator.propertyPath);
            }
            foreach (string path in new[] { "farTerrainRefinement", "farTerrainComparison", "foliageParent", "treeSettings.treeCellSize", "treeSettings.treeSpawnChance",
                "treeSettings.treeMinDistance", "grassSettings.grassInstanceDataPropertyName",
                "grassSettings.enableBillboardRenderFade", "grassSettings.billboardRenderFadeDuration",
                "grassSettings.billboardFadeDitherPixelSize", "grassSettings.billboardUniformScaleRange",
                "grassSettings.randomizeBillboardYaw", "grassSettings.billboardSeedOffset" })
                Check(so.FindProperty(path) == null, "Obsolete field remains serialized: " + path);

            void Set(string path, bool value)
            {
                so.FindProperty(path).boolValue = value;
                so.ApplyModifiedPropertiesWithoutUndo(); so.Update();
            }
            void Active(string path, bool expected)
            {
                Check(so.FindProperty(path) != null, "Dependency references missing property: " + path);
                Check((WorldManagerInspectorRules.InactiveReason(so, path) == null) == expected,
                    "Unexpected active state: " + path);
            }
            Set("enableFarTerrain", false); Active("farTerrainHeightGridResolution", false);
            Active("farTerrainApplyBudgetMsPerFrame", false); Active("viewDistance", true);
            Set("enableFarTerrain", true); Active("farTerrainHeightGridResolution", true);
            Set("treeSettings.enableDistantTrees", true);
            Active("treeSettings.billboardTreeChunkRingRadius", false); Active("treeSettings.distantTreeDistanceChunks", true);
            Set("treeSettings.enableDistantTrees", false);
            Active("treeSettings.billboardTreeChunkRingRadius", true); Active("treeSettings.distantTreeDistanceChunks", false);
            Set("treeSettings.enableDistantTrees", true); Set("treeSettings.distantTreeDensityAware", false);
            Active("treeSettings.distantTreeCrowdingThreshold", false); Active("treeSettings.distantTreeProtectedCount", true);
            Set("fernSettings.matchGrassRenderDistance", true);
            Active("fernSettings.renderDistance", false); Active("fernSettings.fadeWidth", false);
            Active("fernSettings.grassRenderDistanceMultiplier", true);
            Set("fernSettings.matchGrassRenderDistance", false);
            Active("fernSettings.renderDistance", true); Active("fernSettings.grassRenderDistanceMultiplier", false);
            Set("fernSettings.matchGrassLodDistances", true); Active("fernSettings.lodStart", false);
            Set("fernSettings.matchGrassLodDistances", false); Active("fernSettings.lodStart", true);
            Set("fernSettings.useDistanceDensity", false); Active("fernSettings.densityRadius3", false);
            Active("fernSettings.density", true);
            Set("fernSettings.enabled", false); Active("fernSettings.enabled", true); Active("fernSettings.cellSize", false);
            Set("fernSettings.enabled", true); Active("fernSettings.cellSize", true);
            Set("grassSettings.gpuIndirectRendering", false); Active("grassSettings.grassCompactShader", false);
            Set("erosion.enabled", false); Active("erosion.amplitude", false);
            Active("erosion.baseElevation", true); Active("erosion.maxMeshSpacing", true);
            Set("erosion.enabled", true); Active("erosion.amplitude", true);
            Set("terrainReceiveShadows", false); Active("terrainHorizonShadows.enabled", false);
            Set("terrainReceiveShadows", true); Active("terrainHorizonShadows.enabled", true);
            so.FindProperty("octaves").intValue = 3; so.ApplyModifiedPropertiesWithoutUndo(); so.Update();
            Check(ClimateGenerator.GetClimateOctaveCount(3) == 1, "Climate count conversion changed.");
            Active("persistence", false); Active("lacunarity", false);
            so.FindProperty("octaves").intValue = 4; so.ApplyModifiedPropertiesWithoutUndo(); so.Update();
            Active("persistence", true); Active("lacunarity", true);

            var otherRoot = new GameObject("Mixed world selection validation");
            otherRoot.SetActive(false);
            try
            {
                var other = otherRoot.AddComponent<WorldManager>();
                using var otherSo = new SerializedObject(other);
                otherSo.FindProperty("enableFarTerrain").boolValue = false;
                otherSo.ApplyModifiedPropertiesWithoutUndo();
                using var mixed = new SerializedObject(new UnityEngine.Object[] { world, other });
                Check(mixed.FindProperty("enableFarTerrain").hasMultipleDifferentValues, "Mixed selection not established.");
                Check(WorldManagerInspectorRules.InactiveReason(mixed, "farTerrainSkirtDepth") == null,
                    "Mixed selections incorrectly lock working settings.");
            }
            finally { UnityEngine.Object.DestroyImmediate(otherRoot); }
            Debug.Log($"WORLD MANAGER INSPECTOR PASS: {sectionPaths.Count} root fields grouped once, 11 obsolete fields removed, dependency toggles and mixed selections checked.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // Scene loading is restricted to the disposable batch-validation project.
    public static void RunBatch()
    {
        try
        {
            Run();
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SmearScene.unity");
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var world in root.GetComponentsInChildren<WorldManager>(true))
                {
                    string before = EditorJsonUtility.ToJson(world);
                    using var so = new SerializedObject(world);
                    var p = so.GetIterator();
                    int visible = 0;
                    while (p.NextVisible(true))
                    {
                        WorldManagerInspectorRules.Label(p);
                        WorldManagerInspectorRules.InactiveReason(so, p.propertyPath);
                        visible++;
                    }
                    Check(before == EditorJsonUtility.ToJson(world), "Inspecting settings altered serialized scene values.");
                    Check(so.FindProperty("terrainMaterial").objectReferenceValue != null, "Scene terrain material reference lost.");
                    Check(so.FindProperty("viewer").objectReferenceValue != null, "Scene viewer reference lost.");
                    Debug.Log($"WORLD MANAGER SCENE PASS: {visible} visible property paths inspected without changing saved values.");
                    count++;
                }
            Check(count > 0, "SmearScene has no World Manager.");
            GrassStreamingValidation.Run();
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
