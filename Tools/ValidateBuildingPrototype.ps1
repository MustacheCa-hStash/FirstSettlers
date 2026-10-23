param([switch]$PrepareOnly, [switch]$CompileOnly, [switch]$Render, [switch]$InputChecks, [switch]$ShadowChecks, [switch]$WattleChecks, [switch]$PreviewChecks, [switch]$StairChecks, [switch]$RoofChecks, [switch]$FramingChecks, [switch]$ColliderDebugChecks, [switch]$IntersectionChecks, [switch]$ModularChecks, [switch]$InstalledAssets, [switch]$UiChecks, [switch]$UiAssets, [switch]$StairUiAssets, [switch]$InventoryChecks, [switch]$InventoryInputChecks, [switch]$StoneChecks, [switch]$BakedStoneWallChecks, [switch]$BakedStoneKitChecks, [switch]$BakedSplitPlankChecks, [switch]$SquareInfillAssets, [ValidatePattern('^[a-z0-9-]+$')][string]$Workspace='building-prototype')
$ErrorActionPreference = 'Stop'
if ($StairUiAssets) { $UiChecks = $true }
if ($InstalledAssets) { $ModularChecks = $true }
# Full-span assets supersede legacy dimensional fixtures. Keep native input validation separate.
if (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets/Models/Buildings/Wood/Modular/SplitPlankWall_4.00Wx3.00Hx0.25D.fbx')) {
    if (-not $PrepareOnly -and -not $CompileOnly -and -not $InputChecks -and -not $UiChecks -and -not $UiAssets -and -not $InventoryChecks -and -not $InventoryInputChecks -and -not $StoneChecks -and -not $BakedStoneWallChecks -and -not $BakedStoneKitChecks -and -not $BakedSplitPlankChecks) { $ModularChecks = $true }
}
$buildingRoot = Split-Path -Parent $PSScriptRoot
$buildingWorkRelative = '.utmp/' + $Workspace
$buildingWork = Join-Path $buildingRoot $buildingWorkRelative
$buildingProject = Join-Path $buildingWork 'unity-project'
$buildingVersion = ((Get-Content (Join-Path $buildingRoot 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
$buildingEditor = "C:/Program Files/Unity/Hub/Editor/$buildingVersion/Editor"
New-Item -ItemType Directory -Path $buildingWork -Force | Out-Null
Push-Location $buildingRoot
try {
    foreach ($buildingKind in 'runtime','editor') {
        $buildingAssembly = if ($buildingKind -eq 'runtime') { 'Assembly-CSharp' } else { 'Assembly-CSharp-Editor' }
        $buildingResponse = Get-ChildItem 'Library/Bee/artifacts' -Filter "$buildingAssembly.rsp" -Recurse |
            Where-Object { $_.FullName -match 'E\.dag' } | Select-Object -First 1
        if (-not $buildingResponse) { throw 'Compile the main Unity project once to obtain reference response files.' }
        $buildingLines = Get-Content -LiteralPath $buildingResponse.FullName | Where-Object {
            $_ -notmatch '^".*\.cs"$' -and $_ -notmatch '^-(out|refout):'
        }
        if ($buildingKind -eq 'editor') {
            $buildingLines = $buildingLines | ForEach-Object {
                $_ -replace 'Library/Bee/artifacts/[^/]+/Assembly-CSharp(\.ref)?\.dll', ($buildingWorkRelative + '/Assembly-CSharp.dll')
            }
        }
        $buildingLines += '-out:"' + $buildingWorkRelative + '/' + $buildingAssembly + '.dll"'
        $buildingFolder = if ($buildingKind -eq 'runtime') { 'Assets/Scripts' } else { 'Assets/Editor' }
        $buildingSources = @(Get-ChildItem $buildingFolder -Filter '*.cs' -Recurse)
        if ($buildingKind -eq 'runtime') { $buildingSources += @(Get-ChildItem 'Assets/Models' -Filter '*.cs' -Recurse) }
        # Nested asmdef sources are already referenced by Unity's response file; do not compile them
        # into Assembly-CSharp again (e.g. the separate unsafe profiling counter assembly).
        $buildingAssemblyFolders = @(Get-ChildItem $buildingFolder -Filter '*.asmdef' -Recurse | ForEach-Object { $_.Directory.FullName + [IO.Path]::DirectorySeparatorChar })
        $buildingSources = @($buildingSources | Where-Object {
            $buildingSourcePath = $_.FullName
            -not @($buildingAssemblyFolders | Where-Object { $buildingSourcePath.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count
        })
        foreach ($buildingSource in $buildingSources) {
            $buildingLines += '"' + $buildingSource.FullName.Replace('\','/') + '"'
        }
        $buildingRsp = Join-Path $buildingWork "$buildingKind.rsp"
        $buildingLines | Set-Content -LiteralPath $buildingRsp -Encoding utf8
        & "$buildingEditor/Data/netcorerun/netcorerun.exe" "$buildingEditor/Data/DotNetSdkRoslyn/csc.dll" "@$buildingRsp"
        if ($LASTEXITCODE -ne 0) { throw "$buildingKind compilation failed." }
    }
    if ($CompileOnly) { return }
    # Only prototype-owned new files receive metadata here.
    $buildingOwned = @(Get-ChildItem 'Assets/Scripts/Building' -File -Recurse | Where-Object { $_.Extension -ne '.meta' })
    $buildingOwned += @(Get-ChildItem 'Assets/Resources/Building' -File -Recurse | Where-Object { $_.Extension -ne '.meta' })
    $buildingOwned += @(Get-Item 'Assets/Scripts/WorldManager/WorldGroundSurface.cs','Assets/Editor/BuildingPrototypeSetup.cs','Assets/Editor/BuildingPrototypeValidation.cs','Assets/Editor/BuildingPrototypeRenderValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/BuildingPrototypeInputValidation.cs','Assets/Editor/BuildingShadowValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/BayPostSetup.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/W21StairSetup.cs','Assets/Editor/W21StairValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/ThatchRoofSetup.cs','Assets/Editor/ThatchRoofValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/FloorFramingSetup.cs','Assets/Editor/BuildCollisionAuthoring.cs','Assets/Editor/BuildFramingValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/BuildColliderDebugValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/BuildIntersectionRegressionValidation.cs','Assets/Editor/ModularWoodSetup.cs','Assets/Editor/ModularWoodValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/OpeningWallSetup.cs','Assets/Editor/OpeningWallValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/SquareInfillSetup.cs','Assets/Editor/SquareInfillValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/StairExtensionSetup.cs','Assets/Editor/StairExtensionValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Scripts/PlayerMovement/SmoothWalkSurface.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/BuildStoneUiAuthoring.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/StoneKitSetup.cs','Assets/Editor/StoneKitValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/LimestoneKitSetup.cs','Assets/Editor/LimestoneKitValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/LimestoneKitExtensionSetup.cs','Assets/Editor/LimestoneKitExtensionValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/TimberInfillKitSetup.cs','Assets/Editor/TimberInfillKitValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/LimestoneCourtyardSetup.cs','Assets/Editor/LimestoneCourtyardValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/LimestoneAccessSetup.cs','Assets/Editor/LimestoneAccessValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/BakedStoneWallSetup.cs','Assets/Editor/BakedStoneWallValidation.cs','Assets/Editor/BakedStoneKitSetup.cs','Assets/Editor/BakedStoneKitValidation.cs','Assets/Editor/BakedSplitPlankWallSetup.cs','Assets/Editor/BakedSplitPlankWallValidation.cs','Assets/Editor/BakedFurnishingsSetup.cs','Assets/Editor/BakedFurnishingsValidation.cs','Assets/Shaders/BakedStoneWallMatte.shader')
    $buildingOwned += @(Get-Item 'Assets/Shaders/TerrainSkyAccess.hlsl')
    $buildingOwned += @(Get-Item 'Assets/Editor/WoodSurfaceSetup.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/FurnitureKitSetup.cs','Assets/Editor/FurnitureKitValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/InventoryUiValidation.cs')
    $buildingOwned += @(Get-Item 'Assets/Scripts/UI/LocalPlayerMenus.cs','Assets/Scripts/UI/InventoryView.cs')
    $buildingOwned += @(Get-ChildItem 'Assets/Resources/Inventory' -File -Recurse | Where-Object { $_.Extension -ne '.meta' })
    $buildingOwned += @(Get-Item 'Assets/Editor/SplitPlankWallSetup.cs','Assets/Editor/WattleWallSetup.cs','Assets/Editor/WattleWallValidation.cs','Assets/Shaders/BuildingWoodMatte.shader','Assets/Shaders/WattleValidationDepthReveal.shader')
    foreach ($buildingFile in $buildingOwned) {
        if (-not (Test-Path -LiteralPath ($buildingFile.FullName + '.meta'))) {
            "fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath ($buildingFile.FullName + '.meta')
        }
    }
    foreach ($buildingFolder in 'Assets/Scripts/Building','Assets/Resources/Building','Assets/Resources/Inventory') {
        if (-not (Test-Path -LiteralPath "$buildingFolder.meta")) {
            "fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))`nfolderAsset: yes`nDefaultImporter:`n  externalObjects: {}" | Set-Content -LiteralPath "$buildingFolder.meta"
        }
    }
    New-Item -ItemType Directory -Path "$buildingProject/Assets/Editor","$buildingProject/Assets/Resources","$buildingProject/Packages","$buildingProject/ProjectSettings" -Force | Out-Null
    Copy-Item -LiteralPath 'Assets/Scripts' -Destination "$buildingProject/Assets" -Recurse -Force
    if ($Render -or $ShadowChecks -or $WattleChecks -or $PreviewChecks -or $RoofChecks -or $FramingChecks -or $ColliderDebugChecks -or $IntersectionChecks -or $ModularChecks -or $UiChecks -or $UiAssets -or $StoneChecks -or $BakedStoneWallChecks -or $BakedStoneKitChecks -or $BakedSplitPlankChecks) { Copy-Item -LiteralPath 'Assets/Settings' -Destination "$buildingProject/Assets" -Recurse -Force }
    Copy-Item -LiteralPath 'Assets/Scripts.meta' -Destination "$buildingProject/Assets" -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath 'Assets/Resources/Building' -Destination "$buildingProject/Assets/Resources" -Recurse -Force
    Copy-Item -LiteralPath 'Assets/Resources/Building.meta' -Destination "$buildingProject/Assets/Resources" -Force
    if (Test-Path -LiteralPath 'Assets/Resources/Lighting') {
        Copy-Item -LiteralPath 'Assets/Resources/Lighting' -Destination "$buildingProject/Assets/Resources" -Recurse -Force
        if (Test-Path -LiteralPath 'Assets/Resources/Lighting.meta') { Copy-Item -LiteralPath 'Assets/Resources/Lighting.meta' -Destination "$buildingProject/Assets/Resources" -Force }
    }
    Copy-Item -LiteralPath 'Assets/Resources/Inventory' -Destination "$buildingProject/Assets/Resources" -Recurse -Force
    Copy-Item -LiteralPath 'Assets/Resources/Inventory.meta' -Destination "$buildingProject/Assets/Resources" -Force
    # Authored wall dependencies must be present for both logical and render checks.
    foreach ($buildingAssetRoot in 'Models','Textures','Materials') {
        New-Item -ItemType Directory -Path "$buildingProject/Assets/$buildingAssetRoot" -Force | Out-Null
        if (Test-Path -LiteralPath "Assets/$buildingAssetRoot/Buildings") {
            Copy-Item -LiteralPath "Assets/$buildingAssetRoot/Buildings" -Destination "$buildingProject/Assets/$buildingAssetRoot" -Recurse -Force
        }
        if (Test-Path -LiteralPath "Assets/$buildingAssetRoot/Buildings.meta") {
            Copy-Item -LiteralPath "Assets/$buildingAssetRoot/Buildings.meta" -Destination "$buildingProject/Assets/$buildingAssetRoot" -Force
        }
    }
    New-Item -ItemType Directory -Path "$buildingProject/Assets/Shaders" -Force | Out-Null
    foreach ($buildingShaderFile in 'BuildingWoodMatte.shader','BakedStoneWallMatte.shader','TreeNightLighting.hlsl','TerrainSkyAccess.hlsl','LocalFlameLighting.hlsl','WattleValidationDepthReveal.shader') {
        Copy-Item -LiteralPath "Assets/Shaders/$buildingShaderFile","Assets/Shaders/$buildingShaderFile.meta" -Destination "$buildingProject/Assets/Shaders" -Force
    }
    foreach ($buildingName in 'BuildingPrototypeSetup','BuildingPrototypeValidation','BuildingPrototypeRenderValidation','BuildingPrototypeInputValidation','SplitPlankWallSetup','WattleWallSetup','WattleWallValidation','BuildingShadowValidation','BayPostSetup','W21StairSetup','W21StairValidation','ThatchRoofSetup','ThatchRoofValidation','FloorFramingSetup','BuildCollisionAuthoring','BuildFramingValidation','BuildColliderDebugValidation','BuildIntersectionRegressionValidation','ModularWoodSetup','ModularWoodValidation') {
        Copy-Item -LiteralPath "Assets/Editor/$buildingName.cs","Assets/Editor/$buildingName.cs.meta" -Destination "$buildingProject/Assets/Editor" -Force
    }
    foreach ($buildingName in 'OpeningWallSetup','OpeningWallValidation','StairExtensionSetup','StairExtensionValidation','StoneKitSetup','StoneKitValidation','BakedStoneWallSetup','BakedStoneWallValidation','BakedStoneKitSetup','BakedStoneKitValidation','BakedSplitPlankWallSetup','BakedSplitPlankWallValidation','BakedFurnishingsSetup','BakedFurnishingsValidation','TorchKitSetup','LocalFlameValidation','LocalFlameSurfaceValidation','SquareInfillSetup','SquareInfillValidation','WoodSurfaceSetup','FurnitureKitSetup','FurnitureKitValidation') {
        Copy-Item -LiteralPath "Assets/Editor/$buildingName.cs","Assets/Editor/$buildingName.cs.meta" -Destination "$buildingProject/Assets/Editor" -Force
    }
    Copy-Item -LiteralPath 'Assets/Editor/BuildStoneUiAuthoring.cs','Assets/Editor/BuildStoneUiAuthoring.cs.meta' -Destination "$buildingProject/Assets/Editor" -Force
    foreach ($buildingName in 'BuildKitDimensions','BuildInteractionValidation','BuildGablePlacementValidation') {
        if (Test-Path -LiteralPath "Assets/Editor/$buildingName.cs") {
            if (-not (Test-Path -LiteralPath "Assets/Editor/$buildingName.cs.meta")) { "fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath "Assets/Editor/$buildingName.cs.meta" }
            Copy-Item -LiteralPath "Assets/Editor/$buildingName.cs","Assets/Editor/$buildingName.cs.meta" -Destination "$buildingProject/Assets/Editor" -Force
        }
    }
    foreach ($buildingName in 'LimestoneKitSetup','LimestoneKitValidation','LimestoneKitExtensionSetup','LimestoneKitExtensionValidation','TimberInfillKitSetup','TimberInfillKitValidation','LimestoneCourtyardSetup','LimestoneCourtyardValidation','LimestoneAccessSetup','LimestoneAccessValidation') {
        Copy-Item -LiteralPath "Assets/Editor/$buildingName.cs","Assets/Editor/$buildingName.cs.meta" -Destination "$buildingProject/Assets/Editor" -Force
    }
    foreach ($buildingName in 'WoodExtensionKitSetup','WoodExtensionKitValidation','StoneCompletionKitSetup','StoneCompletionKitValidation') {
        Copy-Item -LiteralPath "Assets/Editor/$buildingName.cs","Assets/Editor/$buildingName.cs.meta" -Destination "$buildingProject/Assets/Editor" -Force
    }
    if (Test-Path -LiteralPath 'ArtReferences/StoneBuildKit/Completion/stone-completion-models.json') {
        New-Item -ItemType Directory -Path "$buildingProject/ArtReferences/StoneBuildKit/Completion" -Force | Out-Null
        Copy-Item -LiteralPath 'ArtReferences/StoneBuildKit/Completion/stone-completion-models.json' -Destination "$buildingProject/ArtReferences/StoneBuildKit/Completion" -Force
    }
    if (Test-Path -LiteralPath 'ArtReferences/IronAgeWood/Extension/wood-extension-models.json') {
        New-Item -ItemType Directory -Path "$buildingProject/ArtReferences/IronAgeWood/Extension" -Force | Out-Null
        Copy-Item -LiteralPath 'ArtReferences/IronAgeWood/Extension/wood-extension-models.json' -Destination "$buildingProject/ArtReferences/IronAgeWood/Extension" -Force
    }
    if (Test-Path -LiteralPath 'ArtReferences/IronAgeFurniture/Models/furniture-models.json') {
        New-Item -ItemType Directory -Path "$buildingProject/ArtReferences/IronAgeFurniture/Models" -Force | Out-Null
        Copy-Item -LiteralPath 'ArtReferences/IronAgeFurniture/Models/furniture-models.json' -Destination "$buildingProject/ArtReferences/IronAgeFurniture/Models" -Force
    }
    if (Test-Path -LiteralPath 'ArtReferences/LimestoneBuildKit/TimberInfill/timber-infill-models.json') {
        New-Item -ItemType Directory -Path "$buildingProject/ArtReferences/LimestoneBuildKit/TimberInfill" -Force | Out-Null
        Copy-Item -LiteralPath 'ArtReferences/LimestoneBuildKit/TimberInfill/timber-infill-models.json' -Destination "$buildingProject/ArtReferences/LimestoneBuildKit/TimberInfill" -Force
    }
    if (Test-Path -LiteralPath 'ArtReferences/LimestoneBuildKit/Courtyard/courtyard-models.json') {
        New-Item -ItemType Directory -Path "$buildingProject/ArtReferences/LimestoneBuildKit/Courtyard" -Force | Out-Null
        Copy-Item -LiteralPath 'ArtReferences/LimestoneBuildKit/Courtyard/courtyard-models.json' -Destination "$buildingProject/ArtReferences/LimestoneBuildKit/Courtyard" -Force
    }
    if (Test-Path -LiteralPath 'ArtReferences/LimestoneBuildKit/Access/access-models.json') {
        New-Item -ItemType Directory -Path "$buildingProject/ArtReferences/LimestoneBuildKit/Access" -Force | Out-Null
        Copy-Item -LiteralPath 'ArtReferences/LimestoneBuildKit/Access/access-models.json' -Destination "$buildingProject/ArtReferences/LimestoneBuildKit/Access" -Force
    }
    Copy-Item -LiteralPath 'Assets/Editor/InventoryUiValidation.cs','Assets/Editor/InventoryUiValidation.cs.meta' -Destination "$buildingProject/Assets/Editor" -Force
    foreach ($buildingName in 'ProjectVersion.txt','TagManager.asset','ProjectSettings.asset') {
        Copy-Item -LiteralPath "ProjectSettings/$buildingName" -Destination "$buildingProject/ProjectSettings" -Force
    }
    $buildingManifest = Get-Content 'Packages/manifest.json' -Raw | ConvertFrom-Json
    $buildingLock = Get-Content 'Packages/packages-lock.json' -Raw | ConvertFrom-Json
    $buildingDependencies = [ordered]@{}
    foreach ($buildingDependency in $buildingManifest.dependencies.PSObject.Properties) {
        if ($buildingDependency.Name.StartsWith('com.unity.modules.')) { $buildingDependencies[$buildingDependency.Name] = $buildingDependency.Value }
    }
    foreach ($buildingPackage in 'com.unity.render-pipelines.universal','com.unity.inputsystem','com.unity.ugui','com.unity.burst','com.unity.collections','com.unity.ai.navigation') {
        $buildingDependencies[$buildingPackage] = $buildingLock.dependencies.$buildingPackage.version
    }
    @{ dependencies = $buildingDependencies } | ConvertTo-Json -Depth 5 | Set-Content "$buildingProject/Packages/manifest.json"
    if ($PrepareOnly) { Write-Output "Prepared $buildingProject"; return }
    $buildingLog = Join-Path $buildingWork $(if ($ModularChecks) { 'modular-wood.log' } elseif ($IntersectionChecks) { 'intersection-regression.log' } elseif ($ColliderDebugChecks) { 'collider-debug.log' } elseif ($FramingChecks) { 'framing.log' } elseif ($RoofChecks) { 'roof.log' } elseif ($StairChecks) { 'stairs.log' } elseif ($PreviewChecks) { 'preview.log' } elseif ($WattleChecks) { 'wattle.log' } elseif ($ShadowChecks) { 'shadows.log' } elseif ($Render) { 'render.log' } elseif ($InputChecks) { 'input.log' } else { 'validation.log' })
    $buildingMethod = if ($ModularChecks) { 'ModularWoodValidation.RunBatch' } elseif ($IntersectionChecks) { 'BuildIntersectionRegressionValidation.RunBatch' } elseif ($ColliderDebugChecks) { 'BuildColliderDebugValidation.RunBatch' } elseif ($FramingChecks) { 'BuildFramingValidation.RunBatch' } elseif ($RoofChecks) { 'ThatchRoofValidation.RunBatch' } elseif ($StairChecks) { 'W21StairValidation.RunBatch' } elseif ($PreviewChecks) { 'WattleWallValidation.RunPreviewBatch' } elseif ($WattleChecks) { 'WattleWallValidation.RunBatch' } elseif ($ShadowChecks) { 'BuildingShadowValidation.RunBatch' } elseif ($Render) { 'BuildingPrototypeRenderValidation.RunBatch' } elseif ($InputChecks) { 'BuildingPrototypeInputValidation.RunBatch' } else { 'BuildingPrototypeValidation.RunBatch' }
    if ($UiChecks -or $UiAssets) {
        $buildingLog = Join-Path $buildingWork 'stone-ui.log'
        $buildingMethod = if ($StairUiAssets) { 'BuildStoneUiAuthoring.StairThumbnailsBatch' } elseif ($UiAssets) { 'BuildStoneUiAuthoring.BuildBatch' } else { 'BuildStoneUiAuthoring.ValidateBatch' }
    }
    if ($InventoryChecks -or $InventoryInputChecks) {
        $buildingLog = Join-Path $buildingWork $(if ($InventoryInputChecks) { 'inventory-input.log' } else { 'inventory-ui.log' })
        $buildingMethod = if ($InventoryInputChecks) { 'InventoryUiValidation.RunInputBatch' } else { 'InventoryUiValidation.RunBatch' }
    }
    $buildingArgs = @('-batchmode','-projectPath',('"'+$buildingProject+'"'),'-executeMethod',$buildingMethod,'-logFile',('"'+$buildingLog+'"'))
    if ($InstalledAssets) { $buildingArgs[4] = 'ModularWoodValidation.RunInstalledBatch' }
    if ($StoneChecks) { $buildingArgs[4] = if ($InstalledAssets) { 'StoneKitValidation.RunInstalledBatch' } else { 'StoneKitValidation.RunBatch' }; $buildingLog = Join-Path $buildingWork 'stone-kit.log'; $buildingArgs[6] = '"'+$buildingLog+'"' }
    if ($BakedStoneWallChecks) {
        $buildingArgs[4] = if ($InstalledAssets) { 'BakedStoneWallValidation.RunInstalledBatch' } else { 'BakedStoneWallValidation.InstallAndRunBatch' }
        $buildingLog = Join-Path $buildingWork 'baked-stone-wall.log'
        $buildingArgs[6] = '"'+$buildingLog+'"'
    }
    if ($BakedStoneKitChecks) {
        $buildingArgs[4] = if ($InstalledAssets) { 'BakedStoneKitValidation.RunInstalledBatch' } else { 'BakedStoneKitValidation.InstallAndRunBatch' }
        $buildingLog = Join-Path $buildingWork 'baked-stone-kit.log'
        $buildingArgs[6] = '"'+$buildingLog+'"'
    }
    if ($BakedSplitPlankChecks) {
        $buildingArgs[4] = if ($InstalledAssets) { 'BakedSplitPlankWallValidation.RunInstalledBatch' } else { 'BakedSplitPlankWallValidation.InstallAndRunBatch' }
        $buildingLog = Join-Path $buildingWork 'baked-split-plank.log'
        $buildingArgs[6] = '"'+$buildingLog+'"'
    }
    if ($SquareInfillAssets) { $buildingArgs[4] = 'SquareInfillSetup.InstallBatch' }
    if (-not $Render -and -not $ShadowChecks -and -not $WattleChecks -and -not $PreviewChecks -and -not $RoofChecks -and -not $FramingChecks -and -not $ColliderDebugChecks -and -not $IntersectionChecks -and -not $ModularChecks -and -not $UiChecks -and -not $UiAssets -and -not $InventoryChecks -and -not $InventoryInputChecks -and -not $StoneChecks -and -not $BakedStoneWallChecks -and -not $BakedStoneKitChecks -and -not $BakedSplitPlankChecks) { $buildingArgs += '-nographics' }
    $buildingProcess = Start-Process -FilePath "$buildingEditor/Unity.exe" -ArgumentList $buildingArgs -WindowStyle Hidden -PassThru
    Write-Output "Isolated Unity validation PID $($buildingProcess.Id); log $buildingLog"
    $buildingProcess.Id | Set-Content (Join-Path $buildingWork 'validation-pid.txt')
    while (-not $buildingProcess.WaitForExit(1000)) { }
    if ($buildingProcess.ExitCode -ne 0) { throw "Unity validation failed; inspect $buildingLog" }
    Write-Output 'Building prototype validation passed.'
} finally { Pop-Location }
