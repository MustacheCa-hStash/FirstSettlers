using System;
using UnityEngine;

/// <summary>Retains the procedural batch. Compaction advances 128 instances at a time;
/// only a complete, current result becomes visible.</summary>
public sealed class BuildingFoliageBatch : IDisposable
{
    private readonly Matrix4x4[] original;
    private readonly Vector4[] originalValues;
    private Matrix4x4[] staging;
    private Vector4[] stagingValues;
    private int retained;
    private readonly BuildFoliageExclusion.Filter filter;
    public Matrix4x4[] Matrices { get; private set; }
    public Vector4[] Values { get; private set; }
    public int Count { get; private set; }
    public BuildingFoliageBatch(Matrix4x4[] matrices, Vector4[] values, float height)
    {
        original=Matrices=matrices; originalValues=Values=values; Count=matrices.Length;
        filter=BuildFoliageExclusion.Active?.Register(matrices.Length,i=>
        {
            var matrix=original[i];
            return new BuildFoliageExclusion.Probe(matrix.MultiplyPoint3x4(Vector3.zero),
                matrix.MultiplyVector(Vector3.up*height).magnitude);
        },Apply);
    }
    private void Apply(int start,bool[] hidden,int count)
    {
        if(start==0)
        {
            retained=0;
            // At most one ordinary instanced batch (1023 matrices), never a whole chunk.
            if(staging==null || ReferenceEquals(staging,Matrices))staging=new Matrix4x4[original.Length];
            if(originalValues!=null && (stagingValues==null || ReferenceEquals(stagingValues,Values)))stagingValues=new Vector4[original.Length];
        }
        for(int i=0;i<count;i++)if(!hidden[i])
        {
            staging[retained]=original[start+i];
            if(originalValues!=null)stagingValues[retained]=originalValues[start+i];
            retained++;
        }
        if(start+count!=original.Length)return;
        var previous=Matrices;var previousValues=Values;
        Matrices=staging;Values=stagingValues;Count=retained;
        staging=ReferenceEquals(previous,original)?null:previous;
        stagingValues=ReferenceEquals(previousValues,originalValues)?null:previousValues;
    }
    public void Dispose()=>filter?.Dispose();
}
