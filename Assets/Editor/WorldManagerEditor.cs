using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WorldManager)), CanEditMultipleObjects]
public class WorldManagerEditor : Editor
{
    // New fields fall through to the inspector instead of silently disappearing.
    internal static readonly string[][] Sections =
    {
        new[] { "World and Scene References", "worldSeed", "chunkSize", "viewer", "viewerCamera", "chunkParent" },
        new[] { "Terrain Shape and Climate", "sampleScale", "mountainWidth", "worldScale", "meshHeightMultiplier", "erosion", "octaves", "persistence", "lacunarity" },
        new[] { "Terrain Rendering and Lighting", "terrainMaterial", "mountainSnowBlendGamma", "terrainReceiveShadows", "terrainHorizonShadows" },
        new[] { "Terrain Streaming", "viewDistance", "colliderDistance", "enableFarTerrain", "farTerrainStartRing", "farTerrainMacroTileSize", "farTerrainHeightGridResolution", "farTerrainControlMapResolution", "farTerrainSkirtDepth" },
        new[] { "Grass and Forest Ground Cover", "grassSettings", "leafClusterSettings", "fernSettings" },
        new[] { "Trees, Bushes and Rocks", "treeSettings" },
        new[] { "Flowers and Shore Plants", "flowerSettings", "cloverSettings", "dandelionSettings", "lilyPadSettings", "cattailSettings" },
        new[] { "Ambient Life", "butterflySettings", "beeSettings" },
        new[] { "Water", "waterMaterial", "globalWaterY", "waterReflectionResolution", "waterReflectionUpdatesPerSecond", "waterReflectionMovingUpdatesPerSecond", "waterReflectionDistance" },
        new[] { "Terrain Streaming Budgets", "maxActiveTerrainDataJobs", "maxActiveFarTerrainJobs", "maxActiveMeshJobs", "maxActiveColliderJobs", "maxTerrainDataResultsAppliedPerFrame", "maxFarTerrainResultsAppliedPerFrame", "maxLODMeshResultsAppliedPerFrame", "maxColliderResultsAppliedPerFrame", "urgentVisibleChunkRingRadius", "maxVisibleChunkContentUpdatesPerFrame", "maxRenderVisibilityChecksPerFrame", "foliageFrustumPaddingChunks", "visibleChunkContentBudgetMsPerFrame", "maxFarTerrainTileContentUpdatesPerFrame", "farTerrainTileContentBudgetMsPerFrame", "completedRequestApplyBudgetMsPerFrame", "terrainDataApplyBudgetMsPerFrame", "farTerrainApplyBudgetMsPerFrame", "lodMeshApplyBudgetMsPerFrame", "colliderApplyBudgetMsPerFrame" },
        new[] { "Generation Profiling", "logTerrainGenerationProfile", "terrainGenerationProfileLogInterval", "resetTerrainGenerationProfileAfterLog" }
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        EditorGUILayout.HelpBox("World creation and cached placement settings apply on regeneration. During Play mode, use Regenerate Terrain after editing. Greyed controls are inactive under the selected options; hover for the reason.", MessageType.Info);
        var drawn = new HashSet<string> { "m_Script" };
        foreach (var section in Sections)
        {
            string key = "FirstSettlers.WorldManager." + section[0];
            bool open = EditorGUILayout.Foldout(SessionState.GetBool(key, section[0] != "Terrain Streaming Budgets" && section[0] != "Generation Profiling"), section[0], true, EditorStyles.foldoutHeader);
            SessionState.SetBool(key, open);
            for (int i = 1; i < section.Length; i++) drawn.Add(section[i]);
            if (!open) continue;
            EditorGUI.indentLevel++;
            for (int i = 1; i < section.Length; i++)
            {
                var property = serializedObject.FindProperty(section[i]);
                if (property != null) DrawProperty(property);
            }
            if (section[0] == "Terrain Shape and Climate") DrawErosionSummary();
            EditorGUI.indentLevel--;
        }
        var iterator = serializedObject.GetIterator();
        bool enter = true;
        while (iterator.NextVisible(enter))
        {
            enter = false;
            if (!drawn.Contains(iterator.propertyPath)) DrawProperty(iterator.Copy());
        }
        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.Space();
        bool canRegenerate = Application.isPlaying;
        foreach (UnityEngine.Object o in targets) canRegenerate &= ((WorldManager)o).isActiveAndEnabled;
        using (new EditorGUI.DisabledScope(!canRegenerate))
            if (GUILayout.Button("Regenerate Terrain"))
                foreach (UnityEngine.Object o in targets) ((WorldManager)o).RegenerateTerrain();
    }

    private void DrawProperty(SerializedProperty property)
    {
        string reason = WorldManagerInspectorRules.InactiveReason(serializedObject, property.propertyPath);
        GUIContent label = WorldManagerInspectorRules.Label(property);
        if (reason != null) label.tooltip += "\nInactive: " + reason;
        using (new EditorGUI.DisabledScope(reason != null))
        {
            if (property.propertyPath == "octaves")
            {
                // Storage includes two octaves subtracted by ClimateGenerator.
                // Show actual sampled count without changing saved values on draw.
                Rect rect = EditorGUILayout.GetControlRect();
                EditorGUI.BeginProperty(rect, label, property);
                bool mixed = EditorGUI.showMixedValue;
                EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                int count = EditorGUI.IntField(rect, label, ClimateGenerator.GetClimateOctaveCount(property.intValue));
                if (EditorGUI.EndChangeCheck()) property.intValue = Math.Max(1, Math.Min(int.MaxValue - 2, count)) + 2;
                EditorGUI.showMixedValue = mixed;
                EditorGUI.EndProperty();
            }
            else if (property.propertyType == SerializedPropertyType.Generic && !property.isArray)
            {
                EditorGUILayout.PropertyField(property, label, false);
                if (!property.isExpanded) return;
                EditorGUI.indentLevel++;
                var child = property.Copy();
                var end = property.GetEndProperty();
                if (child.NextVisible(true) && !SerializedProperty.EqualContents(child, end))
                    do { DrawProperty(child.Copy()); }
                    while (child.NextVisible(false) && !SerializedProperty.EqualContents(child, end));
                EditorGUI.indentLevel--;
            }
            else EditorGUILayout.PropertyField(property, label, true);
        }
    }

    private void DrawErosionSummary()
    {
        var erosion = serializedObject.FindProperty("erosion");
        float wave = erosion.FindPropertyRelative("wavelength").floatValue;
        var stretch = erosion.FindPropertyRelative("stretch").vector2Value;
        wave *= Mathf.Min(stretch.x, stretch.y);
        float minimum = erosion.FindPropertyRelative("minimumWavelength").floatValue;
        int octaves = erosion.FindPropertyRelative("octaves").intValue;
        float lacunarity = Mathf.Max(1.2f, erosion.FindPropertyRelative("lacunarity").floatValue);
        int count = 0; float finest = wave;
        for (int i = 0; i < octaves && wave >= minimum; i++)
        { count++; finest = wave; wave /= lacunarity; }
        if (erosion.FindPropertyRelative("enabled").boolValue)
        {
            EditorGUILayout.LabelField("Active erosion octaves", count.ToString());
            if (count > 0)
                EditorGUILayout.LabelField("Finest nominal wavelength", finest.ToString("F1") + " terrain units");
            if (count == 0)
                EditorGUILayout.HelpBox("Minimum Wavelength excludes every octave. Reduce it or increase Wavelength / Stretch to produce erosion.", MessageType.Warning);
            else if (finest < erosion.FindPropertyRelative("maxMeshSpacing").intValue * 4f)
                EditorGUILayout.HelpBox("The finest gullies have fewer than four samples at the coarsest permitted mesh spacing. Reduce Max Mesh Spacing or increase Minimum Wavelength to improve silhouettes.", MessageType.Info);
        }
    }
}
