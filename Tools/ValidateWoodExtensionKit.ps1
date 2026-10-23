param([switch]$Install)
$ErrorActionPreference='Stop'
$woodRoot=Split-Path -Parent $PSScriptRoot
Push-Location $woodRoot
try {
    & Tools/ValidateBuildingPrototype.ps1 -PrepareOnly -UiAssets -Workspace wood-extension
    $woodProject=Join-Path $woodRoot '.utmp/wood-extension/unity-project'
    $woodVersion=((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    foreach($woodCase in @(
        ,@('WoodExtensionKitValidation.InstallAndRunBatch','extension.log','WOOD EXTENSION PASS:')
        ,@('ModularWoodValidation.RunInstalledBatch','modular-regression.log','MODULAR WOOD PASS:')
        ,@('StoneKitValidation.RunInstalledBatch','stone-regression.log','STONE KIT PASS:')
    )) {
        $woodLog=Join-Path $woodRoot ('.utmp/wood-extension/'+$woodCase[1])
        $woodArgs=@('-batchmode','-force-d3d11','-projectPath',('"'+$woodProject+'"'),'-executeMethod',$woodCase[0],'-logFile',('"'+$woodLog+'"'))
        $woodProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$woodVersion/Editor/Unity.exe" -ArgumentList $woodArgs -WindowStyle Hidden -PassThru
        Write-Output "Wood extension validation PID $($woodProcess.Id): $($woodCase[0])"
        while(-not $woodProcess.WaitForExit(1000)) { }
        if($woodProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $woodLog -SimpleMatch $woodCase[2] -Quiet)) {throw "Wood extension validation failed; inspect $woodLog"}
    }
    if($Install) {
        & 'C:/Program Files/Blender Foundation/Blender 5.0/5.0/python/bin/python.exe' Tools/InstallWoodExtensionKit.py
        if($LASTEXITCODE -ne 0){throw 'Validated wood asset copy failed.'}
    }
} finally {Pop-Location}
