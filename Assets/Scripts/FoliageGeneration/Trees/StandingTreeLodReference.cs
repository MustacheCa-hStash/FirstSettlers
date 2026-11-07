using UnityEngine;

// Optional per-prefab calibration for the instanced renderer's projected size.
[DisallowMultipleComponent]
public sealed class StandingTreeLodReference : MonoBehaviour
{
    [SerializeField, Min(.01f)] private float scale = 1f;

    public float Scale => Mathf.Max(.01f, scale);
}
