using UnityEngine;

/// <summary>Independent blue accents using the same placement policy as the approved poppies.</summary>
[System.Serializable]
public sealed class CornflowerSettings : MeadowFlowerPatchSettings
{
    public CornflowerSettings()
    {
        patchCellSize = 34f;
        patchSpawnChance = .34f;
        patchNoiseScale = .022f;
        patchNoiseThreshold = .47f;
        minPlantsPerPatch = 24;
        maxPlantsPerPatch = 56;
        patchRadiusRange = new Vector2(3f, 6.5f);
        uniformScaleRange = new Vector2(.46f, .62f);
        seedOffset = 86000;
        darkTint = new Color(.87f, .92f, 1f, 1f);
        lightTint = Color.white;
    }
}
