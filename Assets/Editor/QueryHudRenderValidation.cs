using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Explicit GPU render check of the real runtime HUD at 1080p and 4K, without generating the world.</summary>
public static class QueryHudRenderValidation
{
    private const BindingFlags Methods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("Tools/UI/Render Query HUD Checks")]
    public static void Run()
    {
        Directory.CreateDirectory(".utmp/query-hud");
        RenderAt(1920, 1080);
        RenderAt(3840, 2160);
        Debug.Log("QUERY HUD RENDER PASS: real UI Toolkit text-only panel, Segoe UI and layout at 1920x1080 and 3840x2160.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    private static void RenderAt(int width, int height)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(QueryHudSetup.PrefabPath);
        var rootObject = Object.Instantiate(prefab);
        rootObject.GetComponent<QueryHudPresenter>().enabled = false;
        var document = rootObject.GetComponent<UIDocument>();
        var settings = Object.Instantiate(document.panelSettings);
        var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        texture.Create();
        var oldTarget = RenderTexture.active;
        Texture2D readback = null;
        try
        {
            settings.targetTexture = texture;
            settings.clearColor = true;
            settings.colorClearValue = new Color(.31f, .43f, .32f, 1);
            document.panelSettings = settings;
            var view = rootObject.GetComponent<QueryHudView>();
            if (!view.Bind()) throw new InvalidOperationException("Render fixture has no query layout.");
            var card = AssetDatabase.LoadAssetAtPath<WorldObjectDefinition>(QueryHudSetup.CardPath);
            var target = new QueryTargetInfo(null, card.DisplayName, card.Icon, QueryTargetCapabilities.None, null, card);
            view.Show(new QueryHudData(target));
            var panel = document.rootVisualElement.panel;
            if (panel == null) throw new InvalidOperationException("No runtime UI Toolkit panel was created.");
            typeof(PanelSettings).GetMethod("ApplyPanelSettings", Methods).Invoke(settings, null);
            for (int i = 0; i < 5; i++)
            {
                panel.GetType().GetMethod("Update", Methods).Invoke(panel, null);
                panel.GetType().GetMethod("ValidateLayout", Methods).Invoke(panel, null);
                panel.GetType().GetMethod("Repaint", Methods, null, new[] { typeof(Event) }, null)
                    .Invoke(panel, new object[] { new Event { type = EventType.Repaint } });
                panel.GetType().GetMethod("Render", Methods).Invoke(panel, null);
            }
            var element = document.rootVisualElement.Q<VisualElement>("query-card");
            var name = document.rootVisualElement.Q<Label>("query-name");
            if (!float.IsFinite(element.worldBound.width) || element.worldBound.width < 159 || element.worldBound.width > 261 ||
                element.worldBound.height < 40 ||
                name.resolvedStyle.unityFontDefinition.fontAsset == null)
                throw new InvalidOperationException("Rendered panel layout or font did not resolve.");
            RenderTexture.active = texture;
            readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readback.Apply();
            // A blank render cannot pass just because a managed VisualElement exists.
            int different = 0;
            var pixels = readback.GetPixels32();
            var background = pixels[0];
            for (int i = 0; i < pixels.Length; i++)
                if (Math.Abs(pixels[i].r - background.r) + Math.Abs(pixels[i].g - background.g) + Math.Abs(pixels[i].b - background.b) > 35)
                    different++;
            if (different < 1000) throw new InvalidOperationException("Query HUD GPU render is blank.");
            string path = ".utmp/query-hud/query-hud-" + width + "x" + height + ".png";
            File.WriteAllBytes(path, readback.EncodeToPNG());
            Debug.Log("QUERY HUD RENDER " + width + "x" + height + ": " + element.worldBound + "; " + different + " changed pixels; " + path);
        }
        finally
        {
            RenderTexture.active = oldTarget;
            if (readback != null) Object.DestroyImmediate(readback);
            Object.DestroyImmediate(rootObject);
            Object.DestroyImmediate(settings);
            texture.Release();
            Object.DestroyImmediate(texture);
        }
    }
}
