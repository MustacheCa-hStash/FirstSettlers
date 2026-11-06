using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TreeImpostorOutlineValidation
{
    public static void RunIntegrated()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        EditorSceneManager.OpenScene("Assets/Scenes/SmearScene.unity");
        var world=UnityEngine.Object.FindAnyObjectByType<WorldManager>();
        var settings=(TreeSettings)typeof(WorldManager).GetField("treeSettings",flags).GetValue(world);
        if(settings.distantTreeOutlineQuality!=TreeImpostorOutlineQuality.Four)throw new Exception("Saved scene does not select the measured four-vertex default");
        int validated=0;
        using(var manager=new DistantTreeManager(settings,1456,128,600,2,.05f,10,.3f,200,.24f,1,WorldFeatureGenerationSettings.Default))
        foreach(var batch in (IEnumerable)typeof(DistantTreeManager).GetField("uniqueBatches",flags).GetValue(manager))
        {
            object Get(string name)=>batch.GetType().GetField(name).GetValue(batch);
            var material=(Material)Get("Material");var source=(Material)Get("SourceMaterial");
            if(material.shader.name!="Custom/TreeHQOctaImpostor"&&material.shader.name!="Custom/SpruceOctaImpostor")continue;
            var mesh=(Mesh)Get("Mesh");var original=(Mesh)Get("SourceMesh");
            if(Get("Outline")==null||!material.IsKeywordEnabled("TREE_IMPOSTOR_OUTLINE")||mesh.vertexCount!=4||mesh.GetIndexCount(0)!=6)
                throw new Exception("World billboard did not install fitted geometry: "+source.name);
            if(source.IsKeywordEnabled("TREE_IMPOSTOR_OUTLINE")||original.vertexCount!=4)throw new Exception("Outline installation altered the authored material/mesh");
            if(mesh.bounds!=DistantTreeGpuBatch.RenderingBounds(original.bounds,material))throw new Exception("Fitted geometry shrank conservative tree culling bounds");
            validated++;
        }
        if(validated!=6)throw new Exception("Expected six distinct fitted tree visuals, got "+validated);
        var library=AssetDatabase.LoadAssetAtPath<TreeImpostorOutlineLibrary>(TreeImpostorOutlineBaker.AssetPath);
        var first=library.entries[0];string path=AssetDatabase.GetAssetPath(first.albedo).Replace("Textures/Trees/Impostors/RedMapleHQ/RedMapleHQ_Octa_AlbedoCoverage.png","Materials/M_Trees/RedMapleHQ/RedMapleHQ_OctaImpostor_M.mat");
        var sourceMaterial=AssetDatabase.LoadAssetAtPath<Material>(path);
        var testMaterial=new Material(sourceMaterial);var testMesh=new Mesh();
        try
        {
            testMaterial.SetFloat("_FramesPerAxis",7);
            if(TreeImpostorOutlineBinding.Apply(testMaterial,testMesh,library,TreeImpostorOutlineQuality.Four)!=null)
                throw new Exception("Mismatched capture layout did not fall back to quad");
        }
        finally{UnityEngine.Object.DestroyImmediate(testMaterial);UnityEngine.Object.DestroyImmediate(testMesh);}
        Debug.Log("TREE_OUTLINE_INTEGRATION_PASS: six resolved visuals, four vertices/two triangles, unchanged source assets and culling bounds, incompatible-layout fallback.");
    }
}
