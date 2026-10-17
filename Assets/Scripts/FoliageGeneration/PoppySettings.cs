using UnityEngine;

/// <summary>Scarlet drifts with more plants and the approved relative spacing.</summary>
[System.Serializable]
public sealed class PoppySettings : MeadowFlowerPatchSettings
{
    public PoppySettings()
    {
        minPlantsPerPatch = 20;
        maxPlantsPerPatch = 48;
        // Twice the population needs twice the area to retain the existing spacing.
        patchRadiusRange = new Vector2(3f, 6f) * Mathf.Sqrt(2f);
    }
}
