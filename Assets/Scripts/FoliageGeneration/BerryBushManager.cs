using System.Collections.Generic;
using UnityEngine;

public class BerryBushManager : MonoBehaviour
{
    private static BerryBushManager instance;

    [SerializeField] private BerryBushDefinition[] definitions;

    private readonly Dictionary<WorldFeatureVariant, BerryBushDefinition> definitionsByVariant = new();
    private readonly Dictionary<ulong, BerryBushState> modifiedStatesById = new();
    private readonly List<BerryBushRuntime> activeBushes = new();
    private GameTimeManager timeManager;

    public static BerryBushManager Instance
    {
        get
        {
            if (instance != null)
                return instance;

            instance = FindFirstObjectByType<BerryBushManager>();

            if (instance == null)
                Debug.LogError("BerryBushManager is required in the scene before berry bushes are generated.");

            return instance;
        }
    }

    public long CurrentBerryCycle { get; private set; }
    public IReadOnlyDictionary<ulong, BerryBushState> ModifiedStates => modifiedStatesById;
    public int ModifiedStateCount => modifiedStatesById.Count;
    public int ActiveBushCount => activeBushes.Count;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        RebuildDefinitionLookup();
    }

    private void OnEnable()
    {
        TrySubscribeToTimeManager();
    }

    private void Start()
    {
        TrySubscribeToTimeManager();
    }

    private void OnDisable()
    {
        if (timeManager != null)
            timeManager.DayAdvanced -= HandleDayAdvanced;

        timeManager = null;
    }

    private void OnValidate()
    {
        RebuildDefinitionLookup();
    }

    public BerryBushDefinition GetDefinition(WorldFeatureVariant variant)
    {
        if (definitionsByVariant.Count == 0)
            RebuildDefinitionLookup();

        definitionsByVariant.TryGetValue(variant, out BerryBushDefinition definition);
        return definition;
    }

    public void Register(BerryBushRuntime bush)
    {
        if (bush == null || activeBushes.Contains(bush))
            return;

        activeBushes.Add(bush);
    }

    public void Unregister(BerryBushRuntime bush)
    {
        if (bush == null)
            return;

        activeBushes.Remove(bush);
    }

    public BerryBushState GetStateOrDefault(ulong bushId, BerryBushDefinition definition)
    {
        if (modifiedStatesById.TryGetValue(bushId, out BerryBushState state))
            return state;

        return CreateDefaultState(definition);
    }

    public void StoreState(ulong bushId, BerryBushState state, BerryBushDefinition definition)
    {
        if (state.IsDefault(definition))
        {
            modifiedStatesById.Remove(bushId);
            return;
        }

        modifiedStatesById[bushId] = state;
    }

    public BerryBushState ReconcileStateForCycle(
        ulong bushId,
        BerryBushDefinition definition,
        long currentBerryCycle)
    {
        BerryBushState state = GetStateOrDefault(bushId, definition);

        if (!state.isBroken &&
            !state.fruitAvailable &&
            state.nextRegrowCycle > 0 &&
            currentBerryCycle >= state.nextRegrowCycle)
        {
            state.fruitAvailable = true;
            state.nextRegrowCycle = 0;
            StoreState(bushId, state, definition);
        }

        return state;
    }

    public void NotifyBerryCycleAdvanced(long currentBerryCycle)
    {
        NotifyBerryRegrowCycleAdvanced(currentBerryCycle);
    }

    public void NotifyBerryRegrowCycleAdvanced(long currentBerryCycle)
    {
        CurrentBerryCycle = currentBerryCycle;

        for (int i = activeBushes.Count - 1; i >= 0; i--)
        {
            BerryBushRuntime bush = activeBushes[i];

            if (bush == null)
            {
                activeBushes.RemoveAt(i);
                continue;
            }

            bush.ReconcileWithCycle(currentBerryCycle);
        }
    }

    public BerryBushState CreateDefaultState(BerryBushDefinition definition)
    {
        if (definition == null)
        {
            Debug.LogError("Cannot create a default berry bush state without a BerryBushDefinition.");
            return new BerryBushState(false, true, 0, 0);
        }

        return new BerryBushState(
            false,
            true,
            0,
            definition.maxHP);
    }

    private void RebuildDefinitionLookup()
    {
        definitionsByVariant.Clear();

        if (definitions == null)
            return;

        for (int i = 0; i < definitions.Length; i++)
        {
            BerryBushDefinition definition = definitions[i];

            if (definition == null)
                continue;

            definitionsByVariant[definition.variant] = definition;
        }
    }

    private void TrySubscribeToTimeManager()
    {
        if (timeManager != null)
            return;

        timeManager = GameTimeManager.Instance;

        if (timeManager == null)
            return;

        timeManager.DayAdvanced += HandleDayAdvanced;
        NotifyBerryRegrowCycleAdvanced(timeManager.CurrentSnapshot.Day);
    }

    private void HandleDayAdvanced(GameTimeSnapshot snapshot)
    {
        NotifyBerryRegrowCycleAdvanced(snapshot.Day);
    }
}
