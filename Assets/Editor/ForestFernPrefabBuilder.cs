using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class ForestFernPrefabBuilder
{
    public const string NearPath="Assets/Resources/Foliage/ForestFern_LOD0.prefab";
    public const string FarPath="Assets/Resources/Foliage/ForestFern_LOD1.prefab";
    public const string MaterialPath="Assets/Materials/M_Grass/M_ForestFern.mat";
    [MenuItem("Tools/Foliage/Build Forest Fern")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Author ferns in Edit mode.");
        var shader=Shader.Find("FirstSettlers/Forest Fern Instanced");
        if(shader==null) throw new InvalidOperationException("Fern shader missing.");
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material==null) {material=new Material(shader);AssetDatabase.CreateAsset(material,MaterialPath);}
        material.shader=shader;material.enableInstancing=true;
        material.SetColor("_BaseColor",new Color(.31f,.47f,.16f));
        material.SetFloat("_AmbientStrength",.12f);material.SetFloat("_WindStrength",.016f);
        EditorUtility.SetDirty(material);
        BuildOne(false,material);BuildOne(true,material);AssetDatabase.SaveAssets();
    }
    private static Vector3 Spine(Vector3 outward,float length,float t) =>
        outward*(length*t)+Vector3.up*(length*.56f*Mathf.Sin(t*Mathf.PI*.88f));
    private static void BuildOne(bool far,Material material)
    {
        var vertices=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
        void Face(Vector3 a,Vector3 b,Vector3 c,Color tone)
        {
            int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);
            colors.Add(tone);colors.Add(tone);colors.Add(tone);
            triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
        }
        for(int frond=0;frond<7;frond++)
        {
            if(far && frond%2==1) continue;
            float angle=frond*2.399963f+.08f*Mathf.Sin(frond*7);
            var outward=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            var side=Vector3.Cross(Vector3.up,outward);
            float length=Mathf.Lerp(.48f,.68f,(Mathf.Sin(frond*17.31f)+1)*.5f);
            float shade=Mathf.Lerp(.94f,1.04f,(Mathf.Sin(frond*13.17f)+1)*.5f);
            var leaf=new Color(shade,shade,shade,1);
            var stem=new Color(.65f,.72f,.48f,1);
            int stemSections=far?2:3;
            Vector3 StemAt(float t)
            {
                float grid=Mathf.Clamp01(t)*stemSections;
                int section=Mathf.Min(stemSections-1,Mathf.FloorToInt(grid));
                return Vector3.Lerp(Spine(outward,length,section/(float)stemSections),
                    Spine(outward,length,(section+1f)/stemSections),grid-section);
            }
            for(int row=0;row<stemSections;row++)
            {
                Vector3 a=Spine(outward,length,row/(float)stemSections),b=Spine(outward,length,(row+1f)/stemSections);
                Vector3 width=side*.007f;
                Face(a-width,b-width,b+width,stem);Face(a-width,b+width,a+width,stem);
            }
            int pairs=far?5:8;
            for(int row=0;row<pairs;row++)
            {
                float t=.12f+row/(float)(pairs-1)*.75f;
                // Seat every leaflet on the rendered stem segments rather than
                // the analytic arc above them; this avoids disconnected leaflets.
                Vector3 root=StemAt(t);
                float reach=length*(.23f*Mathf.Pow(Mathf.Sin(t*Mathf.PI),.85f)+.015f);
                float halfWidth=length*(.027f+.057f*Mathf.Sin(t*Mathf.PI));
                foreach(int sign in new[]{-1,1})
                {
                    Vector3 tip=root+side*sign*reach+outward*(.024f+length*.04f)+Vector3.up*.008f;
                    Vector3 shoulder=Vector3.Lerp(root,tip,.43f)+Vector3.up*.009f;
                    Face(root,shoulder-outward*halfWidth,tip,leaf);
                    Face(root,tip,shoulder+outward*halfWidth,leaf);
                }
            }
            var end=StemAt(.9f);var endTip=Spine(outward,length,1.05f);
            Face(end-side*.03f,endTip,end+side*.03f,leaf);
        }
        string name=far?"ForestFern_LOD1":"ForestFern_LOD0";
        var built=new Mesh {name=name};built.SetVertices(vertices);built.SetColors(colors);built.SetTriangles(triangles,0);
        built.RecalculateNormals();built.RecalculateBounds();
        string meshPath="Assets/Models/Foliage/"+name+".asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if(mesh==null) {AssetDatabase.CreateAsset(built,meshPath);mesh=built;}
        else
        {
            mesh.Clear();mesh.vertices=built.vertices;mesh.normals=built.normals;mesh.colors=built.colors;
            mesh.triangles=built.triangles;mesh.bounds=built.bounds;mesh.UploadMeshData(false);
            EditorUtility.SetDirty(mesh);Object.DestroyImmediate(built);
        }
        var obj=new GameObject(name);
        try
        {
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
            PrefabUtility.SaveAsPrefabAsset(obj,far?FarPath:NearPath);
        }
        finally {Object.DestroyImmediate(obj);}
        Debug.Log($"FERN BUILT: {name}, {mesh.triangles.Length/3} triangles, {mesh.vertexCount} vertices, {mesh.bounds.size} bounds; uniform leaflet colors, no bitmap/veins.");
    }
}
