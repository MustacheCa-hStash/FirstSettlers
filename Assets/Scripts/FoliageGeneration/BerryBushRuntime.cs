using UnityEngine;

public class BerryBushRuntime : MonoBehaviour
{
    [SerializeField] private BerryBushDefinition definitionOverride;
    [SerializeField] private Transform fruitVisualParent;
    [SerializeField] private GameObject fruitVisualRoot;

    private BerryBushManager manager;
    private BerryBushInstanceData instanceData;
    private BerryBushDefinition definition;
    private BerryBushState state;
    private bool initialized;

    public ulong BushId => instanceData.id;
    public WorldFeatureVariant Variant => instanceData.variant;
    public bool IsBroken => initialized && state.isBroken;
    public bool HasFruit => initialized && !state.isBroken && state.fruitAvailable;
    public int HarvestAmount => definition != null ? definition.harvestAmount : 0;

    public void Initialize(BerryBushInstanceData instanceData, BerryBushManager manager)
    {
        this.instanceData = instanceData;
        this.manager = manager != null ? manager : BerryBushManager.Instance;

        if (this.manager == null)
        {
            Debug.LogError($"Berry bush {instanceData.id} cannot initialize because no BerryBushManager exists in the scene.");
            enabled = false;
            return;
        }

        definition = definitionOverride != null
            ? definitionOverride
            : this.manager.GetDefinition(instanceData.variant);

        if (definition == null)
        {
            Debug.LogError($"Berry bush {instanceData.id} cannot initialize because no definition exists for {instanceData.variant}.");
            enabled = false;
            return;
        }

        state = this.manager.ReconcileStateForCycle(
            instanceData.id,
            definition,
            this.manager.CurrentBerryCycle);

        initialized = true;
        EnsureFruitVisual();
        RefreshFruitVisual();
        this.manager.Register(this);
    }

    public bool HarvestAtCurrentCycle()
    {
        if (manager == null)
            return false;

        return HarvestAtCycle(manager.CurrentBerryCycle);
    }

    public bool HarvestAtCycle(long currentBerryCycle)
    {
        if (!initialized || state.isBroken || !state.fruitAvailable)
            return false;

        state.fruitAvailable = false;
        state.nextRegrowCycle = currentBerryCycle + GetRegrowCycleLength();
        manager.StoreState(instanceData.id, state, definition);
        RefreshFruitVisual();
        return true;
    }

    public void ApplyDamage(int damage)
    {
        if (!initialized || state.isBroken || damage <= 0)
            return;

        state.hp -= damage;

        if (state.hp <= 0)
        {
            Break();
            return;
        }

        manager.StoreState(instanceData.id, state, definition);
    }

    public void Break()
    {
        if (!initialized || state.isBroken)
            return;

        state.isBroken = true;
        state.fruitAvailable = false;
        state.nextRegrowCycle = 0;
        state.hp = 0;
        manager.StoreState(instanceData.id, state, definition);
        RefreshFruitVisual();
    }

    public void ReconcileWithCycle(long currentBerryCycle)
    {
        if (!initialized)
            return;

        state = manager.ReconcileStateForCycle(
            instanceData.id,
            definition,
            currentBerryCycle);

        RefreshFruitVisual();
    }

    private void OnDestroy()
    {
        if (manager != null)
            manager.Unregister(this);
    }

    private int GetRegrowCycleLength()
    {
        if (definition == null)
            return 1;

        return Mathf.Max(1, definition.regrowCycleLength);
    }

    private void EnsureFruitVisual()
    {
        if (fruitVisualRoot != null || definition == null || definition.fruitVisualPrefab == null)
            return;

        Transform parent = fruitVisualParent != null ? fruitVisualParent : transform;
        fruitVisualRoot = Instantiate(definition.fruitVisualPrefab, parent, false);
    }

    private void RefreshFruitVisual()
    {
        if (fruitVisualRoot == null)
            return;

        fruitVisualRoot.SetActive(!state.isBroken && state.fruitAvailable);
    }
}
