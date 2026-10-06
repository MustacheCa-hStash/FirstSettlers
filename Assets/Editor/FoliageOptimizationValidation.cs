using System;
using UnityEditor;
using UnityEngine;

public static class FoliageOptimizationValidation
{
    [MenuItem("Tools/Terrain/Validate Foliage Optimizations (synthetic)")]
    public static void Run()
    {
        ResidentGrassCullingValidation.Run();
        FoliagePublicationValidation.Run();
        WaterReflectionVisibilityValidation.Run();
        GrassStreamingValidation.Run();
        GroundFoliageStreamingValidation.Run();
        Debug.Log("FOLIAGE OPTIMIZATIONS PASS");
    }
    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
}
