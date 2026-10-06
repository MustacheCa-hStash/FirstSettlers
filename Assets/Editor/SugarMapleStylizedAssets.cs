using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Non-destructive setup and GPU checks for the new maple materials.</summary>
public static class SugarMapleStylizedAssets
{
    public const string LeafTexture = "Assets/Textures/Trees/SugarMaple/T_SugarMapleLeafClump_Stylized.png";
    public const string BarkTexture = "Assets/Textures/Trees/Bark/T_SugarMapleBark_Stylized.png";
    public const string LeafMaterial = "Assets/Materials/M_Trees/SugarMaple/M_SugarMapleLeaf_Stylized.mat";
    public const string BarkMaterial = "Assets/Materials/M_Trees/SugarMaple/M_SugarMapleBark_Stylized.mat";
    public const string RedLeafTexture = "Assets/Textures/Trees/RedMaple/T_RedMapleLeafClump_Stylized.png";
    public const string RedLeafMaterial = "Assets/Materials/M_Trees/RedMaple/M_RedMapleLeaf_Stylized.mat";
    public const string RedBarkMaterial = "Assets/Materials/M_Trees/RedMaple/M_RedMapleBark_Stylized.mat";
    private const string Output = "Logs/SugarMapleStylized";

    [MenuItem("Tools/Art/Sugar Maple/Create Stylized Materials")]
    public static void CreateAssets()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureTexture(LeafTexture, true);
        ConfigureTexture(BarkTexture, false);
        ConfigureTexture(RedLeafTexture, true);
        CreateMaterial(LeafMaterial, "Custom/SugarMapleStylizedLeaf", LeafTexture, true);
        CreateMaterial(BarkMaterial, "Custom/SugarMapleStylizedBark", BarkTexture, false);
        CreateMaterial(RedLeafMaterial, "Custom/RedMapleStylizedLeaf", RedLeafTexture, true);
        CreateMaterial(RedBarkMaterial, "Custom/RedMapleStylizedBark", BarkTexture, false);
        AssetDatabase.SaveAssets();
        Debug.Log("MAPLE MATERIALS: sugar/red maple leaf and bark materials are configured; both bark materials share one texture.");
    }

    private static void ConfigureTexture(string path, bool leaf)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        Check(importer != null, "Missing texture: " + path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = leaf ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        importer.alphaIsTransparency = leaf;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = leaf;
        importer.alphaTestReferenceValue = .5f;
        importer.wrapMode = leaf ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.npotScale = leaf ? TextureImporterNPOTScale.None : TextureImporterNPOTScale.ToNearest;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.compressionQuality = 100;
        importer.isReadable = false;
        importer.streamingMipmaps = false;
        var desktop = importer.GetPlatformTextureSettings("Standalone");
        desktop.overridden = true;
        desktop.maxTextureSize = 1024;
        desktop.format = TextureImporterFormat.BC7;
        desktop.textureCompression = TextureImporterCompression.CompressedHQ;
        desktop.compressionQuality = 100;
        importer.SetPlatformTextureSettings(desktop);
        importer.SaveAndReimport();
    }

    private static void CreateMaterial(string path, string shaderName, string texturePath, bool leaf)
    {
        // Re-running setup imports textures without resetting any artist-edited colors.
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        var shader = Shader.Find(shaderName);
        Check(shader != null, "Missing shader: " + shaderName);
        var material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path),
            enableInstancing = true, doubleSidedGI = leaf };
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        if (leaf)
        {
            material.SetFloat("_SeasonAutumnAmount", 0f);
            material.SetFloat("_Cutoff", .5f);
        }
        AssetDatabase.CreateAsset(material, path);
    }

    [MenuItem("Tools/Art/Sugar Maple/Validate Stylized Materials")]
    public static void Validate()
    {
        ShaderUtil.allowAsyncCompilation = false;
        var sourceLeaf = AssetDatabase.LoadAssetAtPath<Material>(LeafMaterial);
        var sourceBark = AssetDatabase.LoadAssetAtPath<Material>(BarkMaterial);
        var sourceRed = AssetDatabase.LoadAssetAtPath<Material>(RedLeafMaterial);
        var sourceRedBark = AssetDatabase.LoadAssetAtPath<Material>(RedBarkMaterial);
        Check(sourceLeaf != null && sourceBark != null && sourceRed != null && sourceRedBark != null, "Create the stylized materials first.");
        Check(sourceLeaf.GetTexture("_BaseMap") == AssetDatabase.LoadAssetAtPath<Texture2D>(LeafTexture), "Leaf texture assignment failed.");
        Check(sourceBark.GetTexture("_BaseMap") == AssetDatabase.LoadAssetAtPath<Texture2D>(BarkTexture), "Bark texture assignment failed.");
        Check(sourceRed.shader.name == "Custom/RedMapleStylizedLeaf" &&
            sourceRed.GetTexture("_BaseMap") == AssetDatabase.LoadAssetAtPath<Texture2D>(RedLeafTexture), "Red maple reclassification reference failed.");
        Check(sourceLeaf.shader.name == "Custom/SugarMapleStylizedLeaf", "Sugar maple shader reference failed.");
        Check(sourceRedBark.shader.name == "Custom/RedMapleStylizedBark" &&
            sourceRedBark.GetTexture("_BaseMap") == sourceBark.GetTexture("_BaseMap"), "Red maple bark does not reuse the existing bark texture.");
        Check(sourceLeaf.GetFloat("_SeasonAutumnAmount") == 0f, "Initial season is not summer.");
        Check(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/SugarMapleLeafTintCutout.shader") != null &&
            AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/SugarMapleBarkUnlit.shader") != null, "Legacy shaders disappeared.");
        var leafImporter = (TextureImporter)AssetImporter.GetAtPath(LeafTexture);
        var barkImporter = (TextureImporter)AssetImporter.GetAtPath(BarkTexture);
        Check(leafImporter.mipMapsPreserveCoverage && leafImporter.alphaIsTransparency &&
            leafImporter.alphaTestReferenceValue == sourceLeaf.GetFloat("_Cutoff"), "Leaf coverage import mismatch.");
        Check(barkImporter.wrapMode == TextureWrapMode.Repeat && barkImporter.alphaSource == TextureImporterAlphaSource.None,
            "Bark is not opaque/repeating.");
        Directory.CreateDirectory(Output);
        var leaf = new Material(sourceLeaf);
        var bark = new Material(sourceBark);
        var redLeaf = new Material(sourceRed);
        var redBark = new Material(sourceRedBark);
        var cameraObject = new GameObject("Maple shader validation camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false; camera.cullingMask = 0;
        camera.orthographic = true; camera.orthographicSize = 1; camera.aspect = 1;
        camera.transform.position = new Vector3(0,0,-3); camera.transform.LookAt(Vector3.zero);
        var mesh = new Mesh { name = "Maple validation card" };
        mesh.vertices = new[] { new Vector3(-.9f,-.9f,0), new Vector3(-.9f,.9f,0),
            new Vector3(.9f,.9f,0), new Vector3(.9f,-.9f,0) };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        mesh.triangles = new[] { 0,1,2, 0,2,3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var target = new RenderTexture(384,384,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        var pixels = new Texture2D(384,384,TextureFormat.RGBA32,false,true);
        var command = new CommandBuffer();
        var previousTarget = RenderTexture.active;
        var previousPipeline = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        try
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            target.Create(); camera.targetTexture = target; camera.Render();
            foreach (var material in new[] { leaf, bark, redLeaf, redBark })
            {
                Check(material.passCount == 4, "Expected forward, shadow, depth and depth-normal passes.");
                foreach (var keywords in new[] { Array.Empty<string>(), new[] { "INSTANCING_ON" },
                    new[] { "INSTANCING_ON", "LOD_FADE_CROSSFADE", "_GBUFFER_NORMALS_OCT" },
                    new[] { "_MAIN_LIGHT_SHADOWS", "_ADDITIONAL_LIGHTS", "_SHADOWS_SOFT" } })
                {
                    material.shaderKeywords = keywords;
                    for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);
                    Check(!ShaderUtil.ShaderHasError(material.shader), "Shader compilation failed: " + material.shader.name);
                }
                material.shaderKeywords = Array.Empty<string>();
                material.SetFloat("_AmbientStrength", 1);
            }
            leaf.SetFloat("_WindStrength", 0); leaf.SetFloat("_WindFlutterStrength", 0);
            leaf.SetFloat("_TranslucencyStrength", 0); leaf.SetFloat("_ColorVariationStrength", 0);
            leaf.SetFloat("_TreeTintStrength", 1);
            Color32[] Render(Material material, string name, bool instanced = false, Color? treeTint = null,
                float time = 0, int pass = 0, Matrix4x4? transform = null)
            {
                material.SetFloat("_StandingTreeEnabled", instanced ? 1 : 0);
                command.Clear(); command.SetRenderTarget(target); command.ClearRenderTarget(true,true,Color.clear);
                command.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));
                command.SetGlobalMatrix("unity_MatrixVP", GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);
                command.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);
                command.SetGlobalVector("_MainLightPosition", new Vector4(0,0,-1,0));
                command.SetGlobalVector("_MainLightColor", new Vector4(.25f,.25f,.25f,1));
                command.SetGlobalVector("_Time", new Vector4(time/20,time,time*2,time*3));
                var matrix = transform ?? Matrix4x4.identity;
                if (instanced)
                {
                    var block = new MaterialPropertyBlock();
                    block.SetVectorArray("_StandingTreeLeaves", new[] { (Vector4)(treeTint ?? Color.white) });
                    block.SetVectorArray("_StandingTreeBark", new[] { (Vector4)(treeTint ?? Color.white) });
                    block.SetVectorArray("_StandingTreeAppearance", new[] { new Vector4(0,1,0,0) });
                    block.SetVectorArray("_StandingTreeFade", new[] { new Vector4(0,1,1,0) });
                    command.DrawMeshInstanced(mesh,0,material,pass,new[] { matrix },1,block);
                }
                else command.DrawMesh(mesh,matrix,material,0,pass);
                Graphics.ExecuteCommandBuffer(command); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,384,384),0,0); pixels.Apply();
                var result = pixels.GetPixels32();
                if (name != null)
                {
                    // The GPU target is linear; PNG viewers expect display-space RGB.
                    var displayPixels = pixels.GetPixels();
                    for (int i = 0; i < displayPixels.Length; i++) displayPixels[i] = displayPixels[i].gamma;
                    var preview = new Texture2D(384,384,TextureFormat.RGBA32,false);
                    try
                    {
                        preview.SetPixels(displayPixels); preview.Apply();
                        File.WriteAllBytes(Output + "/" + name + ".png", preview.EncodeToPNG());
                    }
                    finally { Object.DestroyImmediate(preview); }
                }
                return result;
            }
            float MeanChannel(Color32[] image, int channel) => (float)image.Where(p=>p.a>127)
                .Average(p=>channel==0?p.r:channel==1?p.g:p.b);
            int Changed(Color32[] a, Color32[] b) => a.Where((p,i)=>!p.Equals(b[i])).Count();
            var summer = Render(leaf,"summer");
            int visible = summer.Count(p=>p.a>127);
            var warm = new Color(1,.5f,.1f,1);
            redLeaf.SetFloat("_WindStrength",0); redLeaf.SetFloat("_WindFlutterStrength",0);
            redLeaf.SetFloat("_SeasonAutumnAmount",0);
            var redSummer = Render(redLeaf,"red-maple-summer");
            Check(redSummer.Count(p=>p.a>127)>30000 && MeanChannel(redSummer,1)>MeanChannel(redSummer,0)*1.3f,
                "Reclassified red maple leaf material failed to render.");
            redLeaf.SetFloat("_SeasonAutumnAmount",1);
            var redAutumn = Render(redLeaf,"red-maple-autumn");
            var redNeutralAutumn = Render(redLeaf,"red-maple-autumn-neutral-instance",true,Color.white);
            Check(MeanChannel(redAutumn,0)>MeanChannel(redAutumn,1)*1.4f &&
                MeanChannel(redNeutralAutumn,0)>MeanChannel(redNeutralAutumn,1)*1.4f,
                "Red maple autumn palette was washed out by the generator's neutral white tint.");
            Check(redSummer.Where((p,i)=>p.a!=redNeutralAutumn[i].a).Count()==0,
                "Red maple autumn tint changed alpha coverage.");
            // A material cached before the world override must react immediately,
            // without mutating its authored season, colors, opacity or placement.
            var previewRoot = new GameObject("Tree season preview validation");
            previewRoot.SetActive(false);
            var previewWorld = previewRoot.AddComponent<WorldManager>();
            previewRoot.SetActive(true);
            try
            {
                int revision = previewWorld.TerrainGenerationRevision;
                leaf.SetFloat("_SeasonAutumnAmount", .25f);
                var authoredBaseline = Render(leaf,null,true,warm);
                previewWorld.SetTreeSeasonSimulation(true, 0);
                var previewSummer = Render(leaf,"season-preview-summer",true,warm);
                previewWorld.SetTreeSeasonSimulation(true, .5f);
                var previewMidpoint = Render(leaf,"season-preview-midpoint",true,warm);
                previewWorld.SetTreeSeasonSimulation(true, 1);
                var previewAutumn = Render(leaf,"season-preview-autumn",true,warm);
                Check(MeanChannel(previewSummer,1)>MeanChannel(previewSummer,0)*1.3f &&
                    MeanChannel(previewAutumn,0)>MeanChannel(previewAutumn,1), "World season override did not update cached instanced leaves.");
                Check(Changed(previewSummer,previewMidpoint)>visible*.8f && Changed(previewMidpoint,previewAutumn)>visible*.8f,
                    "Season midpoint is not a live intermediate color.");
                Check(previewSummer.Where((p,i)=>p.a!=previewAutumn[i].a).Count()==0, "Season preview changed alpha coverage.");
                Check(leaf.GetFloat("_SeasonAutumnAmount")==.25f && previewWorld.TerrainGenerationRevision==revision,
                    "Preview changed the authored material season or regenerated the world.");
                previewWorld.SetTreeSeasonSimulation(false, 1);
                var restored = Render(leaf,null,true,warm);
                Check(Changed(restored,authoredBaseline)==0, "Disabling preview did not restore authored shading.");
                previewWorld.SetTreeSeasonSimulation(true, 2);
                Check(previewWorld.TreeSeasonAutumnAmount==1 && Shader.GetGlobalFloat("_TreeSeasonSimulationAutumnAmount")==1,
                    "Season preview did not clamp its upper endpoint.");
                previewWorld.SetTreeSeasonSimulation(true, -1);
                Check(previewWorld.TreeSeasonAutumnAmount==0, "Season preview did not clamp its lower endpoint.");
                previewWorld.enabled = false;
                WorldManager.RefreshTreeSeasonSimulation();
                Check(Shader.GetGlobalFloat("_TreeSeasonSimulationEnabled")==0, "Disabling World Manager leaked its season override.");
                previewWorld.enabled = true;
                previewWorld.SetTreeSeasonSimulation(true, .5f);
            }
            finally { Object.DestroyImmediate(previewRoot); }
            WorldManager.RefreshTreeSeasonSimulation();
            Check(Shader.GetGlobalFloat("_TreeSeasonSimulationEnabled")==0, "Destroying World Manager leaked the season override.");
            leaf.SetFloat("_SeasonAutumnAmount",0);

            foreach (string shaderPath in new[] {
                "SugarMapleLeafTintCutout", "RedMapleLeafTintCutout", "BirchLeafTintCutout", "OakLeafTintCutout",
                "SugarMapleBillboardTintCutout", "RedMapleBillboardTintCutout", "BirchBillboardTintCutout",
                "SugarMapleLOD2BillboardSimpleLitCutout", "RedMapleLOD2BillboardSimpleLitCutout" })
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/"+shaderPath+".shader");
                var material = new Material(shader) { enableInstancing = true };
                try
                {
                    for (int pass=0;pass<material.passCount;pass++) ShaderUtil.CompilePass(material,pass,true);
                    Check(!ShaderUtil.ShaderHasError(shader), "Season-enabled legacy shader failed: "+shader.name);
                }
                finally { Object.DestroyImmediate(material); }
            }
            WorldManagerInspectorValidation.Run();
            Check(visible>30000 && visible<110000, "Leaf alpha clipping rendered an empty/full rectangle: " + visible);
            Check(MeanChannel(summer,1)>MeanChannel(summer,0)*1.3f, "Summer leaves are not green.");
            var red = new Color(.8f,.08f,.04f,1);
            var instancedSummer = Render(leaf,"summer-instanced",true,warm);
            Check(MeanChannel(instancedSummer,1)>MeanChannel(instancedSummer,0)*1.3f,
                "Existing warm instance tint leaked into summer.");
            leaf.SetFloat("_SeasonAutumnAmount",1);
            var autumn = Render(leaf,"autumn");
            Check(MeanChannel(autumn,0)>MeanChannel(autumn,1), "Autumn tint is not warm.");
            Check(summer.Where((p,i)=>p.a!=autumn[i].a).Count()==0,"Seasonal tint changed opacity.");
            var autumnWarm = Render(leaf,"autumn-instanced-gold",true,warm);
            var autumnRed = Render(leaf,"autumn-instanced-red",true,red);
            Check(Changed(autumnWarm,autumnRed)>visible*.8f, "Per-tree autumn tint is not used.");
            leaf.SetFloat("_SeasonAutumnAmount",0);
            var back = Render(leaf,"summer-backface",transform:Matrix4x4.Rotate(Quaternion.Euler(0,180,0)));
            Check(back.Count(p=>p.a>127)>30000,"Two-sided leaf rendering failed.");
            leaf.SetFloat("_WindHeightMin",-1); leaf.SetFloat("_WindHeightMax",1);
            leaf.SetFloat("_WindStrength",.3f); leaf.SetFloat("_WindFlutterStrength",.1f);
            var windA = Render(leaf,null,time:1); var windB = Render(leaf,null,time:4);
            Check(Changed(windA,windB)>1000,"Leaf wind did not animate.");
            var barkPixels = Render(bark,"bark");
            Check(barkPixels.Count(p=>p.a>127)>110000,"Bark coverage is not opaque.");
            var redBarkPixels = Render(redBark,"red-maple-bark");
            Check(redBarkPixels.Count(p=>p.a>127)>110000,"Red maple bark coverage is not opaque.");
            Check(MeanChannel(redBarkPixels,0)/MeanChannel(redBarkPixels,2) <
                MeanChannel(barkPixels,0)/MeanChannel(barkPixels,2), "Red maple bark is not cooler than sugar maple bark.");
            var barkWarm = Render(bark,null,true,warm); var barkRed = Render(bark,null,true,red);
            Check(Changed(barkWarm,barkRed)>100000,"Per-tree bark tint is not used.");
            Debug.Log("SUGAR MAPLE STYLIZED PASS: all four shader passes/variants compile; texture imports, cutout coverage, " +
                "green summer, warm autumn, unchanged seasonal opacity, per-tree tints, backfaces and wind validated on GPU. " +
                "Red maple bark, live world season override/midpoint/cleanup and legacy shaders also validated. Preview images: " + Output);
        }
        finally
        {
            RenderTexture.active = previousTarget;
            GraphicsSettings.defaultRenderPipeline = previousPipeline; QualitySettings.renderPipeline = previousQuality;
            command.Dispose(); target.Release();
            Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(leaf); Object.DestroyImmediate(bark);
            Object.DestroyImmediate(redLeaf); Object.DestroyImmediate(redBark);
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    public static void RunBatch()
    {
        try { CreateAssets(); Validate(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
}
