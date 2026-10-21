param([switch]$PrepareOnly)
$ErrorActionPreference='Stop'
$standRoot=Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Path "$standRoot/.utmp/stands" -Force | Out-Null
$standProject=& "$standRoot/Tools/ValidateRocks.ps1" -PrepareOnly -Exclusions
New-Item -ItemType Directory -Path "$standProject/Assets/Resources/Foliage","$standProject/Assets/ScriptableObjects" -Force | Out-Null
Copy-Item -LiteralPath "$standRoot/Assets/Resources/Foliage/TreeDistribution.asset","$standRoot/Assets/Resources/Foliage/TreeDistribution.asset.meta" -Destination "$standProject/Assets/Resources/Foliage" -Force
Copy-Item -LiteralPath "$standRoot/Assets/ScriptableObjects/WorldObjects" -Destination "$standProject/Assets/ScriptableObjects" -Recurse -Force
Copy-Item -LiteralPath "$standRoot/Assets/Editor/TreeStandValidation.cs","$standRoot/Assets/Editor/TreeStandValidation.cs.meta" -Destination "$standProject/Assets/Editor" -Force
foreach($standValidator in 'TreeRegistryValidation','DistantTreeValidation','TreeGameplayValidation'){
    Copy-Item -LiteralPath "$standRoot/Assets/Editor/$standValidator.cs","$standRoot/Assets/Editor/$standValidator.cs.meta" -Destination "$standProject/Assets/Editor" -Force
}
# Copy the scene's actual near-prefab geometry for scale checks, without opening/saving the scene.
Copy-Item -LiteralPath "$standRoot/Assets/Scenes/SmearScene.unity" -Destination "$standProject/ValidationSceneBindings.txt" -Force
$standScene=Get-Content -LiteralPath "$standRoot/Assets/Scenes/SmearScene.unity" -Raw
$standQueue=[System.Collections.Generic.Queue[string]]::new()
foreach($standMatch in [regex]::Matches($standScene,'    (?:mapleTreePrefab|sugarMapleTreePrefab|birchAspenTreePrefab|beechTreePrefab|spruceTreePrefab|whitePineTreePrefab|oakTreePrefab|grasslandWillowTreePrefab): \{fileID: [^,}\r\n]+, guid: (\w+)')){$standQueue.Enqueue($standMatch.Groups[1].Value)}
$standCopied=@{}
while($standQueue.Count -gt 0){
    $standGuid=$standQueue.Dequeue();if($standCopied.ContainsKey($standGuid)){continue};$standCopied[$standGuid]=$true
    $standSourceMeta=& rg -l "^guid: $standGuid`$" "$standRoot/Assets" -g '*.meta' | Select-Object -First 1
    if(-not $standSourceMeta){continue}
    $standSource=$standSourceMeta.Substring(0,$standSourceMeta.Length-5)
    if([IO.Path]::GetExtension($standSource) -notin '.prefab','.asset','.fbx'){continue}
    $standRelative=[IO.Path]::GetRelativePath($standRoot,$standSource)
    $standTarget=Join-Path $standProject $standRelative
    New-Item -ItemType Directory -Path (Split-Path -Parent $standTarget) -Force | Out-Null
    Copy-Item -LiteralPath $standSource -Destination $standTarget -Force
    Copy-Item -LiteralPath $standSourceMeta -Destination ($standTarget+'.meta') -Force
    if([IO.Path]::GetExtension($standSource) -ne '.fbx'){
        foreach($standReference in [regex]::Matches((Get-Content -LiteralPath $standSource -Raw),'guid: ([0-9a-f]{32})')){$standQueue.Enqueue($standReference.Groups[1].Value)}
    }
}
if($PrepareOnly){Write-Output $standProject;return}
$standVersion=((Get-Content "$standRoot/ProjectSettings/ProjectVersion.txt" -First 1) -split ': ')[1]
$standUnity="C:/Program Files/Unity/Hub/Editor/$standVersion/Editor/Unity.exe"
$standLog=Join-Path $standRoot '.utmp/stands/validation.log'
$standArgs=@('-batchmode','-nographics','-projectPath',$standProject,'-executeMethod','TreeStandValidation.RunBatch','-logFile',$standLog)
$standProcess=Start-Process -FilePath $standUnity -ArgumentList ($standArgs | ForEach-Object {'"'+$_+'"'}) -WindowStyle Hidden -PassThru
Write-Output "Stand Unity process $($standProcess.Id); log $standLog"
