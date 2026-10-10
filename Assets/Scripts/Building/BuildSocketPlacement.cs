using UnityEngine;

/// <summary>Full-span profiles snap their boundary lines/endpoints, independently of half-thickness bounds.</summary>
public static class BuildSocketPlacement
{
    public static bool Solve(BuildDefinition d,Vector3 hit,Vector3 normal,BuildPieceRecord target,int turn,Vector3Int nudge,Vector3? viewer,bool preferTop,out BuildPreview preview)
    {
        preview=default;if(target==null)return false;
        var q=BuildGeometry.Rotation(target.WorldYawStep);var inverse=Quaternion.Inverse(q);var point=inverse*(hit-target.Origin);var n=inverse*normal;
        var b=target.Definition.LocalBounds;var view=viewer.HasValue?inverse*(viewer.Value-target.Origin):point+n;
        bool wall=target.Definition.kind==BuildPartKind.Wall && target.Definition.jointSockets?.Length>0;
        bool post=target.Definition.kind==BuildPartKind.Corner && b.min.x<0 && b.min.z<0;
        bool selectedWall=d.kind==BuildPartKind.Wall && d.jointSockets?.Length>0;
        bool selectedPost=d.kind==BuildPartKind.Corner && d.LocalBounds.min.x<0 && d.LocalBounds.min.z<0;
        bool upper=n.y>.6f || point.y>=b.center.y;Vector3 origin;int yaw=BuildGeometry.Turn(turn);string hint;bool top=false;
        if(selectedPost && wall)
        {
            origin=new Vector3(point.x<b.center.x?0:b.max.x,n.y>.6f?b.min.y+target.Definition.StackRise:b.min.y,0);hint="Post at wall endpoint";
        }
        else if(selectedPost && post)
        {
            int side=Side(n);
            origin=upper?new Vector3(0,b.min.y+target.Definition.StackRise,0):BuildGeometry.Rotation(side)*Vector3.back*.25f;
            hint=upper?"Stack post":"Adjacent post";
        }
        else if(selectedPost && target.Definition.kind is BuildPartKind.Floor or BuildPartKind.Foundation)
        {
            float Snap(float value,float min,float max)=>Mathf.Abs(value-min)<.25f?min:Mathf.Abs(value-max)<.25f?max:BuildGeometry.Tick(value)*BuildGeometry.Unit;
            origin=new Vector3(Snap(point.x,b.min.x,b.max.x),n.y<-.6f?b.min.y-d.LocalBounds.max.y:b.max.y,Snap(point.z,b.min.z,b.max.z));hint=n.y<-.6f?"Post underneath surface":"Post on boundary grid";
        }
        else if(selectedWall && wall)
        {
            if(upper){origin=new Vector3(0,b.min.y+target.Definition.StackRise,0);hint="Stack full-height wall";}
            else
            {
                bool left=point.x<b.center.x;
                float along=yaw%4==0?(left?0:b.max.x):Mathf.Clamp(BuildGeometry.Tick(point.x)*BuildGeometry.Unit,0,b.max.x);
                var joint=new Vector3(along,b.min.y,0);var tangent=BuildGeometry.Rotation(yaw)*Vector3.right;
                bool end=yaw%4==0?left:Vector3.Dot(tangent,view-joint)<0;
                origin=joint-BuildGeometry.Rotation(yaw)*(end?new Vector3(d.LocalBounds.max.x,0,0):Vector3.zero);
                hint=yaw%4==0?"Continue wall from endpoint":"Wall at declared junction";
            }
        }
        else if(selectedWall && post)
        {
            yaw=BuildGeometry.Turn((upper?0:Side(n)+2)+turn);origin=new Vector3(0,upper?b.min.y+target.Definition.StackRise:b.min.y,0);hint=upper?"Wall above post":"Wall from post centre";
        }
        else if(d.kind==BuildPartKind.Floor && wall && n.y>=-.6f)
        {
            var shape=BuildGeometry.WorldBounds(d.LocalBounds,Vector3.zero,yaw);
            bool positive=Mathf.Abs(n.z)>.5f?n.z>0:view.z>=0;
            top=n.y>.6f || point.y>b.max.y+(preferTop?-.03f:.03f);
            origin=new Vector3(b.center.x-shape.center.x,top?b.min.y+target.Definition.StackRise-shape.max.y:BuildGeometry.Tick(point.y)*BuildGeometry.Unit-shape.max.y,positive?-shape.min.z:-shape.max.z);
            hint=top?"Floor at storey level":"Floor from wall boundary";
        }
        else if(d.kind==BuildPartKind.Floor && post && n.y>=-.6f)
        {
            var shape=BuildGeometry.WorldBounds(d.LocalBounds,Vector3.zero,yaw);
            origin=new Vector3(view.x>=0?-shape.min.x:-shape.max.x,b.min.y+target.Definition.StackRise-shape.max.y,view.z>=0?-shape.min.z:-shape.max.z);hint="Floor bay from post centre";top=true;
        }
        else return false;
        int worldYaw=BuildGeometry.Turn(target.WorldYawStep+yaw);Vector3 world=target.Origin+q*(origin+(Vector3)nudge*BuildGeometry.Unit);
        // A rotated endpoint can lie between ticks in its parent's frame. Preserve the exact socket.
        preview=new BuildPreview{Definition=d,Origin=world,WorldYaw=worldYaw,Frame=new BuildGridFrame{Origin=world,YawStep=(byte)worldYaw},Hint=hint,TopAttachment=top};
        return true;
    }
    private static int Side(Vector3 n)=>Mathf.Abs(n.x)>Mathf.Abs(n.z)?n.x>0?6:2:n.z>0?4:0;
}
