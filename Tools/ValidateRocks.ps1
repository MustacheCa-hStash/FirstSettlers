param([switch]$PrepareOnly, [switch]$BuildAssets, [switch]$Render, [switch]$Investigate, [switch]$Benchmark, [switch]$Surfaces, [switch]$Exclusions, [Nullable[int]]$Seed = $null)
$ErrorActionPreference='Stop'
if($Benchmark){$Investigate=$true}
$rockRoot=Split-Path -Parent $PSScriptRoot
$rockProject=Join-Path $rockRoot '.utmp/rocks/unity-project'
$rockVersion=((Get-Content "$rockRoot/ProjectSettings/ProjectVersion.txt" -First 1) -split ': ')[1]
$rockUnity="C:/Program Files/Unity/Hub/Editor/$rockVersion/Editor/Unity.exe"
New-Item -ItemType Directory -Path "$rockProject/Assets/Editor","$rockProject/Assets/Models/Rocks","$rockProject/Assets/Textures/Rock","$rockProject/Assets/Shaders","$rockProject/Assets/Resources","$rockProject/ProjectSettings","$rockProject/Packages" -Force | Out-Null
# Generate metadata only for assets owned by this feature, preserving existing Unity identities.
$rockOwned=@(Get-ChildItem "$rockRoot/Assets/Models/Rocks/Presets","$rockRoot/Assets/Textures/Rock/Presets","$rockRoot/Assets/Scripts/FoliageGeneration/Rocks" -File -Recurse | Where-Object {$_.Extension -ne '.meta'})
$rockOwned+=@(Get-Item "$rockRoot/Assets/Editor/RockPresetSetup.cs","$rockRoot/Assets/Editor/RockPresetValidation.cs","$rockRoot/Assets/Editor/RockPerformanceInvestigation.cs","$rockRoot/Assets/Editor/RockPerformanceValidation.cs","$rockRoot/Assets/Shaders/WeatheredRock.shader")
foreach($rockFile in $rockOwned){if(-not(Test-Path -LiteralPath ($rockFile.FullName+'.meta'))){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath ($rockFile.FullName+'.meta')}}
foreach($rockDir in 'Assets/Models/Rocks/Presets','Assets/Textures/Rock/Presets'){
    if(-not(Test-Path -LiteralPath "$rockRoot/$rockDir.meta")){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))`nfolderAsset: yes`nDefaultImporter:`n  externalObjects: {}" | Set-Content -LiteralPath "$rockRoot/$rockDir.meta"}
}
Copy-Item -LiteralPath "$rockRoot/Assets/Scripts" -Destination "$rockProject/Assets" -Recurse -Force
Copy-Item -LiteralPath "$rockRoot/Assets/Settings" -Destination "$rockProject/Assets" -Recurse -Force
foreach($rockPath in 'Models/Rocks/Presets','Textures/Rock/Presets'){
    Copy-Item -LiteralPath "$rockRoot/Assets/$rockPath" -Destination (Split-Path -Parent "$rockProject/Assets/$rockPath") -Recurse -Force
    Copy-Item -LiteralPath "$rockRoot/Assets/$rockPath.meta" -Destination "$rockProject/Assets/$rockPath.meta" -Force
}
foreach($rockName in 'RockPresetSetup','RockPresetValidation','RockPerformanceInvestigation','RockPerformanceValidation','FoliagePublicationValidation','QueryTargetValidation','BillboardGrassStreamingValidation','GrassStreamingValidation','FoliageExclusionProfileAuthoring') {Copy-Item -LiteralPath "$rockRoot/Assets/Editor/$rockName.cs","$rockRoot/Assets/Editor/$rockName.cs.meta" -Destination "$rockProject/Assets/Editor" -Force}
if(Test-Path "$rockRoot/Assets/ScriptableObjects/FoliageExclusions"){
    New-Item -ItemType Directory -Path "$rockProject/Assets/ScriptableObjects" -Force | Out-Null
    Copy-Item -LiteralPath "$rockRoot/Assets/ScriptableObjects/FoliageExclusions","$rockRoot/Assets/ScriptableObjects/FoliageExclusions.meta" -Destination "$rockProject/Assets/ScriptableObjects" -Recurse -Force
}
if($Exclusions){
    New-Item -ItemType Directory -Path "$rockProject/Assets/Prefabs","$rockProject/Assets/Models/Grass+Flowers" -Force | Out-Null
    Copy-Item -Path "$rockRoot/Assets/Prefabs/Clover*" -Destination "$rockProject/Assets/Prefabs" -Force
    Copy-Item -Path "$rockRoot/Assets/Models/Grass+Flowers/*" -Destination "$rockProject/Assets/Models/Grass+Flowers" -Force
    Copy-Item -LiteralPath "$rockRoot/Assets/Models/Grass+Flowers.meta" -Destination "$rockProject/Assets/Models/Grass+Flowers.meta" -Force
    Copy-Item -LiteralPath "$rockRoot/Assets/Editor/FoliageExclusionValidation.cs","$rockRoot/Assets/Editor/FoliageExclusionValidation.cs.meta" -Destination "$rockProject/Assets/Editor" -Force
    foreach($rockValidation in 'ShoreAndTreeHabitatValidation','WaterGenerationValidation'){
        Copy-Item -LiteralPath "$rockRoot/Assets/Editor/$rockValidation.cs","$rockRoot/Assets/Editor/$rockValidation.cs.meta" -Destination "$rockProject/Assets/Editor" -Force
    }
}
if($Investigate){
    New-Item -ItemType Directory -Path "$rockProject/Assets/Scenes","$rockProject/Assets/ScriptableObjects" -Force | Out-Null
    Copy-Item -LiteralPath "$rockRoot/Assets/Scenes/SmearScene.unity","$rockRoot/Assets/Scenes/SmearScene.unity.meta" -Destination "$rockProject/Assets/Scenes" -Force
    Copy-Item -LiteralPath "$rockRoot/Assets/ScriptableObjects/WorldObjects" -Destination "$rockProject/Assets/ScriptableObjects" -Recurse -Force
}
foreach($rockName in 'WeatheredRock.shader','TreeNightLighting.hlsl') {Copy-Item -LiteralPath "$rockRoot/Assets/Shaders/$rockName","$rockRoot/Assets/Shaders/$rockName.meta" -Destination "$rockProject/Assets/Shaders" -Force}
if(Test-Path "$rockRoot/Assets/Resources/Rocks") {Copy-Item -LiteralPath "$rockRoot/Assets/Resources/Rocks" -Destination "$rockProject/Assets/Resources" -Recurse -Force;Copy-Item -LiteralPath "$rockRoot/Assets/Resources/Rocks.meta" -Destination "$rockProject/Assets/Resources" -Force}
foreach($rockName in 'ProjectVersion.txt','TagManager.asset','ProjectSettings.asset','GraphicsSettings.asset','QualitySettings.asset') {Copy-Item -LiteralPath "$rockRoot/ProjectSettings/$rockName" -Destination "$rockProject/ProjectSettings" -Force}
$rockManifest=Get-Content "$rockRoot/Packages/manifest.json" -Raw | ConvertFrom-Json
$rockLock=Get-Content "$rockRoot/Packages/packages-lock.json" -Raw | ConvertFrom-Json
$rockDependencies=[ordered]@{}
foreach($rockDependency in $rockManifest.dependencies.PSObject.Properties){if($rockDependency.Name.StartsWith('com.unity.modules.')){$rockDependencies[$rockDependency.Name]=$rockDependency.Value}}
foreach($rockPackage in 'com.unity.render-pipelines.universal','com.unity.inputsystem','com.unity.ugui','com.unity.burst','com.unity.collections','com.unity.ai.navigation'){$rockDependencies[$rockPackage]=$rockLock.dependencies.$rockPackage.version}
@{dependencies=$rockDependencies} | ConvertTo-Json -Depth 5 | Set-Content "$rockProject/Packages/manifest.json"
if($PrepareOnly){Write-Output $rockProject;return}
$rockMethod=if($Exclusions){'ShoreAndTreeHabitatValidation.RunBatch'}elseif($Surfaces){'RockPresetSetup.SurfaceBatch'}elseif($Benchmark){'RockPerformanceInvestigation.BenchmarkBatch'}elseif($Investigate){'RockPerformanceInvestigation.RunBatch'}elseif($BuildAssets){'RockPresetSetup.BuildBatch'}elseif($Render){'RockPresetValidation.RenderBatch'}else{'RockPresetValidation.RunBatch'}
$rockLog=Join-Path $rockRoot ('.utmp/rocks/'+$(if($Exclusions){'exclusions'}elseif($Surfaces){'surfaces'}elseif($Investigate){'investigation'}elseif($BuildAssets){'assets'}elseif($Render){'render'}else{'validation'})+'.log')
$rockArgs=@('-batchmode','-projectPath',$rockProject,'-executeMethod',$rockMethod,'-logFile',$rockLog)
if($null -ne $Seed){$rockArgs+=@('-rockBenchmarkSeed',$Seed.ToString())}
if(-not $Render -and -not $Benchmark){$rockArgs+='-nographics'}
$rockProcess=Start-Process -FilePath $rockUnity -ArgumentList ($rockArgs | ForEach-Object {'"'+$_+'"'}) -WindowStyle Hidden -PassThru
Write-Output "Rock Unity process $($rockProcess.Id); log $rockLog"
