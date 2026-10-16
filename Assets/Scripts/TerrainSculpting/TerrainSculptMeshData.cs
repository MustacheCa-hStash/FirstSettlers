using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Caller-owned derived geometry; no authoritative density is stored in this object.</summary>
public sealed class TerrainSculptMeshData
{
    public readonly double3[] Positions;
    public readonly float3[] Normals;
    public readonly int[] Triangles;
    public readonly float2[] SurfaceData; // UV1.x = exposed stone blend; absent on collision/cache meshes.
    public readonly int SampledBricks,SkippedBricks;
    public int TriangleCount => Triangles.Length / 3;
    public long PayloadBytes => (long)Positions.Length * 24 + (long)Normals.Length * 12 + (long)Triangles.Length * 4+(long)(SurfaceData?.Length??0)*8;

    internal TerrainSculptMeshData(double3[] positions, float3[] normals, int[] triangles,int sampledBricks,int skippedBricks,float2[] surfaceData=null)
    { Positions = positions; Normals = normals; Triangles = triangles;SampledBricks=sampledBricks;SkippedBricks=skippedBricks;SurfaceData=surfaceData; }

    // Unity Mesh allocation/publication belongs on the main thread. Use one common origin for
    // adjoining pieces so shared double positions undergo exactly the same float conversion.
    public Mesh CreateMesh(double3 commonOrigin, string name = "Sculpted terrain")
    {
        var mesh = new Mesh { name = name, indexFormat = Positions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        try
        {
            var vertices = new Vector3[Positions.Length]; var normals = new Vector3[Normals.Length];
            for (int i = 0; i < vertices.Length; i++)
            { vertices[i] = (Vector3)(float3)(Positions[i] - commonOrigin); normals[i] = (Vector3)Normals[i]; }
            mesh.vertices = vertices; mesh.normals = normals; mesh.triangles = Triangles;
            if(SurfaceData!=null)
            {
                var uv=new Vector2[SurfaceData.Length];for(int i=0;i<uv.Length;i++)uv[i]=(Vector2)SurfaceData[i];
                mesh.uv2=uv;
            }
            mesh.RecalculateBounds(); return mesh;
        }
        catch { if (Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh); throw; }
    }
}

internal sealed class TerrainSculptMeshBuffer
{
    internal readonly List<double3> Positions = new List<double3>();
    internal readonly List<float3> Normals = new List<float3>();
    internal readonly List<int> Triangles = new List<int>();
    private readonly int maxVertices, maxTriangles;
    private readonly TerrainSculptBuildBudget budget;
    internal TerrainSculptMeshBuffer(int vertices = 500000, int triangles = 1000000, TerrainSculptBuildBudget budget = null)
    { maxVertices = vertices; maxTriangles = triangles; this.budget = budget; }
    internal int Add(double3 point, double3 normal)
    {
        if(!math.all(math.isfinite(point))||!math.all(math.isfinite(normal)))throw new ArgumentException("Mesh positions and normals must be finite.");
        if (Positions.Count >= maxVertices) throw new TerrainDensityCapacityException();
        budget?.Charge(320); // Lists/arrays, edge indexes, normals, Unity conversion/upload copies.
        Positions.Add(point); Normals.Add((float3)math.normalizesafe(normal, new double3(0, 1, 0))); return Positions.Count - 1;
    }
    // Cached vertices have already been validated/normalized. Preserve their exact seam normals.
    internal int AddStored(double3 point,float3 normal)
    {
        if(Positions.Count>=maxVertices)throw new TerrainDensityCapacityException();
        budget?.Charge(320);Positions.Add(point);Normals.Add(normal);return Positions.Count-1;
    }
    internal void Triangle(int a, int b, int c, double3 towardAir)
    {
        if (a == b || b == c || a == c) return;
        double3 cross = math.cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
        if (math.lengthsq(cross) < 1e-24) return;
        if (Triangles.Count / 3 >= maxTriangles) throw new TerrainDensityCapacityException();
        budget?.Charge(64);
        if (math.dot(cross, towardAir) < 0) { int swap = b; b = c; c = swap; }
        Triangles.Add(a); Triangles.Add(b); Triangles.Add(c);
    }
    internal void Append(TerrainSculptMeshData mesh)
    {
        if((long)Positions.Count+mesh.Positions.Length>maxVertices||(long)Triangles.Count+mesh.Triangles.Length>(long)maxTriangles*3)
            throw new TerrainDensityCapacityException();
        int offset=Positions.Count;
        for(int i=0;i<mesh.Positions.Length;i++)Add(mesh.Positions[i],mesh.Normals[i]);
        budget?.Charge((long)mesh.Triangles.Length*22);
        foreach(int index in mesh.Triangles)Triangles.Add(index+offset);
    }
    internal TerrainSculptMeshData Finish(int sampledBricks=0,int skippedBricks=0) => new TerrainSculptMeshData(Positions.ToArray(), Normals.ToArray(), Triangles.ToArray(),sampledBricks,skippedBricks);
}

public readonly struct TerrainDensityCellBounds
{
    public readonly int3 Min, Max; // Maximum is exclusive; these are cell coordinates.
    public long CellCount => checked(checked(((long)Max.x - Min.x) * ((long)Max.y - Min.y)) * ((long)Max.z - Min.z));
    public TerrainDensityCellBounds(int3 min, int3 max)
    {
        if (math.any(max <= min) || math.any(min < int.MinValue + 32) || math.any(max > int.MaxValue - 32))
            throw new ArgumentOutOfRangeException(nameof(max));
        Min = min; Max = max;
    }
}
