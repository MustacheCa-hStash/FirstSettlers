using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>Passive development HUD and uncollidable brush outline. No device or UI event input.</summary>
internal sealed class TerrainSculptView : IDisposable
{
    private readonly GameObject hud,outline;
    private readonly Text text;
    private readonly LineRenderer[] lines=new LineRenderer[12];
    private readonly Material material;
    private TerrainDensityBrushShape previousShape=(TerrainDensityBrushShape)(-1);
    private string previousText;
    private bool disposed;
    private static readonly Color Valid=new Color(.35f,1,.65f),Invalid=new Color(1,.35f,.3f),Pending=new Color(1,.78f,.3f);
    internal TerrainSculptView(Transform parent)
    {
        hud=new GameObject("Terrain sculpt development HUD");hud.transform.SetParent(parent,false);
        var canvas=hud.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=45;
        var scaler=hud.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        var panel=new GameObject("Sculpt status");panel.transform.SetParent(hud.transform,false);
        var rect=panel.AddComponent<RectTransform>();rect.anchorMin=rect.anchorMax=Vector2.one;rect.pivot=Vector2.one;
        rect.anchoredPosition=new Vector2(-18,-18);rect.sizeDelta=new Vector2(510,230);
        var image=panel.AddComponent<Image>();image.color=new Color(.035f,.055f,.07f,.9f);image.raycastTarget=false;
        var label=new GameObject("Controls and save status");label.transform.SetParent(panel.transform,false);
        text=label.AddComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize=17;text.alignment=TextAnchor.UpperLeft;text.color=new Color(.92f,.95f,.94f);text.raycastTarget=false;
        text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Overflow;
        rect=label.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(14,12);rect.offsetMax=new Vector2(-14,-12);

        outline=new GameObject("Terrain sculpt brush outline");outline.layer=GameplayLayers.QueryOnly;
        var template=Resources.Load<Material>("TerrainSculpting/BrushPreview");
        if(template!=null)material=new Material(template){name="Local sculpt brush",hideFlags=HideFlags.DontSave};
        // No primitive GameObjects/colliders: the preview cannot hit itself or block building/movement.
        for(int i=0;i<lines.Length;i++)
        {
            var child=new GameObject("Brush edge "+i);child.transform.SetParent(outline.transform,false);child.layer=GameplayLayers.QueryOnly;
            var line=child.AddComponent<LineRenderer>();lines[i]=line;line.useWorldSpace=false;line.widthMultiplier=.024f;
            line.sharedMaterial=material;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.numCapVertices=2;
        }
        Hide();
    }
    internal void Show(TerrainDensityBrushShape shape,float size,string status,TerrainEditPersistence persistence,
        bool preview,bool valid,bool busy,TerrainDensityBrush brush)
    {
        if(disposed||hud==null||outline==null||text==null)return;
        hud.SetActive(true);
        string dimensions=shape==TerrainDensityBrushShape.Sphere?$"Sphere · radius {size:0.0} m · diameter {size*2:0.0} m":$"Box · half-extent {size:0.0} m · width {size*2:0.0} m";
        string save=persistence==null?"Session only · edits reset when the world restarts\nDisk saves are disabled; existing saves are ignored":
            (persistence.FailedToLoad?"Load blocked · see F3":!persistence.Ready?"Loading saved terrain":persistence.ReadOnly?"Read-only recovery · see F3":
                persistence.LastError!=null?"Save failed · F10 retries; edits stay in memory":persistence.IsSaving?"Saving terrain":persistence.Dirty?"Unsaved committed edits":"Terrain edits saved")+
            "\n"+(!persistence.Ready?"Saved revision unavailable":persistence.Dirty?$"Last saved revision {persistence.SavedRevision}":$"Saved revision {persistence.SavedRevision}");
        string saveControl=persistence==null?"":" · F10: save / retry";
        string content=$"TERRAIN EXCAVATION · DEVELOPMENT\n{dimensions} · reach 6 m\nLMB: cut / hold (max 4/s) · Wheel: size\nR: shape{saveControl} · F6 / Esc: exit\n\n{status}\n{save}";
        if(content!=previousText)text.text=previousText=content;
        outline.SetActive(preview&&material!=null);
        if(!preview)return;
        if(previousShape!=shape){SetShape(shape);previousShape=shape;}
        // World-space box axes stay fixed even when the owning player turns.
        outline.transform.SetPositionAndRotation((Vector3)(Unity.Mathematics.float3)brush.Center,Quaternion.identity);
        outline.transform.localScale=Vector3.one*size;
        Color color=valid?Valid:busy?Pending:Invalid;
        material?.SetColor("_Color",color);
    }
    private void SetShape(TerrainDensityBrushShape shape)
    {
        for(int i=0;i<lines.Length;i++)lines[i].enabled=shape==TerrainDensityBrushShape.Box||i<3;
        if(shape==TerrainDensityBrushShape.Sphere)
        {
            for(int plane=0;plane<3;plane++)
            {
                var line=lines[plane];line.loop=true;line.positionCount=64;
                for(int i=0;i<64;i++)
                {
                    float a=i*Mathf.PI*2/64,c=Mathf.Cos(a),s=Mathf.Sin(a);
                    line.SetPosition(i,plane==0?new Vector3(c,s,0):plane==1?new Vector3(c,0,s):new Vector3(0,c,s));
                }
            }
        }
        else
        {
            int edge=0;
            for(int axis=0;axis<3;axis++)for(int a=-1;a<=1;a+=2)for(int b=-1;b<=1;b+=2)
            {
                Vector3 lo=axis==0?new Vector3(-1,a,b):axis==1?new Vector3(a,-1,b):new Vector3(a,b,-1);
                Vector3 hi=lo;hi[axis]=1;var line=lines[edge++];line.loop=false;line.positionCount=2;line.SetPosition(0,lo);line.SetPosition(1,hi);
            }
        }
    }
    internal void Hide(){if(hud!=null)hud.SetActive(false);if(outline!=null)outline.SetActive(false);}
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;Hide();
        TerrainAssetOwner.Destroy(hud);TerrainAssetOwner.Destroy(outline);TerrainAssetOwner.Destroy(material);
    }
}
