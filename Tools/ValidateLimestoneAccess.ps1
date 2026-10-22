param([switch]$Install)
$ErrorActionPreference='Stop'
$accessRoot=Split-Path -Parent $PSScriptRoot
Push-Location $accessRoot
try {
    & Tools/ValidateBuildingPrototype.ps1 -PrepareOnly -UiAssets -Workspace limestone-kit
    $accessProject=Join-Path $accessRoot '.utmp/limestone-kit/unity-project'
    $accessVersion=((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    $accessCases=@(
        ,@('LimestoneAccessValidation.InstallAndRunBatch','access-unity.log','ACCESS PASS:')
        ,@('LimestoneCourtyardValidation.RunInstalledBatch','access-courtyard-regression.log','COURTYARD PASS:')
        ,@('TimberInfillKitValidation.RunInstalledBatch','access-timber-regression.log','TIMBER INFILL PASS:')
        ,@('LimestoneKitExtensionValidation.RunInstalledBatch','access-l04-l09-regression.log','LIMESTONE EXTENSION PASS:')
        ,@('LimestoneKitValidation.RunInstalledBatch','access-l01-l03-regression.log','LIMESTONE PASS:')
        ,@('StoneKitValidation.RunInstalledBatch','access-stone-regression.log','STONE KIT PASS:')
        ,@('ModularWoodValidation.RunInstalledBatch','access-modular-regression.log','MODULAR WOOD PASS:')
    )
    foreach($accessCase in $accessCases) {
        $accessLog=Join-Path $accessRoot ('.utmp/limestone-kit/'+$accessCase[1])
        $accessArgs=@('-batchmode','-force-d3d11','-projectPath',('"'+$accessProject+'"'),'-executeMethod',$accessCase[0],'-logFile',('"'+$accessLog+'"'))
        $accessProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$accessVersion/Editor/Unity.exe" -ArgumentList $accessArgs -WindowStyle Hidden -PassThru
        Write-Output "Access validation PID $($accessProcess.Id): $($accessCase[0])"
        while(-not $accessProcess.WaitForExit(1000)){}
        if($accessProcess.ExitCode -ne 0 -or -not(Select-String -LiteralPath $accessLog -SimpleMatch $accessCase[2] -Quiet)){throw "Access validation failed: $accessLog"}
    }
    if($Install){& 'C:/Program Files/Blender Foundation/Blender 5.0/5.0/python/bin/python.exe' Tools/InstallLimestoneAccess.py;if($LASTEXITCODE -ne 0){throw 'Selective access install failed.'};& Tools/BuildLimestoneAccessSheet.ps1}
}finally{Pop-Location}
