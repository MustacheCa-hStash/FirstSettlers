using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Explicit UI asset baking and isolated render/interaction checks. Never saves a world scene.</summary>
public static class BuildStoneUiAuthoring
{
    private const string Folder="Assets/Resources/Building/UI";
    private const string Captures=".utmp/building-prototype/stone-ui";
    private const BindingFlags Methods=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    [MenuItem("Tools/Building/Rebuild Stone UI Assets")]
    public static void Bake()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Bake building UI assets outside Play mode.");
        Directory.CreateDirectory(Folder+"/Fonts");Directory.CreateDirectory(Folder+"/Thumbnails");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Import(Folder+"/StonePanel.png",512);Import(Folder+"/StonePillar.png",1024);
        Font("StoneBody","Segoe UI","Regular");Font("StoneHeading","Georgia","Bold");
        var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        if(catalog==null)throw new InvalidOperationException("Installed build catalog missing.");
        foreach(var d in catalog.presets)if(d!=null)
        {Thumbnail(d);if(d.flipVariant!=null)Thumbnail(d.flipVariant);}
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);AssetDatabase.SaveAssets();
        Debug.Log("STONE UI ASSETS: limestone skin, pillar, heading/body fonts and actual-mesh thumbnails baked without modifying catalog or saved scene.");
    }
    public static void BuildBatch()=>Batch(true);
    public static void ValidateBatch()=>Batch(false);
    public static void StairThumbnailsBatch()
    {
        try
        {
            var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
            foreach(var id in new[]{StairExtensionSetup.W22Id,StairExtensionSetup.W24Id})Thumbnail(catalog.Find(id));
            AssetDatabase.SaveAssets();Validate();EditorApplication.Exit(0);
        }catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Batch(bool bake)
    {
        try{if(bake)Bake();Validate();EditorApplication.Exit(0);}
        catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    private static void Import(string file,int size)
    {
        AssetDatabase.ImportAsset(file,ImportAssetOptions.ForceSynchronousImport);
        var importer=AssetImporter.GetAtPath(file) as TextureImporter;
        if(importer==null)throw new InvalidOperationException("Missing UI image: "+file);
        importer.textureType=TextureImporterType.Default;importer.textureShape=TextureImporterShape.Texture2D;importer.sRGBTexture=true;
        importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.isReadable=false;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;
        importer.maxTextureSize=size;importer.SaveAndReimport();
    }
    private static void Font(string filename,string family,string face)
    {
        string file=Folder+"/Fonts/"+filename+".asset";
        if(AssetDatabase.LoadAssetAtPath<FontAsset>(file)!=null)return;
        var font=FontAsset.CreateFontAsset(family,face,48,6,GlyphRenderMode.SDFAA);
        if(font==null)throw new InvalidOperationException("Font unavailable: "+family+" "+face);
        font.name=family+" "+face;font.atlasPopulationMode=AtlasPopulationMode.DynamicOS;
        font.isMultiAtlasTexturesEnabled=true;
        font.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 /.,:-×°·");
        AssetDatabase.CreateAsset(font,file);
        if(font.material!=null)AssetDatabase.AddObjectToAsset(font.material,font);
        foreach(var atlas in font.atlasTextures)if(atlas!=null)AssetDatabase.AddObjectToAsset(atlas,font);
        EditorUtility.SetDirty(font);
    }
    public static void BakeThumbnail(BuildDefinition definition)=>Thumbnail(definition);
    private static void Thumbnail(BuildDefinition definition)
    {
        if(definition.mesh==null||definition.material==null)throw new InvalidOperationException("Missing component render data: "+definition.contentId);
        var meshObject=new GameObject("UI thumbnail "+definition.contentId);meshObject.layer=31;
        meshObject.AddComponent<MeshFilter>().sharedMesh=definition.mesh;
        meshObject.AddComponent<MeshRenderer>().sharedMaterial=definition.material;
        var cameraObject=new GameObject("Thumbnail camera");var camera=cameraObject.AddComponent<Camera>();
        camera.enabled=false;camera.orthographic=true;camera.aspect=4f/3;camera.cullingMask=1<<31;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
        camera.nearClipPlane=.01f;camera.farClipPlane=40;
        var extra=cameraObject.AddComponent<UniversalAdditionalCameraData>();extra.renderPostProcessing=false;
        var bounds=definition.mesh.bounds;
        camera.transform.position=bounds.center+new Vector3(5,3.5f,-7).normalized*12;
        camera.transform.LookAt(bounds.center);
        float xExtent=0,yExtent=0;
        for(int i=0;i<8;i++)
        {
            var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
            Vector3 local=camera.transform.InverseTransformPoint(corner);
            xExtent=Mathf.Max(xExtent,Mathf.Abs(local.x));yExtent=Mathf.Max(yExtent,Mathf.Abs(local.y));
        }
        camera.orthographicSize=Mathf.Max(yExtent,xExtent/camera.aspect)*1.15f;
        var lightObject=new GameObject("Thumbnail light");var light=lightObject.AddComponent<Light>();
        light.type=LightType.Directional;light.intensity=1.4f;light.color=Color.white;
        light.shadows=LightShadows.None;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(40,-35,0);
        var target=new RenderTexture(512,384,24,RenderTextureFormat.ARGB32);target.Create();
        var oldPipeline=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;
        var oldTarget=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation=false;
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            string file=Folder+"/Thumbnails/"+BuildUiContent.ThumbnailKey(definition)+".png";
            Save(target,file);Import(file,512);
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation=async;GraphicsSettings.defaultRenderPipeline=oldPipeline;
            QualitySettings.renderPipeline=oldQuality;RenderTexture.active=oldTarget;
            Object.DestroyImmediate(meshObject);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);
            target.Release();Object.DestroyImmediate(target);
        }
    }
    [MenuItem("Tools/Building/Validate Stone UI (synthetic)")]
    public static void Validate()
    {
        Directory.CreateDirectory(Captures);
        var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        var stoneTexture=Resources.Load<Texture2D>("Building/UI/StonePanel");
        var pillarTexture=Resources.Load<Texture2D>("Building/UI/StonePillar");
        Require(stoneTexture!=null&&stoneTexture.width==512,"Stone panel was not imported as a 2D skin texture");
        Require(pillarTexture!=null&&pillarTexture.height==1024,"Stone pillar was not imported as a 2D skin texture");
        foreach(var d in catalog.presets)
        {
            Require(Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d))!=null,"Missing thumbnail "+d.contentId);
            if(d.flipVariant!=null)Require(Resources.Load<Texture2D>("Building/UI/Thumbnails/"+BuildUiContent.ThumbnailKey(d.flipVariant))!=null,"Missing mirrored thumbnail");
        }
        foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(3840,2160),new Vector2Int(1024,768)})Render(catalog,size.x,size.y);
        Debug.Log("STONE UI PASS: real-mesh thumbnails, category filters, staged selection/confirmation, both gable slopes, quick-piece selection, passive HUD and contained controls at 720p/1080p/4K/4:3. No saved scene modified.");
    }
    private static void Render(BuildCatalog source,int width,int height)
    {
        var catalog=Object.Instantiate(source);catalog.panel=Object.Instantiate(source.panel);
        var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);target.Create();
        var host=new GameObject("Stone UI fixture");BuildMenuView view=null;BuildDefinition committed=null;
        var previous=RenderTexture.active;
        try
        {
            catalog.panel.targetTexture=target;catalog.panel.clearColor=true;catalog.panel.colorClearValue=Color.clear;
            view=new BuildMenuView(catalog,host.transform,d=>committed=d);view.SetState(true,true);
            var root=host.GetComponentInChildren<UIDocument>().rootVisualElement;var panel=root.panel;
            typeof(PanelSettings).GetMethod("ApplyPanelSettings",Methods).Invoke(catalog.panel,null);
            void Draw()
            {
                for(int i=0;i<5;i++)
                {
                    panel.GetType().GetMethod("Update",Methods).Invoke(panel,null);
                    panel.GetType().GetMethod("ValidateLayout",Methods).Invoke(panel,null);
                    panel.GetType().GetMethod("Repaint",Methods,null,new[]{typeof(Event)},null).Invoke(panel,new object[]{new Event{type=EventType.Repaint}});
                    panel.GetType().GetMethod("Render",Methods).Invoke(panel,null);
                }
            }
            var presets=root.Query<Button>(className:"build-preset").ToList();
            Require(presets.Count==source.presets.Length,"Catalog population mismatch");
            Require(committed==null,"Highlighting committed a piece");
            void Click(Button button)
            {
                using var click=ClickEvent.GetPooled();click.target=button;
                typeof(Clickable).GetMethod("SimulateSingleClick",Methods).Invoke(button.clickable,new object[]{click,0});
            }
            Click(presets[0]);Require(committed==null,"Grid click entered placement before confirmation");
            var stairButton=root.Q<Button>(StairExtensionSetup.W22Id);
            stairButton.Focus();Draw();Require(view.Highlighted==source.Find(StairExtensionSetup.W22Id),"Keyboard focus did not highlight W22");
            using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=stairButton;stairButton.SendEvent(submit);}
            Require(committed==source.Find(StairExtensionSetup.W22Id),"Enter after highlighting did not choose W22");committed=null;
            var railButton=root.Q<Button>(StairExtensionSetup.W24Id);
            using(var up=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=0,clickCount=2}))
            using(var doubleClick=ClickEvent.GetPooled(up)){doubleClick.target=railButton;railButton.SendEvent(doubleClick);}
            Require(committed==source.Find(StairExtensionSetup.W24Id),"Double-click did not choose W24");committed=null;
            Click(root.Q<Button>("quick-"+source.presets[2].contentId));
            Require(committed==source.presets[2],"Quick piece did not choose its actual catalog definition");
            var gable=source.Find(OpeningWallSetup.GableId);
            view.SetSelection(gable);view.Filter(6);Draw();
            int shown=0;foreach(var b in presets)if(b.resolvedStyle.display!=DisplayStyle.None)shown++;
            Require(shown==2,"Roofs filter did not contain roof and one half-gable");
            view.FlipSelection();Require(view.Highlighted==gable.flipVariant,"Menu slope flip failed");
            view.ConfirmSelection();Require(committed==gable.flipVariant,"Confirmation selected wrong slope");
            committed=null;view.SetSelection(gable.flipVariant);
            using(var submit=NavigationSubmitEvent.GetPooled())
            {submit.target=root.Q<Button>(gable.contentId);root.Q<Button>(gable.contentId).SendEvent(submit);}
            Require(committed==gable.flipVariant,"Focused canonical tile overrode the staged slope on keyboard submit");
            committed=null;
            using(var up=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=0,clickCount=2}))
            using(var doubleClick=ClickEvent.GetPooled(up)){doubleClick.target=root.Q<Button>(gable.contentId);root.Q<Button>(gable.contentId).SendEvent(doubleClick);}
            Require(committed==gable.flipVariant,"Double-click lost staged gable slope");
            view.Filter(5);Draw();int stairsShown=0;foreach(var button in presets)if(button.resolvedStyle.display!=DisplayStyle.None)stairsShown++;
            Require(stairsShown==3,"Stairs filter must include W21/W22/W24");
            view.SetSelection(gable);view.Filter(0);Draw();
            var menu=root.Q<VisualElement>("build-menu");
            void CheckCentered()
            {
                Require(Mathf.Abs(menu.worldBound.center.x-root.worldBound.center.x)<.5f &&
                    Mathf.Abs(menu.worldBound.center.y-root.worldBound.center.y)<.5f,
                    "Build menu is not centered in the viewport");
            }
            CheckCentered();
            foreach(var stone in root.Query<VisualElement>(className:"stone-panel").ToList())
                Require(stone.resolvedStyle.backgroundImage.texture==Resources.Load<Texture2D>("Building/UI/StonePanel"),"Stone panel lost its background texture: "+string.Join(",",stone.GetClasses()));
            foreach(var pillar in root.Query<VisualElement>(className:"stone-pillar").ToList())
                Require(pillar.resolvedStyle.backgroundImage.texture==Resources.Load<Texture2D>("Building/UI/StonePillar")&&pillar.worldBound.height>100,"Stone pillar missing or collapsed");
            var choose=root.Q<Button>("build-choose");
            Require(menu.worldBound.width>500&&float.IsFinite(menu.worldBound.height),"Menu did not lay out");
            Require(choose.worldBound.yMax<=menu.worldBound.yMax+.1f&&choose.worldBound.xMax<=menu.worldBound.xMax+.1f,"Choose control overflow");
            foreach(var b in root.Query<Button>(className:"build-category").ToList())
                Require(b.worldBound.yMax<=menu.worldBound.yMax+.1f,"Category overflow");
            var scroll=root.Q<ScrollView>("build-presets");
            Require(scroll.contentViewport.worldBound.height>100,"Grid viewport collapsed");
            Save(target,$"{Captures}/catalog-{width}x{height}.png");
            scroll.ScrollTo(presets[presets.Count-1]);Draw();
            var last=presets[presets.Count-1].worldBound;
            Require(last.yMax<=scroll.contentViewport.worldBound.yMax+.5f&&last.yMin>=scroll.contentViewport.worldBound.yMin-.5f,"Last component unreachable");
            Save(target,$"{Captures}/catalog-bottom-{width}x{height}.png");
            // Panel GUI scale changes logical viewport dimensions and can switch responsive layouts.
            float originalScale=catalog.panel.scale;
            foreach(float guiScale in new[]{.75f,1.25f})
            {
                catalog.panel.scale=guiScale;
                typeof(PanelSettings).GetMethod("ApplyPanelSettings",Methods).Invoke(catalog.panel,null);
                Draw();CheckCentered();
            }
            catalog.panel.scale=originalScale;
            typeof(PanelSettings).GetMethod("ApplyPanelSettings",Methods).Invoke(catalog.panel,null);
            Draw();CheckCentered();
            view.SetState(true,false);view.Show("Half-gable · 45°","Supported · ready to place",true);Draw();
            Require(root.Q<VisualElement>("build-hud").pickingMode==PickingMode.Ignore,"HUD captures world input");
            Save(target,$"{Captures}/placement-{width}x{height}.png");
            view.Show("Half-gable · 45°","Detached · 3 seconds to reconnect",true,true);Draw();
            Require(root.Q<Label>("build-status").ClassListContains("build-warning"),"Warning styling missing");
            Save(target,$"{Captures}/warning-{width}x{height}.png");
            view.Show("Half-gable","Aim at a surface within reach",false);Draw();
            Require(root.Q<Label>("build-status").ClassListContains("build-error"),"Invalid styling missing");
        }
        finally
        {
            RenderTexture.active=previous;view?.Dispose();Object.DestroyImmediate(host);
            Object.DestroyImmediate(catalog.panel);Object.DestroyImmediate(catalog);target.Release();Object.DestroyImmediate(target);
        }
    }
    private static void Save(RenderTexture target,string file)
    {
        var previous=RenderTexture.active;RenderTexture.active=target;
        var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        try
        {
            pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
            int visible=0;foreach(var p in pixels.GetPixels32())if(p.a>20)visible++;
            Require(visible>100,"Blank UI/thumbnail render: "+file);File.WriteAllBytes(file,pixels.EncodeToPNG());
        }
        finally{Object.DestroyImmediate(pixels);RenderTexture.active=previous;}
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
