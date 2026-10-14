param([switch]$BuildAssets,[switch]$Render,[switch]$PrepareOnly,[switch]$Benchmark)
$ErrorActionPreference='Stop'
$daisyRoot=Split-Path -Parent $PSScriptRoot
$daisyProject=Join-Path $daisyRoot '.utmp/daisy/unity-project'
$daisyVersion=((Get-Content "$daisyRoot/ProjectSettings/ProjectVersion.txt" -First 1) -split ': ')[1]
$daisyUnity="C:/Program Files/Unity/Hub/Editor/$daisyVersion/Editor/Unity.exe"
New-Item -ItemType Directory -Path "$daisyProject/Assets/Editor","$daisyProject/Assets/Models/Grass+Flowers","$daisyProject/Assets/Textures/Flowers","$daisyProject/Assets/Materials/M_Flowers","$daisyProject/Assets/Prefabs","$daisyProject/Assets/Shaders/S_Grass","$daisyProject/Packages","$daisyProject/ProjectSettings" -Force | Out-Null
$daisyFiles=@('Assets/Editor/DaisyWeedAuthoring.cs','Assets/Editor/DaisyWeedValidation.cs','Assets/Editor/DaisyWeedPerformance.cs','Assets/Shaders/S_Grass/DaisyWeedsPainted.shader','Assets/Models/Grass+Flowers/DaisyWeeds_Painted.fbx','Assets/Models/Grass+Flowers/DaisyWeeds_Painted.json','Assets/Textures/Flowers/T_DaisyWeeds_Painted_Atlas.png')
foreach($daisyFile in $daisyFiles){if(-not(Test-Path -LiteralPath "$daisyRoot/$daisyFile.meta")){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath "$daisyRoot/$daisyFile.meta"}}
Copy-Item -LiteralPath "$daisyRoot/Assets/Scripts" -Destination "$daisyProject/Assets" -Recurse -Force
Copy-Item -LiteralPath "$daisyRoot/Assets/Settings" -Destination "$daisyProject/Assets" -Recurse -Force
$daisyFiles+=@('Assets/Prefabs/daisyWeeds.prefab','Assets/Materials/M_Flowers/M_DaisyWeeds.mat','Assets/Models/Grass+Flowers/daisyWeeds.fbx','Assets/Textures/Flowers/daisyWeeds.png','Assets/Shaders/S_Grass/DaisyWeedsColor.shader','Assets/Shaders/TreeNightLighting.hlsl')
if(Test-Path "$daisyRoot/Assets/Models/Grass+Flowers/DaisyWeeds_Painted_Runtime.asset"){$daisyFiles+='Assets/Models/Grass+Flowers/DaisyWeeds_Painted_Runtime.asset'}
foreach($daisyFile in $daisyFiles){Copy-Item -LiteralPath "$daisyRoot/$daisyFile","$daisyRoot/$daisyFile.meta" -Destination (Split-Path -Parent "$daisyProject/$daisyFile") -Force}
foreach($daisyName in 'ProjectVersion.txt','TagManager.asset','ProjectSettings.asset','GraphicsSettings.asset','QualitySettings.asset'){Copy-Item -LiteralPath "$daisyRoot/ProjectSettings/$daisyName" -Destination "$daisyProject/ProjectSettings" -Force}
$daisyManifest=Get-Content "$daisyRoot/Packages/manifest.json" -Raw | ConvertFrom-Json
$daisyLock=Get-Content "$daisyRoot/Packages/packages-lock.json" -Raw | ConvertFrom-Json
$daisyDependencies=[ordered]@{}
foreach($daisyDependency in $daisyManifest.dependencies.PSObject.Properties){if($daisyDependency.Name.StartsWith('com.unity.modules.')){$daisyDependencies[$daisyDependency.Name]=$daisyDependency.Value}}
foreach($daisyPackage in 'com.unity.render-pipelines.universal','com.unity.inputsystem','com.unity.ugui','com.unity.burst','com.unity.collections','com.unity.ai.navigation'){$daisyDependencies[$daisyPackage]=$daisyLock.dependencies.$daisyPackage.version}
@{dependencies=$daisyDependencies} | ConvertTo-Json -Depth 5 | Set-Content "$daisyProject/Packages/manifest.json"
if($PrepareOnly){Write-Output $daisyProject;return}
$daisyMethod=if($Benchmark){'DaisyWeedPerformance.RunBatch'}elseif($BuildAssets){'DaisyWeedAuthoring.BuildBatch'}elseif($Render){'DaisyWeedValidation.RenderBatch'}else{'DaisyWeedValidation.RunBatch'}
$daisyLog=Join-Path $daisyRoot ('.utmp/daisy/'+$(if($Benchmark){'benchmark'}elseif($BuildAssets){'assets'}elseif($Render){'render'}else{'validation'})+'.log')
$daisyArguments=@('-batchmode','-projectPath',$daisyProject,'-executeMethod',$daisyMethod,'-logFile',$daisyLog)
if(-not $Render -and -not $Benchmark){$daisyArguments+='-nographics'}
$daisyProcess=Start-Process -FilePath $daisyUnity -ArgumentList ($daisyArguments | ForEach-Object {'"'+$_+'"'}) -WindowStyle Hidden -PassThru
Write-Output "Daisy Unity process $($daisyProcess.Id); log $daisyLog"
