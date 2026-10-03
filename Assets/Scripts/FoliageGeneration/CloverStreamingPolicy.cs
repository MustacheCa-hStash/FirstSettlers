using UnityEngine;

public static class CloverStreamingPolicy
{
    public static float RenderDistance(CloverSettings settings,int size,float scale) =>
        Mathf.Max(1,settings.activeRingRadius)*Mathf.Max(.001f,size*scale);
    public static float FadeWidth(CloverSettings settings,int size,float scale) =>
        Mathf.Clamp(settings.renderFadeWidthChunks*Mathf.Max(.001f,size*scale),.001f,RenderDistance(settings,size,scale));
    public static bool WithinRange(Vector3 viewer,ChunkCoord chunk,int size,float scale,float radius)
    {
        float width=Mathf.Max(.001f,size*scale);
        var center=new Vector2((chunk.x+.5f)*width,(chunk.z+.5f)*width);
        return GrassStreamingPolicy.DistanceToSquare(new Vector2(viewer.x,viewer.z),center,width*.5f)<=radius;
    }
}
