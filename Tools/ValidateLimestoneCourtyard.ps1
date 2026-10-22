param([switch]$Install)
$ErrorActionPreference='Stop'
$courtyardRoot=Split-Path -Parent $PSScriptRoot
Push-Location $courtyardRoot
try {
    & Tools/ValidateBuildingPrototype.ps1 -PrepareOnly -UiAssets -Workspace limestone-kit
    $courtyardProject=Join-Path $courtyardRoot '.utmp/limestone-kit/unity-project'
    $courtyardVersion=((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    $courtyardCases=@(
        ,@('LimestoneCourtyardValidation.InstallAndRunBatch','courtyard-unity.log','COURTYARD PASS:')
        ,@('TimberInfillKitValidation.RunInstalledBatch','courtyard-timber-regression.log','TIMBER INFILL PASS:')
        ,@('LimestoneKitExtensionValidation.RunInstalledBatch','courtyard-l04-l09-regression.log','LIMESTONE EXTENSION PASS:')
        ,@('LimestoneKitValidation.RunInstalledBatch','courtyard-l01-l03-regression.log','LIMESTONE PASS:')
        ,@('StoneKitValidation.RunInstalledBatch','courtyard-stone-regression.log','STONE KIT PASS:')
        ,@('ModularWoodValidation.RunInstalledBatch','courtyard-modular-regression.log','MODULAR WOOD PASS:')
    )
    foreach($courtyardCase in $courtyardCases) {
        $courtyardLog=Join-Path $courtyardRoot ('.utmp/limestone-kit/'+$courtyardCase[1])
        $courtyardArgs=@('-batchmode','-force-d3d11','-projectPath',('"'+$courtyardProject+'"'),'-executeMethod',$courtyardCase[0],'-logFile',('"'+$courtyardLog+'"'))
        $courtyardProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$courtyardVersion/Editor/Unity.exe" -ArgumentList $courtyardArgs -WindowStyle Hidden -PassThru
        Write-Output "Courtyard validation PID $($courtyardProcess.Id): $($courtyardCase[0])"
        while(-not $courtyardProcess.WaitForExit(1000)){}
        if($courtyardProcess.ExitCode -ne 0 -or -not(Select-String -LiteralPath $courtyardLog -SimpleMatch $courtyardCase[2] -Quiet)){throw "Courtyard validation failed: $courtyardLog"}
    }
    if($Install){& 'C:/Program Files/Blender Foundation/Blender 5.0/5.0/python/bin/python.exe' Tools/InstallLimestoneCourtyard.py;if($LASTEXITCODE -ne 0){throw 'Selective courtyard install failed.'}}
}finally{Pop-Location}
