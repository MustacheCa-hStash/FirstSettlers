using UnityEngine;

[CreateAssetMenu(menuName = "Foliage/Berry Bush Definition")]
public class BerryBushDefinition : ScriptableObject
{
    public WorldFeatureVariant variant;
    public GameObject fruitVisualPrefab;
    public int harvestAmount = 1;
    public int regrowCycleLength = 1;
    public int maxHP = 3;

    private void OnValidate()
    {
        harvestAmount = Mathf.Max(1, harvestAmount);
        regrowCycleLength = Mathf.Max(1, regrowCycleLength);
        maxHP = Mathf.Max(1, maxHP);
    }
}
