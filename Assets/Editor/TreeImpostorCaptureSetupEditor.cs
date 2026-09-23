using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TreeImpostorCaptureSetup))]
public sealed class TreeImpostorCaptureSetupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var setup = (TreeImpostorCaptureSetup)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Capture preparation", EditorStyles.boldLabel);

        if (GUILayout.Button("1. Calculate Bounds and Capture Sphere"))
        {
            Undo.RecordObject(setup, "Calculate Tree Impostor Capture Bounds");
            if (setup.RecalculateBounds(out string message))
                EditorUtility.SetDirty(setup);
            Debug.Log(message, setup);
        }

        using (new EditorGUI.DisabledScope(setup.CaptureCamera == null || setup.TreeRoot == null))
        {
            if (GUILayout.Button("2. Frame Camera From Current Direction"))
            {
                Undo.RecordObject(setup.CaptureCamera.transform, "Frame Tree Impostor Capture Camera");
                Undo.RecordObject(setup.CaptureCamera, "Configure Tree Impostor Capture Camera");
                if (setup.ConfigureCameraFromCurrentDirection(out string message))
                    EditorUtility.SetDirty(setup.CaptureCamera);
                Debug.Log(message, setup);
            }
        }

        if (GUILayout.Button("3. Validate Isolated Capture Scene"))
            LogValidation(setup, setup.ValidateSetup());
    }

    private static void LogValidation(TreeImpostorCaptureSetup setup, List<string> messages)
    {
        foreach (string message in messages)
        {
            if (message.StartsWith("ERROR"))
                Debug.LogError(message, setup);
            else if (message.StartsWith("WARNING"))
                Debug.LogWarning(message, setup);
            else
                Debug.Log(message, setup);
        }
    }
}
