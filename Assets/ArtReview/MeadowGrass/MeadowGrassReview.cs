using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public sealed class MeadowGrassReview : MonoBehaviour
{
    public Camera reviewCamera;
    public Material prototypeMaterial, pilotMaterial;
    public Mesh[] nearMeshes, shortNearMeshes, midMeshes;
    public Mesh pilotMesh;
    public GameObject shadeCaster;

    private const float Spacing = 128f * .3f / 144f;
    private Transform instances;
    private int mode = -1;
    private bool lookActive;

    public static float GroundHeight(float x, float z) =>
        .15f*z + .13f*x + .30f*Mathf.Sin(z*.62f) + .18f*Mathf.Cos(x*.91f + z*.23f);

    private void Start()
    {
        Shader.SetGlobalVector("_FSGroundDaylight", new Vector4(1, 1, 0, 0));
        SetView(0);
        SetMode(0);
    }

    public void SetMode(int selection)
    {
        if (selection == mode) return;
        mode = selection;
        if (instances)
        {
            instances.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(instances.gameObject);
            else DestroyImmediate(instances.gameObject);
        }
        if (mode == 2) return;

        instances = new GameObject("Review grass instances").transform;
        instances.SetParent(transform, false);
        var random = new System.Random(3242);
        var material = mode == 3 ? pilotMaterial : prototypeMaterial;
        for (int row = 0; row < 10f / Spacing - 1; row++)
        for (int column = 0; column < 8f / Spacing - 1; column++)
        {
            float x = (column + .04f + .92f * (float)random.NextDouble()) * Spacing - 4;
            float z = (row + .04f + .92f * (float)random.NextDouble()) * Spacing;
            float scale = .9f + .4f * (float)random.NextDouble();
            float yaw = 360 * (float)random.NextDouble();
            int variant = random.Next(nearMeshes.Length);
            // Mix the cell coordinates independently of placement randomness.
            bool tall = ((column * 73856093 ^ row * 19349663 ^ (column * row * 83492791 >> 11)) & 3) == 0;
            var obj = new GameObject("Grass", typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(instances, false);
            obj.transform.localPosition = new Vector3(x, GroundHeight(x,z) - .018f, z);
            Vector3 normal = new(GroundHeight(x-.05f,z)-GroundHeight(x+.05f,z), .10f,
                GroundHeight(x,z-.05f)-GroundHeight(x,z+.05f));
            var slope = Quaternion.FromToRotation(Vector3.up, normal.normalized);
            obj.transform.localRotation = Quaternion.Slerp(Quaternion.identity, slope, .38f) *
                Quaternion.Euler(0, yaw, 0);
            obj.transform.localScale = Vector3.one * scale;
            obj.GetComponent<MeshFilter>().sharedMesh = mode == 3 ? pilotMesh :
                mode == 1 ? midMeshes[variant] : (tall ? nearMeshes : shortNearMeshes)[variant];
            var renderer = obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
        }
    }

    public void SetView(int view)
    {
        Vector3 position, target;
        switch (view)
        {
            case 1: position = new(-2.9f,1.33f,2.2f); target = new(1.6f,1.01f,5.3f); break;
            case 2: position = new(0,6.7f,4.8f); target = new(0,.3f,5.1f); break;
            case 3: position = new(0,2.6f,8.6f); target = new(0,1.05f,3.0f); break;
            case 4: position = new(.22f,1.01f,2.15f); target = new(.22f,.92f,2.85f); break;
            default: position = new(0,1.48f,.42f); target = new(-.15f,.97f,5.8f); break;
        }
        reviewCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target-position));
    }

    private void Update()
    {
        var keys = Keyboard.current;
        var mouse = Mouse.current;
        if (keys == null) return;
        if (keys.digit1Key.wasPressedThisFrame) SetMode(0);
        if (keys.digit2Key.wasPressedThisFrame) SetMode(1);
        if (keys.digit3Key.wasPressedThisFrame) SetMode(2);
        if (keys.digit4Key.wasPressedThisFrame) SetMode(3);
        if (keys.digit5Key.wasPressedThisFrame) SetView(0);
        if (keys.digit6Key.wasPressedThisFrame) SetView(1);
        if (keys.digit7Key.wasPressedThisFrame) SetView(2);
        if (keys.digit8Key.wasPressedThisFrame) SetView(3);
        if (keys.digit9Key.wasPressedThisFrame) SetView(4);
        if (keys.lKey.wasPressedThisFrame) shadeCaster.SetActive(!shadeCaster.activeSelf);
        var pose = reviewCamera.transform;
        bool looking = mouse != null && mouse.rightButton.isPressed;
        if (looking)
        {
            if (lookActive)
            {
                var delta = mouse.delta.ReadValue() * .15f;
                pose.rotation = Quaternion.Euler(Mathf.DeltaAngle(0, pose.eulerAngles.x) - delta.y,
                    pose.eulerAngles.y + delta.x, 0);
            }
        }
        lookActive = looking;
        Vector3 movement = new((keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0),
            (keys.eKey.isPressed ? 1 : 0) - (keys.qKey.isPressed ? 1 : 0),
            (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0));
        pose.position += Quaternion.Euler(0,pose.eulerAngles.y,0) * movement.normalized * (2 * Time.unscaledDeltaTime);
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(10,10,580,64),
            "1 Near   2 Mid   3 Ground   4 Pilot     |     5 Eye level   6 Slope   7 Overhead\n" +
            "8 Downhill   9 Blade close-up\nWASD move   Q/E down/up   Right mouse look   L toggle shade");
    }
}
