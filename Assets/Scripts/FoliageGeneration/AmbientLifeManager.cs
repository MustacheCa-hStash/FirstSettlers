using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

// Chunk records own habitat and daily residents. Only the nearby residents have live flight state.
public sealed class AmbientLifeManager : IDisposable
{
    private static readonly ProfilerMarker ReconcileMarker = new("FS.Butterflies.Reconcile");
    private static readonly ProfilerMarker FlightMarker = new("FS.Butterflies.Flight");
    private static readonly ProfilerMarker DrawMarker = new("FS.Butterflies.Draw");
    private static readonly ProfilerMarker BeeReconcileMarker = new("FS.Bees.Reconcile");
    private static readonly ProfilerMarker BeeFlightMarker = new("FS.Bees.Flight");
    private static readonly ProfilerMarker BeeDrawMarker = new("FS.Bees.Draw");
    private static readonly int ButterflyInstanceDataId = Shader.PropertyToID("_ButterflyInstanceData");
    private static readonly int WingHingeXId = Shader.PropertyToID("_WingHingeX");
    private static readonly int FlapFrequencyId = Shader.PropertyToID("_FlapFrequency");
    private static readonly int FlapClosedAngleId = Shader.PropertyToID("_FlapClosedAngle");
    private static readonly int OrangeId = Shader.PropertyToID("_Orange");
    private static readonly int YellowId = Shader.PropertyToID("_Yellow");
    private static readonly int PinkId = Shader.PropertyToID("_Pink");
    private static readonly int PurpleId = Shader.PropertyToID("_Purple");
    private static readonly int BlueId = Shader.PropertyToID("_Blue");

    private readonly ButterflySettings settings;
    private readonly BeeSettings beeSettings;
    private readonly FlowerSettings flowerSettings;
    private readonly int worldSeed;
    private readonly int chunkSize;
    private readonly float worldScale;
    private readonly float chunkWorldSize;
    private readonly float terrainHeightScale;
    private readonly float waterSurfaceY;
    private readonly Dictionary<ChunkCoord, ActiveChunk> activeChunks = new();
    private readonly HashSet<ChunkCoord> desiredChunks = new();
    private readonly List<ChunkCoord> staleChunks = new();
    private readonly List<ChunkCoord> ringOffsets = new();
    private readonly Dictionary<long, HotspotCell> hotspotCells = new();
    private readonly List<HotspotCell> sortedCells = new();
    private readonly List<PendingTransfer> pendingTransfers = new();
    private readonly HashSet<uint> selectedIds = new();
    private readonly Matrix4x4[] drawMatrices = new Matrix4x4[1023];
    private readonly Vector4[] drawInstanceData = new Vector4[1023];
    private readonly MaterialPropertyBlock drawProperties = new();

    private int cachedRingRadius = -1;
    private int cachedSettingsSignature = int.MinValue;
    private int lastDay = int.MinValue;
    private bool lastDaylight;
    private GameObject resolvedPrefab;
    private Mesh renderMesh;
    private Material renderMaterial;
    private Matrix4x4 meshLocalMatrix = Matrix4x4.identity;
    private int renderLayer;
    private bool warnedInvalidPrefab;
    private bool warnedUnsupportedInstancing;
    private float flapFrequency = 5.5f;
    private float cachedBeeFlapFrequency = float.NaN;
    private float cachedBeeWingAngle = float.NaN;

    private sealed class ActiveChunk
    {
        public ChunkButterflyData Data;
        public int Day;
        public int FlowersRevision;
        public readonly List<LiveButterfly> Butterflies = new();
    }

    private struct HotspotCell
    {
        public long Key;
        public Vector3 Position;
        public int FlowerCount;
        public uint RepresentativeRank;
    }

    private enum FlightState : byte { Flying, Hovering, Landing, Resting }

    private struct LiveButterfly
    {
        public uint Id;
        public uint RandomState;
        public Vector3 Home;
        public Vector3 Position;
        public Vector3 Facing;
        public Vector3 TravelDirection;
        public Vector3 SafeDirection;
        public Vector3 Target;
        public float PreferredHeight;
        public float GlideDistance;
        public float HoverBaseY;
        public float HoverStartAge;
        public float Scale;
        public float TravelSpeed;
        public float BankDegrees;
        public float Age;
        public float WingPhase;
        public float RestBlend;
        public float VisualPitchDegrees;
        public float VerticalVelocity;
        public float DecisionTimer;
        public float PauseTimer;
        public FlightState State;
    }

    private struct PendingTransfer
    {
        public ChunkCoord Source;
        public ChunkCoord Destination;
        public LiveButterfly Butterfly;
    }

    public AmbientLifeManager(ButterflySettings settings, FlowerSettings flowerSettings,
        int worldSeed, int chunkSize, float worldScale,
        float meshHeightMultiplier, float waterSurfaceY)
    {
        this.settings = settings;
        beeSettings = settings as BeeSettings;
        this.flowerSettings = flowerSettings;
        this.worldSeed = worldSeed;
        this.chunkSize = chunkSize;
        this.worldScale = Mathf.Max(0.0001f, worldScale);
        chunkWorldSize = chunkSize * this.worldScale;
        terrainHeightScale = meshHeightMultiplier * this.worldScale;
        this.waterSurfaceY = waterSurfaceY;
    }

    public void Update(ChunkManager manager, ChunkCoord viewerCoord, Camera camera, float deltaTime)
    {
        if (settings == null || !settings.enabled || flowerSettings == null ||
            !flowerSettings.enableFlowers)
        {
            activeChunks.Clear();
            return;
        }

        ResolvePrefab();
        UpdateBeeMaterial();
        GameTimeManager clock = GameTimeManager.Instance;
        GameTimeSnapshot time = clock != null ? clock.CurrentSnapshot : default;
        int day = clock != null ? time.Day : 0;
        bool daylight = clock == null || time.IsDaylight;
        if (day != lastDay || daylight != lastDaylight)
        {
            activeChunks.Clear();
            lastDay = day;
            lastDaylight = daylight;
        }
        if (!daylight)
            return;

        int signature = GetSettingsSignature();
        if (signature != cachedSettingsSignature)
        {
            activeChunks.Clear();
            cachedSettingsSignature = signature;
        }

        using ((beeSettings != null ? BeeReconcileMarker : ReconcileMarker).Auto())
            Reconcile(manager, viewerCoord, day, clock != null ? time.TotalGameMinutes : 0L);

        float step = Mathf.Clamp(deltaTime, 0f, 0.1f);
        if (step > 0f)
        {
            using ((beeSettings != null ? BeeFlightMarker : FlightMarker).Auto())
            {
                pendingTransfers.Clear();
                foreach (KeyValuePair<ChunkCoord, ActiveChunk> entry in activeChunks)
                {
                    ActiveChunk chunk = entry.Value;
                    for (int i = 0; i < chunk.Butterflies.Count; i++)
                    {
                        LiveButterfly butterfly = chunk.Butterflies[i];
                        Tick(manager, ref butterfly, step);
                        AdvanceVisuals(ref butterfly, step);
                        chunk.Butterflies[i] = butterfly;
                        ChunkCoord positionChunk = WorldToChunk(butterfly.Position);
                        if (!positionChunk.Equals(entry.Key))
                            pendingTransfers.Add(new PendingTransfer
                            {
                                Source = entry.Key, Destination = positionChunk, Butterfly = butterfly
                            });
                    }
                }
                ProcessTransfers(manager, viewerCoord, day);
            }
        }

        if (renderMesh != null && renderMaterial != null)
        {
            if (SystemInfo.supportsInstancing)
            {
                using ((beeSettings != null ? BeeDrawMarker : DrawMarker).Auto())
                    Draw(camera);
            }
            else if (!warnedUnsupportedInstancing)
            {
                warnedUnsupportedInstancing = true;
                Debug.LogWarning($"{(beeSettings != null ? "Bee" : "Butterfly")} instancing is unavailable on this graphics device.");
            }
        }
    }

    private void Reconcile(ChunkManager manager, ChunkCoord viewerCoord, int day, long gameMinute)
    {
        int radius = Mathf.Min(Mathf.Max(0, settings.activeRingRadius),
            Mathf.Max(0, flowerSettings.activeRingRadius));
        if (radius != cachedRingRadius)
            RebuildRingOffsets(radius);

        desiredChunks.Clear();
        int remaining = Mathf.Max(1, settings.maxActive);
        int habitatBuildsRemaining = 1;
        for (int i = 0; i < ringOffsets.Count && remaining > 0; i++)
        {
            ChunkCoord offset = ringOffsets[i];
            ChunkCoord coord = new(viewerCoord.x + offset.x, viewerCoord.z + offset.z);
            ChunkRecord record = manager.GetChunkRecord(coord);
            if (record == null || !record.HasTerrainData || record.DominantBiome != BiomeType.Grassland || record.ActiveRuntime == null ||
                !record.ActiveRuntime.IsVisible || !record.ActiveRuntime.HasTerrainMesh || record.FoliageData == null ||
                !record.FoliageData.flowersGenerated)
                continue;

            ChunkButterflyData data = EnsureChunkData(manager, record, day, ref habitatBuildsRemaining);
            if (data == null)
                continue;
            int count = Mathf.Min(data.Residents.Count, remaining);
            if (count == 0)
                continue;

            desiredChunks.Add(coord);
            if (!activeChunks.TryGetValue(coord, out ActiveChunk active))
            {
                active = new ActiveChunk();
                activeChunks.Add(coord, active);
            }
            Synchronize(manager, active, data, count, day, gameMinute);
            remaining -= count;
        }

        staleChunks.Clear();
        foreach (ChunkCoord coord in activeChunks.Keys)
            if (!desiredChunks.Contains(coord)) staleChunks.Add(coord);
        for (int i = 0; i < staleChunks.Count; i++)
            activeChunks.Remove(staleChunks[i]);
    }

    private void RebuildRingOffsets(int radius)
    {
        ringOffsets.Clear();
        for (int x = -radius; x <= radius; x++)
            for (int z = -radius; z <= radius; z++)
                if (x * x + z * z <= radius * radius)
                    ringOffsets.Add(new ChunkCoord(x, z));
        ringOffsets.Sort((a, b) =>
        {
            int distance = (a.x * a.x + a.z * a.z).CompareTo(b.x * b.x + b.z * b.z);
            if (distance != 0) return distance;
            int xOrder = a.x.CompareTo(b.x);
            return xOrder != 0 ? xOrder : a.z.CompareTo(b.z);
        });
        cachedRingRadius = radius;
    }

    private ChunkButterflyData EnsureChunkData(ChunkManager manager, ChunkRecord record, int day,
        ref int habitatBuildsRemaining)
    {
        ChunkButterflyData data = GetChunkData(record);
        if (data == null)
        {
            data = new ChunkButterflyData();
            if (beeSettings != null) record.BeeData = data;
            else record.ButterflyData = data;
        }

        ChunkFoliageData foliage = record.FoliageData;
        bool habitatChanged = !ReferenceEquals(data.FlowerSource, foliage) ||
            !ReferenceEquals(data.TerrainSource, record.HeightMap) ||
            data.FlowersRevision != foliage.FlowersRevision || data.SettingsSignature != cachedSettingsSignature;
        if (habitatChanged)
        {
            if (habitatBuildsRemaining <= 0)
                return null;
            habitatBuildsRemaining--;
            BuildHotspots(manager, record, data);
            data.FlowerSource = foliage;
            data.TerrainSource = record.HeightMap;
            data.FlowersRevision = foliage.FlowersRevision;
            data.SettingsSignature = cachedSettingsSignature;
        }
        if (habitatChanged || data.RosterDay != day)
            BuildRoster(record.ChunkCoord, data, day);
        return data;
    }

    private void BuildHotspots(ChunkManager manager, ChunkRecord record, ChunkButterflyData data)
    {
        data.Hotspots.Clear();
        hotspotCells.Clear();
        sortedCells.Clear();

        float half = chunkWorldSize * 0.5f;
        float cellSize = Mathf.Max(0.5f, settings.hotspotCellSize);
        ChunkCoord coord = record.ChunkCoord;
        float centerX = (coord.x + 0.5f) * chunkWorldSize;
        float centerZ = (coord.z + 0.5f) * chunkWorldSize;
        List<FlowerInstanceData> flowers = record.FoliageData.flowerInstances;
        // Sampling at most 512 flowers bounds main-thread work even for dense meadow chunks.
        int stride = Mathf.Max(1, Mathf.CeilToInt(flowers.Count / 512f));
        for (int i = 0; i < flowers.Count; i += stride)
        {
            Vector3 local = flowers[i].localPosition;
            if (local.x < -half || local.x >= half || local.z < -half || local.z >= half)
                continue;
            float worldX = centerX + local.x;
            float worldZ = centerZ + local.z;
            if (!TrySampleDryGround(manager, worldX, worldZ, out float groundY))
                continue;

            int cellX = Mathf.FloorToInt((local.x + half) / cellSize);
            int cellZ = Mathf.FloorToInt((local.z + half) / cellSize);
            long key = ((long)cellX << 32) | (uint)cellZ;
            uint rank = Mix((uint)i + 1u);
            if (!hotspotCells.TryGetValue(key, out HotspotCell cell))
                cell = new HotspotCell { Key = key, RepresentativeRank = uint.MaxValue };
            cell.FlowerCount++;
            if (rank < cell.RepresentativeRank)
            {
                cell.RepresentativeRank = rank;
                cell.Position = new Vector3(worldX, groundY, worldZ);
            }
            hotspotCells[key] = cell;
        }

        foreach (HotspotCell cell in hotspotCells.Values)
            sortedCells.Add(cell);
        sortedCells.Sort((a, b) =>
        {
            int countOrder = b.FlowerCount.CompareTo(a.FlowerCount);
            return countOrder != 0 ? countOrder : a.Key.CompareTo(b.Key);
        });
        int limit = Mathf.Min(Mathf.Max(1, settings.maxHotspotsPerChunk), sortedCells.Count);
        for (int i = 0; i < limit; i++)
            data.Hotspots.Add(new ButterflyHotspot(sortedCells[i].Position, sortedCells[i].FlowerCount));
    }

    private void BuildRoster(ChunkCoord coord, ChunkButterflyData data, int day)
    {
        GenerateDailyRoster(settings, worldSeed, coord, data, day);
    }

    public static void GenerateDailyRoster(ButterflySettings settings, int worldSeed,
        ChunkCoord coord, ChunkButterflyData data, int day)
    {
        if (data.RosterDay != day)
        {
            data.Migrants.Clear();
            data.DepartedIds.Clear();
        }
        data.Residents.Clear();
        data.RosterDay = day;
        float habitat = Mathf.Clamp01(data.Hotspots.Count / 3f);
        int maxResidents = Mathf.Max(1, settings.maxPerChunk);
        BeeSettings bees = settings as BeeSettings;
        int speciesSeed = bees == null ? worldSeed : worldSeed ^ unchecked((int)0x6c8e9cf5u);
        int slots = maxResidents;
        if (bees != null)
        {
            uint groupSeed = Hash(speciesSeed, coord.x, coord.z, day, -1);
            float groupChance = Mathf.Clamp01(settings.spawnChance) * habitat;
            slots = data.Hotspots.Count > 0 && ToUnitFloat(Mix(groupSeed ^ 0xa8476d35u)) < groupChance
                ? Mathf.Min(maxResidents, ToUnitFloat(Mix(groupSeed ^ 0x632be59bu)) <
                    Mathf.Clamp01(bees.threeBeeChance) ? 3 : 2)
                : 0;
        }
        for (int slot = 0; slot < slots; slot++)
        {
            uint id = Hash(speciesSeed, coord.x, coord.z, day, slot);
            float chance = Mathf.Clamp01(settings.spawnChance) * habitat / (1f + slot * 1.5f);
            if (data.DepartedIds.Contains(id) || data.Hotspots.Count == 0 ||
                (bees == null && ToUnitFloat(Mix(id ^ 0xa8476d35u)) >= chance))
                continue;
            int homeIndex = (int)(Mix(id ^ 0x632be59bu) % (uint)data.Hotspots.Count);
            float height = Mathf.Lerp(Mathf.Min(settings.preferredHeightMin, settings.preferredHeightMax),
                Mathf.Max(settings.preferredHeightMin, settings.preferredHeightMax),
                ToUnitFloat(Mix(id ^ 0x52ed70a1u)));
            float scale = Mathf.Lerp(Mathf.Min(settings.modelScaleMin, settings.modelScaleMax),
                Mathf.Max(settings.modelScaleMin, settings.modelScaleMax),
                ToUnitFloat(Mix(id ^ 0xf342be01u)));
            data.Residents.Add(new ButterflyResident(id, homeIndex,
                data.Hotspots[homeIndex].WorldPosition, height, scale));
        }
        data.Residents.AddRange(data.Migrants);
    }

    private void Synchronize(ChunkManager manager, ActiveChunk active, ChunkButterflyData data,
        int count, int day, long gameMinute)
    {
        active.Data = data;
        active.Day = day;
        active.FlowersRevision = data.FlowersRevision;
        selectedIds.Clear();
        for (int i = 0; i < count; i++)
            selectedIds.Add(data.Residents[i].Id);
        for (int i = active.Butterflies.Count - 1; i >= 0; i--)
            if (!selectedIds.Contains(active.Butterflies[i].Id))
                active.Butterflies.RemoveAt(i);
        for (int i = 0; i < count; i++)
        {
            ButterflyResident resident = data.Residents[i];
            bool present = false;
            for (int j = 0; j < active.Butterflies.Count; j++)
                if (active.Butterflies[j].Id == resident.Id) { present = true; break; }
            if (present) continue;
            Vector3 home = resident.HomeWorldPosition;
            // Coarse elapsed time changes the starting position without simulating absent residents.
            uint random = Mix(resident.Id ^ (uint)(gameMinute / 5L));
            if (random == 0) random = 1u;
            float angle = Next01(ref random) * Mathf.PI * 2f;
            float radius = Next01(ref random) * 2f;
            float x = home.x + Mathf.Cos(angle) * radius;
            float z = home.z + Mathf.Sin(angle) * radius;
            if (!TrySampleDryGround(manager, x, z, out float groundY) ||
                !IsDryPath(manager, home, new Vector3(x, 0f, z)))
            {
                x = home.x;
                z = home.z;
                groundY = home.y;
            }
            Vector3 forward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            LiveButterfly butterfly = new LiveButterfly
            {
                Id = resident.Id,
                RandomState = random,
                Home = home,
                Position = new Vector3(x, groundY + resident.PreferredHeight, z),
                Facing = forward,
                TravelDirection = forward,
                SafeDirection = forward,
                Target = home,
                PreferredHeight = resident.PreferredHeight,
                Scale = resident.Scale,
                TravelSpeed = Mathf.Max(0.1f, settings.flightSpeed) * 0.6f,
                WingPhase = ToUnitFloat(Mix(resident.Id ^ 0x85ebca6bu)) * Mathf.PI * 2f,
                VisualPitchDegrees = settings.flightPitchDegrees,
                DecisionTimer = 0f
            };
            ChooseTarget(manager, ref butterfly);
            active.Butterflies.Add(butterfly);
        }
    }

    private void Tick(ChunkManager manager, ref LiveButterfly butterfly, float dt)
    {
        butterfly.Age += dt;
        if (!TrySampleDryGround(manager, butterfly.Position.x, butterfly.Position.z, out float groundY))
        {
            butterfly.Position = butterfly.Home + Vector3.up * butterfly.PreferredHeight;
            butterfly.State = FlightState.Flying;
            butterfly.TravelSpeed = 0f;
            butterfly.BankDegrees = 0f;
            butterfly.VerticalVelocity = 0f;
            butterfly.DecisionTimer = 0f;
            return;
        }

        if (butterfly.State == FlightState.Resting)
        {
            butterfly.TravelSpeed = 0f;
            butterfly.BankDegrees = Mathf.MoveTowards(butterfly.BankDegrees, 0f, 45f * dt);
            butterfly.Position.y = RestingY(groundY);
            butterfly.PauseTimer -= dt;
            if (butterfly.PauseTimer <= 0f)
                ChooseTarget(manager, ref butterfly);
            return;
        }

        if (butterfly.State == FlightState.Hovering)
        {
            butterfly.TravelSpeed = 0f;
            butterfly.BankDegrees = Mathf.MoveTowards(butterfly.BankDegrees, 0f, 45f * dt);
            butterfly.PauseTimer -= dt;
            float hoverY = beeSettings != null
                ? Mathf.Max(butterfly.HoverBaseY +
                    Mathf.Sin((butterfly.Age - butterfly.HoverStartAge) * 2.1f) * 0.06f,
                    Mathf.Max(groundY, waterSurfaceY) + Mathf.Max(0.1f, settings.terrainClearance))
                : RestingY(groundY);
            float previousY = butterfly.Position.y;
            butterfly.Position.y = Mathf.MoveTowards(butterfly.Position.y, hoverY,
                Mathf.Max(0.1f, settings.verticalSpeed) * dt);
            butterfly.VerticalVelocity = (butterfly.Position.y - previousY) / dt;
            if (butterfly.PauseTimer <= 0f)
                ChooseTarget(manager, ref butterfly);
            return;
        }

        Vector3 toTarget = butterfly.Target - butterfly.Position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.36f)
        {
            if (butterfly.State == FlightState.Landing)
            {
                if (butterfly.Position.y <= RestingY(groundY) +
                    (beeSettings != null ? 0.02f : 0.08f))
                {
                    butterfly.Position.y = RestingY(groundY);
                    butterfly.TravelSpeed = 0f;
                    butterfly.State = FlightState.Resting;
                    float minRest = Mathf.Max(0.1f, settings.restDurationMin);
                    butterfly.PauseTimer = Mathf.Lerp(minRest,
                        Mathf.Max(minRest, settings.restDurationMax), Next01(ref butterfly.RandomState));
                    butterfly.VerticalVelocity = 0f;
                    return;
                }
                // Keep tracing a small arc around the target until the descent is complete.
            }
            else
            {
                butterfly.TravelSpeed = 0f;
                butterfly.State = FlightState.Hovering;
                butterfly.HoverBaseY = butterfly.Position.y;
                butterfly.HoverStartAge = butterfly.Age;
                butterfly.PauseTimer = beeSettings != null
                    ? Mathf.Lerp(0.4f, 0.9f, Next01(ref butterfly.RandomState))
                    : Mathf.Lerp(0.8f, 1.8f, Next01(ref butterfly.RandomState));
                return;
            }
        }

        butterfly.DecisionTimer -= dt;
        Vector3 desired = toTarget.sqrMagnitude > 0.0025f
            ? toTarget.normalized : butterfly.TravelDirection;
        if (butterfly.DecisionTimer <= 0f)
        {
            float wanderPhase = butterfly.Age * Mathf.Clamp(settings.wanderFrequency, 0.02f, 1f) *
                                Mathf.PI * 2f + ToUnitFloat(Mix(butterfly.Id ^ 0xc2b2ae35u)) *
                                Mathf.PI * 2f;
            float wanderAngle = Mathf.Sin(wanderPhase) * Mathf.Clamp(settings.wanderDegrees, 0f, 35f) *
                                Mathf.Clamp01((toTarget.magnitude - 0.6f) / 2f);
            desired = Quaternion.AngleAxis(wanderAngle, Vector3.up) * desired;
            butterfly.SafeDirection = FindDryDirection(manager, butterfly.Position, desired);
            butterfly.DecisionTimer = beeSettings != null
                ? Mathf.Lerp(0.28f, 0.45f, Next01(ref butterfly.RandomState))
                : Mathf.Lerp(0.18f, 0.32f, Next01(ref butterfly.RandomState));
            if (butterfly.SafeDirection.sqrMagnitude < 0.5f)
                ChooseTarget(manager, ref butterfly);
        }
        if (butterfly.SafeDirection.sqrMagnitude < 0.5f)
        {
            butterfly.TravelSpeed = 0f;
            return;
        }

        float turnRate = Mathf.Max(0.1f, settings.turnDegreesPerSecond);
        Vector3 previousDirection = butterfly.TravelDirection;
        butterfly.TravelDirection = Vector3.RotateTowards(previousDirection, butterfly.SafeDirection,
            Mathf.Deg2Rad * turnRate * dt, 0f).normalized;
        float turnFraction = Vector3.Cross(previousDirection, butterfly.TravelDirection).y /
                             (Mathf.Deg2Rad * turnRate * dt);
        float targetBank = -Mathf.Clamp(turnFraction, -1f, 1f) *
                           Mathf.Clamp(settings.turnBankDegrees, 0f, 20f);
        butterfly.BankDegrees = Mathf.Lerp(butterfly.BankDegrees, targetBank, Mathf.Clamp01(dt * 5f));
        float speed = Mathf.Max(0.1f, settings.flightSpeed);
        // Bees should already be nearly stopped when they enter the 0.6 m visit radius.
        float arrival = Mathf.Clamp01((toTarget.magnitude - 0.45f) / 1.5f);
        float desiredSpeed = speed * (beeSettings != null ? arrival : Mathf.Lerp(0.2f, 1f, arrival));
        butterfly.TravelSpeed = Mathf.MoveTowards(butterfly.TravelSpeed, desiredSpeed,
            Mathf.Max(0.1f, settings.flightAcceleration) * dt);
        Vector3 next = butterfly.Position + butterfly.TravelDirection * (butterfly.TravelSpeed * dt);
        if (!CanEnterChunk(manager, WorldToChunk(next)) ||
            !TrySampleDryGround(manager, next.x, next.z, out float nextGround))
        {
            // Momentum can briefly point toward shore. Redirect along the already checked dry heading.
            butterfly.DecisionTimer = 0f;
            butterfly.TravelDirection = butterfly.SafeDirection;
            butterfly.TravelSpeed = Mathf.Min(butterfly.TravelSpeed, speed * 0.6f);
            butterfly.BankDegrees = 0f;
            next = butterfly.Position + butterfly.TravelDirection * (butterfly.TravelSpeed * dt);
            if (!CanEnterChunk(manager, WorldToChunk(next)) ||
                !TrySampleDryGround(manager, next.x, next.z, out nextGround))
            {
                butterfly.TravelSpeed = 0f;
                butterfly.SafeDirection = Vector3.zero;
                return;
            }
        }

        butterfly.Facing = Vector3.RotateTowards(butterfly.Facing, butterfly.TravelDirection,
            Mathf.Deg2Rad * Mathf.Max(0.1f, settings.bodyTurnDegreesPerSecond) * dt, 0f).normalized;
        butterfly.Facing = Vector3.RotateTowards(butterfly.TravelDirection, butterfly.Facing,
            Mathf.Deg2Rad * Mathf.Clamp(settings.maxFacingLagDegrees, 0f, 60f), 0f).normalized;
        butterfly.Position.x = next.x;
        butterfly.Position.z = next.z;
        float aheadX = next.x + butterfly.TravelDirection.x * Mathf.Max(0.5f, settings.waterLookAhead);
        float aheadZ = next.z + butterfly.TravelDirection.z * Mathf.Max(0.5f, settings.waterLookAhead);
        if (!TrySampleDryGround(manager, aheadX, aheadZ, out float aheadGround))
            aheadGround = nextGround;
        float desiredY;
        if (butterfly.State == FlightState.Landing)
        {
            float remaining = Vector2.Distance(new Vector2(next.x, next.z),
                new Vector2(butterfly.Target.x, butterfly.Target.z));
            float glide = Mathf.Clamp01((butterfly.GlideDistance - remaining) /
                Mathf.Max(0.1f, butterfly.GlideDistance - 0.6f));
            glide = glide * glide * (3f - 2f * glide);
            desiredY = Mathf.Lerp(nextGround + butterfly.PreferredHeight,
                RestingY(nextGround), glide);
        }
        else
            desiredY = Mathf.Max(nextGround, aheadGround) + butterfly.PreferredHeight +
                       Mathf.Sin(butterfly.Age * 3.1f) * 0.15f;
        float previousFlightY = butterfly.Position.y;
        butterfly.Position.y = Mathf.MoveTowards(butterfly.Position.y, desiredY,
            Mathf.Max(0.1f, settings.verticalSpeed) * dt);
        float clearance = Mathf.Max(0.1f, settings.terrainClearance);
        butterfly.Position.y = Mathf.Max(butterfly.Position.y,
            Mathf.Max(nextGround + clearance, waterSurfaceY + clearance));
        float verticalSpeed = (butterfly.Position.y - previousFlightY) / dt;
        butterfly.VerticalVelocity = Mathf.Lerp(butterfly.VerticalVelocity, verticalSpeed,
            Mathf.Clamp01(dt * 4f));
    }

    private void AdvanceVisuals(ref LiveButterfly butterfly, float dt)
    {
        float resting = butterfly.State == FlightState.Resting ? 1f : 0f;
        if (beeSettings != null)
            butterfly.RestBlend = Mathf.MoveTowards(butterfly.RestBlend, resting, 2.5f * dt);
        else
            butterfly.RestBlend = resting;

        float flapRate = Mathf.Lerp(1f,
            Mathf.Clamp(settings.restingFlapRate, 0.05f, 1f), butterfly.RestBlend);
        float speedVariation = Mathf.Lerp(0.88f, 1.12f,
            ToUnitFloat(Mix(Mix(butterfly.Id ^ 0x85ebca6bu))));
        butterfly.WingPhase = Mathf.Repeat(butterfly.WingPhase +
            dt * flapFrequency * speedVariation * flapRate * Mathf.PI * 2f, Mathf.PI * 2f);

        if (beeSettings == null) return;
        float targetPitch = 0f;
        if (butterfly.State == FlightState.Flying || butterfly.State == FlightState.Landing)
        {
            float climb = Mathf.Clamp(butterfly.VerticalVelocity /
                Mathf.Max(0.1f, settings.verticalSpeed), -1f, 1f);
            targetPitch = settings.flightPitchDegrees + climb * settings.climbPitchDegrees;
        }
        else if (butterfly.State == FlightState.Hovering)
            targetPitch = settings.flightPitchDegrees * 0.5f;
        butterfly.VisualPitchDegrees = Mathf.MoveTowards(butterfly.VisualPitchDegrees,
            Mathf.Clamp(targetPitch, -45f, 45f), 40f * dt);
    }

    private void ChooseTarget(ChunkManager manager, ref LiveButterfly butterfly)
    {
        butterfly.State = FlightState.Flying;
        float legRadius = Mathf.Max(0.5f, settings.territoryRadius);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 candidate = default;
            bool flowerTarget = false;
            bool beeFlowerRoute = beeSettings != null && attempt == 0 &&
                Next01(ref butterfly.RandomState) < Mathf.Clamp01(beeSettings.flowerTargetChance) &&
                TryChooseBeeFlowerTarget(manager, butterfly.Position, legRadius, out candidate,
                    out flowerTarget);
            if (!beeFlowerRoute)
            {
                ChunkCoord currentChunk = WorldToChunk(butterfly.Position);
                ChunkCoord hotspotChunk = new(currentChunk.x + (int)(Next01(ref butterfly.RandomState) * 3f) - 1,
                    currentChunk.z + (int)(Next01(ref butterfly.RandomState) * 3f) - 1);
                ChunkButterflyData hotspotData = GetChunkData(manager.GetChunkRecord(hotspotChunk));
                flowerTarget = beeSettings == null && (attempt & 1) == 0 &&
                    hotspotData != null && hotspotData.Hotspots.Count > 0;
                if (flowerTarget)
                {
                    int index = (int)(Next01(ref butterfly.RandomState) * hotspotData.Hotspots.Count);
                    candidate = hotspotData.Hotspots[Mathf.Min(index, hotspotData.Hotspots.Count - 1)].WorldPosition;
                }
                else
                {
                    float heading = Mathf.Atan2(butterfly.TravelDirection.z, butterfly.TravelDirection.x);
                    float angle = heading + (Next01(ref butterfly.RandomState) * 2f - 1f) * 1.9f;
                    float radius = Mathf.Lerp(1f, legRadius, Next01(ref butterfly.RandomState));
                    candidate = butterfly.Position + new Vector3(Mathf.Cos(angle) * radius, 0f,
                        Mathf.Sin(angle) * radius);
                }
            }
            Vector2 fromCurrent = new Vector2(candidate.x - butterfly.Position.x, candidate.z - butterfly.Position.z);
            if (fromCurrent.sqrMagnitude > legRadius * legRadius || fromCurrent.sqrMagnitude < 1f ||
                !TrySampleDryGround(manager, candidate.x, candidate.z, out float groundY) ||
                !IsDryPath(manager, butterfly.Position, candidate))
                continue;
            float distance = fromCurrent.magnitude;
            bool landing = distance >= 2f && (beeSettings == null || flowerTarget) &&
                           Next01(ref butterfly.RandomState) < Mathf.Clamp01(settings.landingChance);
            butterfly.State = landing ? FlightState.Landing : FlightState.Flying;
            float targetY = landing ? RestingY(groundY) : groundY + butterfly.PreferredHeight;
            butterfly.Target = new Vector3(candidate.x, targetY, candidate.z);
            if (landing)
            {
                float heightToLose = Mathf.Max(0f, butterfly.Position.y - targetY);
                float requiredDistance = heightToLose * Mathf.Max(0.1f, settings.flightSpeed) /
                                         Mathf.Max(0.1f, settings.verticalSpeed) + 0.7f;
                butterfly.GlideDistance = Mathf.Min(distance,
                    Mathf.Max(settings.landingApproachDistance, requiredDistance));
            }
            butterfly.DecisionTimer = 0f;
            return;
        }
        butterfly.Target = butterfly.Position;
        butterfly.DecisionTimer = 0f;
    }

    private bool TryChooseBeeFlowerTarget(ChunkManager manager, Vector3 position,
        float legRadius, out Vector3 candidate, out bool reachesFlower)
    {
        candidate = default;
        reachesFlower = false;
        ChunkCoord center = WorldToChunk(position);
        float bestDistanceSq = legRadius * legRadius * 9f;
        bool found = false;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                ChunkButterflyData data = GetChunkData(manager.GetChunkRecord(
                    new ChunkCoord(center.x + dx, center.z + dz)));
                if (data == null) continue;
                for (int i = 0; i < data.Hotspots.Count; i++)
                {
                    Vector3 flower = data.Hotspots[i].WorldPosition;
                    float distanceSq = (new Vector2(flower.x - position.x,
                        flower.z - position.z)).sqrMagnitude;
                    if (distanceSq < 1f || distanceSq >= bestDistanceSq) continue;
                    bestDistanceSq = distanceSq;
                    candidate = flower;
                    found = true;
                }
            }
        if (!found) return false;
        reachesFlower = bestDistanceSq <= legRadius * legRadius;
        if (!reachesFlower)
        {
            Vector3 delta = candidate - position;
            delta.y = 0f;
            candidate = position + delta.normalized * (legRadius * 0.9f);
        }
        return true;
    }

    private Vector3 FindDryDirection(ChunkManager manager, Vector3 position, Vector3 preferred)
    {
        float lookAhead = Mathf.Max(0.5f, settings.waterLookAhead);
        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            Vector3 direction = Quaternion.AngleAxis(i * 45f, Vector3.up) * preferred;
            Vector3 probe = position + direction * lookAhead;
            if (!IsDryPath(manager, position, probe))
                continue;
            float score = Vector3.Dot(direction, preferred);
            if (score > bestScore)
            {
                bestScore = score;
                best = direction;
            }
        }
        return best;
    }

    private float RestingY(float groundY)
    {
        float clearance = Mathf.Max(0.1f, settings.terrainClearance);
        float visitHeight = beeSettings != null ? beeSettings.flowerVisitHeight : 0.2f;
        return Mathf.Max(groundY + Mathf.Max(visitHeight, clearance), waterSurfaceY + clearance);
    }

    private ChunkButterflyData GetChunkData(ChunkRecord record) => record == null ? null :
        beeSettings != null ? record.BeeData : record.ButterflyData;

    private bool IsDryPath(ChunkManager manager, Vector3 from, Vector3 to)
    {
        float distance = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(0.15f, worldScale * 0.5f)));
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float x = Mathf.Lerp(from.x, to.x, t);
            float z = Mathf.Lerp(from.z, to.z, t);
            if (!CanEnterChunk(manager, WorldToChunk(x, z)) ||
                !TrySampleDryGround(manager, x, z, out _))
                return false;
        }
        return true;
    }

    private ChunkCoord WorldToChunk(Vector3 position) => WorldToChunk(position.x, position.z);

    private ChunkCoord WorldToChunk(float x, float z) =>
        new(Mathf.FloorToInt(x / chunkWorldSize), Mathf.FloorToInt(z / chunkWorldSize));

    private static bool CanEnterChunk(ChunkManager manager, ChunkCoord coord)
    {
        ChunkRecord record = manager.GetChunkRecord(coord);
        return record != null && record.HasTerrainData && record.DominantBiome == BiomeType.Grassland && record.ActiveRuntime != null &&
            record.ActiveRuntime.IsVisible && record.ActiveRuntime.HasTerrainMesh &&
            record.FoliageData != null && record.FoliageData.flowersGenerated;
    }

    private void ProcessTransfers(ChunkManager manager, ChunkCoord viewerCoord, int day)
    {
        // A boundary crossing establishes its new owner in the same update.
        int habitatBuildsRemaining = pendingTransfers.Count;
        int radius = Mathf.Min(Mathf.Max(0, settings.activeRingRadius),
            Mathf.Max(0, flowerSettings.activeRingRadius));
        for (int i = 0; i < pendingTransfers.Count; i++)
        {
            PendingTransfer transfer = pendingTransfers[i];
            if (!activeChunks.TryGetValue(transfer.Source, out ActiveChunk source))
                continue;
            ChunkRecord destinationRecord = manager.GetChunkRecord(transfer.Destination);
            if (destinationRecord == null || !CanEnterChunk(manager, transfer.Destination))
                continue;
            ChunkButterflyData destination = EnsureChunkData(manager, destinationRecord, day,
                ref habitatBuildsRemaining);
            if (destination == null)
                continue;
            if (!TrySampleDryGround(manager, transfer.Butterfly.Position.x,
                    transfer.Butterfly.Position.z, out float groundY))
                continue;
            Vector3 newHome = new(transfer.Butterfly.Position.x, groundY,
                transfer.Butterfly.Position.z);
            if (!MoveResident(source.Data, destination, transfer.Butterfly.Id, newHome))
                continue;

            for (int j = source.Butterflies.Count - 1; j >= 0; j--)
                if (source.Butterflies[j].Id == transfer.Butterfly.Id)
                    source.Butterflies.RemoveAt(j);

            int dx = transfer.Destination.x - viewerCoord.x;
            int dz = transfer.Destination.z - viewerCoord.z;
            if (dx * dx + dz * dz > radius * radius)
                continue;
            if (!activeChunks.TryGetValue(transfer.Destination, out ActiveChunk target))
            {
                target = new ActiveChunk { Data = destination, Day = day,
                    FlowersRevision = destination.FlowersRevision };
                activeChunks.Add(transfer.Destination, target);
            }
            LiveButterfly live = transfer.Butterfly;
            live.Home = newHome;
            if (!target.Butterflies.Exists(butterfly => butterfly.Id == live.Id))
                target.Butterflies.Add(live);
        }
        pendingTransfers.Clear();
    }

    // A same-day transfer changes the owning roster without changing the butterfly's identity.
    public static bool MoveResident(ChunkButterflyData source, ChunkButterflyData destination,
        uint id, Vector3 newHome)
    {
        if (source == null || destination == null || ReferenceEquals(source, destination) ||
            source.RosterDay != destination.RosterDay)
            return false;
        int sourceIndex = source.Residents.FindIndex(resident => resident.Id == id);
        if (sourceIndex < 0)
            return false;
        ButterflyResident original = source.Residents[sourceIndex];
        source.Residents.RemoveAt(sourceIndex);
        source.Migrants.RemoveAll(resident => resident.Id == id);
        source.DepartedIds.Add(id);
        if (destination.Residents.Exists(resident => resident.Id == id))
            return true;
        ButterflyResident migrant = new(id, -1, newHome,
            original.PreferredHeight, original.Scale);
        destination.Migrants.Add(migrant);
        destination.Residents.Add(migrant);
        return true;
    }

    private bool TrySampleDryGround(ChunkManager manager, float worldX, float worldZ, out float groundY)
    {
        groundY = 0f;
        if (chunkWorldSize <= 0f)
            return false;
        int cx = Mathf.FloorToInt(worldX / chunkWorldSize);
        int cz = Mathf.FloorToInt(worldZ / chunkWorldSize);
        ChunkCoord coord = new(cx, cz);
        ChunkRecord record = manager.GetChunkRecord(coord);
        if (record == null || record.DominantBiome != BiomeType.Grassland) return false;
        if (record.BiomeMap == null) return false;
        int px = Mathf.Clamp(Mathf.RoundToInt((worldX/ worldScale)-cx*chunkSize)+1,1,record.BiomeMap.GetLength(0)-2);
        int pz = Mathf.Clamp(Mathf.RoundToInt((worldZ/ worldScale)-cz*chunkSize)+1,1,record.BiomeMap.GetLength(1)-2);
        if (record.BiomeMap[px,pz] != BiomeType.Grassland) return false;
        return ButterflyTerrainSampler.TrySampleDryGround(record, coord,
            worldX, worldZ, chunkSize, worldScale, terrainHeightScale, waterSurfaceY,
            settings.shoreBuffer, out groundY);
    }

    private void ResolvePrefab()
    {
        GameObject prefab = settings.prefab;
        if (ReferenceEquals(prefab, resolvedPrefab))
            return;
        resolvedPrefab = prefab;
        renderMesh = null;
        if (renderMaterial != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(renderMaterial);
            else UnityEngine.Object.DestroyImmediate(renderMaterial);
            renderMaterial = null;
        }
        warnedInvalidPrefab = false;
        if (prefab == null)
            return;

        MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
        MeshFilter filter = renderers.Length == 1 ? renderers[0].GetComponent<MeshFilter>() : null;
        Material[] materials = renderers.Length == 1 ? renderers[0].sharedMaterials : null;
        if (filter == null || filter.sharedMesh == null || filter.sharedMesh.subMeshCount < 1 ||
            materials == null || materials.Length < 1 || materials[0] == null)
        {
            WarnInvalidPrefab();
            return;
        }
        for (int i = 1; i < materials.Length; i++)
        {
            if (materials[i] != materials[0])
            {
                WarnInvalidPrefab();
                return;
            }
        }
        renderMesh = filter.sharedMesh;
        renderMaterial = new Material(materials[0]) { enableInstancing = true };
        if (beeSettings != null)
        {
            cachedBeeFlapFrequency = float.NaN;
            cachedBeeWingAngle = float.NaN;
            SetColorIfPresent(renderMaterial, OrangeId, new Color(1f, 0.72f, 0.28f));
            SetColorIfPresent(renderMaterial, YellowId, new Color(1f, 0.85f, 0.35f));
            SetColorIfPresent(renderMaterial, PinkId, new Color(0.96f, 0.73f, 0.35f));
            SetColorIfPresent(renderMaterial, PurpleId, new Color(0.93f, 0.8f, 0.43f));
            SetColorIfPresent(renderMaterial, BlueId, new Color(0.98f, 0.89f, 0.55f));
        }
        flapFrequency = renderMaterial.HasProperty(FlapFrequencyId)
            ? Mathf.Max(0f, renderMaterial.GetFloat(FlapFrequencyId)) : 5.5f;
        // The FBX importer may bake its unit scale into either the mesh or its transforms.
        // Derive the hinge from mesh-local bounds so the shader works in both cases.
        if (renderMaterial.HasProperty(WingHingeXId))
            renderMaterial.SetFloat(WingHingeXId, renderMesh.bounds.extents.x * 0.07f);
        meshLocalMatrix = Matrix4x4.Scale(prefab.transform.localScale) *
                          (prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix);
        renderLayer = renderers[0].gameObject.layer;
    }

    private static void SetColorIfPresent(Material material, int propertyId, Color color)
    {
        if (material.HasProperty(propertyId)) material.SetColor(propertyId, color);
    }

    private void UpdateBeeMaterial()
    {
        if (beeSettings == null || renderMaterial == null) return;
        if (cachedBeeFlapFrequency != beeSettings.wingbeatsPerSecond)
        {
            cachedBeeFlapFrequency = Mathf.Clamp(beeSettings.wingbeatsPerSecond, 0f, 20f);
            flapFrequency = cachedBeeFlapFrequency;
            if (renderMaterial.HasProperty(FlapFrequencyId))
                renderMaterial.SetFloat(FlapFrequencyId, cachedBeeFlapFrequency);
        }
        if (cachedBeeWingAngle != beeSettings.raisedWingAngle)
        {
            cachedBeeWingAngle = Mathf.Clamp(beeSettings.raisedWingAngle, 0f, 85f);
            if (renderMaterial.HasProperty(FlapClosedAngleId))
                renderMaterial.SetFloat(FlapClosedAngleId, cachedBeeWingAngle);
        }
    }

    private void WarnInvalidPrefab()
    {
        if (warnedInvalidPrefab) return;
        warnedInvalidPrefab = true;
        Debug.LogWarning($"{(beeSettings != null ? "Bee" : "Butterfly")} prefab needs exactly one MeshRenderer and MeshFilter, with one material shared by all submeshes.");
    }

    private void Draw(Camera camera)
    {
        Quaternion yawCorrection = Quaternion.Euler(0f, settings.modelYawOffset, 0f);
        foreach (ActiveChunk chunk in activeChunks.Values)
        {
            int start = 0;
            while (start < chunk.Butterflies.Count)
            {
                int count = Mathf.Min(drawMatrices.Length, chunk.Butterflies.Count - start);
                for (int i = 0; i < count; i++)
                {
                    LiveButterfly butterfly = chunk.Butterflies[start + i];
                    float pitch = 0f;
                    if (butterfly.State == FlightState.Flying || butterfly.State == FlightState.Landing)
                    {
                        float climb = Mathf.Clamp(butterfly.VerticalVelocity /
                            Mathf.Max(0.1f, settings.verticalSpeed), -1f, 1f);
                        pitch = settings.flightPitchDegrees + climb * settings.climbPitchDegrees;
                    }
                    else if (butterfly.State == FlightState.Hovering)
                        pitch = settings.flightPitchDegrees * 0.5f;
                    pitch = beeSettings != null ? butterfly.VisualPitchDegrees : Mathf.Clamp(pitch, -45f, 45f);
                    Quaternion facing = Quaternion.LookRotation(butterfly.Facing, Vector3.up) *
                                        Quaternion.Euler(-pitch, 0f, butterfly.BankDegrees) * yawCorrection;
                    drawMatrices[i] = Matrix4x4.TRS(butterfly.Position, facing,
                        Vector3.one * butterfly.Scale) * meshLocalMatrix;
                    uint colorSeed = Mix(butterfly.Id ^ 0x9e3779b9u);
                    drawInstanceData[i] = new Vector4(colorSeed % 5u,
                        butterfly.WingPhase,
                        Mathf.Lerp(0.88f, 1.12f, ToUnitFloat(Mix(colorSeed))),
                        butterfly.RestBlend);
                }
                drawProperties.Clear();
                drawProperties.SetVectorArray(ButterflyInstanceDataId, drawInstanceData);
                for (int submesh = 0; submesh < renderMesh.subMeshCount; submesh++)
                    Graphics.DrawMeshInstanced(renderMesh, submesh, renderMaterial, drawMatrices, count, drawProperties,
                        ShadowCastingMode.Off, false, renderLayer, camera, LightProbeUsage.Off);
                start += count;
            }
        }
    }

    private int GetSettingsSignature()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + Mathf.RoundToInt(settings.spawnChance * 10000f);
            hash = hash * 31 + Mathf.Max(1, settings.maxPerChunk);
            hash = hash * 31 + Mathf.Max(1, settings.maxHotspotsPerChunk);
            hash = hash * 31 + Mathf.RoundToInt(settings.hotspotCellSize * 1000f);
            hash = hash * 31 + Mathf.RoundToInt(settings.shoreBuffer * 1000f);
            hash = hash * 31 + Mathf.RoundToInt(settings.preferredHeightMin * 1000f);
            hash = hash * 31 + Mathf.RoundToInt(settings.preferredHeightMax * 1000f);
            hash = hash * 31 + Mathf.RoundToInt(settings.modelScaleMin * 1000f);
            hash = hash * 31 + Mathf.RoundToInt(settings.modelScaleMax * 1000f);
            if (beeSettings != null)
                hash = hash * 31 + Mathf.RoundToInt(beeSettings.threeBeeChance * 10000f);
            return hash;
        }
    }

    private static uint Hash(int seed, int x, int z, int day, int slot)
    {
        uint value = Mix((uint)seed);
        value = Mix(value ^ (uint)x);
        value = Mix(value ^ (uint)z);
        value = Mix(value ^ (uint)day);
        return Mix(value ^ (uint)slot);
    }

    private static uint Mix(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            return value ^ (value >> 16);
        }
    }

    private static float ToUnitFloat(uint value) => (value >> 8) * (1f / 16777216f);

    private static float Next01(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return ToUnitFloat(state);
    }

    public void Dispose()
    {
        activeChunks.Clear();
        if (renderMaterial != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(renderMaterial);
            else UnityEngine.Object.DestroyImmediate(renderMaterial);
            renderMaterial = null;
        }
    }
}
