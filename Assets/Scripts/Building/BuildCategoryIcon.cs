using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Resolution-independent category symbols, without external icon fonts.</summary>
public sealed class BuildCategoryIcon : VisualElement
{
    private readonly int category;
    public BuildCategoryIcon(int category)
    { this.category=category; pickingMode=PickingMode.Ignore; AddToClassList("build-category-icon"); generateVisualContent+=Draw; }
    private void Draw(MeshGenerationContext context)
    {
        var p=context.painter2D; p.strokeColor=resolvedStyle.color; p.lineWidth=1.8f;
        var scale=new Vector2(contentRect.width/32f,contentRect.height/32f);
        void Line(params Vector2[] points)
        {
            p.BeginPath(); p.MoveTo(Vector2.Scale(points[0],scale));
            for(int i=1;i<points.Length;i++)p.LineTo(Vector2.Scale(points[i],scale)); p.Stroke();
        }
        void Box(float x,float y,float w,float h)=>Line(new(x,y),new(x+w,y),new(x+w,y+h),new(x,y+h),new(x,y));
        switch(category)
        {
            case 0: Box(5,5,8,8); Box(19,5,8,8); Box(5,19,8,8); Box(19,19,8,8); break;
            case 1: Line(new(16,3),new(29,10),new(16,17),new(3,10),new(16,3)); Line(new(3,10),new(3,22),new(16,29),new(29,22),new(29,10)); Line(new(16,17),new(16,29)); break;
            case 2: Box(5,4,22,24); Line(new(11,4),new(11,28)); Line(new(21,4),new(21,28)); break;
            case 3: Line(new(16,4),new(29,16),new(16,28),new(3,16),new(16,4)); Line(new(9,10),new(23,22)); Line(new(23,10),new(9,22)); break;
            case 4: Box(12,3,8,26); Line(new(16,3),new(16,29)); break;
            case 5: Line(new(3,28),new(3,23),new(9,23),new(9,17),new(15,17),new(15,11),new(21,11),new(21,5),new(28,5),new(28,28),new(3,28)); break;
            case 6: Line(new(3,28),new(16,4),new(29,28),new(3,28)); Line(new(16,4),new(16,28)); break;
        }
    }
}
