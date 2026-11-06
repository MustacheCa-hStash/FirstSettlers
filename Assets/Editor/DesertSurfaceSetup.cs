using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Project-native technical bake. Generated albedo artwork is never edited.</summary>
public static class DesertSurfaceSetup
{
    public const string Root = "Assets/Textures/Ground/Desert/";
    public const string MaterialPath = "Assets/Materials/M_Terrain/M_TerrainBase.mat";

    [MenuItem("Tools/Terrain/Install Dry Desert Surfaces")]
    public static void Install()
    {
        InstallSurface("CrackedClay", "Clay", .75f, .28f);
        InstallSurface("DryWash", "Wash", .80f, .22f);
        AssetDatabase.SaveAssets();
    }

    private static void Configure(string path, bool normal, bool readable = false)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = !normal;
        importer.isReadable = readable;
        importer.maxTextureSize = 1024;
        importer.npotScale = TextureImporterNPOTScale.ToNearest;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.flipGreenChannel = false;
        importer.textureCompression = readable ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name="Standalone", overridden=true, maxTextureSize=1024,
            format=readable?TextureImporterFormat.RGBA32:normal?TextureImporterFormat.BC5:TextureImporterFormat.BC7
        });
        importer.SaveAndReimport();
    }

    public static void InstallSurface(string name, string slot, float tiling, float normalStrength, string root = Root)
    {
        string albedo=root+"T_"+name+"_Albedo.png",normal=root+"T_"+name+"_NormalGL.png",height=root+"T_"+name+"_Height.png";
        Configure(albedo,false,true);
        var source=AssetDatabase.LoadAssetAtPath<Texture2D>(albedo);
        if(source==null)throw new InvalidOperationException("Expected an imported 2D texture: "+albedo);
        var pixels=source.GetPixels32();int width=source.width,depth=source.height;
        Color average=Color.clear;
        foreach(var pixel in pixels)average+=((Color)pixel).linear;
        average=(average/pixels.Length).gamma;average.a=1;
        if(!File.Exists(normal) || !File.Exists(height))Bake(name,pixels,width,depth,height,normal);
        Configure(albedo,false);Configure(normal,true);
        AssetDatabase.ImportAsset(height,ImportAssetOptions.ForceSynchronousImport);
        var heightImporter=(TextureImporter)AssetImporter.GetAtPath(height);
        heightImporter.textureShape=TextureImporterShape.Texture2D;
        heightImporter.sRGBTexture=false;heightImporter.mipmapEnabled=false;heightImporter.maxTextureSize=1024;
        heightImporter.wrapMode=TextureWrapMode.Repeat;heightImporter.SaveAndReimport();
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material==null)throw new InvalidOperationException("Missing terrain base material.");
        bool firstInstall=material.GetTexture("_"+slot+"Albedo")==null;
        material.SetTexture("_"+slot+"Albedo",AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
        material.SetTexture("_"+slot+"Normal",AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
        material.SetColor("_"+slot+"AverageColor",average);
        if(firstInstall)
        {
            material.SetColor("_"+slot+"Color",Color.white);
            material.SetFloat("_"+slot+"Tiling",tiling);
            material.SetFloat("_"+slot+"NormalStrength",normalStrength);
            if(root==Root)
            {
                material.SetFloat("_DrySurfaceStrength",1);
                material.SetFloat("_DrySurfaceFadeStart",70);material.SetFloat("_DrySurfaceFadeEnd",220);
                material.SetFloat("_DrySurfaceNormalEnd",65);
            }
            else { material.SetFloat("_IceFadeStart",90);material.SetFloat("_IceFadeEnd",260); }
        }
        EditorUtility.SetDirty(material);
        Debug.Log($"TERRAIN TEXTURE: {name}, {width}x{depth}, BC7/BC5, linear-average distance color {average}.");
    }

    private static float[] Blur(float[] source,int width,int height,int radius)
    {
        var horizontal=new float[source.Length];var result=new float[source.Length];int count=radius*2+1;
        int Wrap(int v,int size)=>(v%size+size)%size;
        for(int y=0;y<height;y++)
        {
            float sum=0;for(int i=-radius;i<=radius;i++)sum+=source[y*width+Wrap(i,width)];
            for(int x=0;x<width;x++)
            {horizontal[y*width+x]=sum/count;sum+=source[y*width+Wrap(x+radius+1,width)]-source[y*width+Wrap(x-radius,width)];}
        }
        for(int x=0;x<width;x++)
        {
            float sum=0;for(int i=-radius;i<=radius;i++)sum+=horizontal[Wrap(i,height)*width+x];
            for(int y=0;y<height;y++)
            {result[y*width+x]=sum/count;sum+=horizontal[Wrap(y+radius+1,height)*width+x]-horizontal[Wrap(y-radius,height)*width+x];}
        }
        return result;
    }

    private static void Bake(string name,Color32[] pixels,int width,int height,string heightPath,string normalPath)
    {
        var luma=new float[pixels.Length];
        for(int i=0;i<luma.Length;i++)luma[i]=(pixels[i].r*.2126f+pixels[i].g*.7152f+pixels[i].b*.0722f)/255f;
        var fine=Blur(luma,width,height,1);var local=Blur(luma,width,height,12);
        var relief=new float[luma.Length];
        for(int i=0;i<relief.Length;i++)relief[i]=Mathf.Clamp01(.55f+(fine[i]-local[i])*(name=="CrackedClay"?1.2f:.7f));
        relief=Blur(relief,width,height,1);
        var heights=new Color32[relief.Length];var normals=new Color32[relief.Length];
        float gain=name=="CrackedClay"?16f:10f;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int i=y*width+x;byte h=(byte)Mathf.RoundToInt(relief[i]*255);heights[i]=new Color32(h,h,h,255);
            float dx=(relief[y*width+(x+1)%width]-relief[y*width+(x+width-1)%width])*.5f;
            float dy=(relief[((y+1)%height)*width+x]-relief[((y+height-1)%height)*width+x])*.5f;
            var n=new Vector3(-dx*gain,-dy*gain,1).normalized;
            normals[i]=new Color32((byte)Mathf.RoundToInt((n.x*.5f+.5f)*255),
                (byte)Mathf.RoundToInt((n.y*.5f+.5f)*255),(byte)Mathf.RoundToInt((n.z*.5f+.5f)*255),255);
        }
        Save(heightPath,heights,width,height);Save(normalPath,normals,width,height);
    }
    private static void Save(string path,Color32[] pixels,int width,int height)
    {
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,false,true);
        try{texture.SetPixels32(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
        finally{Object.DestroyImmediate(texture);}
    }
}
