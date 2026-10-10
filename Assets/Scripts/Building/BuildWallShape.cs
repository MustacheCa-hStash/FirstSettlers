using UnityEngine;

/// <summary>Authored apertures and gable profiles refine physical solids, never placement eligibility.</summary>
public static class BuildWallShape
{
    public static bool AllowsCover(BuildDefinition d,Vector3 joint)
    {
        // Half-gable framing is already authored to its slope; a rectangular post would project above it.
        if(d.IsHalfGable)return false;
        var cover=new Bounds(joint+Vector3.up*(d.LocalBounds.size.y*.5f),new Vector3(.26f,d.LocalBounds.size.y,.26f));
        foreach(var aperture in d.wallOpenings??System.Array.Empty<Bounds>())
        {
            Vector3 overlap=Vector3.Min(cover.max,aperture.max)-Vector3.Max(cover.min,aperture.min);
            if(overlap.x>.001f && overlap.y>.001f && overlap.z>.001f)return false;
        }
        return true;
    }
    public static Vector3 Peak(BuildDefinition d)=>new(d.gableSlope==GableSlope.Rising?d.LocalBounds.max.x:d.LocalBounds.min.x,d.LocalBounds.max.y,0);
    public static BuildRenderAttachment[] RoofSeam(BuildPieceRecord p,System.Collections.Generic.IReadOnlyList<BuildPieceRecord> neighbours)
    {
        if(!p.Definition.IsHalfGable || p.Definition.gableRoofSeamMesh==null)return System.Array.Empty<BuildRenderAttachment>();
        var q=BuildGeometry.Rotation(p.WorldYawStep);
        Vector3 low=p.Origin+q*new Vector3(p.Definition.gableSlope==GableSlope.Rising?0:2,0,0),high=p.Origin+q*Peak(p.Definition);
        foreach(var roof in neighbours)
        {
            if(roof.Definition.kind!=BuildPartKind.Roof)continue;
            var inverse=Quaternion.Inverse(BuildGeometry.Rotation(roof.WorldYawStep));Vector3 a=inverse*(low-roof.Origin),b=inverse*(high-roof.Origin);
            bool atEnd=Mathf.Abs(a.x)<.012f || Mathf.Abs(a.x-BuildRoof.Width)<.012f;
            if(atEnd && Mathf.Abs(a.y)<.012f && Mathf.Abs(a.z)<.012f && Mathf.Abs(b.x-a.x)<.012f && Mathf.Abs(b.y-BuildRoof.Rise)<.012f && Mathf.Abs(b.z-BuildRoof.Run)<.012f)
                return new[]{new BuildRenderAttachment{mesh=p.Definition.gableRoofSeamMesh,material=p.Definition.material,localMatrix=Matrix4x4.identity}};
        }
        return System.Array.Empty<BuildRenderAttachment>();
    }
    public static System.Collections.Generic.IEnumerable<Bounds> FloorSections(BuildDefinition d,float floorBottom)
    {
        if(d.solidBoxes!=null && d.solidBoxes.Length>0){foreach(var box in d.solidBoxes)yield return box;yield break;}
        var b=d.LocalBounds;
        if(d.IsHalfGable)
        {
            float fraction=Mathf.Clamp01((floorBottom-b.min.y)/b.size.y);
            if(floorBottom>=b.max.y-.005f)yield break;
            Vector3 lo=b.min,hi=b.max;
            if(d.gableSlope==GableSlope.Rising)lo.x+=b.size.x*fraction;else hi.x-=b.size.x*fraction;
            b.SetMinMax(lo,hi);
        }
        yield return b;
    }
}
