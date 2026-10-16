using System;
using UnityEngine;

/// <summary>Bounded GPU cell lists. Rebuilt only when source membership crosses an 8 m cell boundary.</summary>
public sealed class LocalFlameSpatialGrid : IDisposable
{
    public const int TableSize=8192,NodeCapacity=8192;
    public struct Entry { public int x,y,z,w; public Entry(Vector3Int p,int head){x=p.x;y=p.y;z=p.z;w=head;} }
    public struct Node { public int light,next; }
    readonly Entry[] table=new Entry[TableSize];
    readonly Node[] nodes=new Node[NodeCapacity];
    ComputeBuffer entries,links;
    ulong previousHash=ulong.MaxValue;
    public static Vector3Int Cell(Vector3 p)=>new(Mathf.FloorToInt(p.x/8),Mathf.FloorToInt(p.y/8),Mathf.FloorToInt(p.z/8));
    public static uint Hash(Vector3Int p)=>unchecked((uint)p.x*73856093u^(uint)p.y*19349663u^(uint)p.z*83492791u);
    public void Update(Vector4[] positions,Vector4[] colors)
    {
        ulong hash=1469598103934665603UL;
        for(int i=0;i<positions.Length;i++)if(colors[i].w>.5f)
        {
            Vector3 p=positions[i];var min=Cell(p-Vector3.one*positions[i].w);var max=Cell(p+Vector3.one*positions[i].w);
            unchecked {hash=(hash^(Hash(min)+(ulong)Hash(max)*31+(uint)i*131))*1099511628211UL;}
        }
        if(hash==previousHash)return;previousHash=hash;
        Array.Clear(table,0,table.Length);int count=0;
        for(int i=0;i<positions.Length;i++)if(colors[i].w>.5f)
        {
            Vector3 p=positions[i];var min=Cell(p-Vector3.one*positions[i].w);var max=Cell(p+Vector3.one*positions[i].w);
            for(int z=min.z;z<=max.z;z++)for(int y=min.y;y<=max.y;y++)for(int x=min.x;x<=max.x;x++)
            {
                var cell=new Vector3Int(x,y,z);int bucket=(int)(Hash(cell)&(TableSize-1));
                while(table[bucket].w!=0 && (table[bucket].x!=x||table[bucket].y!=y||table[bucket].z!=z))bucket=(bucket+1)&(TableSize-1);
                if(count>=NodeCapacity)throw new InvalidOperationException("Flame grid capacity exceeded.");
                nodes[count]=new Node{light=i,next=table[bucket].w};table[bucket]=new Entry(cell,++count);
            }
        }
        entries??=new ComputeBuffer(TableSize,16);links??=new ComputeBuffer(NodeCapacity,8);
        entries.SetData(table);links.SetData(nodes);
    }
    public void Bind(){Shader.SetGlobalBuffer("_FSFlameCells",entries);Shader.SetGlobalBuffer("_FSFlameLinks",links);}
    public void Dispose(){entries?.Release();links?.Release();}
}
