param([switch]$Install)
$ErrorActionPreference='Stop'
$stoneCompletionRoot=Split-Path -Parent $PSScriptRoot
Push-Location $stoneCompletionRoot
try {
    & Tools/ValidateBuildingPrototype.ps1 -PrepareOnly -UiAssets -Workspace stone-completion
    $stoneCompletionProject=Join-Path $stoneCompletionRoot '.utmp/stone-completion/unity-project'
    $stoneCompletionVersion=((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    foreach($stoneCompletionCase in @(
        ,@('StoneCompletionKitValidation.InstallAndRunBatch','completion.log','STONE COMPLETION PASS:')
        ,@('StoneKitValidation.RunInstalledBatch','stone-regression.log','STONE KIT PASS:')
        ,@('ModularWoodValidation.RunInstalledBatch','wood-regression.log','MODULAR WOOD PASS:')
        ,@('LimestoneAccessValidation.RunInstalledBatch','access-regression.log','ACCESS PASS:')
    )) {
        $stoneCompletionLog=Join-Path $stoneCompletionRoot ('.utmp/stone-completion/'+$stoneCompletionCase[1])
        $stoneCompletionArgs=@('-batchmode','-force-d3d11','-projectPath',('"'+$stoneCompletionProject+'"'),'-executeMethod',$stoneCompletionCase[0],'-logFile',('"'+$stoneCompletionLog+'"'))
        $stoneCompletionProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$stoneCompletionVersion/Editor/Unity.exe" -ArgumentList $stoneCompletionArgs -WindowStyle Hidden -PassThru
        Write-Output "Stone completion PID $($stoneCompletionProcess.Id): $($stoneCompletionCase[0])"
        while(-not $stoneCompletionProcess.WaitForExit(1000)) { }
        if($stoneCompletionProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $stoneCompletionLog -SimpleMatch $stoneCompletionCase[2] -Quiet)) {throw "Stone completion validation failed; inspect $stoneCompletionLog"}
    }
    if($Install) {
        & 'C:/Program Files/Blender Foundation/Blender 5.0/5.0/python/bin/python.exe' Tools/InstallStoneCompletionKit.py
        if($LASTEXITCODE -ne 0){throw 'Validated stone asset copy failed.'}
    }
} finally {Pop-Location}
