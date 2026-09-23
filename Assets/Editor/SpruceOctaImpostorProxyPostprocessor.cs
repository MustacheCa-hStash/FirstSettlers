using UnityEditor;
using UnityEngine;

public sealed class SpruceOctaImpostorProxyPostprocessor : AssetPostprocessor
{
    private const string ProxyPath = "Assets/Models/Trees/Spruce_OctaImpostor_Proxy.fbx";
    private static readonly Vector3 CaptureCenter = new Vector3(0.0910787f, 3.0420964f, -0.07994366f);
    private const float CaptureRadius = 4.2999973f;

    private void OnPostprocessModel(GameObject root)
    {
        if (assetPath != ProxyPath) return;
        foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (meshFilter.sharedMesh == null) continue;
            meshFilter.sharedMesh.bounds = new Bounds(CaptureCenter, Vector3.one * (CaptureRadius * 2f));
        }
    }
}
