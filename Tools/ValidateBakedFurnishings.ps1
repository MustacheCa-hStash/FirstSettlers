param([switch]$PrepareOnly,[switch]$Install)
$ErrorActionPreference='Stop'
$furnishingRoot=Split-Path -Parent $PSScriptRoot
Push-Location $furnishingRoot
try {
    & Tools/ValidateBuildingPrototype.ps1 -PrepareOnly -UiAssets -Workspace baked-stone-wall
    if($PrepareOnly){return}
    $furnishingProject=Join-Path $furnishingRoot '.utmp/baked-stone-wall/unity-project'
    $furnishingVersion=((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    foreach($furnishingCase in @(@('BakedFurnishingsValidation.InstallAndRunBatch','baked-furnishings-unity.log','FURNITURE PASS:'),@('LocalFlameValidation.RunPlayBatch','baked-furnishings-flame.log','LOCAL FLAME PLAY PASS:'))) {
        $furnishingLog=Join-Path $furnishingRoot ('.utmp/'+$furnishingCase[1])
        $furnishingArgs=@('-batchmode','-force-d3d11','-projectPath',('"'+$furnishingProject+'"'),'-executeMethod',$furnishingCase[0],'-logFile',('"'+$furnishingLog+'"'))
        $furnishingProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$furnishingVersion/Editor/Unity.exe" -ArgumentList $furnishingArgs -WindowStyle Hidden -Wait -PassThru
        if($furnishingProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $furnishingLog -SimpleMatch $furnishingCase[2] -Quiet)){throw "Furnishing check failed: $furnishingLog"}
    }
    if($Install){python Tools/InstallBakedFurnishings.py;if($LASTEXITCODE -ne 0){throw 'Validated asset copy failed.'}}
} finally {Pop-Location}
