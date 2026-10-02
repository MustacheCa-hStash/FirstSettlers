using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Builds single-root, single-submesh assets for FoliageManager's instanced path.
/// Preview scenes are isolated authoring scenes; no world scene or Play mode is used.</summary>
public static class CloverPrefabAuthoring
{
    const string Models = "Assets/Models/Grass+Flowers/";
    const string TexturePath = "Assets/Textures/Flowers/T_CloverLeaf_v02.png";
    const string MaterialPath = "Assets/Materials/M_Flowers/M_Clover/M_CloverLeaf_v02.mat";
    const string PreviewDir = "ArtReferences/Clover/";
    static readonly string[] Names = { "CloverClump_v02_A", "CloverClump_v02_B_Flowering", "CloverClump_v02_Edge", "CloverSingle_v02" };

    [Serializable] sealed class MeshData
    {
        public string name;
        public Vector3[] positions, normals;
        public Vector2[] uvs;
        public Color[] colors;
        public int[] triangles;
    }

    [MenuItem("Tools/Foliage/Clover v02/Build Prefabs")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Clover authoring requires Edit mode.");
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = .5f;
        importer.maxTextureSize = 256; // Broad color shapes, deliberately low texture detail.
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.SaveAndReimport();
        var shader = Shader.Find("Custom/CloverLeafInstancedLit");
        if (shader == null) throw new InvalidOperationException("Clover v02 shader missing.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "M_CloverLeaf_v02" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
        material.SetTextureScale("_BaseMap", Vector2.one);
        material.SetTextureOffset("_BaseMap", Vector2.zero);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Cutoff", .5f);
        material.SetFloat("_NormalUpBlend", .25f);
        material.SetFloat("_AmbientStrength", .16f);
        material.SetFloat("_InstanceVariationStrength", .10f);
        material.SetFloat("_ReceiveShadows", 1);
        material.SetFloat("_FadeStartDistance", 42);
        material.SetFloat("_FadeEndDistance", 58);
        material.SetVector("_CloverInstanceData", new Vector4(0, .5f, 0, 0));
        material.enableInstancing = true;
        material.doubleSidedGI = true;
        material.renderQueue = 2450;
        EditorUtility.SetDirty(material);
        foreach (string name in Names)
        {
            var data = JsonUtility.FromJson<MeshData>(File.ReadAllText(Models + name + ".json"));
            if (data.positions == null || data.normals.Length != data.positions.Length ||
                data.uvs.Length != data.positions.Length || data.colors.Length != data.positions.Length)
                throw new InvalidOperationException("Invalid authoring streams for " + name);
            var built = new Mesh { name = name + "_Runtime", vertices = data.positions,
                normals = data.normals, uv = data.uvs, colors = data.colors, triangles = data.triangles };
            built.RecalculateBounds();
            string meshPath = Models + name + "_Runtime.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) { AssetDatabase.CreateAsset(built, meshPath); mesh = built; }
            else
            {
                // Assign native streams explicitly: copying serialized Mesh data can
                // leave a previously uploaded GPU buffer alive in this editor session.
                mesh.Clear();
                mesh.vertices = data.positions; mesh.normals = data.normals;
                mesh.uv = data.uvs; mesh.colors = data.colors; mesh.triangles = data.triangles;
                mesh.RecalculateBounds(); mesh.UploadMeshData(false);
                EditorUtility.SetDirty(mesh); Object.DestroyImmediate(built);
            }
            var root = new GameObject(name);
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/" + name + ".prefab");
                Debug.Log($"CLOVER_BUILT {name}: {mesh.vertexCount} vertices / {mesh.triangles.Length / 3} triangles / bounds {mesh.bounds}");
            }
            finally { Object.DestroyImmediate(root); }
        }
        AssetDatabase.SaveAssets();
        Validate();
    }

    [MenuItem("Tools/Foliage/Clover v02/Validate Assets")]
    public static void Validate()
    {
        foreach (string name in Names)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab");
            if (prefab == null || prefab.transform.childCount != 0 || prefab.transform.localScale != Vector3.one)
                throw new InvalidOperationException("Prefab must have identity root and no children: " + name);
            Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
            Material mat = prefab.GetComponent<MeshRenderer>().sharedMaterial;
            if (mesh.subMeshCount != 1 || !mat.enableInstancing || mat.GetTextureScale("_BaseMap") != Vector2.one)
                throw new InvalidOperationException("Instancing/submesh/UV setup invalid: " + name);
            Vector3 size = mesh.bounds.size;
            if (size.y < .1f || size.y > 1.7f || size.x > 7f || size.z > 7f)
                throw new InvalidOperationException("Baked dimensions invalid: " + name + " " + size);
            Vector3[] v = mesh.vertices;
            foreach (int index in mesh.triangles)
                if (index < 0 || index >= v.Length) throw new InvalidOperationException("Invalid triangle index.");
            foreach (Vector2 uv in mesh.uv)
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) throw new InvalidOperationException("UV outside leaflet.");
            int[] tris = mesh.triangles;
            for (int i = 0; i < tris.Length; i += 3)
                if (Vector3.Cross(v[tris[i + 1]] - v[tris[i]], v[tris[i + 2]] - v[tris[i]]).sqrMagnitude < 1e-14f)
                    throw new InvalidOperationException("Degenerate geometry in " + name);
            if (ShaderUtil.ShaderHasError(mat.shader)) throw new InvalidOperationException("Clover shader compile error.");
        }
        Debug.Log("CLOVER_VALIDATION_PASS: identity roots, one submesh/material, baked Y-up geometry, UV range, triangle indices/areas, shader.");
    }

    [MenuItem("Tools/Foliage/Clover v02/Build and Render Previews")]
    public static void BuildAndPreview()
    {
        Build();
        Directory.CreateDirectory(PreviewDir);
        Scene preview = EditorSceneManager.NewPreviewScene();
        var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        bool oldAsyncCompilation = ShaderUtil.allowAsyncCompilation;
        AmbientMode oldMode = RenderSettings.ambientMode;
        Color oldAmbient = RenderSettings.ambientLight;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.33f, .38f, .43f);
            groundMaterial.SetColor("_BaseColor", new Color(.23f, .255f, .16f));
            groundMaterial.SetFloat("_Smoothness", 0);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            SceneManager.MoveGameObjectToScene(ground, preview);
            ground.transform.localScale = Vector3.one;
            ground.transform.position = new Vector3(0, -.004f, 0);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            Light sun = MakeObject("Clover authoring light", preview).AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = .95f;
            sun.color = new Color(1, .97f, .90f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52, -32, 0);
            Camera camera = MakeObject("Clover authoring camera", preview).AddComponent<Camera>();
            camera.scene = preview; camera.cameraType = CameraType.Preview; camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.18f, .22f, .24f);
            camera.nearClipPlane = .008f; camera.farClipPlane = 30; camera.fieldOfView = 38;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false; cameraData.renderShadows = true;
            GameObject clump = MakeClump(Names[0], Vector3.zero, 0, .5f, preview);
            Pose(camera, new Vector3(.68f, .65f, -.82f), new Vector3(0, .04f, 0));
            Capture(camera, "Clover_v02_Unity_Clump.png");
            Pose(camera, new Vector3(0, 1.18f, -.025f), new Vector3(0, .04f, 0));
            Capture(camera, "Clover_v02_Unity_Top.png");
            Object.DestroyImmediate(clump);
            var flowering = MakeClump(Names[1], Vector3.zero, 32, .5f, preview);
            Pose(camera, new Vector3(.66f, .54f, -.72f), new Vector3(0, .055f, 0));
            Capture(camera, "Clover_v02_Unity_Flowering.png");
            Object.DestroyImmediate(flowering);
            var single = MakeClump(Names[3], Vector3.zero, 0, .5f, preview);
            Pose(camera, new Vector3(.14f, .18f, -.24f), new Vector3(0, .049f, 0));
            Capture(camera, "Clover_v02_Unity_Single.png");
            Object.DestroyImmediate(single);
            var random = new System.Random(441);
            for (int i = 0; i < 28; i++)
            {
                float a = i * 2.399963f, r = Mathf.Sqrt(i / 28f);
                string name = i % 7 == 0 ? Names[1] : i % 3 == 0 ? Names[2] : Names[0];
                MakeClump(name, new Vector3(Mathf.Cos(a) * r * 1.2f, 0, Mathf.Sin(a) * r * .83f),
                    (float)random.NextDouble() * 360, .5f * Mathf.Lerp(.82f, 1.1f, (float)random.NextDouble()), preview);
            }
            Pose(camera, new Vector3(1.9f, 2.2f, -2.6f), new Vector3(0, .04f, 0));
            Capture(camera, "Clover_v02_Unity_Patch.png");
            Pose(camera, new Vector3(.80f, .32f, -1.7f), new Vector3(0, .045f, 0));
            Capture(camera, "Clover_v02_Unity_GroundView.png");
            groundMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Textures/Ground/LeafLitter/forest_floor_albedo.png"));
            groundMaterial.SetTextureScale("_BaseMap", Vector2.one * 5);
            groundMaterial.SetColor("_BaseColor", Color.white);
            Pose(camera, new Vector3(.80f, .70f, -1.55f), new Vector3(0, .035f, 0));
            Capture(camera, "Clover_v02_Unity_Woodland.png");
            Validate();
            Debug.Log("CLOVER_PREVIEWS_PASS: seven actual URP asset renders; no world scene loaded.");
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = oldAsyncCompilation;
            RenderSettings.ambientMode = oldMode;
            RenderSettings.ambientLight = oldAmbient;
            Object.DestroyImmediate(groundMaterial);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    static GameObject MakeObject(string name, Scene scene)
    {
        var obj = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(obj, scene); return obj;
    }
    static GameObject MakeClump(string name, Vector3 position, float yaw, float scale, Scene scene)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab");
        var obj = MakeObject(name, scene);
        obj.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        obj.transform.localScale = Vector3.one * scale;
        obj.AddComponent<MeshFilter>().sharedMesh = prefab.GetComponent<MeshFilter>().sharedMesh;
        var renderer = obj.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = prefab.GetComponent<MeshRenderer>().sharedMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        Debug.Log($"CLOVER_PREVIEW_INSTANCE {name} scale {obj.transform.localScale} world bounds {renderer.bounds}");
        return obj;
    }
    static void Pose(Camera camera, Vector3 eye, Vector3 target)
    { camera.transform.position = eye; camera.transform.LookAt(target); }
    static void Capture(Camera camera, string filename)
    {
        RenderTexture target = RenderTexture.GetTemporary(2800, 2000, 24, RenderTextureFormat.ARGB32);
        RenderTexture resolved = RenderTexture.GetTemporary(1400, 1000, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        var pixels = new Texture2D(1400, 1000, TextureFormat.RGB24, false);
        try
        {
            // Initialize the renderer and compile its passes before the saved frame.
            for (int frame = 0; frame < 3; frame++)
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            Graphics.Blit(target, resolved); RenderTexture.active = resolved;
            pixels.ReadPixels(new Rect(0, 0, 1400, 1000), 0, 0); pixels.Apply();
            Color32[] colors = pixels.GetPixels32();
            int min = 255, max = 0, magenta = 0, foliage = 0;
            for (int i = 0; i < colors.Length; i += 47)
            {
                min = Mathf.Min(min, colors[i].g); max = Mathf.Max(max, colors[i].g);
                if (colors[i].r > 180 && colors[i].b > 180 && colors[i].g < 80) magenta++;
                if (colors[i].g > colors[i].r * 1.14f && colors[i].g > colors[i].b * 1.25f && colors[i].g > 85) foliage++;
            }
            if (max - min < 15 || magenta > colors.Length / 4700 || foliage < 40)
                throw new InvalidOperationException("Blank or failed-shader preview: " + filename);
            File.WriteAllBytes(PreviewDir + filename, pixels.EncodeToPNG());
            Debug.Log("CLOVER_RENDERED " + filename);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target); RenderTexture.ReleaseTemporary(resolved);
            Object.DestroyImmediate(pixels);
        }
    }
}
