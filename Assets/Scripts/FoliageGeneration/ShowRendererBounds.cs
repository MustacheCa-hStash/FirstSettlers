using UnityEngine;

// Diagnostic only: does not alter renderer bounds or visibility.
public sealed class ShowRendererBounds : MonoBehaviour
{
    [Tooltip("Optional: drag one tree here to isolate it. Empty draws all child MeshRenderers.")]
    public Transform target;
    public bool showBoxes = true;
    [Tooltip("Illustrative sphere enclosing Renderer.bounds, not Unity's exact internal GPU culling bound.")]
    public bool showApproximateSpheres = true;

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = Matrix4x4.identity;
        foreach (var renderer in (target != null ? target : transform).GetComponentsInChildren<MeshRenderer>())
        {
            Bounds bounds = renderer.bounds;
            if (showBoxes)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
            if (showApproximateSpheres)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(bounds.center, bounds.extents.magnitude);
            }
        }
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
