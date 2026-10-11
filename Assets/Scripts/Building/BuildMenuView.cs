using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

/// <summary>Stone catalog and passive HUD. Hardware input remains in LocalPlayerInput.</summary>
public sealed class BuildMenuView : IDisposable
{
    private sealed class Entry
    {
        public BuildDefinition definition;
        public Button button, quick;
    }
    private readonly GameObject obj;
    private readonly VisualElement root, menu, hud, grid;
    private readonly ScrollView scroll;
    private readonly Label heading, status, detailName, detailSize, description, slope, count;
    private readonly Image detailImage;
    private readonly Button choose, flip;
    private readonly Action<BuildDefinition> select;
    private readonly List<Entry> entries=new();
    private readonly List<Button> categories=new();
    private BuildDefinition highlighted;
    private string lastHeading,lastStatus;
    private bool visible,disposed;
    public BuildDefinition Highlighted=>highlighted;

    public BuildMenuView(BuildCatalog catalog,Transform parent,Action<BuildDefinition> select)
    {
        this.select=select;
        obj=new GameObject("Building UI"); obj.transform.SetParent(parent,false);
        var document=obj.AddComponent<UIDocument>();
        document.panelSettings=catalog.panel; document.visualTreeAsset=catalog.layout; document.sortingOrder=30;
        root=document.rootVisualElement; root.pickingMode=PickingMode.Ignore;
        menu=root.Q<VisualElement>("build-menu"); hud=root.Q<VisualElement>("build-hud");
        heading=root.Q<Label>("build-heading"); status=root.Q<Label>("build-status");
        scroll=root.Q<ScrollView>("build-presets"); grid=scroll.contentContainer; grid.AddToClassList("build-grid");
        detailName=root.Q<Label>("build-detail-name"); detailSize=root.Q<Label>("build-detail-size");
        description=root.Q<Label>("build-detail-description"); detailImage=root.Q<Image>("build-detail-image");
        slope=root.Q<Label>("build-slope"); count=root.Q<Label>("build-count");
        choose=root.Q<Button>("build-choose"); flip=root.Q<Button>("build-flip");
        choose.clicked+=ConfirmSelection; flip.clicked+=FlipSelection;
        var regular=Resources.Load<FontAsset>("Building/UI/Fonts/StoneBody");
        var title=Resources.Load<FontAsset>("Building/UI/Fonts/StoneHeading");
        if(regular!=null)root.style.unityFontDefinition=FontDefinition.FromSDFFont(regular);
        if(title!=null)foreach(var element in root.Query<VisualElement>(className:"stone-heading").ToList())
            element.style.unityFontDefinition=FontDefinition.FromSDFFont(title);
        var rail=root.Q<VisualElement>("build-categories");
        for(int i=0;i<BuildUiContent.Categories.Length;i++)
        {
            int category=i;
            var button=new Button(()=>Filter(category)){name="build-category-"+i};
            button.AddToClassList("build-category"); button.Add(new BuildCategoryIcon(i));
            button.Add(new Label(BuildUiContent.Categories[i]){pickingMode=PickingMode.Ignore});
            rail.Add(button); categories.Add(button);
        }
        var definitions=new List<BuildDefinition>();
        foreach(var definition in catalog.presets)if(definition!=null)definitions.Add(definition);
        definitions.Sort((a,b)=>BuildUiContent.Order(a).CompareTo(BuildUiContent.Order(b)));
        var quickBar=root.Q<VisualElement>("build-quick-pieces");
        foreach(var definition in definitions)
        {
            var entry=new Entry{definition=definition};
            var texture=Thumbnail(definition);
            entry.button=new Button(()=>SetSelection(entry.definition)){name=definition.contentId,
                tooltip=BuildUiContent.Name(definition)+" · "+BuildUiContent.Dimensions(definition)};
            entry.button.AddToClassList("build-preset");
            var image=new Image{image=texture,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
            image.AddToClassList("build-thumbnail"); entry.button.Add(image);
            var label=new Label(BuildUiContent.Name(definition)){pickingMode=PickingMode.Ignore};
            label.AddToClassList("build-piece-name"); entry.button.Add(label); grid.Add(entry.button);
            entry.quick=new Button(()=>{SetSelection(entry.definition);ConfirmSelection();})
                {name="quick-"+definition.contentId,tooltip=BuildUiContent.Name(definition)};
            entry.quick.AddToClassList("build-quick-piece");
            entry.quick.Add(new Image{image=texture,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore});
            quickBar.Add(entry.quick); entries.Add(entry);
        }
        root.RegisterCallback<GeometryChangedEvent>(OnGeometry);
        root.RegisterCallback<NavigationSubmitEvent>(OnSubmit,TrickleDown.TrickleDown); IgnorePicking(hud);
        SetSelection(definitions.Find(x=>x.kind==BuildPartKind.Foundation)??(definitions.Count>0?definitions[0]:null));
        Filter(0); SetState(false,false);
    }
    private static Texture2D Thumbnail(BuildDefinition d)=>Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d));
    private static void IgnorePicking(VisualElement node)
    {node.pickingMode=PickingMode.Ignore;foreach(var child in node.Children())IgnorePicking(child);}
    private void OnGeometry(GeometryChangedEvent e)
    {
        menu.EnableInClassList("build-compact",e.newRect.width<1550);
        menu.EnableInClassList("build-narrow",e.newRect.width<1150);
    }
    public void Filter(int category)
    {
        int shown=0;
        foreach(var entry in entries)
        {
            bool show=category==0||BuildUiContent.Category(entry.definition)==category;
            entry.button.style.display=show?DisplayStyle.Flex:DisplayStyle.None; if(show)shown++;
        }
        for(int i=0;i<categories.Count;i++)categories[i].EnableInClassList("selected",i==category);
        count.text=shown+(shown==1?" component":" components"); scroll.scrollOffset=Vector2.zero;
    }
    public void SetSelection(BuildDefinition definition)
    {
        if(definition==null){choose.SetEnabled(false);return;}
        highlighted=definition; detailName.text=BuildUiContent.Name(definition);
        detailSize.text=BuildUiContent.Dimensions(definition); description.text=BuildUiContent.Description(definition);
        detailImage.image=Thumbnail(definition);
        bool canFlip=definition.flipVariant!=null;
        flip.style.display=canFlip?DisplayStyle.Flex:DisplayStyle.None;
        slope.style.display=canFlip?DisplayStyle.Flex:DisplayStyle.None;
        slope.text=definition.gableSlope==GableSlope.Falling?"Falling slope":"Rising slope";
        choose.SetEnabled(true);
        foreach(var entry in entries)
        {
            bool chosen=entry.definition==definition||entry.definition.flipVariant==definition;
            entry.button.EnableInClassList("selected",chosen);entry.quick.EnableInClassList("selected",chosen);
        }
    }
    public void FlipSelection(){if(visible&&highlighted!=null&&highlighted.flipVariant!=null)SetSelection(highlighted.flipVariant);}
    public void ConfirmSelection(){if(visible&&highlighted!=null)select(highlighted);}
    private void OnSubmit(NavigationSubmitEvent e)
    {
        if(!visible)return;
        // Enter confirms the staged variant rather than reactivating a focused canonical tile.
        e.StopImmediatePropagation();ConfirmSelection();
    }
    public void SetState(bool active,bool choosing)
    {
        visible=active&&choosing; menu.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
        hud.style.display=active&&!choosing?DisplayStyle.Flex:DisplayStyle.None;
    }
    public void Show(string title,string message,bool valid,bool warning=false)
    {
        if(lastHeading!=title)heading.text=lastHeading=title;
        if(lastStatus!=message)status.text=lastStatus=message;
        status.EnableInClassList("build-error",!valid);status.EnableInClassList("build-warning",valid&&warning);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;root.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
        root.UnregisterCallback<NavigationSubmitEvent>(OnSubmit,TrickleDown.TrickleDown);
        choose.clicked-=ConfirmSelection;flip.clicked-=FlipSelection;
        if(obj!=null)BuildLifetime.Destroy(obj);
    }
}
