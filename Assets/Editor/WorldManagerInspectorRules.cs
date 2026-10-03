using System;
using UnityEditor;
using UnityEngine;

// Dependency rules are separate from IMGUI so they can be checked against real
// SerializedObjects, including inherited fields and mixed selections.
internal static class WorldManagerInspectorRules
{
    private static readonly (string parent, string toggle)[] Features = {
        ("flowerSettings", "enableFlowers"), ("cloverSettings", "enableClover"), ("dandelionSettings", "enableDandelions"),
        ("lilyPadSettings", "enableLilyPads"), ("cattailSettings", "enableCattails"),
        ("butterflySettings", "enabled"), ("beeSettings", "enabled"), ("leafClusterSettings", "enabled"), ("fernSettings", "enabled") };
    private static readonly string[] ScatterParents = { "leafClusterSettings", "fernSettings" };
    private static readonly string[] ErosionOnly = { "amplitude", "wavelength", "octaves", "lacunarity", "persistence", "minimumWavelength", "stretch", "rotation", "offset", "seedOffset", "gullyWeight", "branching", "cellSize", "normalization", "ridgeRounding", "valleyRounding", "directionSmoothing", "slopeResponse", "heightScale", "fadeTarget", "reliefRadius", "reliefContrast", "valleyAndPeakHeights", "gentleMountainErosion" };

    private static bool Off(SerializedObject so, string path)
    {
        var p = so.FindProperty(path);
        return p != null && !p.hasMultipleDifferentValues && !p.boolValue;
    }
    private static bool On(SerializedObject so, string path)
    {
        var p = so.FindProperty(path);
        return p != null && !p.hasMultipleDifferentValues && p.boolValue;
    }
    private static bool Within(string path, string parent) => path.StartsWith(parent + ".", StringComparison.Ordinal);

    internal static string InactiveReason(SerializedObject so, string path)
    {
        if ((path.StartsWith("farTerrain", StringComparison.Ordinal) || path == "maxActiveFarTerrainJobs" || path == "maxFarTerrainResultsAppliedPerFrame" || path == "maxFarTerrainTileContentUpdatesPerFrame") && Off(so, "enableFarTerrain"))
            return "Enable Far Terrain is off.";
        if ((path == "terrainGenerationProfileLogInterval" || path == "resetTerrainGenerationProfileAfterLog") && Off(so, "logTerrainGenerationProfile")) return "Generation profiling is off.";
        if (path.StartsWith("waterReflection", StringComparison.Ordinal))
        {
            var p = so.FindProperty("waterMaterial");
            var material = p.objectReferenceValue as Material;
            if (!p.hasMultipleDifferentValues && (material == null || material.shader == null || material.shader.name != "FirstSettlers/Murky Planar Water")) return "Planar reflections require the Murky Planar Water material.";
        }
        if (path == "persistence" || path == "lacunarity")
        {
            var p = so.FindProperty("octaves");
            if (!p.hasMultipleDifferentValues && ClimateGenerator.GetClimateOctaveCount(p.intValue) <= 1) return "Climate persistence and lacunarity need at least two climate octaves.";
        }
        foreach (var feature in Features)
            if (Within(path, feature.parent) && path != feature.parent + "." + feature.toggle && Off(so, feature.parent + "." + feature.toggle)) return "This feature is disabled.";
        if (Within(path, "terrainHorizonShadows"))
        {
            if (Off(so, "terrainReceiveShadows")) return "Terrain Receive Shadows is off.";
            if (path != "terrainHorizonShadows.enabled" && Off(so, "terrainHorizonShadows.enabled")) return "Terrain horizon shadows are disabled.";
        }
        if (Within(path, "treeSettings"))
        {
            string name = path.Substring("treeSettings.".Length);
            if (name.StartsWith("distantTree", StringComparison.Ordinal) && Off(so, "treeSettings.enableDistantTrees")) return "Enable Distant Trees is off.";
            if ((name == "billboardTreeChunkStartRingRadius" || name == "billboardTreeChunkRingRadius") && On(so, "treeSettings.enableDistantTrees")) return "Legacy billboard rings are replaced by Distant Tree Distance Chunks.";
            if ((name == "distantTreeCrowdingThreshold" || name == "distantTreeEdgeProtection") && Off(so, "treeSettings.distantTreeDensityAware")) return "Density-aware thinning is off.";
            if (name == "distantTreeDepthBands" && Off(so, "treeSettings.distantTreeDepthOrdering")) return "Depth ordering is off.";
            if (name == "distantTreeCompactShader" && Off(so, "treeSettings.distantTreeGpuCompaction")) return "GPU tree compaction is off; CPU fallback remains available.";
        }
        foreach (string parent in ScatterParents)
        {
            if (!Within(path, parent)) continue;
            string name = path.Substring(parent.Length + 1);
            if ((name == "renderDistance" || name == "fadeWidth") && On(so, parent + ".matchGrassRenderDistance")) return "Range and fade are derived from grass settings.";
            if (name == "grassRenderDistanceMultiplier" && Off(so, parent + ".matchGrassRenderDistance")) return "Manual render distance is selected.";
            if (name.StartsWith("density", StringComparison.Ordinal) && name != "density" && Off(so, parent + ".useDistanceDensity")) return "Distance-ring density is off.";
            if (name == "scatterCompactShader" && Off(so, parent + ".gpuIndirectRendering")) return "GPU scatter is off; CPU instancing is used.";
            if (parent == "fernSettings" && (name == "lodStart" || name == "lodEnd" || name == "coarseLodStart" || name == "coarseLodEnd") && On(so, "fernSettings.matchGrassLodDistances")) return "Fern LOD distances are derived from grass settings.";
        }
        if (path == "grassSettings.grassCompactShader" && Off(so, "grassSettings.gpuIndirectRendering")) return "GPU grass is off; CPU instancing is used.";
        if (Within(path, "erosion"))
        {
            string name = path.Substring("erosion.".Length);
            if ((name == "riverValleyWidth" || name == "riverValleyFlattening") && Off(so, "erosion.carveRivers")) return "River carving is off.";
            if (Array.IndexOf(ErosionOnly, name) >= 0 && Off(so, "erosion.enabled")) return "World erosion is off; base landforms and mesh fidelity still apply.";
            var mode = so.FindProperty("erosion.fadeTarget");
            if (!mode.hasMultipleDifferentValues)
            {
                bool altitude = mode.enumValueIndex == (int)ErosionFadeTarget.Altitude;
                if (altitude && (name == "reliefRadius" || name == "reliefContrast")) return "Local-relief controls are unused in Altitude mode.";
                if (!altitude && name == "valleyAndPeakHeights") return "Altitude heights are unused in Local Relief mode.";
            }
        }
        return null;
    }

    internal static GUIContent Label(SerializedProperty property)
    {
        string name = property.displayName, tip = property.tooltip;
        switch (property.propertyPath)
        {
            case "octaves": name = "Climate Octaves"; tip = "Actual moisture/temperature noise octave count. Independent of erosion/base-landform octaves."; break;
            case "persistence": name = "Climate Persistence"; break;
            case "lacunarity": name = "Climate Lacunarity"; break;
            case "farTerrainHeightGridResolution": name = "Boundary Chunk Height Grid Resolution"; tip = "Used by single far chunks at the near/far boundary. Quadtree patches use fixed 33/65-point grids; their resolution is not controlled here. Erosion Max Mesh Spacing may raise this minimum."; break;
            case "farTerrainControlMapResolution": name = "Far Control Map Base Resolution"; tip = "Single far chunks use this size. Quadtree map size is (this - 1) * Macro Tile Size + 1, clamped to 2–128."; break;
            case "grassSettings.activeRingRadius": name = "Detailed Grass Distance (Chunks)"; break;
            case "grassSettings.billboardRingRadius": name = "Outer Grass Distance (Chunks)"; break;
            case "grassSettings.maxSubChunkGenerationsPerFrame": name = "Grass Job Starts / Completions Per Frame"; tip = "Separate per-frame caps for starting and collecting grass subchunk jobs; each stage uses this count."; break;
            case "grassSettings.subChunkGenerationBudgetMsPerFrame": name = "Grass Job Scheduling Budget (Ms)"; break;
        }
        return new GUIContent(name, tip);
    }
}
