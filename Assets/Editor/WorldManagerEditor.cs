using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WorldManager))]
public class WorldManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        var erosion = serializedObject.FindProperty("erosion");
        float wave = erosion.FindPropertyRelative("wavelength").floatValue;
        var stretch = erosion.FindPropertyRelative("stretch").vector2Value;
        wave *= Mathf.Min(stretch.x, stretch.y);
        float minimum = erosion.FindPropertyRelative("minimumWavelength").floatValue;
        int octaves = erosion.FindPropertyRelative("octaves").intValue;
        float lacunarity = erosion.FindPropertyRelative("lacunarity").floatValue;
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
        EditorGUILayout.HelpBox("Erosion affects the entire heightfield. Settings apply on world creation. During Play mode, regenerate to apply a coherent snapshot to every terrain and placement request. Wavelength/spacing are terrain XZ units; multiply by World Scale for scene units.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
            if (GUILayout.Button("Regenerate Terrain")) ((WorldManager)target).RegenerateTerrain();
    }
}
