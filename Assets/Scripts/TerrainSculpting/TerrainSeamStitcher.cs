using System.Collections.Generic;

internal static class TerrainSeamStitcher
{
    internal static void Join(TerrainSculptMeshBuffer output,TerrainBoundaryChain chain,List<int> outer,int axis)
    {
        var inner=chain.Vertices;int i=0,j=0;
        while(i+1<inner.Count||j+1<outer.Count)
        {
            bool advanceInner=j+1==outer.Count||(i+1<inner.Count&&output.Positions[inner[i+1]][axis]<=output.Positions[outer[j+1]][axis]);
            // The new triangle owns the reverse of the volume boundary half-edge.
            // This remains defined for a vertical run, where dot(cross,Up) is zero.
            int a=inner[i],b=outer[j],c=advanceInner?inner[i+1]:outer[j+1];
            output.TriangleOriented(a,chain.Forward?b:c,chain.Forward?c:b);
            if(advanceInner)i++;else j++;
        }
    }
}
