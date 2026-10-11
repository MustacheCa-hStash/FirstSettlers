param([string]$Source, [string]$Output)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common
if (-not ('TrimSamplePacker' -as [type])) {
Add-Type -ReferencedAssemblies @([System.Drawing.Bitmap].Assembly.Location, [System.Drawing.Rectangle].Assembly.Location, "$PSHOME\System.Private.Windows.GdiPlus.dll", "$PSHOME\System.Private.Windows.Core.dll") -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
public static class TrimSamplePacker {
  static byte[] Read(Bitmap b) {
    var d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
    var p=new byte[b.Width*b.Height*4]; Marshal.Copy(d.Scan0,p,0,p.Length);b.UnlockBits(d);return p;
  }
  static void Write(Bitmap b,byte[] p) {
    var d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
    Marshal.Copy(p,0,d.Scan0,p.Length);b.UnlockBits(d);
  }
  public static string Pack(string source,string original,string output) {
    const int N=2048;var src=new Bitmap(source);var old=new Bitmap(original);
    var dst=new Bitmap(N,N,PixelFormat.Format32bppArgb);
    // Allocation rectangles are fixed by the existing atlas; content has 16px gutters.
    int[][] cells={new[]{0,0,2048,256},new[]{0,256,2048,256},new[]{0,512,2048,256},new[]{0,768,2048,256},
      new[]{0,1024,512,512},new[]{512,1024,512,512},new[]{0,1536,1024,256},new[]{0,1792,1024,256}};
    using(var g=Graphics.FromImage(dst)) {
      g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
      g.PixelOffsetMode=PixelOffsetMode.HighQuality;
      for(int k=0;k<cells.Length;k++) {
        var r=cells[k];int px=k<4?0:16;
        var target=new Rectangle(r[0]+px,r[1]+16,r[2]-px*2,r[3]-32);
        // Ignore a small source border to keep generated cell-boundary strokes outside the artwork.
        float inset=k==4||k==5?.035f:.01f;
        var crop=new RectangleF((r[0]+r[2]*inset)*src.Width/N,(r[1]+r[3]*inset)*src.Height/N,
          r[2]*(1-2*inset)*src.Width/N,r[3]*(1-2*inset)*src.Height/N);
        g.DrawImage(src,target,crop,GraphicsUnit.Pixel);
      }
    }
    var p=Read(dst);var orig=Read(old);
    for(int k=0;k<cells.Length;k++) {
      var r=cells[k];int px=k<4?0:16,x0=r[0]+px,x1=r[0]+r[2]-px-1,y0=r[1]+16,y1=r[1]+r[3]-17;
      if(k<4||k==6||k==7) {
        // Symmetric seam blend is confined to 48 texels at the repeat boundary.
        for(int y=y0;y<=y1;y++)for(int d=0;d<48;d++)for(int c=0;c<3;c++) {
          int a=(y*N+x0+d)*4+c,b=(y*N+x1-d)*4+c;
          double weight=(48.0-d)/48.0,mean=(p[a]+p[b])*.5;
          p[a]=(byte)Math.Round(p[a]*(1-weight)+mean*weight);
          p[b]=(byte)Math.Round(p[b]*(1-weight)+mean*weight);
        }
      }
      // Extrude edge pixels into the same sampling gutters as the original texture.
      for(int y=r[1];y<r[1]+r[3];y++)for(int x=r[0];x<r[0]+r[2];x++) {
        int a=(y*N+x)*4,b=(Math.Max(y0,Math.Min(y1,y))*N+Math.Max(x0,Math.Min(x1,x)))*4;
        for(int c=0;c<3;c++)p[a+c]=p[b+c];p[a+3]=255;
      }
    }
    // Wattle cell including all its gutters is retained byte-for-byte, not regenerated.
    for(int y=1024;y<N;y++)Array.Copy(orig,(y*N+1024)*4,p,(y*N+1024)*4,1024*4);
    int alphaDiff=0,wattleDiff=0,seamDiff=0;
    for(int i=3;i<p.Length;i+=4)if(p[i]!=orig[i])alphaDiff++;
    for(int y=1024;y<N;y++)for(int x=1024;x<N;x++)for(int c=0;c<4;c++)if(p[(y*N+x)*4+c]!=orig[(y*N+x)*4+c])wattleDiff++;
    for(int y=0;y<1024;y++)for(int c=0;c<4;c++)seamDiff=Math.Max(seamDiff,Math.Abs(p[y*N*4+c]-p[(y*N+N-1)*4+c]));
    if(alphaDiff!=0||wattleDiff!=0||seamDiff!=0)throw new Exception("Atlas compatibility validation failed");
    Write(dst,p);dst.Save(output,ImageFormat.Png);src.Dispose();old.Dispose();dst.Dispose();
    return "{\"size\":[2048,2048],\"paddingPixels\":16,\"alphaChangedPixels\":"+alphaDiff+",\"wattleChangedChannels\":"+wattleDiff+",\"woodRepeatEdgeMaxDifference\":"+seamDiff+"}";
  }
}
'@
}
$root = (Resolve-Path -LiteralPath "$PSScriptRoot\..\..\..\..").Path
[TrimSamplePacker]::Pack($Source, "$root\Assets\Textures\Buildings\Wood\ordinary-wood-trim-albedo.png", $Output)
