param([switch]$PrepareOnly, [switch]$CompileOnly, [switch]$Render, [switch]$InputChecks, [switch]$ShadowChecks, [switch]$WattleChecks, [switch]$PreviewChecks, [switch]$StairChecks)
$ErrorActionPreference = 'Stop'
$buildingRoot = Split-Path -Parent $PSScriptRoot
$buildingWork = Join-Path $buildingRoot '.utmp/building-prototype'
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
                $_ -replace 'Library/Bee/artifacts/[^/]+/Assembly-CSharp(\.ref)?\.dll', '.utmp/building-prototype/Assembly-CSharp.dll'
            }
        }
        $buildingLines += '-out:".utmp/building-prototype/' + $buildingAssembly + '.dll"'
        $buildingFolder = if ($buildingKind -eq 'runtime') { 'Assets/Scripts' } else { 'Assets/Editor' }
        $buildingSources = @(Get-ChildItem $buildingFolder -Filter '*.cs' -Recurse)
        if ($buildingKind -eq 'runtime') { $buildingSources += @(Get-ChildItem 'Assets/Models' -Filter '*.cs' -Recurse) }
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
    $buildingOwned += @(Get-Item 'Assets/Scripts/PlayerMovement/SmoothWalkSurface.cs')
    $buildingOwned += @(Get-Item 'Assets/Editor/SplitPlankWallSetup.cs','Assets/Editor/WattleWallSetup.cs','Assets/Editor/WattleWallValidation.cs','Assets/Shaders/BuildingWoodMatte.shader','Assets/Shaders/WattleValidationDepthReveal.shader')
    foreach ($buildingFile in $buildingOwned) {
        if (-not (Test-Path -LiteralPath ($buildingFile.FullName + '.meta'))) {
            "fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath ($buildingFile.FullName + '.meta')
        }
    }
    foreach ($buildingFolder in 'Assets/Scripts/Building','Assets/Resources/Building') {
        if (-not (Test-Path -LiteralPath "$buildingFolder.meta")) {
            "fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))`nfolderAsset: yes`nDefaultImporter:`n  externalObjects: {}" | Set-Content -LiteralPath "$buildingFolder.meta"
        }
    }
    New-Item -ItemType Directory -Path "$buildingProject/Assets/Editor","$buildingProject/Assets/Resources","$buildingProject/Packages","$buildingProject/ProjectSettings" -Force | Out-Null
    Copy-Item -LiteralPath 'Assets/Scripts' -Destination "$buildingProject/Assets" -Recurse -Force
    if ($Render -or $ShadowChecks -or $WattleChecks -or $PreviewChecks) { Copy-Item -LiteralPath 'Assets/Settings' -Destination "$buildingProject/Assets" -Recurse -Force }
    Copy-Item -LiteralPath 'Assets/Scripts.meta' -Destination "$buildingProject/Assets" -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath 'Assets/Resources/Building' -Destination "$buildingProject/Assets/Resources" -Recurse -Force
    Copy-Item -LiteralPath 'Assets/Resources/Building.meta' -Destination "$buildingProject/Assets/Resources" -Force
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
    foreach ($buildingShaderFile in 'BuildingWoodMatte.shader','TreeNightLighting.hlsl','WattleValidationDepthReveal.shader') {
        Copy-Item -LiteralPath "Assets/Shaders/$buildingShaderFile","Assets/Shaders/$buildingShaderFile.meta" -Destination "$buildingProject/Assets/Shaders" -Force
    }
    foreach ($buildingName in 'BuildingPrototypeSetup','BuildingPrototypeValidation','BuildingPrototypeRenderValidation','BuildingPrototypeInputValidation','SplitPlankWallSetup','WattleWallSetup','WattleWallValidation','BuildingShadowValidation','BayPostSetup','W21StairSetup','W21StairValidation') {
        Copy-Item -LiteralPath "Assets/Editor/$buildingName.cs","Assets/Editor/$buildingName.cs.meta" -Destination "$buildingProject/Assets/Editor" -Force
    }
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
    $buildingLog = Join-Path $buildingWork $(if ($StairChecks) { 'stairs.log' } elseif ($PreviewChecks) { 'preview.log' } elseif ($WattleChecks) { 'wattle.log' } elseif ($ShadowChecks) { 'shadows.log' } elseif ($Render) { 'render.log' } elseif ($InputChecks) { 'input.log' } else { 'validation.log' })
    $buildingMethod = if ($StairChecks) { 'W21StairValidation.RunBatch' } elseif ($PreviewChecks) { 'WattleWallValidation.RunPreviewBatch' } elseif ($WattleChecks) { 'WattleWallValidation.RunBatch' } elseif ($ShadowChecks) { 'BuildingShadowValidation.RunBatch' } elseif ($Render) { 'BuildingPrototypeRenderValidation.RunBatch' } elseif ($InputChecks) { 'BuildingPrototypeInputValidation.RunBatch' } else { 'BuildingPrototypeValidation.RunBatch' }
    $buildingArgs = @('-batchmode','-projectPath',('"'+$buildingProject+'"'),'-executeMethod',$buildingMethod,'-logFile',('"'+$buildingLog+'"'))
    if (-not $Render -and -not $ShadowChecks -and -not $WattleChecks -and -not $PreviewChecks) { $buildingArgs += '-nographics' }
    $buildingProcess = Start-Process -FilePath "$buildingEditor/Unity.exe" -ArgumentList $buildingArgs -WindowStyle Hidden -PassThru
    Write-Output "Isolated Unity validation PID $($buildingProcess.Id); log $buildingLog"
    $buildingProcess.Id | Set-Content (Join-Path $buildingWork 'validation-pid.txt')
    while (-not $buildingProcess.WaitForExit(1000)) { }
    if ($buildingProcess.ExitCode -ne 0) { throw "Unity validation failed; inspect $buildingLog" }
    Write-Output 'Building prototype validation passed.'
} finally { Pop-Location }
