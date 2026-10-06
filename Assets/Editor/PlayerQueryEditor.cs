using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlayerQuery))]
public sealed class PlayerQueryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var query = (PlayerQuery)target;
        if (query.ViewCamera == null)
            EditorGUILayout.HelpBox("Assign this player's view camera explicitly.", MessageType.Info);
        if (!Application.isPlaying)
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Current Geometry Hit", EditorStyles.boldLabel);
        var result = query.Current;
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.Toggle("Has Hit", result.HasHit);
            EditorGUILayout.ObjectField("Collider", result.Collider, typeof(Collider), true);
            EditorGUILayout.ObjectField("Solid Collider", query.SolidHit.Collider, typeof(Collider), true);
            EditorGUILayout.ObjectField("Query-only Collider", query.QueryOnlyHit.Collider, typeof(Collider), true);
            if (result.HasHit)
            {
                EditorGUILayout.EnumPopup("Role", result.Kind);
                EditorGUILayout.FloatField("Distance", result.Distance);
                EditorGUILayout.Vector3Field("Point", result.Point);
                EditorGUILayout.Vector3Field("Normal", result.Normal);
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resolved Target", EditorStyles.boldLabel);
            EditorGUILayout.Toggle("Has Target", result.HasTarget);
            if (result.HasTarget)
            {
                var info = result.Target;
                EditorGUILayout.ObjectField("Provider", info.Source, typeof(QueryTarget), true);
                EditorGUILayout.TextField("Display Name", info.DisplayName);
                EditorGUILayout.ObjectField("Icon", info.Icon, typeof(Sprite), false);
                EditorGUILayout.ObjectField("Datacard", info.Definition, typeof(WorldObjectDefinition), false);
                EditorGUILayout.TextField("Description", info.Description ?? string.Empty);
                EditorGUILayout.EnumFlagsField("Capabilities", info.Capabilities);
                if (info.TryGetData<TreeRecord>(out var tree))
                {
                    EditorGUILayout.TextField("Tree ID", tree.Id.ToString());
                    EditorGUILayout.EnumPopup("Tree State", tree.State);
                    EditorGUILayout.EnumPopup("Variant", tree.Placement.variant);
                }
                else if (info.TryGetData<RockQueryData>(out var rock))
                {
                    EditorGUILayout.Toggle("Generated Placement", rock.IsGenerated);
                    if (rock.Placement.HasValue)
                    {
                        EditorGUILayout.EnumPopup("Variant", rock.Placement.Value.variant);
                        EditorGUILayout.IntField("Prefab Index", rock.Placement.Value.prefabIndex);
                    }
                }
            }
        }
    }

    public override bool RequiresConstantRepaint() => Application.isPlaying;
}
