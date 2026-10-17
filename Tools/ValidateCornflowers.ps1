param([switch]$BuildAssets,[switch]$Render,[switch]$PrepareOnly)
$ErrorActionPreference='Stop'
$cornflowerRoot=Split-Path -Parent $PSScriptRoot
$cornflowerProject=Join-Path $cornflowerRoot '.utmp/poppy/unity-project'
$cornflowerVersion=((Get-Content "$cornflowerRoot/ProjectSettings/ProjectVersion.txt" -First 1) -split ': ')[1]
$cornflowerUnity="C:/Program Files/Unity/Hub/Editor/$cornflowerVersion/Editor/Unity.exe"
foreach($cornflowerDir in 'Assets/Editor','Assets/Models/Grass+Flowers','Assets/Textures/Flowers','Assets/Materials/M_Flowers','Assets/Prefabs','Assets/Shaders/S_Grass','Packages','ProjectSettings'){
    New-Item -ItemType Directory -Path "$cornflowerProject/$cornflowerDir" -Force | Out-Null
}
New-Item -ItemType Directory -Path "$cornflowerRoot/.utmp/cornflower" -Force | Out-Null
& (Join-Path $PSScriptRoot 'ValidatePoppies.ps1') -PrepareOnly | Out-Null
$cornflowerFiles=@('Assets/Editor/CornflowerAuthoring.cs','Assets/Editor/CornflowerValidation.cs','Assets/Shaders/S_Grass/PoppyPainted.shader',
    'Assets/Models/Grass+Flowers/Cornflower_Painted.fbx','Assets/Models/Grass+Flowers/Cornflower_Painted.json',
    'Assets/Textures/Flowers/T_Cornflower_Painted_Floret.png','Assets/Shaders/TreeNightLighting.hlsl')
foreach($cornflowerOptional in 'Assets/Prefabs/Cornflower.prefab','Assets/Materials/M_Flowers/M_Cornflower.mat','Assets/Models/Grass+Flowers/Cornflower_Painted_Runtime.asset'){
    if(Test-Path -LiteralPath "$cornflowerRoot/$cornflowerOptional"){$cornflowerFiles+=$cornflowerOptional}
}
foreach($cornflowerFile in $cornflowerFiles){
    if(-not(Test-Path -LiteralPath "$cornflowerRoot/$cornflowerFile.meta")){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath "$cornflowerRoot/$cornflowerFile.meta"}
    Copy-Item -LiteralPath "$cornflowerRoot/$cornflowerFile","$cornflowerRoot/$cornflowerFile.meta" -Destination (Split-Path -Parent "$cornflowerProject/$cornflowerFile") -Force
}
Copy-Item -LiteralPath "$cornflowerRoot/Assets/Scripts","$cornflowerRoot/Assets/Settings" -Destination "$cornflowerProject/Assets" -Recurse -Force
foreach($cornflowerName in 'ProjectVersion.txt','TagManager.asset','ProjectSettings.asset','GraphicsSettings.asset','QualitySettings.asset'){
    Copy-Item -LiteralPath "$cornflowerRoot/ProjectSettings/$cornflowerName" -Destination "$cornflowerProject/ProjectSettings" -Force
}
$cornflowerManifest=Get-Content "$cornflowerRoot/Packages/manifest.json" -Raw | ConvertFrom-Json
$cornflowerLock=Get-Content "$cornflowerRoot/Packages/packages-lock.json" -Raw | ConvertFrom-Json
$cornflowerDependencies=[ordered]@{}
foreach($cornflowerDependency in $cornflowerManifest.dependencies.PSObject.Properties){if($cornflowerDependency.Name.StartsWith('com.unity.modules.')){$cornflowerDependencies[$cornflowerDependency.Name]=$cornflowerDependency.Value}}
foreach($cornflowerPackage in 'com.unity.render-pipelines.universal','com.unity.inputsystem','com.unity.ugui','com.unity.burst','com.unity.collections','com.unity.ai.navigation'){$cornflowerDependencies[$cornflowerPackage]=$cornflowerLock.dependencies.$cornflowerPackage.version}
@{dependencies=$cornflowerDependencies} | ConvertTo-Json -Depth 5 | Set-Content "$cornflowerProject/Packages/manifest.json"
if($PrepareOnly){Write-Output $cornflowerProject;return}
$cornflowerMethod=if($BuildAssets){'CornflowerAuthoring.BuildBatch'}elseif($Render){'CornflowerValidation.RenderBatch'}else{'CornflowerValidation.RunBatch'}
$cornflowerLog=Join-Path $cornflowerRoot ('.utmp/cornflower/'+$(if($BuildAssets){'assets'}elseif($Render){'render'}else{'validation'})+'.log')
$cornflowerArguments=@('-batchmode','-projectPath',$cornflowerProject,'-executeMethod',$cornflowerMethod,'-logFile',$cornflowerLog)
if(-not $Render){$cornflowerArguments+='-nographics'}
$cornflowerProcess=Start-Process -FilePath $cornflowerUnity -ArgumentList ($cornflowerArguments | ForEach-Object {'"'+$_+'"'}) -WindowStyle Hidden -PassThru
Write-Output "Cornflower Unity process $($cornflowerProcess.Id); log $cornflowerLog"
$cornflowerProcess.WaitForExit()
if($cornflowerProcess.ExitCode -ne 0){throw "Cornflower Unity validation exited $($cornflowerProcess.ExitCode). See $cornflowerLog"}
if($BuildAssets){
    foreach($cornflowerBuilt in 'Assets/Prefabs/Cornflower.prefab','Assets/Materials/M_Flowers/M_Cornflower.mat','Assets/Models/Grass+Flowers/Cornflower_Painted_Runtime.asset'){
        Copy-Item -LiteralPath "$cornflowerProject/$cornflowerBuilt","$cornflowerProject/$cornflowerBuilt.meta" -Destination (Split-Path -Parent "$cornflowerRoot/$cornflowerBuilt") -Force
    }
    foreach($cornflowerImported in 'Assets/Textures/Flowers/T_Cornflower_Painted_Floret.png.meta','Assets/Models/Grass+Flowers/Cornflower_Painted.fbx.meta'){
        Copy-Item -LiteralPath "$cornflowerProject/$cornflowerImported" -Destination "$cornflowerRoot/$cornflowerImported" -Force
    }
}
if($Render){
    New-Item -ItemType Directory -Path "$cornflowerRoot/ArtReferences/Cornflowers" -Force | Out-Null
    foreach($cornflowerView in 'cornflower-clump','cornflower-back','cornflower-overhead','cornflower-patch','cornflower-side-front','cornflower-side-back','cornflower-side-left','cornflower-side-right'){
        Copy-Item -LiteralPath "$cornflowerProject/.utmp/cornflower/$cornflowerView.png" -Destination "$cornflowerRoot/ArtReferences/Cornflowers/$cornflowerView.png" -Force
    }
}
Write-Output "Cornflower Unity checks passed. Log: $cornflowerLog"
