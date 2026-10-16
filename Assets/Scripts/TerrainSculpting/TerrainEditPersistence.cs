using System;
using System.Diagnostics;
using System.Threading.Tasks;

/// <summary>One bounded IO task, two-second debounce, main-thread status/flush API.
/// Density remains resident whether dirty or saved; derived-cache retirement never evicts it.</summary>
public sealed class TerrainEditPersistence : IDisposable
{
    private readonly TerrainDensityWorld world;
    private readonly TerrainEditStore store;
    private readonly Func<double> now;
    private readonly Action<string> report;
    private Task operation;
    private bool loading = true, force, disposed;
    private long observedRevision;
    private double changedAt;
    public bool Ready { get; private set; }
    public bool FailedToLoad { get; private set; }
    public bool IsSaving => !loading && operation != null;
    public bool ReadOnly => store.ReadOnly;
    public long SavedRevision { get; private set; }
    public bool Dirty => Ready && world.Revision > SavedRevision;
    public string LastError { get; private set; }
    public string Status { get; private set; } = "Loading terrain edits";
    public bool CanExcavate => Ready && !ReadOnly && LastError == null && !disposed;

    public TerrainEditPersistence(TerrainDensityWorld world, TerrainEditStore store, Action<string> report = null, Func<double> clock = null)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.store = store ?? throw new ArgumentNullException(nameof(store)); this.report = report;
        world.RequirePersistenceHeadroom();
        now = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        operation = Task.Run(() => store.Load(world));
    }
    public void Update()
    {
        if (disposed) return;
        Complete(false);
        if (!Ready || FailedToLoad) return;
        long revision = world.Revision;
        if (revision != observedRevision)
        { observedRevision=revision; changedAt=now();if(operation==null&&!ReadOnly)Status="Terrain edits are unsaved (revision "+revision+")"; }
        if(!Dirty&&operation==null)force=false;
        if (operation == null && Dirty && !ReadOnly && (force || LastError == null && now()-changedAt >= 2)) StartSave();
    }
    private void Complete(bool wait)
    {
        if (operation == null || !wait && !operation.IsCompleted) return;
        try
        {
            operation.GetAwaiter().GetResult();
            SavedRevision = store.SavedRevision; LastError = null;
            if (loading) { Ready=true; observedRevision=world.Revision; changedAt=now(); }
            Status = store.Warning ?? (world.Revision>SavedRevision ? "Terrain edits are unsaved" : "Terrain edits saved (revision "+SavedRevision+")");
            if (loading && store.Warning != null) report?.Invoke(store.Warning);
        }
        catch (Exception error)
        {
            LastError=error.Message;
            if (loading) { FailedToLoad=true; Status="Terrain load blocked: "+error.Message; }
            else Status="Terrain save failed; committed edits remain in memory: "+error.Message;
            report?.Invoke(Status);
        }
        finally { loading=false; operation=null; }
    }
    private void StartSave()
    {
        force=false;
        try
        {
            var snapshot=world.CaptureSnapshot();
            try { operation=Task.Run(() => { using(snapshot)store.Save(snapshot); }); }
            catch { snapshot.Dispose();throw; }
            Status="Saving terrain revision "+snapshot.Revision;
        }
        catch(Exception error)
        { LastError=error.Message;Status="Terrain save admission failed; edits remain in memory: "+error.Message;report?.Invoke(Status); }
    }
    /// <summary>Nonblocking request. P5 exposes this through development controls.</summary>
    public bool RequestSave()
    {
        if(disposed||FailedToLoad||ReadOnly)return false;
        force=true;return true;
    }
    /// <summary>Orderly shutdown/explicit blocking flush. Main-thread commits must be stopped.</summary>
    public bool Flush()
    {
        if(disposed)return false;
        Complete(true);
        if(!Ready||FailedToLoad||ReadOnly)return false;
        if(Dirty){StartSave();Complete(true);}
        return !Dirty&&LastError==null;
    }
    public void Dispose()
    {
        if(disposed)return;
        try{Flush();}finally{disposed=true;store.Dispose();}
    }
}
