param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$p6Root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'ValidateTerrainSculptingInput.ps1') -PrepareOnly
$p6Project = Join-Path $p6Root '.utmp/building-prototype/unity-project'
foreach ($p6File in @('Assets/Editor/TerrainSculptingWorldGraphicsValidation.cs','Assets/Editor/TerrainSculptingWorldGraphicsValidation.cs.meta',
    'Assets/Editor/TerrainSkyAccessDebug.shader','Assets/Editor/TerrainSkyAccessDebug.shader.meta',
    'Assets/Editor/TerrainMaterialInspector.cs','Assets/Editor/TerrainMaterialInspector.cs.meta',
    'Assets/Editor/TerrainMaterialInspectorRules.cs','Assets/Editor/TerrainMaterialInspectorRules.cs.meta',
    'Assets/Editor/TerrainMaterialInspectorValidation.cs','Assets/Editor/TerrainMaterialInspectorValidation.cs.meta',
    'Assets/Editor/CliffTextureValidation.cs','Assets/Editor/CliffTextureValidation.cs.meta',
    'Assets/Materials/M_Terrain/M_TerrainBase.mat','Assets/Materials/M_Terrain/M_TerrainBase.mat.meta',
    'Assets/Editor/ResidentGrassCullingValidation.cs','Assets/Editor/ResidentGrassCullingValidation.cs.meta',
    'Assets/Shaders/S_Terrain/CustomStylizedTerrain.shader','Assets/Shaders/S_Terrain/CustomStylizedTerrain.shader.meta',
    'Assets/Shaders/S_Terrain/GrassGroundSurface.hlsl','Assets/Shaders/S_Terrain/GrassGroundSurface.hlsl.meta',
    'Assets/Shaders/GrassCompact.compute','Assets/Shaders/GrassCompact.compute.meta',
    'Assets/Shaders/GrassIndirectInstance.hlsl','Assets/Shaders/GrassIndirectInstance.hlsl.meta',
    'Assets/Shaders/ForestScatterInstance.hlsl','Assets/Shaders/ForestScatterInstance.hlsl.meta',
    'Assets/Shaders/S_Grass/GrassInstancedTerrainTint.shader','Assets/Shaders/S_Grass/GrassInstancedTerrainTint.shader.meta',
    'Assets/Shaders/S_Grass/ForestFernInstanced.shader','Assets/Shaders/S_Grass/ForestFernInstanced.shader.meta',
    'Assets/Resources/Foliage/ForestScatterCompact.compute','Assets/Resources/Foliage/ForestScatterCompact.compute.meta',
    'Assets/Textures/Rock/Presets/PaintedStone_Albedo.png','Assets/Textures/Rock/Presets/PaintedStone_Albedo.png.meta')) {
    $p6Destination = Join-Path $p6Project $p6File
    New-Item -ItemType Directory -Path (Split-Path -Parent $p6Destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $p6Root $p6File) -Destination $p6Destination -Force
}
if ($PrepareOnly) { return }
$p6Version = ((Get-Content (Join-Path $p6Root 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
$p6Log = Join-Path $p6Root '.utmp/terrain-sculpting/p6-graphics.log'
$p6Args = @('-batchmode','-force-d3d11','-projectPath',('"'+$p6Project+'"'),'-executeMethod','TerrainSculptingWorldGraphicsValidation.RunBatch',
    '-terrainSourceRoot',('"'+$p6Root+'"'),'-logFile',('"'+$p6Log+'"'))
$p6Process = Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$p6Version/Editor/Unity.exe" -ArgumentList $p6Args -WindowStyle Hidden -PassThru
$p6Process.WaitForExit()
if ($p6Process.ExitCode -ne 0) { throw "P6 graphics validation failed: $p6Log" }
if (-not (Select-String -LiteralPath $p6Log -Pattern 'TERRAIN SCULPTING P6 GRAPHICS PASS:' -Quiet)) { throw 'Unity exited without the P6 graphics completion marker.' }
Write-Output "P6 graphics validation passed: $p6Log"
