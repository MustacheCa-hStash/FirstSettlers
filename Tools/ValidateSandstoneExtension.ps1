param([switch]$Install)
$ErrorActionPreference='Stop'
$sandstoneExtensionRoot=Split-Path -Parent $PSScriptRoot
Push-Location $sandstoneExtensionRoot
try {
    & Tools/ValidateBuildingPrototype.ps1 -PrepareOnly -UiAssets -Workspace sandstone-extension
    $sandstoneExtensionProject=Join-Path $sandstoneExtensionRoot '.utmp/sandstone-extension/unity-project'
    $sandstoneExtensionVersion=((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    $sandstoneExtensionCases=@(
        @{Method='SandstoneExtensionValidation.InstallAndRunBatch';Name='validation';Marker='SANDSTONE EXTENSION PASS:'}
        @{Method='SandstoneKitValidation.InstallAndRunBatch';Name='ss01-ss11-regression';Marker='SANDSTONE PASS:'}
        @{Method='StoneKitValidation.RunInstalledBatch';Name='stone-regression';Marker='STONE KIT PASS:'}
        @{Method='ModularWoodValidation.RunInstalledBatch';Name='wood-regression';Marker='MODULAR WOOD PASS:'}
        @{Method='BuildInteractionValidation.RunBatch';Name='interaction-regression';Marker='BUILD INTERACTION PASS:'}
    )
    foreach($sandstoneExtensionCase in $sandstoneExtensionCases) {
        $sandstoneExtensionLog=Join-Path $sandstoneExtensionRoot ('.utmp/sandstone-extension/'+$sandstoneExtensionCase.Name+'.log')
        $sandstoneExtensionArgs=@('-batchmode','-force-d3d11','-projectPath',('"'+$sandstoneExtensionProject+'"'),'-executeMethod',$sandstoneExtensionCase.Method,'-logFile',('"'+$sandstoneExtensionLog+'"'))
        $sandstoneExtensionProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$sandstoneExtensionVersion/Editor/Unity.exe" -ArgumentList $sandstoneExtensionArgs -WindowStyle Hidden -PassThru
        Write-Output "Sandstone extension PID $($sandstoneExtensionProcess.Id): $($sandstoneExtensionCase.Method)"
        while(-not $sandstoneExtensionProcess.WaitForExit(1000)) { }
        if($sandstoneExtensionProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $sandstoneExtensionLog -SimpleMatch $sandstoneExtensionCase.Marker -Quiet)) {throw "Sandstone extension failed; inspect $sandstoneExtensionLog"}
    }
    if($Install) {
        & 'C:/Program Files/Blender Foundation/Blender 5.0/5.0/python/bin/python.exe' Tools/InstallSandstoneExtension.py
        if($LASTEXITCODE -ne 0) {throw 'Validated sandstone extension installation failed.'}
    }
} finally {Pop-Location}
