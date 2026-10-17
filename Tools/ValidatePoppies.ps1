param([switch]$BuildAssets,[switch]$Render,[switch]$PrepareOnly)
$ErrorActionPreference='Stop'
$poppyRoot=Split-Path -Parent $PSScriptRoot
$poppyProject=Join-Path $poppyRoot '.utmp/poppy/unity-project'
$poppyVersion=((Get-Content "$poppyRoot/ProjectSettings/ProjectVersion.txt" -First 1) -split ': ')[1]
$poppyUnity="C:/Program Files/Unity/Hub/Editor/$poppyVersion/Editor/Unity.exe"
foreach($poppyDir in 'Assets/Editor','Assets/Models/Grass+Flowers','Assets/Textures/Flowers','Assets/Materials/M_Flowers','Assets/Prefabs','Assets/Shaders/S_Grass','Packages','ProjectSettings'){
    New-Item -ItemType Directory -Path "$poppyProject/$poppyDir" -Force | Out-Null
}
$poppyFiles=@('Assets/Editor/PoppyAuthoring.cs','Assets/Editor/PoppyValidation.cs','Assets/Shaders/S_Grass/PoppyPainted.shader',
    'Assets/Models/Grass+Flowers/Poppy_Painted.fbx','Assets/Models/Grass+Flowers/Poppy_Painted.json',
    'Assets/Textures/Flowers/T_Poppy_Painted_Atlas.png','Assets/Shaders/TreeNightLighting.hlsl')
foreach($poppyOptional in 'Assets/Prefabs/Poppy.prefab','Assets/Materials/M_Flowers/M_Poppy.mat','Assets/Models/Grass+Flowers/Poppy_Painted_Runtime.asset'){
    if(Test-Path -LiteralPath "$poppyRoot/$poppyOptional"){$poppyFiles+=$poppyOptional}
}
foreach($poppyFile in $poppyFiles){
    if(-not(Test-Path -LiteralPath "$poppyRoot/$poppyFile.meta")){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath "$poppyRoot/$poppyFile.meta"}
    Copy-Item -LiteralPath "$poppyRoot/$poppyFile","$poppyRoot/$poppyFile.meta" -Destination (Split-Path -Parent "$poppyProject/$poppyFile") -Force
}
Copy-Item -LiteralPath "$poppyRoot/Assets/Scripts","$poppyRoot/Assets/Settings" -Destination "$poppyProject/Assets" -Recurse -Force
foreach($poppyName in 'ProjectVersion.txt','TagManager.asset','ProjectSettings.asset','GraphicsSettings.asset','QualitySettings.asset'){
    Copy-Item -LiteralPath "$poppyRoot/ProjectSettings/$poppyName" -Destination "$poppyProject/ProjectSettings" -Force
}
$poppyManifest=Get-Content "$poppyRoot/Packages/manifest.json" -Raw | ConvertFrom-Json
$poppyLock=Get-Content "$poppyRoot/Packages/packages-lock.json" -Raw | ConvertFrom-Json
$poppyDependencies=[ordered]@{}
foreach($poppyDependency in $poppyManifest.dependencies.PSObject.Properties){if($poppyDependency.Name.StartsWith('com.unity.modules.')){$poppyDependencies[$poppyDependency.Name]=$poppyDependency.Value}}
foreach($poppyPackage in 'com.unity.render-pipelines.universal','com.unity.inputsystem','com.unity.ugui','com.unity.burst','com.unity.collections','com.unity.ai.navigation'){$poppyDependencies[$poppyPackage]=$poppyLock.dependencies.$poppyPackage.version}
@{dependencies=$poppyDependencies} | ConvertTo-Json -Depth 5 | Set-Content "$poppyProject/Packages/manifest.json"
if($PrepareOnly){Write-Output $poppyProject;return}
$poppyMethod=if($BuildAssets){'PoppyAuthoring.BuildBatch'}elseif($Render){'PoppyValidation.RenderBatch'}else{'PoppyValidation.RunBatch'}
$poppyLog=Join-Path $poppyRoot ('.utmp/poppy/'+$(if($BuildAssets){'assets'}elseif($Render){'render'}else{'validation'})+'.log')
$poppyArguments=@('-batchmode','-projectPath',$poppyProject,'-executeMethod',$poppyMethod,'-logFile',$poppyLog)
if(-not $Render){$poppyArguments+='-nographics'}
$poppyProcess=Start-Process -FilePath $poppyUnity -ArgumentList ($poppyArguments | ForEach-Object {'"'+$_+'"'}) -WindowStyle Hidden -PassThru
Write-Output "Poppy Unity process $($poppyProcess.Id); log $poppyLog"
$poppyProcess.WaitForExit()
if($poppyProcess.ExitCode -ne 0){throw "Poppy Unity validation exited $($poppyProcess.ExitCode). See $poppyLog"}
if($BuildAssets){
    foreach($poppyBuilt in 'Assets/Prefabs/Poppy.prefab','Assets/Materials/M_Flowers/M_Poppy.mat','Assets/Models/Grass+Flowers/Poppy_Painted_Runtime.asset'){
        Copy-Item -LiteralPath "$poppyProject/$poppyBuilt","$poppyProject/$poppyBuilt.meta" -Destination (Split-Path -Parent "$poppyRoot/$poppyBuilt") -Force
    }
    foreach($poppyImported in 'Assets/Textures/Flowers/T_Poppy_Painted_Atlas.png.meta','Assets/Models/Grass+Flowers/Poppy_Painted.fbx.meta'){
        Copy-Item -LiteralPath "$poppyProject/$poppyImported" -Destination "$poppyRoot/$poppyImported" -Force
    }
}
if($Render){
    New-Item -ItemType Directory -Path "$poppyRoot/ArtReferences/Poppies" -Force | Out-Null
    foreach($poppyView in 'poppy-clump','poppy-back','poppy-overhead','poppy-patch','poppy-side-front','poppy-side-back','poppy-side-left','poppy-side-right'){
        Copy-Item -LiteralPath "$poppyProject/.utmp/poppy/$poppyView.png" -Destination "$poppyRoot/ArtReferences/Poppies/$poppyView.png" -Force
    }
}
Write-Output "Poppy Unity checks passed. Log: $poppyLog"
