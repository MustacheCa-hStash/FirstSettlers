param([switch]$Install)
$ErrorActionPreference='Stop'
$masonryRoot=Split-Path -Parent $PSScriptRoot
Push-Location $masonryRoot
try {
    & Tools/ValidateBuildingPrototype.ps1 -PrepareOnly -UiAssets -Workspace masonry-expansion
    $masonryProject=Join-Path $masonryRoot '.utmp/masonry-expansion/unity-project'
    $masonryVersion=((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    $masonryCases=@(
        @{Method='MasonryExpansionKitValidation.InstallAndRunBatch';Name='validation';Marker='MASONRY EXPANSION PASS:'}
        @{Method='ModularWoodValidation.RunInstalledBatch';Name='wood-regression';Marker='MODULAR WOOD PASS:'}
        @{Method='StoneKitValidation.RunInstalledBatch';Name='stone-regression';Marker='STONE KIT PASS:'}
        @{Method='BuildInteractionValidation.RunBatch';Name='interaction-regression';Marker='BUILD INTERACTION PASS:'}
    )
    foreach($masonryCase in $masonryCases) {
        $masonryLog=Join-Path $masonryRoot ('.utmp/masonry-expansion/'+$masonryCase.Name+'.log')
        $masonryArgs=@('-batchmode','-force-d3d11','-projectPath',('"'+$masonryProject+'"'),'-executeMethod',$masonryCase.Method,'-logFile',('"'+$masonryLog+'"'))
        $masonryProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$masonryVersion/Editor/Unity.exe" -ArgumentList $masonryArgs -WindowStyle Hidden -PassThru
        Write-Output ($masonryCase.Method+' PID '+$masonryProcess.Id)
        while(-not $masonryProcess.WaitForExit(1000)){}
        if($masonryProcess.ExitCode -ne 0 -or -not(Select-String -LiteralPath $masonryLog -SimpleMatch $masonryCase.Marker -Quiet)){throw ('Validation failed: '+$masonryLog)}
    }
    if($Install){& 'C:/Program Files/Blender Foundation/Blender 5.0/5.0/python/bin/python.exe' Tools/InstallMasonryExpansion.py;if($LASTEXITCODE -ne 0){throw 'Scoped installation failed.'}}
}finally{Pop-Location}
