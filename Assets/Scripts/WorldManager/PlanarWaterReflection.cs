using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>One reflection camera for every water mesh on the shared world-space plane.</summary>
public sealed class PlanarWaterReflection : MonoBehaviour
{
    private static readonly int ReflectionTextureId = Shader.PropertyToID("_WaterReflectionTex");
    private static readonly int ReflectionValidId = Shader.PropertyToID("_WaterReflectionValid");
    private static readonly int NormalTextureId = Shader.PropertyToID("_WaterNormalTex");
    private static readonly int NormalValidId = Shader.PropertyToID("_WaterNormalValid");
    private const int WaterLayer = 4;

    private Camera sourceCamera;
    private Camera reflectionCamera;
    private RenderTexture reflectionTexture;
    private Texture2D surfaceNormalTexture;
    private float waterY;
    private float resolutionScale;
    private float updateInterval;
    private float maxDistance;
    private float nextUpdateTime;
    private Vector3 lastRenderedPosition;
    private Quaternion lastRenderedRotation;
    private bool hasRendered;
    private bool requestChecked;
    private bool requestSupported;

    public void Configure(Camera source, float surfaceY, float textureScale = 0.5f,
        float updatesPerSecond = 30f, float reflectionDistance = 300f)
    {
        sourceCamera = source;
        waterY = surfaceY;
        resolutionScale = Mathf.Clamp(textureScale, 0.25f, 1f);
        updateInterval = 1f / Mathf.Max(1f, updatesPerSecond);
        maxDistance = Mathf.Max(20f, reflectionDistance);
        nextUpdateTime = 0f;
        hasRendered = false;
        requestChecked = false;

        if (surfaceNormalTexture == null)
            surfaceNormalTexture = CreateSurfaceNormalTexture();
        Shader.SetGlobalTexture(NormalTextureId, surfaceNormalTexture);
        Shader.SetGlobalFloat(NormalValidId, 1f);

        if (sourceCamera == null)
        {
            Shader.SetGlobalFloat(ReflectionValidId, 0f);
            return;
        }

        if (reflectionCamera == null)
        {
            GameObject cameraObject = new GameObject("Water Planar Reflection Camera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.transform.SetParent(transform, false);
            reflectionCamera = cameraObject.AddComponent<Camera>();
            reflectionCamera.enabled = false;
            UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderType = CameraRenderType.Base;
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;
            cameraData.requiresDepthTexture = false;
            cameraData.requiresColorTexture = false;
        }

        Shader.SetGlobalFloat(ReflectionValidId, 0f);
    }

    private void LateUpdate()
    {
        if (sourceCamera == null || !sourceCamera.isActiveAndEnabled)
            return;

        if (!requestChecked)
        {
            requestSupported = RenderPipeline.SupportsRenderRequest(
                reflectionCamera, new UniversalRenderPipeline.SingleCameraRequest());
            requestChecked = true;
            if (!requestSupported)
                Debug.LogWarning("This render pipeline does not support the water planar reflection request.", this);
        }
        if (!requestSupported)
            return;

        // Below the plane, this above-water reflection is not useful.
        if (sourceCamera.transform.position.y <= waterY + 0.05f)
        {
            hasRendered = false;
            Shader.SetGlobalFloat(ReflectionValidId, 0f);
            return;
        }

        // Keep a moving viewer's reflection synchronized. The 30 Hz limit is
        // useful when stationary, but skipping camera turns makes reflections jump.
        bool cameraMoved = !hasRendered ||
            (sourceCamera.transform.position - lastRenderedPosition).sqrMagnitude > 0.0001f ||
            Quaternion.Angle(sourceCamera.transform.rotation, lastRenderedRotation) > 0.05f;
        if (!cameraMoved && Time.unscaledTime < nextUpdateTime)
            return;
        nextUpdateTime = Time.unscaledTime + updateInterval;

        EnsureTexture();
        if (reflectionTexture == null)
            return;

        RenderReflection();
        lastRenderedPosition = sourceCamera.transform.position;
        lastRenderedRotation = sourceCamera.transform.rotation;
        hasRendered = true;
    }

    private void EnsureTexture()
    {
        int width = Mathf.Max(64, Mathf.RoundToInt(sourceCamera.pixelWidth * resolutionScale));
        int height = Mathf.Max(64, Mathf.RoundToInt(sourceCamera.pixelHeight * resolutionScale));
        if (reflectionTexture != null && reflectionTexture.width == width && reflectionTexture.height == height)
            return;

        if (reflectionTexture != null)
        {
            reflectionTexture.Release();
            Destroy(reflectionTexture);
        }

        reflectionTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGBHalf)
        {
            name = "Water Planar Reflection",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1,
            useMipMap = true,
            autoGenerateMips = false
        };
        reflectionTexture.Create();
        hasRendered = false;
    }

    private static Texture2D CreateSurfaceNormalTexture()
    {
        const int size = 128;
        var heights = new float[size * size];
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;
                heights[y * size + x] = PeriodicNoise(u, v, 4) * 0.65f +
                    PeriodicNoise(u, v, 9) * 0.35f;
            }
        }

        for (int y = 0; y < size; y++)
        {
            int previousY = ((y + size - 1) % size) * size;
            int nextY = ((y + 1) % size) * size;
            for (int x = 0; x < size; x++)
            {
                float dx = heights[y * size + (x + 1) % size] -
                    heights[y * size + (x + size - 1) % size];
                float dy = heights[nextY + x] - heights[previousY + x];
                pixels[y * size + x] = new Color(
                    Mathf.Clamp01(0.5f - dx * 4f),
                    Mathf.Clamp01(0.5f - dy * 4f), 1f, 1f);
            }
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
        {
            name = "Water Surface Normals",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 2
        };
        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }

    private static float PeriodicNoise(float u, float v, int cells)
    {
        float x = u * cells;
        float y = v * cells;
        float a = Mathf.PerlinNoise(x, y);
        float b = Mathf.PerlinNoise(x - cells, y);
        float c = Mathf.PerlinNoise(x, y - cells);
        float d = Mathf.PerlinNoise(x - cells, y - cells);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    private void RenderReflection()
    {
        reflectionCamera.CopyFrom(sourceCamera);
        reflectionCamera.enabled = false;
        reflectionCamera.targetTexture = null;
        reflectionCamera.cullingMask = sourceCamera.cullingMask & ~(1 << WaterLayer);
        reflectionCamera.farClipPlane = Mathf.Min(sourceCamera.farClipPlane, maxDistance);
        reflectionCamera.allowMSAA = false;
        reflectionCamera.useOcclusionCulling = false;

        Vector3 sourcePosition = sourceCamera.transform.position;
        Vector3 reflectedPosition = sourcePosition;
        reflectedPosition.y = 2f * waterY - sourcePosition.y;
        Vector3 sourceAngles = sourceCamera.transform.eulerAngles;
        reflectionCamera.transform.SetPositionAndRotation(reflectedPosition,
            Quaternion.Euler(-sourceAngles.x, sourceAngles.y, sourceAngles.z));

        // Mirror around y = waterY, then discard geometry beneath the surface.
        Matrix4x4 mirror = Matrix4x4.identity;
        mirror.m11 = -1f;
        mirror.m13 = 2f * waterY;
        reflectionCamera.worldToCameraMatrix = sourceCamera.worldToCameraMatrix * mirror;

        Vector3 planePoint = new Vector3(0f, waterY + 0.03f, 0f);
        Vector3 cameraSpacePoint = reflectionCamera.worldToCameraMatrix.MultiplyPoint(planePoint);
        Vector3 cameraSpaceNormal = reflectionCamera.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;
        Vector4 clipPlane = new Vector4(cameraSpaceNormal.x, cameraSpaceNormal.y,
            cameraSpaceNormal.z, -Vector3.Dot(cameraSpacePoint, cameraSpaceNormal));
        reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(clipPlane);

        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = reflectionTexture };
        bool previousInvertCulling = GL.invertCulling;
        try
        {
            GL.invertCulling = !previousInvertCulling;
            RenderPipeline.SubmitRenderRequest(reflectionCamera, request);
        }
        finally
        {
            GL.invertCulling = previousInvertCulling;
        }

        reflectionTexture.GenerateMips();
        Shader.SetGlobalTexture(ReflectionTextureId, reflectionTexture);
        Shader.SetGlobalFloat(ReflectionValidId, 1f);
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(ReflectionValidId, 0f);
    }

    private void OnDestroy()
    {
        Shader.SetGlobalFloat(ReflectionValidId, 0f);
        Shader.SetGlobalFloat(NormalValidId, 0f);
        if (reflectionTexture != null)
        {
            reflectionTexture.Release();
            Destroy(reflectionTexture);
        }
        if (reflectionCamera != null)
            Destroy(reflectionCamera.gameObject);
        if (surfaceNormalTexture != null)
            Destroy(surfaceNormalTexture);
    }
}
