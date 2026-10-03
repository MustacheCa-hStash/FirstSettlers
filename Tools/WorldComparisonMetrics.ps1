param([int]$ViewerX = 0, [int]$ViewerZ = 0)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskMeshSource = Get-Content -LiteralPath (Join-Path $taskRoot 'Assets/Scripts/TerrainGeneration/MeshGeneration/MeshGenerator.cs') -Raw
$taskChunkSource = Get-Content -LiteralPath (Join-Path $taskRoot 'Assets/Scripts/ChunkManager/ChunkManager.cs') -Raw
$taskScene = Get-Content -LiteralPath (Join-Path $taskRoot 'Assets/Scenes/SmearScene.unity') -Raw
function Read-SceneNumber([string]$Name) {
    $taskMatch = [regex]::Match($taskScene, '(?m)^  ' + [regex]::Escape($Name) + ': ([0-9.]+)\r?$')
    if (-not $taskMatch.Success) { throw "Scene field missing: $Name" }
    return [double]::Parse($taskMatch.Groups[1].Value, [cultureinfo]::InvariantCulture)
}
$taskStart = $taskMeshSource.IndexOf('    private static void BuildTerrainTopology(')
$taskEnd = $taskMeshSource.IndexOf('    private static int EstimateTerrainVertexCapacity(', $taskStart)
$taskTopology = $taskMeshSource.Substring($taskStart, $taskEnd - $taskStart)
$taskStart = $taskChunkSource.IndexOf('    private bool ShouldUseFarTerrain(')
$taskEnd = $taskChunkSource.IndexOf('    private void EnsureFarTerrainRequested(', $taskStart)
$taskPatchMethods = $taskChunkSource.Substring($taskStart, $taskEnd - $taskStart)
$taskHarness = @'
using System;
using System.Collections.Generic;
public struct ChunkCoord { public int x,z; public ChunkCoord(int a,int b){x=a;z=b;} }
public struct FarTerrainPatchKey {
 public ChunkCoord Origin; public int SizeInChunks;
 public FarTerrainPatchKey(ChunkCoord p,int s){Origin=p;SizeInChunks=s;}
 public override string ToString(){return SizeInChunks+":"+Origin.x+":"+Origin.z;}
}
public static class Mathf {
 public static int Abs(int n){return Math.Abs(n);}
 public static int Min(int a,int b){return Math.Min(a,b);}
 public static int Max(int a,int b){return Math.Max(a,b);}
 public static int Clamp(int n,int a,int b){return Math.Min(b,Math.Max(a,n));}
}
public class WorldComparisonMetrics {
 private const int FarTerrainMaxPatchSizeInChunks=32;
 private bool enableFarTerrain=true;
 private int farTerrainStartRing,farTerrainMacroTileSize;
 public static long[] Topology(int size,int step){
  var vertices=new HashSet<int>(); long triangles=0;
  BuildTerrainTopology(size,step,(x,z)=>{int k=z*(size+1)+x;vertices.Add(k);return k;},(a,b,c)=>triangles++);
  return new long[]{vertices.Count,triangles};
 }
 public static long[] FarMesh(int resolution){
  long segments=4L*(resolution-1);
  return new long[]{resolution*(long)resolution+2*segments,2L*(resolution-1)*(resolution-1)+2*segments};
 }
 public string Coverage(int vx,int vz,int radius,int size,int start,int minimum,int singleResolution){
  farTerrainStartRing=start;farTerrainMacroTileSize=minimum;
  var viewer=new ChunkCoord(vx,vz); var patches=new Dictionary<string,FarTerrainPatchKey>();
  int cells=0,normal=0,singleFar=0; long vertices=0,triangles=0;
  for(int x=-radius;x<=radius;x++)for(int z=-radius;z<=radius;z++){
   if(x*x+z*z>radius*radius)continue; cells++;
   var target=new ChunkCoord(vx+x,vz+z); FarTerrainPatchKey patch;
   if(TryGetFarTerrainPatch(viewer,target,out patch)){patches[patch.ToString()]=patch;continue;}
   normal++;int ring=Math.Max(Math.Abs(x),Math.Abs(z)); long[] geometry;
   if(ShouldUseFarTerrain(viewer,target)){singleFar++;geometry=FarMesh(singleResolution);}
   else {int lod=ring<=1?0:ring<=3?1:ring<=5?2:ring<=7?3:4;geometry=Topology(size,1<<lod);}
   vertices+=geometry[0];triangles+=geometry[1];
  }
  var counts=new SortedDictionary<int,int>();
  foreach(var patch in patches.Values){
   int p=patch.SizeInChunks;counts[p]=counts.ContainsKey(p)?counts[p]+1:1;
   int resolution=p<=minimum*2||p>=FarTerrainMaxPatchSizeInChunks?65:33;
   var geometry=FarMesh(resolution);vertices+=geometry[0];triangles+=geometry[1];
  }
  string distribution="";foreach(var pair in counts)distribution+=pair.Key+"x"+pair.Key+"="+pair.Value+" ";
  return "Viewer ("+vx+","+vz+"): logical cells="+cells+", normal records="+normal+", single-chunk far="+singleFar+", macro records="+patches.Count+", macro sizes ["+distribution.Trim()+"], requested terrain vertices="+vertices+", triangles="+triangles;
 }
'@
if (-not ('WorldComparisonMetrics' -as [type])) {
    Add-Type -TypeDefinition ($taskHarness + $taskTopology + $taskPatchMethods + '}')
}
$taskSize = [int](Read-SceneNumber 'chunkSize')
$taskScale = Read-SceneNumber 'worldScale'
$taskRadius = [int](Read-SceneNumber 'viewDistance')
"Chunk width: $($taskSize * $taskScale); terrain radius: $($taskRadius * $taskSize * $taskScale)"
0..4 | ForEach-Object {
    $taskGeometry = [WorldComparisonMetrics]::Topology($taskSize, [int][math]::Pow(2, $_))
    "LOD ${_}: vertices=$($taskGeometry[0]), triangles=$($taskGeometry[1])"
}
$taskMetrics = New-Object WorldComparisonMetrics
$taskMetrics.Coverage($ViewerX, $ViewerZ, $taskRadius, $taskSize, [int](Read-SceneNumber 'farTerrainStartRing'), [int](Read-SceneNumber 'farTerrainMacroTileSize'), [int](Read-SceneNumber 'farTerrainHeightGridResolution'))
"Coverage counts model all requested terrain in every direction, including full boundary macro tiles. They are not GPU visibility, measured frame costs, foliage, water, reflections, shadows, or transitional overlaps."
