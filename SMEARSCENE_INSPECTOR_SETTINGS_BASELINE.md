# SmearScene Inspector Settings Baseline

Source: `git show HEAD:Assets/Scenes/SmearScene.unity` at the time this file was created. Use this as a reference for the pre-test inspector values if the current scene has temporary profiling changes.

This is not a full scene backup. It records the serialized inspector fields most relevant to the streaming/performance tests.

```yaml
  viewDistance: 100
  colliderDistance: 5
  enableFarTerrain: 1
  farTerrainStartRing: 9
  farTerrainMacroTileSize: 4
  farTerrainHeightGridResolution: 9
  farTerrainControlMapResolution: 16
  farTerrainSkirtDepth: 6
  chunkSize: 128
  grassSettings:
    gpuIndirectRendering: 1
    billboardCoverage: 0.2
    transitionWidthChunks: 0.35
    representationTransitionSeconds: 0.4
    representationHysteresis: 0.03
    maxConcurrentGrassJobs: 8
    grassCompletionBudgetMs: 0.35
    maxGrassUploadsPerFrame: 8
    grassUploadBudgetMs: 0.35
    cellsPerAxis: 125
    cellJitter: 1
    activeRingRadius: 1
    nearGrassPrecomputeChunkPadding: 0
    subChunksPerChunk: 10
    activeSubChunkRadius: 0
    maxSubChunkGenerationsPerFrame: 8
    subChunkGenerationBudgetMsPerFrame: 1
    foregroundFoliageWorkBudgetMsPerFrame: 1.35
    maxFoliageManagementChunksPerFrame: 24
    foliageManagementBudgetMsPerFrame: 0.75
    maxQueuedFoliageManagementWork: 2048
    maxGroundFoliageGenerationsPerFrame: 2
    groundFoliageGenerationBudgetMsPerFrame: 0.75
    maxRenderBatchRebuildsPerFrame: 1
    renderBatchRebuildBudgetMsPerFrame: 0.35
    maxQueuedGrassSubChunkWork: 128
    maxQueuedGroundFoliageGenerationWork: 48
    maxQueuedRenderBatchWork: 96
    densityRadius3: 1
    densityRadius6: 0.6
    densityRadius10: 0.2
    densityBeyond10: 0.06
    uniformScaleRange: {x: 0.9, y: 1.3}
    randomizeYaw: 1
    receiveGrassShadows: 1
    seedOffset: 5000
    billboardRingRadius: 3
    billboardCellsPerAxis: 60
    billboardSpawnChance: 0.05
    billboardUniformScaleRange: {x: 0.9, y: 1.1}
    randomizeBillboardYaw: 1
    billboardSeedOffset: 9000
    enableBillboardRenderFade: 1
    billboardRenderFadeDuration: 0.65
    billboardFadeDitherPixelSize: 1
  flowerSettings:
    activeRingRadius: 3
    uniformScaleRange: {x: 0.4, y: 0.5}
    randomizeYaw: 1
    seedOffset: 18000
  cloverSettings:
    activeRingRadius: 1
    uniformScaleRange: {x: 0.4, y: 0.6}
    randomizeYaw: 1
    seedOffset: 24000
  dandelionSettings:
    activeRingRadius: 2
    uniformScaleRange: {x: 0.4, y: 0.5}
    randomizeYaw: 1
    seedOffset: 32000
  treeSettings:
    seedOffset: 12000
    seedOffset: 27183
  logTerrainGenerationProfile: 1
  maxActiveTerrainDataJobs: 4
  maxActiveFarTerrainJobs: 1
  maxActiveMeshJobs: 6
  maxActiveColliderJobs: 2
  maxTerrainDataResultsAppliedPerFrame: 3
  maxFarTerrainResultsAppliedPerFrame: 1
  maxLODMeshResultsAppliedPerFrame: 16
  maxColliderResultsAppliedPerFrame: 2
  urgentVisibleChunkRingRadius: 1
  maxVisibleChunkContentUpdatesPerFrame: 12
  maxRenderVisibilityChecksPerFrame: 120
  foliageFrustumPaddingChunks: 1
  visibleChunkContentBudgetMsPerFrame: 0.75
  maxFarTerrainTileContentUpdatesPerFrame: 3
  farTerrainTileContentBudgetMsPerFrame: 0.25
  completedRequestApplyBudgetMsPerFrame: 5
  terrainDataApplyBudgetMsPerFrame: 1.25
  farTerrainApplyBudgetMsPerFrame: 0.25
  lodMeshApplyBudgetMsPerFrame: 1.25
  colliderApplyBudgetMsPerFrame: 0.35
```

Project GC setting baseline:

```yaml
  incrementalIl2cppBuild: {}
  gcIncremental: 1
```
