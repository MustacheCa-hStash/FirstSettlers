using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;
using Unity.Profiling;

/// <summary>Immutable, bounded derived cache. A worker builds a successor and publication
/// swaps it with its density/render/collider transaction. Failed/stale batches cannot poison it.</summary>
internal sealed class TerrainSculptMeshCache
{
    private const long EntryBytes=1024; // Owner stamps, key/index collections, object and growth slack.
    private readonly Dictionary<Key,Entry> entries=new Dictionary<Key,Entry>();
    private readonly List<Key> recent=new List<Key>();
    private ITerrainDensityGenerator generator;
    private TerrainDensityProfile profile;
    internal long Charge { get; private set; }
    internal readonly struct Key:IEquatable<Key>
    {
        internal readonly int3 Min,Max;
        private readonly bool visual;
        private readonly double3 anchor;
        internal Key(TerrainDensityCellBounds bounds,ITerrainDensityField field)
        {
            Min=bounds.Min;Max=bounds.Max;visual=field is TerrainSculptVisualField;
            anchor=visual?((TerrainSculptVisualField)field).CacheAnchor:default;
        }
        public bool Equals(Key other)=>math.all(Min==other.Min)&&math.all(Max==other.Max)&&visual==other.visual&&math.all(anchor==other.anchor);
        public override bool Equals(object obj)=>obj is Key key&&Equals(key);
        public override int GetHashCode()=>HashCode.Combine(Min.GetHashCode(),Max.GetHashCode(),visual,anchor.GetHashCode());
    }
    private sealed class Entry
    {
        internal readonly TerrainSculptMeshData Mesh;
        private readonly long[] owners=new long[27];
        private readonly int3 first;
        internal long Charge=>Mesh.PayloadBytes*2+EntryBytes;
        internal Entry(TerrainSculptMeshData mesh,TerrainDensitySnapshot snapshot,int3 cell)
        {
            Mesh=mesh;first=TerrainDensityCoordinates.BrickOf(cell)-1;
            int i=0;
            for(int z=0;z<3;z++)for(int y=0;y<3;y++)for(int x=0;x<3;x++)owners[i++]=Identity(snapshot,first+new int3(x,y,z));
        }
        internal bool Valid(TerrainDensitySnapshot snapshot)
        {
            int i=0;
            for(int z=0;z<3;z++)for(int y=0;y<3;y++)for(int x=0;x<3;x++)
                if(owners[i++]!=Identity(snapshot,first+new int3(x,y,z)))return false;
            return true;
        }
        private static long Identity(TerrainDensitySnapshot snapshot,int3 key)=>snapshot.Bricks.TryGetValue(key,out var brick)?brick.Identity:0;
    }
    internal sealed class Builder
    {
        private static readonly ProfilerMarker ExtractMarker=new ProfilerMarker("FS.Streaming.Sculpt.ExtractBricks");
        private readonly TerrainSculptMeshCache previous,next=new TerrainSculptMeshCache();
        private readonly TerrainDensitySnapshot snapshot;
        private readonly TerrainSculptBuildBudget budget;
        private readonly long limit;
        internal int Built { get; private set; }
        internal int Reused { get; private set; }
        internal double ExtractMs { get; private set; }
        internal Builder(TerrainSculptMeshCache previous,TerrainDensitySnapshot snapshot,TerrainSculptBuildBudget budget,long limit)
        {
            this.previous=previous!=null&&ReferenceEquals(previous.generator,snapshot.Generator)&&previous.profile.Equals(snapshot.Profile)?previous:null;
            this.snapshot=snapshot;this.budget=budget;this.limit=limit;
            next.generator=snapshot.Generator;next.profile=snapshot.Profile;
        }
        internal TerrainSculptMeshData Get(ITerrainDensityField field,TerrainDensityCellBounds bounds)
        {
            budget.Check();var key=new Key(bounds,field);
            if(next.entries.TryGetValue(key,out var hit)){Reused++;return hit.Mesh;}
            if(previous!=null&&previous.entries.TryGetValue(key,out hit)&&hit.Valid(snapshot))
            {Reused++;Retain(key,hit);return hit.Mesh;}
            long start=Stopwatch.GetTimestamp();
            TerrainSculptMeshData mesh;
            using(ExtractMarker.Auto())mesh=TerrainDensityMesher.Build(field,bounds,budget:budget);
            ExtractMs+=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;Built++;
            if(mesh.PayloadBytes*2+EntryBytes<=limit-next.Charge)
            {
                budget.ChargePersistent(EntryBytes);budget.RetainCharged(mesh.PayloadBytes*2);
                Add(key,new Entry(mesh,snapshot,bounds.Min));
            }
            return mesh;
        }
        private void Retain(Key key,Entry entry)
        {
            if(entry.Charge>limit-next.Charge)return;
            budget.ChargePersistent(EntryBytes);Add(key,entry);
        }
        private void Add(Key key,Entry entry)
        {
            next.entries.Add(key,entry);next.recent.Add(key);next.Charge+=entry.Charge;
        }
        internal TerrainSculptMeshCache Finish()
        {
            // Accessed bricks get priority; unused valid entries survive only inside the cache cap.
            // No snapshot or old density owner is retained by a cache entry.
            if(previous!=null)foreach(var key in previous.recent)
            {
                budget.Check();if(next.entries.ContainsKey(key))continue;
                var entry=previous.entries[key];
                if(entry.Charge<=limit-next.Charge&&entry.Valid(snapshot))Retain(key,entry);
            }
            return next;
        }
    }
}
