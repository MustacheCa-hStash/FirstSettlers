using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Only nearby placed lights own effect objects. Solid torch meshes stay in ordinary build batches.</summary>
public sealed class LocalFlameBuildBridge : IDisposable
{
    readonly BuildWorld world;
    readonly Dictionary<ulong,GameObject> effects=new();
    readonly Stack<GameObject> pool=new();
    readonly List<BuildPieceRecord> nearby=new(),selected=new(32);
    readonly List<ulong> retiring=new(32);
    GameObject effectsRoot;
    float nextUpdate;
    int revision=-1;
    public LocalFlameBuildBridge(BuildWorld world){this.world=world;}
    public void Update()
    {
        if(world.Camera==null || Time.unscaledTime<nextUpdate && revision==world.Session.Revision)return;
        revision=world.Session.Revision;
        nextUpdate=Time.unscaledTime+.1f;
        Vector3 focus=world.Camera.transform.position;
        world.Session.Query(new Bounds(focus,Vector3.one*192),nearby);selected.Clear();
        foreach(var piece in nearby)
        {
            if(piece.Definition.flamePrefab==null || (piece.Origin-focus).sqrMagnitude>96*96)continue;
            int index=0;float distance=(piece.Origin-focus).sqrMagnitude;
            while(index<selected.Count && (selected[index].Origin-focus).sqrMagnitude<=distance)index++;
            if(index>=LocalFlameLighting.VisualCapacity)continue;
            selected.Insert(index,piece);if(selected.Count>LocalFlameLighting.VisualCapacity)selected.RemoveAt(LocalFlameLighting.VisualCapacity);
        }
        retiring.Clear();
        foreach(var pair in effects)
        {
            bool keep=false;foreach(var p in selected)if(p.Id==pair.Key){keep=true;break;}
            if(!keep)retiring.Add(pair.Key);
        }
        foreach(var id in retiring){var effect=effects[id];effect.SetActive(false);pool.Push(effect);effects.Remove(id);}
        foreach(var piece in selected)
        {
            if(effects.ContainsKey(piece.Id))continue;
            // A pool is per prefab, preventing future lamp effects from reusing torch artwork.
            GameObject effect=null;
            if(pool.Count>0 && pool.Peek().name==piece.Definition.flamePrefab.name+"(Clone)")effect=pool.Pop();
            if(effect==null)
            {
                // BuildWorld belongs to the moving player; placed pieces use world-space records.
                // Keep active and pooled effects in the owning scene, outside the player hierarchy.
                if(effectsRoot==null)
                {
                    effectsRoot=new GameObject("Building Flames (Pooled)");
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(effectsRoot,world.gameObject.scene);
                }
                effect=UnityEngine.Object.Instantiate(piece.Definition.flamePrefab,piece.Origin,BuildGeometry.Rotation(piece.WorldYawStep),effectsRoot.transform);
            }
            effect.transform.SetPositionAndRotation(piece.Origin,BuildGeometry.Rotation(piece.WorldYawStep));
            var source=effect.GetComponent<LocalFlameSource>();source.coverageWorld=world;source.ignoredGeometryOwner=piece;
            LocalFlameLighting.Ensure().targetCamera=world.Camera;
            effect.SetActive(true);effects.Add(piece.Id,effect);
        }
    }
    public void Dispose()
    {
        if(effectsRoot!=null){effectsRoot.SetActive(false);BuildLifetime.Destroy(effectsRoot);effectsRoot=null;}
        effects.Clear();pool.Clear();
    }
}
