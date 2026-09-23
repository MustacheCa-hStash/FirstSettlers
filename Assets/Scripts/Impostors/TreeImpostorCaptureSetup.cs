using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class TreeImpostorCaptureSetup : MonoBehaviour
{
    [Header("Required references")]
    [SerializeField] private Transform treeRoot;
    [SerializeField] private Camera captureCamera;
    [SerializeField] private Material leafMaterial;
    [SerializeField] private Material barkMaterial;

    [Header("Capture bounds")]
    [Tooltip("Extra radius beyond the combined renderer bounds, expressed as a fraction of the measured radius.")]
    [SerializeField, Range(0f, 0.1f)] private float radiusPaddingFraction = 0.02f;
    [SerializeField] private Vector3 localBoundsMin;
    [SerializeField] private Vector3 localBoundsMax;
    [SerializeField] private Vector3 captureCenterLocal;
    [SerializeField, Min(0f)] private float captureRadius;

    public Transform TreeRoot => treeRoot;
    public Camera CaptureCamera => captureCamera;
    public Material LeafMaterial => leafMaterial;
    public Material BarkMaterial => barkMaterial;
    public Vector3 LocalBoundsMin => localBoundsMin;
    public Vector3 LocalBoundsMax => localBoundsMax;
    public Vector3 CaptureCenterLocal => captureCenterLocal;
    public float CaptureRadius => captureRadius;

    public bool RecalculateBounds(out string message)
    {
        if (treeRoot == null)
        {
            message = "Assign Tree Root before calculating capture bounds.";
            return false;
        }

        Renderer[] renderers = treeRoot.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Vector3 minimum = default;
        Vector3 maximum = default;

        foreach (Renderer renderer in renderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            Bounds bounds = renderer.bounds;
            foreach (Vector3 corner in GetCorners(bounds))
            {
                Vector3 localCorner = treeRoot.InverseTransformPoint(corner);
                if (!found)
                {
                    minimum = localCorner;
                    maximum = localCorner;
                    found = true;
                }
                else
                {
                    minimum = Vector3.Min(minimum, localCorner);
                    maximum = Vector3.Max(maximum, localCorner);
                }
            }
        }

        if (!found)
        {
            message = "No enabled renderers were found below Tree Root.";
            return false;
        }

        Vector3 center = (minimum + maximum) * 0.5f;
        float radius = 0f;
        Bounds localBounds = new Bounds(center, maximum - minimum);
        foreach (Vector3 corner in GetCorners(localBounds))
            radius = Mathf.Max(radius, Vector3.Distance(center, corner));

        localBoundsMin = minimum;
        localBoundsMax = maximum;
        captureCenterLocal = center;
        captureRadius = radius * (1f + radiusPaddingFraction);
        message = $"Measured {renderers.Length} renderer(s). Center: {captureCenterLocal:F4}; radius with padding: {captureRadius:F4}.";
        return true;
    }

    public bool ConfigureCameraFromCurrentDirection(out string message)
    {
        if (captureCamera == null)
        {
            message = "Assign Capture Camera before framing it.";
            return false;
        }

        if (captureRadius <= 0f && !RecalculateBounds(out message))
            return false;

        Vector3 centerWorld = treeRoot.TransformPoint(captureCenterLocal);
        Vector3 forward = captureCamera.transform.forward.sqrMagnitude > 0.0001f
            ? captureCamera.transform.forward.normalized
            : Vector3.forward;
        float cameraDistance = captureRadius * 2.5f;

        captureCamera.orthographic = true;
        captureCamera.orthographicSize = captureRadius;
        captureCamera.nearClipPlane = Mathf.Max(0.01f, cameraDistance - captureRadius * 1.25f);
        captureCamera.farClipPlane = cameraDistance + captureRadius * 1.25f;
        captureCamera.transform.position = centerWorld - forward * cameraDistance;
        captureCamera.transform.rotation = Quaternion.LookRotation(centerWorld - captureCamera.transform.position, Vector3.up);
        message = $"Framed {captureCamera.name} for its current viewing direction. Orthographic size: {captureCamera.orthographicSize:F4}.";
        return true;
    }

    public List<string> ValidateSetup()
    {
        var messages = new List<string>();

        if (treeRoot == null)
        {
            messages.Add("ERROR: Tree Root is not assigned.");
            return messages;
        }

        if (treeRoot.position != Vector3.zero || treeRoot.rotation != Quaternion.identity || treeRoot.lossyScale != Vector3.one)
            messages.Add("ERROR: Tree Root must have world position (0,0,0), rotation (0,0,0), and scale (1,1,1) during capture.");

        if (leafMaterial == null || barkMaterial == null)
            messages.Add("ERROR: Assign both Leaf Material and Bark Material.");

        foreach (Renderer renderer in treeRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            foreach (Material material in renderer.sharedMaterials)
            {
                if (material != null && material != leafMaterial && material != barkMaterial)
                    messages.Add($"WARNING: {renderer.name} uses unexpected material {material.name}.");
            }
        }

        if (captureCamera == null)
        {
            messages.Add("ERROR: Capture Camera is not assigned.");
        }
        else
        {
            if (!captureCamera.orthographic)
                messages.Add("ERROR: Capture Camera must be orthographic.");
            if (captureCamera.clearFlags != CameraClearFlags.SolidColor)
                messages.Add("ERROR: Capture Camera Clear Flags must be Solid Color.");
            if (captureCamera.backgroundColor.a != 0f)
                messages.Add("WARNING: Set Capture Camera background alpha to 0 for color/coverage capture.");
            if ((captureCamera.cullingMask & (1 << treeRoot.gameObject.layer)) == 0)
                messages.Add("ERROR: Capture Camera culling mask excludes the tree root's layer.");
        }

        if (RenderSettings.fog)
            messages.Add("ERROR: Disable scene fog for semantic capture.");
        if (RenderSettings.skybox != null)
            messages.Add("ERROR: Remove the scene skybox for semantic capture.");
        if (Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length > 0)
            messages.Add("ERROR: Disable all scene lights for semantic capture. Later capture passes write material data, not beauty lighting.");

        foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!renderer.transform.IsChildOf(treeRoot))
                messages.Add($"ERROR: Non-tree renderer is active in this scene: {renderer.name}.");
        }

        if (captureRadius <= 0f)
            messages.Add("WARNING: Capture bounds have not yet been calculated.");
        if (messages.Count == 0)
            messages.Add("PASS: The isolated capture-scene requirements are met.");
        return messages;
    }

    private static IEnumerable<Vector3> GetCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        yield return new Vector3(min.x, min.y, min.z);
        yield return new Vector3(min.x, min.y, max.z);
        yield return new Vector3(min.x, max.y, min.z);
        yield return new Vector3(min.x, max.y, max.z);
        yield return new Vector3(max.x, min.y, min.z);
        yield return new Vector3(max.x, min.y, max.z);
        yield return new Vector3(max.x, max.y, min.z);
        yield return new Vector3(max.x, max.y, max.z);
    }

    private void OnDrawGizmosSelected()
    {
        if (treeRoot == null || captureRadius <= 0f)
            return;

        Gizmos.matrix = treeRoot.localToWorldMatrix;
        Gizmos.color = new Color(0.15f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireCube((localBoundsMin + localBoundsMax) * 0.5f, localBoundsMax - localBoundsMin);
        Gizmos.color = new Color(1f, 0.75f, 0.15f, 0.9f);
        Gizmos.DrawWireSphere(captureCenterLocal, captureRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(Vector3.zero, Vector3.right);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(Vector3.zero, Vector3.up);
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(Vector3.zero, Vector3.forward);
    }
}
