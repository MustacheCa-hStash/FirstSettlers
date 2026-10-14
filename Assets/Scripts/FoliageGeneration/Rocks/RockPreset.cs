using UnityEngine;

/// <summary>Authoring only. Generated visuals read meshes directly; bodies contain only collision/query components.</summary>
[DisallowMultipleComponent]
public sealed class RockPreset : MonoBehaviour
{
    public Mesh collisionHull;
    public Mesh distantMesh;
    [Min(1)] public float lodDistance = 60;
    [Min(0)] public float shadowDistance = 140;
    [Range(0f, 1f)] public float groundEmbed = .08f;
}
