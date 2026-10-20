using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Scoped imports for L25, L27, L28, L31 (two slopes), L33 and L35.</summary>
public static class TimberInfillKitSetup
{
    public const string Models = "Assets/Models/Buildings/TimberInfill";
    public const string Colour = "Assets/Textures/Buildings/TimberInfill/TimberInfill_Colour.png";
    public const string Normal = "Assets/Textures/Buildings/TimberInfill/TimberInfill_Normal.png";
    public const string MaterialPath = "Assets/Materials/Buildings/TimberInfill/TimberInfill.mat";
    public const string ManifestPath = "ArtReferences/LimestoneBuildKit/TimberInfill/timber-infill-models.json";
    public static readonly string[] Slugs = {"l25-framed-daub-wall", "l27-framed-doorway", "l28-open-window-wall", "l31-framed-gable-rising", "l33-layered-shingle-roof", "l35-braced-door-canopy"};
    public static readonly string[] Ids = Array.ConvertAll(Slugs, s => "build.wood." + s);
    public const string FallingId = "build.wood.l31-framed-gable-falling";
    public static bool Available => File.Exists(Models + "/L35.fbx") && File.Exists(ManifestPath);
    [Serializable] public class Manifest { public Part[] parts; }
    [Serializable] public class Part { public string tag, slug, name, file, slope; public float[] size, minimum, opening, visual_minimum, visual_maximum; public int triangles; }
    public static Vector3 V(float[] a) => new(a[0], a[1], a[2]);
    public static Bounds Box(Vector3 a, Vector3 b) => new((a+b)*.5f, b-a);

    [MenuItem("Tools/Building/Install L25 L27 L28 L31 L33 L35")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install components outside Play mode.");
        var catalog = AssetDatabase.LoadAssetAtPath<BuildCatalog>(BuildingPrototypeSetup.CatalogPath);
        if (catalog == null) throw new InvalidOperationException("Existing catalog required.");
        var presets = new List<BuildDefinition>(catalog.presets);
        foreach (var d in Create()) { int index = presets.FindIndex(p => p != null && p.contentId == d.contentId); if (index < 0) presets.Add(d); else presets[index] = d; }
        catalog.presets = presets.ToArray(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        foreach (var id in Ids) { var d = catalog.Find(id); BuildStoneUiAuthoring.BakeThumbnail(d); if (d.flipVariant != null) BuildStoneUiAuthoring.BakeThumbnail(d.flipVariant); }
        AssetDatabase.SaveAssets();
        Debug.Log("TIMBER INFILL INSTALLED: L25/L27/L28/L31/L33/L35, open window, solid layered shingles, supported wall canopy.");
    }

    public static BuildDefinition[] Create()
    {
        Directory.CreateDirectory("Assets/Materials/Buildings/TimberInfill"); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var material = BakedStoneWallSetup.CreateMaterial(MaterialPath, Colour, Normal, "Timber, lime daub and cleft shingles");
        material.SetColor("_BaseColor", Color.white); material.SetFloat("_Saturation", 1); material.SetFloat("_Contrast", 1); material.SetFloat("_Brightness", 1);
        EditorUtility.SetDirty(material);
        var result = new List<BuildDefinition>(); BuildDefinition rising = null, falling = null;
        var source = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        foreach (var p in source.parts)
        {
            string path = BuildingPrototypeSetup.Folder + "/" + p.slug;
            var d = AssetDatabase.LoadAssetAtPath<BuildDefinition>(path + ".asset");
            if (d == null) { d = ScriptableObject.CreateInstance<BuildDefinition>(); AssetDatabase.CreateAsset(d, path + ".asset"); }
            d.contentId = "build.wood." + p.slug; d.displayName = p.name;
            bool roof = p.tag == "L33", canopy = p.tag == "L35", gable = p.tag.StartsWith("L31", StringComparison.Ordinal);
            d.kind = roof ? BuildPartKind.Roof : canopy ? BuildPartKind.Furniture : BuildPartKind.Wall;
            d.sizeUnits = Vector3Int.RoundToInt(V(p.size)/BuildGeometry.Unit); d.minimumUnits = Vector3Int.zero; d.boundsOffset = V(p.minimum);
            d.stackRiseUnits = gable || roof ? 8 : 12; d.wallEndInsetUnits = 0; d.wallPlacementMode = WallPlacementMode.Panel;
            d.material = material; d.preserveAuthoredMesh = true; d.maxHealth = 100; d.canGroundOnTerrain = false; d.requiresFullBearing = false;
            d.jointCoverVariants = Array.Empty<Mesh>(); d.jointCoverMaterial = null; d.uvVariants = Array.Empty<Mesh>();
            d.solidBoxes = Array.Empty<Bounds>(); d.wallOpenings = Array.Empty<Bounds>(); d.occupiedVolumes = Array.Empty<BuildConvexVolume>();
            d.roofContinuationMesh = null; d.gableRoofSeamMesh = null; d.roofAttachment = gable ? RoofAttachmentMode.Gable : RoofAttachmentMode.None;
            d.gableSlope = !gable ? GableSlope.None : p.slope == "falling" ? GableSlope.Falling : GableSlope.Rising; d.flipVariant = null;
            d.furnitureWallMounted = canopy;
            d.furnitureSupportPoints = canopy ? new[] {new Vector3(-.85f,-.65f,0), new Vector3(.85f,-.65f,0),new Vector3(-.85f,.92f,0),new Vector3(.85f,.92f,0)} : Array.Empty<Vector3>();
            var sockets = new List<BuildJointSocket>();
            if (d.kind == BuildPartKind.Wall)
            {
                if (gable) foreach (float x in new[] {0f,2f}) sockets.Add(new BuildJointSocket {localPosition=new Vector3(x,0,0), outward=x==0?Vector3.left:Vector3.right});
                else for (int j=0;j<=16;j++)
                    if (p.opening == null || p.opening.Length==0 || p.opening[1]>0 || j*.25f<p.opening[0] || j*.25f>p.opening[2])
                        sockets.Add(new BuildJointSocket {localPosition=new Vector3(j*.25f,0,0),outward=j==0?Vector3.left:Vector3.right,interior=j>0&&j<16});
            }
            d.jointSockets = sockets.ToArray();
            string modelPath = Models + "/" + p.file;
            var imported = ModularWoodSetup.Import(modelPath);
            var visual = WattleWallSetup.BakeStaticMesh(imported, Vector3.zero, p.name, out _, true);
            visual.RecalculateTangents();
            if ((visual.bounds.min-V(p.visual_minimum)).sqrMagnitude>.000001f || (visual.bounds.max-V(p.visual_maximum)).sqrMagnitude>.000001f)
                throw new InvalidOperationException("FBX changed structural axes or origin: " + p.tag);
            d.mesh = Save(visual, path + "-mesh.asset");
            if (p.opening != null && p.opening.Length==4)
            {
                float x0=p.opening[0], y0=p.opening[1], x1=p.opening[2], y1=p.opening[3];
                d.wallOpenings = new[] { Box(new Vector3(x0,y0,-.125f),new Vector3(x1,y1,.125f)) };
                var solids=new List<Bounds>{Box(new Vector3(0,0,-.125f),new Vector3(x0,3,.125f)),Box(new Vector3(x1,0,-.125f),new Vector3(4,3,.125f)),Box(new Vector3(x0,y1,-.125f),new Vector3(x1,3,.125f))};
                if (y0>0) solids.Add(Box(new Vector3(x0,0,-.125f),new Vector3(x1,y0,.125f)));
                d.solidBoxes=solids.ToArray();
            }
            if (gable)
            {
                bool f=p.slope=="falling";
                var points=f?new[]{new Vector2(0,0),new Vector2(2,0),new Vector2(0,2)}:new[]{new Vector2(0,0),new Vector2(2,0),new Vector2(2,2)};
                var v=new Vector3[6];for(int i=0;i<3;i++){v[i]=new Vector3(points[i].x,points[i].y,-.125f);v[i+3]=new Vector3(points[i].x,points[i].y,.125f);}
                d.occupiedVolumes=new[]{Hull(path,0,v,new[]{0,2,1,3,4,5,0,1,4,0,4,3,1,2,5,1,5,4,2,0,3,2,3,5})};
                if(f)falling=d;else rising=d;
            }
            if (roof)
            {
                d.occupiedVolumes=new[]{Slope(path,0,0,4,0,2,0,2,.34f,2.34f), Cube(path,1,new Vector3(0,0,0),new Vector3(4,.13f,.25f))};
                d.roofContinuationMesh=Save(ClipEave(d.mesh),path+"-joined-mesh.asset");
            }
            if (canopy)
            {
                var volumes=new List<BuildConvexVolume>{Slope(path,0,-1,1,0,1.22f,1,-.22f,1.34f,.12f), Cube(path,1,new Vector3(-1,.85f,0),new Vector3(1,1,.14f))};
                int n=2;
                foreach(float x in new[]{-.925f,.775f})
                {
                    volumes.Add(Cube(path,n++,new Vector3(x,-.75f,0),new Vector3(x+.15f,1,.13f)));
                    var yz=new[]{new Vector2(-.70f,.08f),new Vector2(.16f,.88f),new Vector2(.28f,.79f),new Vector2(-.52f,.06f)};
                    var v=new Vector3[8];for(int i=0;i<4;i++){v[i]=new Vector3(x,yz[i].x,yz[i].y);v[i+4]=new Vector3(x+.15f,yz[i].x,yz[i].y);}
                    volumes.Add(Hull(path,n++,v,BoxTriangles));
                }
                d.occupiedVolumes=volumes.ToArray();
            }
            Prefab(d,path); EditorUtility.SetDirty(d);
            if(p.slope!="falling")result.Add(d);
        }
        if(rising==null||falling==null)throw new InvalidOperationException("Both gable slopes required.");
        rising.flipVariant=falling;falling.flipVariant=rising;EditorUtility.SetDirty(rising);EditorUtility.SetDirty(falling);
        AssetDatabase.SaveAssets();return result.ToArray();
    }

    private static readonly int[] BoxTriangles={0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7};
    private static BuildConvexVolume Cube(string path,int n,Vector3 lo,Vector3 hi) => Slope(path,n,lo.x,hi.x,lo.z,hi.z,lo.y,lo.y,hi.y,hi.y);
    private static BuildConvexVolume Slope(string path,int n,float x0,float x1,float z0,float z1,float low0,float low1,float high0,float high1) => Hull(path,n,new[]{new Vector3(x0,low0,z0),new Vector3(x1,low0,z0),new Vector3(x1,low1,z1),new Vector3(x0,low1,z1),new Vector3(x0,high0,z0),new Vector3(x1,high0,z0),new Vector3(x1,high1,z1),new Vector3(x0,high1,z1)},BoxTriangles);
    private static BuildConvexVolume Hull(string path,int n,Vector3[] vertices,int[] indices)
    {
        indices=(int[])indices.Clone();Vector3 centre=Vector3.zero;foreach(var v in vertices)centre+=v/vertices.Length;
        for(int i=0;i<indices.Length;i+=3)
            if(Vector3.Dot(Vector3.Cross(vertices[indices[i+1]]-vertices[indices[i]],vertices[indices[i+2]]-vertices[indices[i]]),(vertices[indices[i]]+vertices[indices[i+1]]+vertices[indices[i+2]])/3-centre)<0)
                (indices[i+1],indices[i+2])=(indices[i+2],indices[i+1]);
        var mesh=new Mesh{name="Occupied convex hull"};mesh.vertices=vertices;mesh.triangles=indices;mesh.RecalculateBounds();mesh.RecalculateNormals();
        return BuildCollisionAuthoring.Volume(Save(mesh,path+"-collision-"+n+".asset"));
    }
    private static Mesh Save(Mesh generated,string path)
    {
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved==null){saved=generated;AssetDatabase.CreateAsset(saved,path);}else{EditorUtility.CopySerialized(generated,saved);Object.DestroyImmediate(generated);EditorUtility.SetDirty(saved);}return saved;
    }
    private static void Prefab(BuildDefinition d,string path)
    {
        var root=new GameObject(d.displayName){layer=GameplayLayers.WorldSolid};
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh=d.mesh;root.AddComponent<MeshRenderer>().sharedMaterial=d.material;
            foreach(var v in d.occupiedVolumes){var c=root.AddComponent<MeshCollider>();c.convex=true;c.sharedMesh=v.mesh;}
            if(d.occupiedVolumes.Length==0)foreach(var b in d.solidBoxes.Length>0?d.solidBoxes:new[]{d.LocalBounds}){var c=root.AddComponent<BoxCollider>();c.center=b.center;c.size=b.size;}
            d.authoringPrefab=PrefabUtility.SaveAsPrefabAsset(root,path+".prefab");
        }finally{Object.DestroyImmediate(root);}
    }
    private static Mesh ClipEave(Mesh source)
    {
        var v=source.vertices;var n=source.normals;var u=source.uv;var t=source.triangles;
        var vs=new List<Vector3>();var ns=new List<Vector3>();var us=new List<Vector2>();var ts=new List<int>();
        var polygon=new List<(Vector3 p,Vector3 n,Vector2 u)>();
        for(int i=0;i<t.Length;i+=3)
        {
            polygon.Clear();for(int j=0;j<3;j++)
            {
                int a=t[i+j],b=t[i+(j+1)%3];bool inside=v[a].z>=0,other=v[b].z>=0;
                if(inside)polygon.Add((v[a],n[a],u[a]));
                if(inside!=other){float f=-v[a].z/(v[b].z-v[a].z);var p=Vector3.Lerp(v[a],v[b],f);p.z=0;polygon.Add((p,Vector3.Lerp(n[a],n[b],f).normalized,Vector2.Lerp(u[a],u[b],f)));}
            }
            for(int j=1;j<polygon.Count-1;j++)
            {
                var a=polygon[0];var b=polygon[j];var c=polygon[j+1];if(Vector3.Cross(b.p-a.p,c.p-a.p).sqrMagnitude<1e-12f)continue;
                foreach(var point in new[]{a,b,c}){ts.Add(vs.Count);vs.Add(point.p);ns.Add(point.n);us.Add(point.u);}
            }
        }
        var mesh=new Mesh{name="Shingle roof without uphill eave"};mesh.SetVertices(vs);mesh.SetNormals(ns);mesh.SetUVs(0,us);mesh.SetTriangles(ts,0);mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
    }
}
