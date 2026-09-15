using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public class ChunkRecord : System.IDisposable
{
    public sealed class NativeTerrainData : System.IDisposable
    {
        public NativeArray<float> HeightMap { get; private set; }
        public NativeArray<float> SlopeMap { get; private set; }
        public NativeArray<BiomeType> BiomeMap { get; private set; }
        public NativeArray<SurfaceType> SurfaceTypeMap { get; private set; }
        public NativeArray<WaterState> WaterStateMap { get; private set; }
        public NativeArray<GroundCoverType> GroundCoverMap { get; private set; }
        public NativeArray<float> RiverMaskMap { get; private set; }

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
            float[,] riverMaskMap)
        {
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
            if (HeightMap.IsCreated) HeightMap.Dispose();
            if (SlopeMap.IsCreated) SlopeMap.Dispose();
            if (BiomeMap.IsCreated) BiomeMap.Dispose();
            if (SurfaceTypeMap.IsCreated) SurfaceTypeMap.Dispose();
            if (WaterStateMap.IsCreated) WaterStateMap.Dispose();
            if (GroundCoverMap.IsCreated) GroundCoverMap.Dispose();
            if (RiverMaskMap.IsCreated) RiverMaskMap.Dispose();
        }
    }

    public float[,] FarTreeHeightGrid { get; private set; }
    public Mesh FarTerrainWaterMesh { get; private set; }
    private ChunkCoord chunkCoord;
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
        colliderRequestInFlight = true;
        colliderRequestVersion++;
        return colliderRequestVersion;
    }

    public void CancelColliderRequest(int requestVersion)
    {
        if (colliderRequestVersion == requestVersion)
        {
            colliderRequestInFlight = false;
        }
    }

    public bool TryCompleteColliderRequest(int requestVersion, Mesh mesh)
    {
        if (!colliderRequestInFlight || requestVersion != colliderRequestVersion)
            return false;

        colliderRequestInFlight = false;

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
        LODTerrainMeshes[lod] = terrainMesh;
    }

    public void StoreLODWaterMesh(int lod, Mesh waterMesh)
    {
        LODWaterMeshes[lod] = waterMesh;
    }


    public void ClearLODTerrainMeshes()
    {
        LODTerrainMeshes.Clear();
    }

    public void ClearLODWaterMeshes()
    {
        LODWaterMeshes.Clear();
    }


    public void ClearAllLODMeshes()
    {
        LODTerrainMeshes.Clear();
        LODWaterMeshes.Clear();
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
        terrainDataRequestVersion++;
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

    public bool TryCompleteTerrainDataRequest(int requestVersion, float[,] returnedHeightMap,
        float[,] returnedSlopeMap,
        float[,] returnedMoistureMap, float[,] returnedTemperatureMap, BiomeType[,] returnedBiomeMap, 
        SurfaceType[,] returnedSurfaceTypeMap, WaterState[,] returnedWaterStateMap,
        GroundCoverType[,] returnedGroundCoverMap, WorldFeaturePlan returnedWorldFeaturePlan, float[,] returnedRiverMaskMap,
        Texture2D[] returnedControlMapData)
    {
        if (!terrainDataRequestInFlight) 
            return false;
        if (requestVersion != terrainDataRequestVersion) 
            return false;

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
        nativeTerrainData?.Dispose();
        nativeTerrainData = new NativeTerrainData(
            heightMap,
            slopeMap,
            biomeMap,
            surfaceTypeMap,
            waterStateMap,
            groundCoverMap,
            riverMaskMap);

        terrainDataRequestInFlight = false;
        return true;
    }

    public int BeginFarTerrainRequest()
    {
        farTerrainRequestVersion++;
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
        return farTerrainRequestInFlight && farTerrainRequestVersion == requestVersion;
    }

    public bool TryCompleteFarTerrainRequest(
        int requestVersion,
        Mesh returnedFarTerrainMesh,
        Texture2D[] returnedFarTerrainControlMapData, Mesh returnedWaterMesh = null, float[,] heightGrid = null)
    {
        if (!farTerrainRequestInFlight)
            return false;

        if (requestVersion != farTerrainRequestVersion)
            return false;

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
        int nextVersion = 1;

        if (meshRequestVersionsByLOD.TryGetValue(lod, out int currentVersion))
            nextVersion = currentVersion + 1;

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

    public bool TryCompleteMeshRequest(int lod, int requestVersion, Mesh terrainMesh, Mesh waterMesh)
    {
        if (!meshRequestsInFlight.Contains(lod))
            return false;

        if (!meshRequestVersionsByLOD.TryGetValue(lod, out int currentVersion))
            return false;

        if (currentVersion != requestVersion)
            return false;

        LODTerrainMeshes[lod] = terrainMesh;
        LODWaterMeshes[lod] = waterMesh;
        meshRequestsInFlight.Remove(lod);
        return true;
    }

    public void Dispose()
    {
        nativeTerrainData?.Dispose();
        nativeTerrainData = null;
    }

}
