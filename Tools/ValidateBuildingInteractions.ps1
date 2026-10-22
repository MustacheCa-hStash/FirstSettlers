param([switch]$Install)
$ErrorActionPreference='Stop'
$interactionRoot=Split-Path -Parent $PSScriptRoot
Push-Location $interactionRoot
try {
    & Tools/ValidateBuildingPrototype.ps1 -PrepareOnly -UiAssets -Workspace building-interactions
    $interactionProject=Join-Path $interactionRoot '.utmp/building-interactions/unity-project'
    $interactionVersion=((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    $interactionCases=@(
        ,@('BuildKitDimensions.ApplyBatch','dimensions','INTERACTION DIMENSIONS APPLIED:')
        ,@('BuildInteractionValidation.RunBatch','interactions-final','BUILD INTERACTION PASS:')
        ,@('ModularWoodValidation.RunInstalledBatch','modular','MODULAR WOOD PASS:')
        ,@('LimestoneAccessValidation.RunInstalledBatch','access','ACCESS PASS:')
        ,@('LimestoneCourtyardValidation.RunInstalledBatch','courtyard','COURTYARD PASS:')
        ,@('LimestoneKitExtensionValidation.RunInstalledBatch','extension','LIMESTONE EXTENSION PASS:')
        ,@('LimestoneKitValidation.RunInstalledBatch','limestone','LIMESTONE PASS:')
        ,@('TimberInfillKitValidation.RunInstalledBatch','timber','TIMBER INFILL PASS:')
        ,@('BuildingPrototypeInputValidation.RunBatch','input','BUILDING INPUT PASS:')
    )
    foreach($interactionCase in $interactionCases) {
        $interactionLog=Join-Path $interactionRoot ('.utmp/building-interactions/'+$interactionCase[1]+'.log')
        $interactionArgs=@('-batchmode','-force-d3d11','-projectPath',('"'+$interactionProject+'"'),'-executeMethod',$interactionCase[0],'-logFile',('"'+$interactionLog+'"'))
        $interactionProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$interactionVersion/Editor/Unity.exe" -ArgumentList $interactionArgs -WindowStyle Hidden -PassThru
        Write-Output ($interactionCase[0]+' PID '+$interactionProcess.Id)
        while(-not $interactionProcess.WaitForExit(1000)){}
        if($interactionProcess.ExitCode -ne 0 -or -not(Select-String -LiteralPath $interactionLog -SimpleMatch $interactionCase[2] -Quiet)){throw "Interaction validation failed: $interactionLog"}
    }
    if($Install){& 'C:/Program Files/Blender Foundation/Blender 5.0/5.0/python/bin/python.exe' Tools/InstallBuildingInteractions.py;if($LASTEXITCODE -ne 0){throw 'Interaction asset installation failed.'}}
} finally { Pop-Location }
