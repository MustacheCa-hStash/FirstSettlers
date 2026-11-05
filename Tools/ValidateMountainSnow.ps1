param([switch]$PrepareOnly, [switch]$SurfaceChecks, [switch]$SeparationChecks)
$ErrorActionPreference = 'Stop'
if ($SurfaceChecks -and $SeparationChecks) { throw 'Choose SurfaceChecks or SeparationChecks for a run.' }
$snowRoot = Split-Path -Parent $PSScriptRoot
$snowWork = Join-Path $snowRoot '.utmp/mountain-snow'
$snowProject = Join-Path $snowWork 'unity-project'
$snowVersion = ((Get-Content (Join-Path $snowRoot 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
New-Item -ItemType Directory -Path "$snowProject/Assets/Editor","$snowProject/Packages","$snowProject/ProjectSettings" -Force | Out-Null
Copy-Item -LiteralPath "$snowRoot/Assets/Scripts" -Destination "$snowProject/Assets" -Recurse -Force
foreach ($snowFile in 'MountainSnowValidation.cs','MountainSnowDistributionValidation.cs','SurfaceBlendValidation.cs') {
    Copy-Item -LiteralPath "$snowRoot/Assets/Editor/$snowFile" -Destination "$snowProject/Assets/Editor" -Force
}
if ($SeparationChecks) {
    foreach ($snowFile in 'SnowRockSeparationValidation.cs','TerrainMaterialInspector.cs','TerrainMaterialInspectorRules.cs',
        'TerrainMaterialInspectorValidation.cs','CliffTextureValidation.cs') {
        Copy-Item -LiteralPath "$snowRoot/Assets/Editor/$snowFile" -Destination "$snowProject/Assets/Editor" -Force
    }
}
if ($SurfaceChecks) {
    foreach ($snowFile in 'TerrainSurfaceSlopeValidation.cs','TerrainSlopeValidation.cs','ForestFloorValidation.cs',
        'BillboardGrassStreamingValidation.cs','GrassStreamingValidation.cs','GroundFoliageStreamingValidation.cs') {
        Copy-Item -LiteralPath "$snowRoot/Assets/Editor/$snowFile" -Destination "$snowProject/Assets/Editor" -Force
    }
}
foreach ($snowSetting in 'ProjectVersion.txt','ProjectSettings.asset','TagManager.asset') {
    Copy-Item -LiteralPath "$snowRoot/ProjectSettings/$snowSetting" -Destination "$snowProject/ProjectSettings" -Force
}
Copy-Item -LiteralPath "$snowRoot/Assets/Settings" -Destination "$snowProject/Assets" -Recurse -Force
# Copy only the terrain material's texture dependencies, retaining their original GUIDs.
$snowMaterial = "$snowRoot/Assets/Materials/M_Terrain/M_TerrainBase.mat"
$snowGuids = @([regex]::Matches((Get-Content $snowMaterial -Raw), 'guid: ([a-f0-9]{32})') | ForEach-Object { $_.Groups[1].Value })
foreach ($snowMeta in Get-ChildItem "$snowRoot/Assets/Textures" -Filter '*.meta' -Recurse -File) {
    if ((Get-Content $snowMeta.FullName -First 2) -match ('guid: (' + ($snowGuids -join '|') + ')')) {
        $snowRelative = $snowMeta.FullName.Substring($snowRoot.Length + 1)
        $snowDestination = Join-Path $snowProject $snowRelative
        New-Item -ItemType Directory -Path (Split-Path -Parent $snowDestination) -Force | Out-Null
        Copy-Item -LiteralPath $snowMeta.FullName -Destination $snowDestination -Force
        Copy-Item -LiteralPath $snowMeta.FullName.Substring(0,$snowMeta.FullName.Length-5) -Destination $snowDestination.Substring(0,$snowDestination.Length-5) -Force
    }
}
foreach ($snowFile in 'Assets/Materials/M_Terrain/M_TerrainBase.mat','Assets/Materials/M_Terrain/M_TerrainBase.mat.meta',
    'Assets/Shaders/S_Terrain/CustomStylizedTerrain.shader','Assets/Shaders/S_Terrain/CustomStylizedTerrain.shader.meta',
    'Assets/Shaders/S_Terrain/GrassGroundSurface.hlsl','Assets/Shaders/TreeNightLighting.hlsl',
    'Assets/Shaders/TerrainSkyAccess.hlsl','Assets/Shaders/LocalFlameLighting.hlsl','Assets/Shaders/WorldLandformFields.hlsl') {
    $snowDestination = Join-Path $snowProject $snowFile
    New-Item -ItemType Directory -Path (Split-Path -Parent $snowDestination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $snowRoot $snowFile) -Destination $snowDestination -Force
}
$snowManifest = Get-Content "$snowRoot/Packages/manifest.json" -Raw | ConvertFrom-Json
$snowLock = Get-Content "$snowRoot/Packages/packages-lock.json" -Raw | ConvertFrom-Json
$snowDependencies = [ordered]@{}
foreach ($snowDependency in $snowManifest.dependencies.PSObject.Properties) {
    if ($snowDependency.Name.StartsWith('com.unity.modules.')) { $snowDependencies[$snowDependency.Name] = $snowDependency.Value }
}
foreach ($snowPackage in 'com.unity.render-pipelines.universal','com.unity.inputsystem','com.unity.ugui','com.unity.burst','com.unity.collections','com.unity.ai.navigation') {
    $snowDependencies[$snowPackage] = $snowLock.dependencies.$snowPackage.version
}
@{dependencies=$snowDependencies} | ConvertTo-Json -Depth 4 | Set-Content "$snowProject/Packages/manifest.json"
if ($PrepareOnly) { return }
$snowLog = Join-Path $snowWork $(if ($SeparationChecks) { 'separation-validation.log' } elseif ($SurfaceChecks) { 'surface-validation.log' } else { 'validation.log' })
$snowMethod = if ($SeparationChecks) { 'SnowRockSeparationValidation.RunBatch' } elseif ($SurfaceChecks) { 'TerrainSurfaceSlopeValidation.RunBatch' } else { 'MountainSnowDistributionValidation.RunBatch' }
$snowOutput = if ($SeparationChecks) { '/ArtReferences/SnowRockSeparation' } elseif ($SurfaceChecks) { '/ArtReferences/TerrainSurfaces' } else { '/ArtReferences/MountainSnow' }
$snowArgs = @('-batchmode','-force-d3d11','-burst-force-sync-compilation','-projectPath',('"'+$snowProject+'"'),
    '-executeMethod',$snowMethod,'-snowOutput',('"'+$snowRoot+$snowOutput+'"'),'-logFile',('"'+$snowLog+'"'))
$snowProcess = Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$snowVersion/Editor/Unity.exe" -ArgumentList $snowArgs -WindowStyle Hidden -PassThru
$snowProcess.WaitForExit()
if ($snowProcess.ExitCode -ne 0) { throw "Mountain snow validation failed: $snowLog" }
$snowMarker = if ($SeparationChecks) { 'SNOW ROCK SEPARATION PASS' } elseif ($SurfaceChecks) { 'TERRAIN SURFACE SLOPE PASS' } else { 'MOUNTAIN SNOW DISTRIBUTION PASS' }
if (-not (Select-String -LiteralPath $snowLog -Pattern $snowMarker -Quiet)) { throw "Missing completion marker: $snowLog" }
Write-Output "Mountain snow validation passed: $snowLog"
