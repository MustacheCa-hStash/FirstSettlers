using UnityEngine;

/// <summary>Prefab authoring only; generation snapshots numeric values before grass jobs run.</summary>
[DisallowMultipleComponent]
public sealed class FoliageExclusionZone : MonoBehaviour
{
    public FoliageExclusionProfile profile;

    public static bool TryResolve(GameObject prefab, Vector3 instanceScale, out Vector2 zone)
    {
        zone = default;
        if (prefab == null || !prefab.TryGetComponent<FoliageExclusionZone>(out var authoring) || authoring.profile == null)
            return false;
        // The instanced render paths replace the prefab root transform with the placement TRS.
        zone = authoring.profile.ScaledZone(instanceScale);
        return true;
    }

    public static void SnapshotRockHull(GameObject prefab, ref RockInstanceData instance)
    {
        if (prefab == null || !prefab.TryGetComponent<FoliageExclusionZone>(out var zone) ||
            zone.profile == null || !zone.profile.HasRockHull) return;
        instance.grassExclusionHull = zone.profile.rockHullPlanes;
        instance.grassHullBounds = zone.profile.rockHullBounds;
        instance.grassContactPadding = FoliageExclusionProfile.NonNegative(zone.profile.rockContactPadding);
        instance.grassContactHeight = FoliageExclusionProfile.NonNegative(zone.profile.rockContactHeight);
        instance.groundEmbed = prefab.GetComponent<RockPreset>()?.groundEmbed ?? 0f;
    }

    private void OnDrawGizmosSelected()
    {
        if (profile == null) return;
        var preset = GetComponent<RockPreset>();
        if (profile.HasRockHull && preset != null && preset.collisionHull != null)
        {
            DrawHullContact(preset);
            return;
        }
        Vector2 zone = profile.ScaledZone(transform.lossyScale);
        DrawCircle(zone.x, new Color(1f, .55f, .1f));
        if (zone.y > 0f) DrawCircle(zone.x + zone.y, new Color(1f, .85f, .2f));
    }

    private void DrawHullContact(RockPreset preset)
    {
        var vertices = preset.collisionHull.vertices;
        var triangles = preset.collisionHull.triangles;
        var matrix = transform.localToWorldMatrix;
        Vector3 embed = Vector3.up * (preset.groundEmbed * Mathf.Abs(transform.lossyScale.y));
        Gizmos.color = new Color(1f, .55f, .1f);
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 first = default;
            int count = 0;
            for (int edge = 0; edge < 3; edge++)
            {
                Vector3 a = matrix.MultiplyPoint3x4(vertices[triangles[i+edge]]) - embed;
                Vector3 b = matrix.MultiplyPoint3x4(vertices[triangles[i+(edge+1)%3]]) - embed;
                float da = a.y - transform.position.y, db = b.y - transform.position.y;
                if ((da < 0f) == (db < 0f) || Mathf.Abs(da-db) < .000001f) continue;
                Vector3 point = Vector3.LerpUnclamped(a, b, da/(da-db));
                if (count++ == 0) first = point;
                else Gizmos.DrawLine(first, point);
            }
        }
    }

    private void DrawCircle(float radius, Color color)
    {
        Gizmos.color = color;
        Vector3 previous = transform.position + Vector3.right * radius;
        for (int i = 1; i <= 64; i++)
        {
            float angle = i * (2f * Mathf.PI / 64f);
            Vector3 next = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
