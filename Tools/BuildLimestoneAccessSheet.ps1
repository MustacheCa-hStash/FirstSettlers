param([string]$SourceFolder='ArtReferences/LimestoneBuildKit/Access')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$sheetRoot=Split-Path -Parent $PSScriptRoot
$sheetFolder=Join-Path $sheetRoot $SourceFolder
$sheetImage=New-Object Drawing.Bitmap 1800,1180
$sheetGraphics=[Drawing.Graphics]::FromImage($sheetImage)
$sheetGraphics.SmoothingMode='AntiAlias'
$sheetGraphics.InterpolationMode='HighQualityBicubic'
$sheetBackground=[Drawing.Color]::FromArgb(43,48,51)
$sheetGraphics.Clear($sheetBackground)
$sheetInk=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(239,230,207))
$sheetMuted=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(171,180,177))
$sheetAccent=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(194,166,108))
$sheetPen=New-Object Drawing.Pen ([Drawing.Color]::FromArgb(72,79,80)),1
$sheetTitle=New-Object Drawing.Font 'Georgia',29,([Drawing.FontStyle]::Regular)
$sheetLabel=New-Object Drawing.Font 'Segoe UI',18,([Drawing.FontStyle]::Regular)
$sheetSmall=New-Object Drawing.Font 'Segoe UI',12,([Drawing.FontStyle]::Regular)
try {
    $sheetGraphics.DrawString('LIMESTONE  /  ACCESS & BEARING',$sheetTitle,$sheetInk,38,25)
    $sheetGraphics.DrawString('L18–L24    •    Installed building components    •    Actual Unity meshes and baked materials',$sheetSmall,$sheetMuted,40,84)
    $sheetGraphics.DrawLine($sheetPen,40,124,1760,124)
    $sheetNames=@('Eaves cornice','Threshold steps','Half-storey stair','Full-storey stair','Bedded landing','Retaining wall','Plain buttress')
    $sheetNotes=@('4 × 0.25 × 0.75 m · fitted top course','1.5 m wide · 0.5 m rise · 2 steps','1.5 m wide · 1.5 m rise · 8 steps','1.5 m wide · 3 m rise · 16 steps','1.5 × 1.5 m · complete solid bedding','4 × 1.5 × 0.75 m · battered front','1 × 3 × 1 m · stepped projection')
    for($sheetIndex=0;$sheetIndex -lt 7;$sheetIndex++) {
        $sheetX=($sheetIndex%4)*450
        $sheetY=140+[math]::Floor($sheetIndex/4)*470
        $sheetTag='L'+($sheetIndex+18)
        $sheetSide=if($sheetIndex -ge 5){'back'}else{'front'}
        $sheetPart=[Drawing.Bitmap]::FromFile((Join-Path $sheetFolder ($sheetTag+'-unity-'+$sheetSide+'.png')))
        try {
            $sheetBg=$sheetPart.GetPixel(0,0);$sheetMinX=$sheetPart.Width;$sheetMinY=$sheetPart.Height;$sheetMaxX=0;$sheetMaxY=0
            for($sheetPY=0;$sheetPY -lt $sheetPart.Height;$sheetPY+=4){for($sheetPX=0;$sheetPX -lt $sheetPart.Width;$sheetPX+=4){
                $sheetPixel=$sheetPart.GetPixel($sheetPX,$sheetPY)
                if(([math]::Abs($sheetPixel.R-$sheetBg.R)+[math]::Abs($sheetPixel.G-$sheetBg.G)+[math]::Abs($sheetPixel.B-$sheetBg.B)) -gt 24){
                    $sheetMinX=[math]::Min($sheetMinX,$sheetPX);$sheetMaxX=[math]::Max($sheetMaxX,$sheetPX);$sheetMinY=[math]::Min($sheetMinY,$sheetPY);$sheetMaxY=[math]::Max($sheetMaxY,$sheetPY)
                }
            }}
            $sheetCrop=New-Object Drawing.Rectangle ([math]::Max(0,$sheetMinX-12)),([math]::Max(0,$sheetMinY-12)),([math]::Min($sheetPart.Width-$sheetMinX+12,$sheetMaxX-$sheetMinX+28)),([math]::Min($sheetPart.Height-$sheetMinY+12,$sheetMaxY-$sheetMinY+28))
            $sheetScale=[math]::Min(390/$sheetCrop.Width,310/$sheetCrop.Height)
            $sheetW=[int]($sheetCrop.Width*$sheetScale);$sheetH=[int]($sheetCrop.Height*$sheetScale)
            $sheetDest=New-Object Drawing.Rectangle ([int]($sheetX+(450-$sheetW)/2)),([int]($sheetY+20+(310-$sheetH)/2)),$sheetW,$sheetH
            $sheetGraphics.DrawImage($sheetPart,$sheetDest,$sheetCrop,[Drawing.GraphicsUnit]::Pixel)
            $sheetGraphics.DrawString($sheetTag,$sheetSmall,$sheetAccent,($sheetX+30),($sheetY+350))
            $sheetGraphics.DrawString($sheetNames[$sheetIndex],$sheetLabel,$sheetInk,($sheetX+30),($sheetY+374))
            $sheetGraphics.DrawString($sheetNotes[$sheetIndex],$sheetSmall,$sheetMuted,($sheetX+30),($sheetY+413))
        }finally{$sheetPart.Dispose()}
    }
    $sheetGraphics.DrawString('SHARED MATERIAL MAPS',$sheetSmall,$sheetAccent,1380,960)
    $sheetGraphics.DrawString('Colour + AO / tangent normal',$sheetSmall,$sheetInk,1380,995)
    $sheetGraphics.DrawString('2048² atlases · opaque matte stone',$sheetSmall,$sheetMuted,1380,1028)
    $sheetAttributes=New-Object Drawing.Imaging.ImageAttributes
    $sheetMatrix=New-Object Drawing.Imaging.ColorMatrix
    $sheetMatrix.Matrix33=0;$sheetMatrix.Matrix43=1
    $sheetAttributes.SetColorMatrix($sheetMatrix)
    try {
        $sheetMapIndex=0
        foreach($sheetMapName in 'Access_ColourAO.png','Access_Normal.png') {
            $sheetMap=[Drawing.Bitmap]::FromFile((Join-Path $sheetRoot ('Assets/Textures/Buildings/LimestoneAccess/'+$sheetMapName)))
            try{$sheetRect=New-Object Drawing.Rectangle (1380+$sheetMapIndex*190),700,170,170;$sheetGraphics.DrawImage($sheetMap,$sheetRect,0,0,$sheetMap.Width,$sheetMap.Height,[Drawing.GraphicsUnit]::Pixel,$sheetAttributes)}finally{$sheetMap.Dispose()}
            $sheetMapIndex++
        }
    }finally{$sheetAttributes.Dispose()}
    $sheetGraphics.DrawLine($sheetPen,40,1095,1760,1095)
    $sheetGraphics.DrawString('292 visible triangles across seven pieces  /  160 character stair traversals  /  Eight placement headings',$sheetSmall,$sheetMuted,40,1120)
    $sheetImage.Save((Join-Path $sheetFolder 'access-catalog.png'),[Drawing.Imaging.ImageFormat]::Png)
}finally {
    foreach($sheetResource in @($sheetGraphics,$sheetImage,$sheetInk,$sheetMuted,$sheetAccent,$sheetPen,$sheetTitle,$sheetLabel,$sheetSmall)){$sheetResource.Dispose()}
}
