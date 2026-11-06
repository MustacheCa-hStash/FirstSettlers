using UnityEditor;
using UnityEngine;

public static class GlacialSurfaceSetup
{
    public const string Root="Assets/Textures/Ground/Glacial/";
    [MenuItem("Tools/Terrain/Install Glacier Ice")]
    public static void Install()
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>(DesertSurfaceSetup.MaterialPath);
        bool firstInstall=material.GetTexture("_IceAlbedo")==null;
        DesertSurfaceSetup.InstallSurface("GlacierIce","Ice",.08f,.12f,Root);
        if(firstInstall){material.SetColor("_IceColor",new Color(.84f,.92f,1f,1f));EditorUtility.SetDirty(material);}
        AssetDatabase.SaveAssets();
    }
}
