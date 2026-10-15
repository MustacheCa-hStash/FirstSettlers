using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Keep wattle framing and roof timber on the selected wood atlas; retain weave/thatch pixels.</summary>
public static class WoodSurfaceSetup
{
    public static Texture2D ActiveTexture
    {
        get
        {
            var wood=AssetDatabase.LoadAssetAtPath<Material>(SplitPlankWallSetup.MaterialPath);
            return wood!=null && wood.GetTexture("_BaseMap") is Texture2D chosen?chosen:
                AssetDatabase.LoadAssetAtPath<Texture2D>(SplitPlankWallSetup.TexturePath);
        }
    }
    private static Texture2D Decode(string path)
    {
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
        if(!texture.LoadImage(File.ReadAllBytes(path))){Object.DestroyImmediate(texture);throw new InvalidOperationException("Cannot decode texture: "+path);}
        return texture;
    }
    private static bool Equal(Color32 a,Color32 b)=>a.r==b.r&&a.g==b.g&&a.b==b.b&&a.a==b.a;
    public static int UpdateRoofTimber(Texture2D active)
    {
        if(active==null)throw new InvalidOperationException("Select a wood atlas on SplitPlankWood first.");
        string sourcePath=AssetDatabase.GetAssetPath(active);
        var wood=Decode(sourcePath);var roof=Decode(ThatchRoofSetup.TexturePath);
        try
        {
            if(wood.width!=2048||wood.height!=2048||roof.width!=2048||roof.height!=2048)
                throw new InvalidOperationException("Wood and roof require the existing 2048-square atlas layout.");
            var source=wood.GetPixels32();var before=roof.GetPixels32();var output=(Color32[])before.Clone();int changed=0;
            // Unity pixels start at bottom-left. Roof wood is in U=0..0.5,V=0.5..1.
            // Its lower-right subcell contains wattle: keep those pixels and every thatch pixel exact.
            for(int y=0;y<1024;y++)for(int x=0;x<1024;x++)
            {
                if(x>=512&&y<512)continue;
                int s=(y*2)*2048+x*2;Color32 a=source[s],b=source[s+1],c=source[s+2048],d=source[s+2049];
                int destination=(y+1024)*2048+x;
                var colour=new Color32((byte)((a.r+b.r+c.r+d.r+2)/4),(byte)((a.g+b.g+c.g+d.g+2)/4),
                    (byte)((a.b+b.b+c.b+d.b+2)/4),before[destination].a);
                if(!Equal(colour,before[destination]))changed++;
                output[destination]=colour;
            }
            for(int y=0;y<2048;y++)for(int x=0;x<2048;x++)
            {
                int i=y*2048+x;bool timber=x<1024&&y>=1024&&!(x>=512&&y<1536);
                if(output[i].a!=before[i].a || !timber&&!Equal(output[i],before[i]))throw new InvalidOperationException("Roof weave/thatch pixels changed.");
            }
            if(changed>0)
            {
                roof.SetPixels32(output);roof.Apply(false,false);File.WriteAllBytes(ThatchRoofSetup.TexturePath,roof.EncodeToPNG());
                AssetDatabase.ImportAsset(ThatchRoofSetup.TexturePath,ImportAssetOptions.ForceSynchronousImport);
            }
            return changed;
        }finally{Object.DestroyImmediate(wood);Object.DestroyImmediate(roof);}
    }
    [MenuItem("Tools/Building/Match Wattle and Roof to Active Wood Texture")]
    public static void Sync()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Match building textures outside Play mode.");
        var active=ActiveTexture;var wattle=AssetDatabase.LoadAssetAtPath<Material>(WattleWallSetup.MaterialPath);
        var roof=AssetDatabase.LoadAssetAtPath<Material>(ThatchRoofSetup.MaterialPath);
        if(active==null||wattle==null||roof==null)throw new InvalidOperationException("Install wood, wattle and roof materials first.");
        var previous=wattle.GetTexture("_BaseMap") as Texture2D;
        if(previous!=null)
        {
            var old=Decode(AssetDatabase.GetAssetPath(previous));var next=Decode(AssetDatabase.GetAssetPath(active));
            try
            {
                if(old.width!=2048||old.height!=2048||next.width!=2048||next.height!=2048)throw new InvalidOperationException("Wattle requires the existing atlas layout.");
                var a=old.GetPixels32();var b=next.GetPixels32();
                for(int y=0;y<2048;y++)for(int x=0;x<2048;x++)
                {
                    int i=y*2048+x;if(a[i].a!=b[i].a||x>=1024&&y<1024&&!Equal(a[i],b[i]))
                        throw new InvalidOperationException("Selected wood atlas changes the existing wattle weave or alpha.");
                }
            }finally{Object.DestroyImmediate(old);Object.DestroyImmediate(next);}
        }
        int changed=UpdateRoofTimber(active);
        wattle.SetTexture("_BaseMap",active);EditorUtility.SetDirty(wattle);
        roof.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(ThatchRoofSetup.TexturePath));EditorUtility.SetDirty(roof);
        AssetDatabase.SaveAssets();
        var catalog=AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        foreach(string id in new[]{"build.prototype.wall",WattleWallSetup.ContentId,ThatchRoofSetup.ContentId})BuildStoneUiAuthoring.BakeThumbnail(catalog.Find(id));
        AssetDatabase.SaveAssets();Directory.CreateDirectory(".utmp/wood-surface-sync");
        File.WriteAllText(".utmp/wood-surface-sync/report.json",JsonUtility.ToJson(new Report{
            woodAtlas=AssetDatabase.GetAssetPath(active),changedRoofTimberPixels=changed,wattleWeavePreserved=true,roofWeaveAndThatchPreserved=true,allAlphaPreserved=true},true));
        Debug.Log("WOOD SURFACES MATCHED: "+active.name+"; roof timber pixels updated="+changed+"; original weave, thatch and alpha preserved.");
    }
    [Serializable] private sealed class Report
    {
        public string woodAtlas;public int changedRoofTimberPixels;public bool wattleWeavePreserved,roofWeaveAndThatchPreserved,allAlphaPreserved;
    }
    public static void SyncBatch()
    {
        try{Sync();EditorApplication.Exit(0);}catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
}
