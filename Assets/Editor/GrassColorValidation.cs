using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class GrassColorValidation
{
    [MenuItem("Tools/Foliage/Validate Grass Face Colors")]
    public static void Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GrassTuftBroad_LOD0.prefab");
        Require(prefab != null, "Missing active grass prefab.");
        var sourceMaterial = prefab.GetComponent<MeshRenderer>().sharedMaterial;
        Require(sourceMaterial.shader.name == "Custom/GrassInstancedTerrainTint", "Active grass material has changed; update this diagnosis.");
        var runtimeMesh = prefab.GetComponent<MeshFilter>().sharedMesh;
        float minX = 1f, maxX = -1f, minZ = 1f, maxZ = -1f;
        foreach (Vector3 normal in runtimeMesh.normals)
        {
            minX = Mathf.Min(minX, normal.x); maxX = Mathf.Max(maxX, normal.x);
            minZ = Mathf.Min(minZ, normal.z); maxZ = Mathf.Max(maxZ, normal.z);
        }
        Debug.Log($"GRASS NORMAL AUDIT: active runtime mesh normals X {minX:F3}..{maxX:F3}, Z {minZ:F3}..{maxZ:F3}; normals are recalculated from blade geometry by GrassTuftBroadPrefabBuilder.");

        // Compile a transient, unsaved copy of the active shader with only lighting removed.
        // All palette/instance/root-tip/texture paths remain identical to production.
        string shaderSource = File.ReadAllText(AssetDatabase.GetAssetPath(sourceMaterial.shader));
        const string colorExpression = "half3 color = grassTint * bladeTone * _BaseColor.rgb * _Color.rgb * lighting;";
        Require(shaderSource.Contains(colorExpression), "Grass color expression changed; update this fixture.");
        Shader unlitShader = ShaderUtil.CreateShaderAsset(shaderSource
            .Replace("Shader \"Custom/GrassInstancedTerrainTint\"", "Shader \"Hidden/Validation/GrassBaseColor\"")
            .Replace(colorExpression, "half3 color = grassTint * bladeTone * _BaseColor.rgb * _Color.rgb;"));
        var lit = new Material(sourceMaterial);
        var unlit = new Material(sourceMaterial) { shader = unlitShader };
        var mesh = new Mesh();
        var blade = new GameObject("Grass color validation card") { layer = 31 };
        blade.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = blade.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.lightProbeUsage = LightProbeUsage.Off;
        var cameraObject = new GameObject("Grass color validation camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false; camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.orthographic = true; camera.orthographicSize = 0.6f; camera.aspect = 1;
        camera.nearClipPlane = 0.01f; camera.farClipPlane = 10;
        cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        var lightObject = new GameObject("Grass color validation sun") { layer = 31 };
        var sun = lightObject.AddComponent<Light>();
        sun.type = LightType.Directional; sun.color = Color.white; sun.shadows = LightShadows.None; sun.cullingMask = 1 << 31;
        sun.transform.rotation = Quaternion.LookRotation(-new Vector3(0, 0.342f, 0.94f), Vector3.right);
        Light previousSun = RenderSettings.sun;
        var previousProbe = RenderSettings.ambientProbe;
        RenderSettings.sun = sun; RenderSettings.ambientProbe = new SphericalHarmonicsL2();
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var pixels = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
        RenderTexture previousTarget = RenderTexture.active;
        try
        {
            mesh.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 1, 0), new Vector3(-0.5f, 1, 0) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            foreach (Material material in new[] { lit, unlit })
            {
                material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                material.SetFloat("_WindStrength", 0); material.SetFloat("_WindFlutterStrength", 0);
                material.SetFloat("_ReceiveShadows", 0); material.SetFloat("_RenderFadeEnabled", 0);
                material.DisableKeyword("_BILLBOARD_RENDER_FADE_ON");
                ShaderUtil.CompilePass(material, 0, true);
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            }
            target.Create();

            Color Render(Material material, Vector3 normal, bool fromBack, float intensity)
            {
                mesh.normals = new[] { normal, normal, normal, normal };
                renderer.sharedMaterial = material;
                camera.transform.position = new Vector3(0, 0.5f, fromBack ? -2 : 2);
                camera.transform.rotation = Quaternion.LookRotation(fromBack ? Vector3.forward : Vector3.back, Vector3.up);
                sun.intensity = intensity;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
                return pixels.GetPixel(32, 32);
            }

            Color baseFront = Render(unlit, Vector3.forward, false, 1);
            Color baseBack = Render(unlit, Vector3.forward, true, 1);
            Color baseOppositeNormal = Render(unlit, Vector3.back, false, 1);
            Require(baseFront.g > 0.01f, "Grass base-color fixture rendered blank.");
            Require(Difference(baseFront, baseBack) < 0.003f, "Front/back grass albedo differs.");
            Require(Difference(baseFront, baseOppositeNormal) < 0.003f, "Blade orientation changes grass albedo.");

            Color litFront = Render(lit, Vector3.forward, false, 1);
            Color litBackView = Render(lit, Vector3.forward, true, 1);
            Color litOppositeNormal = Render(lit, Vector3.back, false, 1);
            Require(Difference(litFront, litBackView) < 0.01f, "Viewing the reverse side changes grass lighting.");
            float dayContrast = Contrast(litFront, litOppositeNormal);
            Color eveningFront = Render(lit, Vector3.forward, false, 0.1f);
            Color eveningOther = Render(lit, Vector3.back, false, 0.1f);
            float eveningContrast = Contrast(eveningFront, eveningOther);
            Require(dayContrast > 0.1f, "Differently oriented grass normals do not explain the screenshot.");
            Require(eveningContrast < dayContrast * 0.6f, "Lower sunlight did not bring grass shades closer together.");
            Debug.Log($"GRASS COLOR PASS: front/back/orientation base-color differences {Difference(baseFront, baseBack):F6}/{Difference(baseFront, baseOppositeNormal):F6}; lit view-side difference {Difference(litFront, litBackView):F6}; opposite-normal contrast day {dayContrast:F3}, weaker evening light {eveningContrast:F3}. Active grass uses the handwritten shader, not SG_Grass_Instance. Base texture RGB is ignored; root/tip gradient and smooth per-tuft palette are intentional.");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            RenderSettings.sun = previousSun; RenderSettings.ambientProbe = previousProbe;
            Object.DestroyImmediate(blade); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(mesh); Object.DestroyImmediate(lit); Object.DestroyImmediate(unlit); Object.DestroyImmediate(unlitShader);
            Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target);
        }
    }

    public static void RunBatch()
    {
        try { TerrainHorizonShadowValidation.Run(); Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static float Difference(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
    private static float Contrast(Color a, Color b) => Difference(a, b) / Mathf.Max(0.001f, (a.g + b.g) * 0.5f);
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}
