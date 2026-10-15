using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class ChunkRecord : System.IDisposable
{
    public sealed class NativeTerrainData : System.IDisposable
    {
        private readonly object lifetimeLock = new object();
        private int readerCount;
        private bool ownerReleased;
        private float[,] sourceHeightMap;
        private float[,] sourceSlopeMap;
        private BiomeType[,] sourceBiomeMap;
        private SurfaceType[,] sourceSurfaceMap;
        private GroundCoverType[,] sourceGroundCoverMap;
        private Unity.Mathematics.float4[,] sourceForestFloorMap;
        private byte[,] sourceForestMembershipMap;

        public sealed class Lease : System.IDisposable
        {
            private NativeTerrainData owner;
            internal Lease(NativeTerrainData owner) { this.owner = owner; }
            public NativeTerrainData Data => owner;
            public void Dispose()
            {
                NativeTerrainData previous = System.Threading.Interlocked.Exchange(ref owner, null);
                previous?.ReleaseReader();
            }
        }

        public Lease AcquireLease()
        {
            lock (lifetimeLock)
            {
                if (ownerReleased) return null;
                readerCount++;
                return new Lease(this);
            }
        }

        public bool Matches(float[,] height, float[,] slope, BiomeType[,] biome,
            SurfaceType[,] surface, GroundCoverType[,] ground,
            Unity.Mathematics.float4[,] forestFloor = null, byte[,] forestMembership = null) =>
            ReferenceEquals(sourceHeightMap, height) && ReferenceEquals(sourceSlopeMap, slope) &&
            ReferenceEquals(sourceBiomeMap, biome) && ReferenceEquals(sourceSurfaceMap, surface) &&
            ReferenceEquals(sourceGroundCoverMap, ground) && ReferenceEquals(sourceForestFloorMap, forestFloor) && ReferenceEquals(sourceForestMembershipMap, forestMembership);

        public NativeArray<float> HeightMap { get; private set; }
        public NativeArray<float> SlopeMap { get; private set; }
        public NativeArray<BiomeType> BiomeMap { get; private set; }
        public NativeArray<SurfaceType> SurfaceTypeMap { get; private set; }
        public NativeArray<WaterState> WaterStateMap { get; private set; }
        public NativeArray<GroundCoverType> GroundCoverMap { get; private set; }
        public NativeArray<float> RiverMaskMap { get; private set; }
        public NativeArray<Unity.Mathematics.float2> ForestGrassMap { get; private set; }
        public NativeArray<byte> ForestMembershipMap { get; private set; }
        public bool HasForestGrassMap => ForestGrassMap.IsCreated && ForestGrassMap.Length > 0;

        public int HeightMapWidth { get; private set; }
        public int HeightMapHeight { get; private set; }
        public int SlopeMapWidth { get; private set; }
        public int SlopeMapHeight { get; private set; }
        public int BiomeMapWidth { get; private set; }
        public int BiomeMapHeight { get; private set; }
        public int SurfaceTypeMapWidth { get; private set; }
        public int SurfaceTypeMapHeight { get; private set; }
        public int WaterStateMapWidth { get; private set; }
        public int WaterStateMapHeight { get; private set; }
        public int GroundCoverMapWidth { get; private set; }
        public int GroundCoverMapHeight { get; private set; }
        public int RiverMaskMapWidth { get; private set; }
        public int RiverMaskMapHeight { get; private set; }

        public bool HasGrassMaps =>
            HeightMap.IsCreated &&
            SurfaceTypeMap.IsCreated &&
            BiomeMap.IsCreated;

        public bool HasGroundCoverMap => GroundCoverMap.IsCreated;

        public NativeTerrainData(
            float[,] heightMap,
            float[,] slopeMap,
            BiomeType[,] biomeMap,
            SurfaceType[,] surfaceTypeMap,
            WaterState[,] waterStateMap,
            GroundCoverType[,] groundCoverMap,
            float[,] riverMaskMap, Unity.Mathematics.float4[,] forestFloorMap = null, byte[,] forestMembershipMap = null)
        {
            sourceHeightMap = heightMap;
            sourceSlopeMap = slopeMap;
            sourceBiomeMap = biomeMap;
            sourceSurfaceMap = surfaceTypeMap;
            sourceGroundCoverMap = groundCoverMap;
            sourceForestFloorMap = forestFloorMap;
            sourceForestMembershipMap = forestMembershipMap;
            try
            {
                ForestMembershipMap = forestMembershipMap == null ? new NativeArray<byte>(0, Allocator.Persistent) : CopyMap(forestMembershipMap, out _, out _);
                ForestGrassMap = CopyForestGrass(forestFloorMap);
                HeightMap = CopyFloatMap(heightMap, out int heightWidth, out int heightHeight);
                HeightMapWidth = heightWidth;
                HeightMapHeight = heightHeight;

                SlopeMap = CopyFloatMap(slopeMap, out int slopeWidth, out int slopeHeight);
                SlopeMapWidth = slopeWidth;
                SlopeMapHeight = slopeHeight;

                BiomeMap = CopyMap(biomeMap, out int biomeWidth, out int biomeHeight);
                BiomeMapWidth = biomeWidth;
                BiomeMapHeight = biomeHeight;

                SurfaceTypeMap = CopyMap(surfaceTypeMap, out int surfaceWidth, out int surfaceHeight);
                SurfaceTypeMapWidth = surfaceWidth;
                SurfaceTypeMapHeight = surfaceHeight;

                WaterStateMap = CopyMap(waterStateMap, out int waterWidth, out int waterHeight);
                WaterStateMapWidth = waterWidth;
                WaterStateMapHeight = waterHeight;

                GroundCoverMap = CopyMap(groundCoverMap, out int groundWidth, out int groundHeight);
                GroundCoverMapWidth = groundWidth;
                GroundCoverMapHeight = groundHeight;

                RiverMaskMap = CopyFloatMap(riverMaskMap, out int riverWidth, out int riverHeight);
                RiverMaskMapWidth = riverWidth;
                RiverMaskMapHeight = riverHeight;
            }
            catch { DisposeArrays(); throw; }

        }

        // Takes ownership of persistent job outputs after terrain generation completes.
        public NativeTerrainData(
            float[,] heightMap, float[,] slopeMap, BiomeType[,] biomeMap,
            SurfaceType[,] surfaceTypeMap, WaterState[,] waterStateMap,
            GroundCoverType[,] groundCoverMap, float[,] riverMaskMap,
            NativeArray<float> heights, NativeArray<float> slopes,
            NativeArray<BiomeType> biomes, NativeArray<SurfaceType> surfaces,
            NativeArray<WaterState> waters, NativeArray<GroundCoverType> covers,
            NativeArray<float> rivers, Unity.Mathematics.float4[,] forestFloorMap = null, byte[,] forestMembershipMap = null)
        {
            sourceHeightMap = heightMap;
            sourceSlopeMap = slopeMap;
            sourceBiomeMap = biomeMap;
            sourceSurfaceMap = surfaceTypeMap;
            sourceGroundCoverMap = groundCoverMap;
            sourceForestFloorMap = forestFloorMap;
            sourceForestMembershipMap = forestMembershipMap;
            try
            {
                ForestMembershipMap = forestMembershipMap == null ? new NativeArray<byte>(0, Allocator.Persistent) : CopyMap(forestMembershipMap, out _, out _);
                ForestGrassMap = CopyForestGrass(forestFloorMap);
            }
            catch { if (ForestMembershipMap.IsCreated) ForestMembershipMap.Dispose(); if (ForestGrassMap.IsCreated) ForestGrassMap.Dispose(); throw; }
                    HeightMap = heights; SlopeMap = slopes; BiomeMap = biomes;
            SurfaceTypeMap = surfaces; WaterStateMap = waters;
            GroundCoverMap = covers; RiverMaskMap = rivers;
            HeightMapWidth = heightMap?.GetLength(0) ?? 0; HeightMapHeight = heightMap?.GetLength(1) ?? 0;
            SlopeMapWidth = slopeMap?.GetLength(0) ?? 0; SlopeMapHeight = slopeMap?.GetLength(1) ?? 0;
            BiomeMapWidth = biomeMap?.GetLength(0) ?? 0; BiomeMapHeight = biomeMap?.GetLength(1) ?? 0;
            SurfaceTypeMapWidth = surfaceTypeMap?.GetLength(0) ?? 0; SurfaceTypeMapHeight = surfaceTypeMap?.GetLength(1) ?? 0;
            WaterStateMapWidth = waterStateMap?.GetLength(0) ?? 0; WaterStateMapHeight = waterStateMap?.GetLength(1) ?? 0;
            GroundCoverMapWidth = groundCoverMap?.GetLength(0) ?? 0; GroundCoverMapHeight = groundCoverMap?.GetLength(1) ?? 0;
            RiverMaskMapWidth = riverMaskMap?.GetLength(0) ?? 0; RiverMaskMapHeight = riverMaskMap?.GetLength(1) ?? 0;
        }

        private static NativeArray<Unity.Mathematics.float2> CopyForestGrass(Unity.Mathematics.float4[,] source)
        {
            // Reuse one grass buffer for habitat density and blurred, membership-weighted moss.
            int width = source?.GetLength(0) ?? 0, height = source?.GetLength(1) ?? 0;
            var result = new NativeArray<Unity.Mathematics.float2>(width * height, Allocator.Persistent);
            for (int x = 0; x < width; x++)
                for (int z = 0; z < height; z++) result[x * height + z] = ForestFloorPolicy.GrassSampleAt(source,x,z);
            return result;
        }

        private static NativeArray<float> CopyFloatMap(float[,] source, out int width, out int height)
        {
            if (source == null)
            {
                width = 0;
                height = 0;
                return default;
            }

            width = source.GetLength(0);
            height = source.GetLength(1);
            var result = new NativeArray<float>(width * height, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int x = 0; x < width; x++)
            {
                int rowOffset = x * height;
                for (int z = 0; z < height; z++)
                    result[rowOffset + z] = source[x, z];
            }
            return result;
        }

        private static NativeArray<T> CopyMap<T>(T[,] source, out int width, out int height) where T : unmanaged
        {
            if (source == null)
            {
                width = 0;
                height = 0;
                return default;
            }

            width = source.GetLength(0);
            height = source.GetLength(1);
            var result = new NativeArray<T>(width * height, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int x = 0; x < width; x++)
            {
                int rowOffset = x * height;
                for (int z = 0; z < height; z++)
                    result[rowOffset + z] = source[x, z];
            }
            return result;
        }

        public void Dispose()
        {
            lock (lifetimeLock)
            {
                if (ownerReleased) return;
                ownerReleased = true;
                if (readerCount != 0) return;
                DisposeArrays();
            }
        }

        private void ReleaseReader()
        {
            lock (lifetimeLock)
            {
                readerCount--;
                if (ownerReleased && readerCount == 0)
                    DisposeArrays();
            }
        }

        private void DisposeArrays()
        {
            if (HeightMap.IsCreated) HeightMap.Dispose();
            if (SlopeMap.IsCreated) SlopeMap.Dispose();
            if (BiomeMap.IsCreated) BiomeMap.Dispose();
            if (SurfaceTypeMap.IsCreated) SurfaceTypeMap.Dispose();
            if (WaterStateMap.IsCreated) WaterStateMap.Dispose();
            if (GroundCoverMap.IsCreated) GroundCoverMap.Dispose();
            if (RiverMaskMap.IsCreated) RiverMaskMap.Dispose();
            if (ForestGrassMap.IsCreated) ForestGrassMap.Dispose();
            if (ForestMembershipMap.IsCreated) ForestMembershipMap.Dispose();
            HeightMap = SlopeMap = RiverMaskMap = default;
            BiomeMap = default; SurfaceTypeMap = default; WaterStateMap = default; GroundCoverMap = default;
            ForestGrassMap = default; ForestMembershipMap = default;
            sourceHeightMap = sourceSlopeMap = null; sourceBiomeMap = null; sourceSurfaceMap = null;
            sourceGroundCoverMap = null; sourceForestFloorMap = null; sourceForestMembershipMap = null;
        }
    }

    public float[,] FarTreeHeightGrid { get; private set; }
    public Mesh FarTerrainWaterMesh { get; private set; }
    private ChunkCoord chunkCoord;
    private readonly TerrainAssetOwner generatedAssets = new();
    private bool disposed;
    private ChunkRuntime activeRuntime;
    private float[,] heightMap;
    private float[,] slopeMap;
    private float[,] moistureMap;
    private float[,] temperatureMap;
    private BiomeType[,] biomeMap;
    private SurfaceType[,] surfaceTypeMap;
    private WaterState[,] waterStateMap;
    private GroundCoverType[,] groundCoverMap;
    private WorldFeaturePlan worldFeaturePlan;
    private float[,] riverMaskMap;
    private Texture2D[] controlMapData;
    private NativeTerrainData nativeTerrainData;
    private ChunkFoliageData foliageData;
    private ChunkButterflyData butterflyData;
    private ChunkButterflyData beeData;
    private Mesh farTerrainMesh;
    private Texture2D[] farTerrainControlMapData;

    private Dictionary<int, Mesh> LODTerrainMeshes = new Dictionary<int, Mesh>();
    private Dictionary<int, Mesh> LODWaterMeshes = new Dictionary<int, Mesh>();

    private Mesh colliderMesh;

    private bool colliderRequestInFlight;
    private int colliderRequestVersion;

    private bool colliderReady;
    private bool colliderDesired;

    private bool terrainDataRequestInFlight;
    private int terrainDataRequestVersion;

    private bool farTerrainRequestInFlight;
    private int farTerrainRequestVersion;
    private bool farTerrainReady;

    private Dictionary<int, int> meshRequestVersionsByLOD = new Dictionary<int, int>();
    private HashSet<int> meshRequestsInFlight = new HashSet<int>();

    public ChunkCoord ChunkCoord => chunkCoord;
    public ChunkRuntime ActiveRuntime => activeRuntime;
    public bool IsLoaded => activeRuntime != null;
    public bool HasTerrainData =>
        heightMap != null &&
        slopeMap != null &&
        moistureMap != null &&
        temperatureMap != null &&
        biomeMap != null &&
        surfaceTypeMap != null &&
        waterStateMap != null &&
        groundCoverMap != null &&
        worldFeaturePlan != null &&
        riverMaskMap != null &&
        controlMapData != null;
    public float[,] HeightMap => heightMap;
    // Smoothed terrain inclination in degrees; gradients remain unscaled derivatives.
    public float[,] SlopeMap => slopeMap;
    public float[,] MoistureMap => moistureMap;
    public float[,] TemperatureMap => temperatureMap;
    public BiomeType[,] BiomeMap => biomeMap;
    private BiomeType[,] ownershipSource;
    private BiomeType? dominantBiome;
    public BiomeType? DominantBiome
    {
        get
        {
            if (!ReferenceEquals(ownershipSource,biomeMap))
            {
                dominantBiome = ChunkBiomeOwnership.Classify(biomeMap);
                ownershipSource = biomeMap;
            }
            return dominantBiome;
        }
    }
    public SurfaceType[,] SurfaceTypeMap => surfaceTypeMap;
    public WaterState[,] WaterStateMap => waterStateMap;
    public GroundCoverType[,] GroundCoverMap => groundCoverMap;
    public WorldFeaturePlan WorldFeaturePlan => worldFeaturePlan;
    public float[,] RiverMaskMap => riverMaskMap;
    public Texture2D[] ControlMapData => controlMapData;
    public NativeTerrainData NativeData => nativeTerrainData;
    public Texture2D[] FarTerrainControlMapData => farTerrainControlMapData;
    public ChunkFoliageData FoliageData {
        get => foliageData;
        set => foliageData = value;
    }
    public ChunkButterflyData ButterflyData {
        get => butterflyData;
        set => butterflyData = value;
    }
    public ChunkButterflyData BeeData {
        get => beeData;
        set => beeData = value;
    }
    public bool IsTerrainDataRequestInFlight => terrainDataRequestInFlight;
    public int TerrainDataRequestVersion => terrainDataRequestVersion;
    public bool IsFarTerrainRequestInFlight => farTerrainRequestInFlight;
    public int FarTerrainRequestVersion => farTerrainRequestVersion;
    public bool HasFarTerrain => farTerrainReady && farTerrainMesh != null && farTerrainControlMapData != null;

    public bool ColliderRequestInFlight => colliderRequestInFlight;
    public bool ColliderReady => colliderReady;
    public bool ColliderDesired
    {
        get => colliderDesired;
        set => colliderDesired = value;
    }

    public ChunkRecord(ChunkCoord chunkCoord)
    {
        this.chunkCoord = chunkCoord;
    }

    public int BeginColliderRequest()
    {
        ThrowIfDisposed();
        colliderRequestInFlight = true;
        colliderRequestVersion = TerrainRequestIdentity.Next();
        return colliderRequestVersion;
    }

    public void CancelColliderRequest(int requestVersion)
    {
        if (colliderRequestVersion == requestVersion)
        {
            colliderRequestInFlight = false;
        }
    }

    public bool IsColliderRequestCurrent(int version) => !disposed && colliderRequestInFlight && version == colliderRequestVersion;

    public bool TryCompleteColliderRequest(int requestVersion, Mesh mesh)
    {
        if (!IsColliderRequestCurrent(requestVersion))
            return false;

        colliderRequestInFlight = false;

        generatedAssets.Replace(colliderMesh, mesh);
        colliderMesh = mesh;
        colliderReady = true;

        return true;
    }

    public bool TryGetColliderMesh(out Mesh mesh)
    {
        mesh = colliderMesh;
        return colliderReady && colliderMesh != null;
    }

    public void ClearColliderMesh()
    {
        generatedAssets.Replace(colliderMesh, (Mesh)null);
        colliderMesh = null;
        colliderReady = false;
    }

    public bool TryGetLODTerrainMesh(int lod, out Mesh terrainMesh)
    {
        return LODTerrainMeshes.TryGetValue(lod, out terrainMesh);
    }

    public bool TryGetLODWaterMesh(int lod, out Mesh waterMesh)
    {
        return LODWaterMeshes.TryGetValue(lod, out waterMesh);
    }


    public void StoreLODTerrainMesh(int lod, Mesh terrainMesh)
    {
        ThrowIfDisposed();
        LODTerrainMeshes.TryGetValue(lod, out var previous);
        generatedAssets.Replace(previous, terrainMesh);
        LODTerrainMeshes[lod] = terrainMesh;
    }

    public void StoreLODWaterMesh(int lod, Mesh waterMesh)
    {
        ThrowIfDisposed();
        LODWaterMeshes.TryGetValue(lod, out var previous);
        generatedAssets.Replace(previous, waterMesh);
        LODWaterMeshes[lod] = waterMesh;
    }


    public void ClearLODTerrainMeshes()
    {
        foreach (var mesh in LODTerrainMeshes.Values) generatedAssets.Replace(mesh, (Mesh)null);
        LODTerrainMeshes.Clear();
    }

    public void ClearLODWaterMeshes()
    {
        foreach (var mesh in LODWaterMeshes.Values) generatedAssets.Replace(mesh, (Mesh)null);
        LODWaterMeshes.Clear();
    }


    public void ClearAllLODMeshes()
    {
        ClearLODTerrainMeshes();
        ClearLODWaterMeshes();
    }

    public void SetActiveRuntime(ChunkRuntime activeRuntime)
    {
        this.activeRuntime = activeRuntime;
    }

    public void ClearActiveRuntime(ChunkRuntime runtime)
    {
        if (activeRuntime == runtime) { activeRuntime = null; }
    }

    public int BeginTerrainDataRequest()
    {
        ThrowIfDisposed();
        terrainDataRequestVersion = TerrainRequestIdentity.Next();
        terrainDataRequestInFlight = true;
        return terrainDataRequestVersion;
    }

    public void CancelTerrainDataRequest(int requestVersion)
    {
        if (terrainDataRequestVersion == requestVersion)
        {
            terrainDataRequestInFlight = false;
        }
    }

    public bool IsTerrainDataRequestCurrent(int version) => !disposed && terrainDataRequestInFlight && version == terrainDataRequestVersion;

    public bool TryCompleteTerrainDataRequest(int requestVersion, float[,] returnedHeightMap,
        float[,] returnedSlopeMap,
        float[,] returnedMoistureMap, float[,] returnedTemperatureMap, BiomeType[,] returnedBiomeMap,
        SurfaceType[,] returnedSurfaceTypeMap, WaterState[,] returnedWaterStateMap,
        GroundCoverType[,] returnedGroundCoverMap, WorldFeaturePlan returnedWorldFeaturePlan, float[,] returnedRiverMaskMap,
        Texture2D[] returnedControlMapData,
        NativeTerrainData retainedNativeData = null)
    {
        if (!IsTerrainDataRequestCurrent(requestVersion))
            return false;

        // Construct fallible native storage before changing accepted ownership or managed inputs.
        var nextNative = retainedNativeData ?? new NativeTerrainData(returnedHeightMap, returnedSlopeMap,
            returnedBiomeMap, returnedSurfaceTypeMap, returnedWaterStateMap, returnedGroundCoverMap, returnedRiverMaskMap,
            returnedWorldFeaturePlan?.ForestStructure.FloorEcologyMap, returnedWorldFeaturePlan?.ForestMembershipMap);
        generatedAssets.Replace(controlMapData, returnedControlMapData);

        heightMap = returnedHeightMap;
        slopeMap = returnedSlopeMap;
        moistureMap = returnedMoistureMap;
        temperatureMap = returnedTemperatureMap;
        biomeMap = returnedBiomeMap;
        surfaceTypeMap = returnedSurfaceTypeMap;
        waterStateMap = returnedWaterStateMap;
        groundCoverMap = returnedGroundCoverMap;
        worldFeaturePlan = returnedWorldFeaturePlan;
        riverMaskMap = returnedRiverMaskMap;
        controlMapData = returnedControlMapData;
        if (!ReferenceEquals(nativeTerrainData, nextNative)) nativeTerrainData?.Dispose();
        nativeTerrainData = nextNative;

        terrainDataRequestInFlight = false;
        return true;
    }

    public int BeginFarTerrainRequest()
    {
        ThrowIfDisposed();
        farTerrainRequestVersion = TerrainRequestIdentity.Next();
        farTerrainRequestInFlight = true;
        return farTerrainRequestVersion;
    }

    public void CancelFarTerrainRequest(int requestVersion)
    {
        if (farTerrainRequestVersion == requestVersion)
        {
            farTerrainRequestInFlight = false;
        }
    }

    public bool IsFarTerrainRequestCurrent(int requestVersion)
    {
        return !disposed && farTerrainRequestInFlight && farTerrainRequestVersion == requestVersion;
    }

    public bool TryCompleteFarTerrainRequest(
        int requestVersion,
        Mesh returnedFarTerrainMesh,
        Texture2D[] returnedFarTerrainControlMapData, Mesh returnedWaterMesh = null, float[,] heightGrid = null)
    {
        if (!IsFarTerrainRequestCurrent(requestVersion))
            return false;

        generatedAssets.Replace(farTerrainMesh, FarTerrainWaterMesh, returnedFarTerrainMesh, returnedWaterMesh);
        generatedAssets.Replace(farTerrainControlMapData, returnedFarTerrainControlMapData);
        farTerrainMesh = returnedFarTerrainMesh;
        FarTerrainWaterMesh = returnedWaterMesh;
        FarTreeHeightGrid = heightGrid;
        farTerrainControlMapData = returnedFarTerrainControlMapData;
        farTerrainReady = farTerrainMesh != null && farTerrainControlMapData != null;
        farTerrainRequestInFlight = false;

        return true;
    }

    public bool TryGetFarTerrainMesh(out Mesh mesh)
    {
        mesh = farTerrainMesh;
        return HasFarTerrain;
    }

    public bool IsMeshRequestInFlight(int lod)
    {
        return meshRequestsInFlight.Contains(lod);
    }

    public int BeginMeshRequest(int lod)
    {
        ThrowIfDisposed();
        int nextVersion = TerrainRequestIdentity.Next();

        meshRequestVersionsByLOD[lod] = nextVersion;
        meshRequestsInFlight.Add(lod);
        return nextVersion;
    }

    public void CancelMeshRequest(int lod, int requestVersion)
    {
        if (!meshRequestVersionsByLOD.TryGetValue(lod, out int currentVersion))
            return;

        if (currentVersion == requestVersion)
            meshRequestsInFlight.Remove(lod);
    }

    public bool IsMeshRequestCurrent(int lod, int version) => !disposed && meshRequestsInFlight.Contains(lod) &&
        meshRequestVersionsByLOD.TryGetValue(lod, out int current) && current == version;

    public bool TryCompleteMeshRequest(int lod, int requestVersion, Mesh terrainMesh, Mesh waterMesh)
    {
        if (!meshRequestsInFlight.Contains(lod))
            return false;

        if (!meshRequestVersionsByLOD.TryGetValue(lod, out int currentVersion))
            return false;

        if (currentVersion != requestVersion)
            return false;

        LODTerrainMeshes.TryGetValue(lod, out var oldTerrain);
        LODWaterMeshes.TryGetValue(lod, out var oldWater);
        generatedAssets.Replace(oldTerrain, oldWater, terrainMesh, waterMesh);
        LODTerrainMeshes[lod] = terrainMesh; LODWaterMeshes[lod] = waterMesh;
        meshRequestsInFlight.Remove(lod);
        return true;
    }

    internal void RefreshDisplayedLOD(int lod)
    {
        if (activeRuntime?.IsShowingLOD(lod) == true)
            activeRuntime.SetMeshes(LODTerrainMeshes[lod], LODWaterMeshes[lod], lod);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ReleaseGeneratedData();
    }

    /// <summary>Coordinator must detach runtimes/consumers first. Tree registry placements are independent.</summary>
    internal void ReleaseGeneratedData()
    {
        InvalidateRequests();
        nativeTerrainData?.Dispose();
        nativeTerrainData = null;
        generatedAssets.Dispose();
        heightMap = slopeMap = moistureMap = temperatureMap = riverMaskMap = null;
        biomeMap = ownershipSource = null; dominantBiome = null;
        surfaceTypeMap = null; waterStateMap = null; groundCoverMap = null; worldFeaturePlan = null;
        controlMapData = farTerrainControlMapData = null;
        foliageData = null; butterflyData = beeData = null;
        farTerrainMesh = FarTerrainWaterMesh = colliderMesh = null; FarTreeHeightGrid = null;
        LODTerrainMeshes.Clear(); LODWaterMeshes.Clear();
        colliderReady = colliderDesired = farTerrainReady = false;
    }

    internal void InvalidateRequests()
    {
        terrainDataRequestInFlight = farTerrainRequestInFlight = colliderRequestInFlight = false;
        meshRequestsInFlight.Clear(); meshRequestVersionsByLOD.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new System.ObjectDisposedException(nameof(ChunkRecord));
    }
}
