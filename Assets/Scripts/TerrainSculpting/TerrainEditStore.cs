using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Unity.Mathematics;
using Unity.Profiling;

public sealed class TerrainEditCompatibilityException : IOException
{ public TerrainEditCompatibilityException(string message) : base(message) { } }

/// <summary>Bounded lossless sidecar. Single caller/IO operation; never reads Unity objects.</summary>
public sealed class TerrainEditStore : IDisposable
{
    public const int FormatVersion = 1;
    private const uint ManifestMagic = 0x4d455346, BrickMagic = 0x42455346;
    private const int MaxEntries = 100000, MaxManifestBytes = 16 * 1024 * 1024, MaxRecordBytes = 16 * 1024;
    private static readonly ProfilerMarker SaveMarker = new ProfilerMarker("FS.Streaming.Sculpt.Save");
    private static readonly ProfilerMarker LoadMarker = new ProfilerMarker("FS.Streaming.Sculpt.Load");
    private readonly TerrainEditIdentity identity;
    private readonly long budget;
    private readonly Action<string> writeCheckpoint;
    private FileStream writerLock;
    private bool initialized, disposed;
    private string activeDigest;
    private Dictionary<int3, Entry> committed = new Dictionary<int3, Entry>();
    private sealed class Entry
    {
        internal int3 Coord;
        internal long Revision;
        internal byte Encoding;
        internal string Digest;
        internal TerrainDensityBrick Brick;
        internal WeakReference<TerrainDensityBrick> Source;
    }
    public string DirectoryPath { get; }
    public long SavedRevision { get; private set; }
    public bool ReadOnly { get; private set; }
    public string Warning { get; private set; }
    public long LastWrittenBytes { get; private set; }

    public TerrainEditStore(string directory, TerrainEditIdentity identity, long densityBudget = TerrainDensityWorld.DefaultBudgetBytes,
        Action<string> writeCheckpoint = null)
    {
        DirectoryPath = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
        this.identity = identity ?? throw new ArgumentNullException(nameof(identity));
        if (densityBudget < 0) throw new ArgumentOutOfRangeException(nameof(densityBudget));
        budget = densityBudget; this.writeCheckpoint = writeCheckpoint;
    }
    private string Active => Path.Combine(DirectoryPath, "manifest.bin");
    private string Previous => Path.Combine(DirectoryPath, "manifest.previous.bin");
    private string Record(string digest) => Path.Combine(DirectoryPath, "bricks", digest + ".brick");
    private void RequireAlive() { if (disposed) throw new ObjectDisposedException(nameof(TerrainEditStore)); }

    /// <summary>Fresh-world only. Entire validation completes before the density root is installed.
    /// A corrupt latest generation may recover its predecessor read-only, with an explicit warning.</summary>
    public void Load(TerrainDensityWorld world)
    {
        RequireAlive(); if (initialized) throw new InvalidOperationException("Store is already loaded.");
        if (!world.Profile.Equals(identity.Profile)) throw new TerrainEditCompatibilityException("Density profile mismatch.");
        using var marker = LoadMarker.Auto();
        Directory.CreateDirectory(Path.Combine(DirectoryPath, "bricks"));
        // Held for the whole service lifetime; a second editor/player cannot overwrite this slot.
        writerLock = new FileStream(Path.Combine(DirectoryPath, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Dictionary<int3, Entry> entries; long revision;
        if (!File.Exists(Active) && !File.Exists(Previous)) { initialized = true; return; }
        try
        {
            if (!File.Exists(Active)) throw new IOException("Latest terrain manifest is missing.");
            entries = ReadManifest(Active, out revision); ReadBricks(entries);
            activeDigest = TerrainEditIdentity.Digest(ReadBounded(Active,MaxManifestBytes));
        }
        catch (TerrainEditCompatibilityException) { throw; } // Never disguise a newer schema/profile as corruption.
        catch (IOException error)
        {
            if (!File.Exists(Previous)) throw new IOException("Terrain save is unavailable; original save files were preserved.", error);
            entries = ReadManifest(Previous, out revision); ReadBricks(entries);
            ReadOnly = true;
            Warning = "Recovered previous complete terrain revision " + revision + ". Saving and excavation are disabled; latest save failed validation: " + error.Message;
        }
        var root = new Dictionary<int3, TerrainDensityBrick>();
        foreach (var entry in entries.Values) root.Add(entry.Coord, entry.Brick);
        world.RestoreCommitted(root, revision);
        foreach (var entry in entries.Values) { entry.Source = new WeakReference<TerrainDensityBrick>(entry.Brick); entry.Brick = null; }
        committed = entries; SavedRevision = revision; initialized = true;
    }

    /// <summary>Writes full canonical owners, never mesh halos, visual fade or a pending edit.</summary>
    public void Save(TerrainDensitySnapshot snapshot)
    {
        RequireAlive(); snapshot.RequireAlive();
        if (!initialized || ReadOnly) throw new InvalidOperationException("Terrain store is not writable.");
        if (snapshot.IncludesPendingEdit) throw new InvalidOperationException("Pending terrain cannot be saved.");
        if (!snapshot.Profile.Equals(identity.Profile)) throw new TerrainEditCompatibilityException("Density profile mismatch.");
        if (snapshot.Revision < SavedRevision) throw new InvalidOperationException("Cannot save an older terrain revision.");
        if (snapshot.Revision == SavedRevision) return;
        if (snapshot.ModifiedBrickCount > MaxEntries) throw new IOException("Terrain manifest entry limit exceeded.");
        using var marker = SaveMarker.Auto();
        // One raw brick, compressed record and verification buffer at a time, also accounted
        // against the density budget. No second unbounded array of the world's samples.
        using var scratch = snapshot.ReserveScratch(64 * 1024);
        if(activeDigest==null?File.Exists(Active):!File.Exists(Active)||TerrainEditIdentity.Digest(ReadBounded(Active,MaxManifestBytes))!=activeDigest)
            throw new IOException("Terrain manifest changed outside this session; save files were preserved.");
        LastWrittenBytes = 0;
        var next = new Dictionary<int3, Entry>();
        var keys = new List<int3>(snapshot.Bricks.Keys); keys.Sort(Compare);
        foreach (var coord in keys)
        {
            TerrainDensityBrick brick = snapshot.Bricks[coord]; Entry entry;
            if (committed.TryGetValue(coord, out var previous) && previous.Source.TryGetTarget(out var original) && ReferenceEquals(brick, original))
            {
                // Detect corruption of reused immutable files before naming them in a successor.
                ReadBrick(previous); entry = previous;
            }
            else
            {
                byte[] record = EncodeBrick(coord, snapshot.Revision, brick);
                string digest = TerrainEditIdentity.Digest(record), path = Record(digest);
                entry = new Entry { Coord=coord, Revision=snapshot.Revision, Encoding=(byte)(brick.IsUniform?0:1), Digest=digest, Source=new WeakReference<TerrainDensityBrick>(brick) };
                if (File.Exists(path)) ReadBrick(entry);
                else
                {
                    string temp = Path.Combine(Path.GetDirectoryName(path),Guid.NewGuid().ToString("N") + ".tmp");
                    WriteDurable(temp, record); File.Move(temp, path); LastWrittenBytes += record.Length;
                    ReadBrick(entry); // Validate the new immutable file before publishing its name.
                }
                writeCheckpoint?.Invoke("RecordWritten");
            }
            next.Add(coord, entry);
        }
        byte[] manifest = EncodeManifest(next, snapshot.Revision);
        string pending = Path.Combine(DirectoryPath, "manifest." + Guid.NewGuid().ToString("N") + ".tmp");
        WriteDurable(pending, manifest);
        ReadManifest(pending, out long verifiedRevision);
        if (verifiedRevision != snapshot.Revision) throw new IOException("Manifest verification failed.");
        writeCheckpoint?.Invoke("ManifestPrepared");
        // File.Replace is atomic on the supported local Windows filesystem and preserves
        // the former manifest. There is deliberately no delete-then-move fallback.
        if (File.Exists(Active)) File.Replace(pending, Active, Previous);
        else File.Move(pending, Active);
        committed = next; SavedRevision = snapshot.Revision; LastWrittenBytes += manifest.Length;
        activeDigest = TerrainEditIdentity.Digest(manifest);
        writeCheckpoint?.Invoke("ManifestPublished");
    }

    private byte[] EncodeBrick(int3 coord, long revision, TerrainDensityBrick brick)
    {
        int rawLength = brick.IsUniform ? 2 : TerrainDensityProfile.DetailedBrickBytes;
        var raw = new byte[rawLength];
        for (int i=0;i<rawLength/2;i++) { short code=brick.Read(i);raw[i*2]=(byte)code;raw[i*2+1]=(byte)(code>>8); }
        using var compressed = new MemoryStream();
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true)) deflate.Write(raw,0,raw.Length);
        byte[] payload=compressed.ToArray();
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output,Encoding.UTF8,true))
        {
            writer.Write(BrickMagic);writer.Write(FormatVersion);WriteCoord(writer,coord);writer.Write(revision);
            writer.Write((byte)(brick.IsUniform?0:1));writer.Write((byte)1); // Codec 1: .NET DeflateStream.
            writer.Write(raw.Length);writer.Write(payload.Length);WriteDigest(writer,TerrainEditIdentity.Digest(raw));writer.Write(payload);
        }
        return output.ToArray();
    }
    private TerrainDensityBrick ReadBrick(Entry entry)
    {
        byte[] file=ReadBounded(Record(entry.Digest),MaxRecordBytes);
        if(TerrainEditIdentity.Digest(file)!=entry.Digest)throw new IOException("Terrain brick checksum failed at "+entry.Coord);
        using var input=new MemoryStream(file,false);using var reader=new BinaryReader(input);
        if(reader.ReadUInt32()!=BrickMagic||reader.ReadInt32()!=FormatVersion)throw new IOException("Invalid terrain brick header.");
        if(!math.all(ReadCoord(reader)==entry.Coord)||reader.ReadInt64()!=entry.Revision)throw new IOException("Terrain brick address/revision mismatch.");
        byte encoding=reader.ReadByte(),codec=reader.ReadByte();int rawLength=reader.ReadInt32(),packedLength=reader.ReadInt32();
        string rawDigest=ReadDigest(reader);
        if(encoding!=entry.Encoding||codec!=1||rawLength!=(encoding==0?2:TerrainDensityProfile.DetailedBrickBytes)||packedLength!=input.Length-input.Position)
            throw new IOException("Invalid terrain brick encoding/length.");
        var raw=new byte[rawLength];
        try
        {
            using var deflate=new DeflateStream(input,CompressionMode.Decompress,true);
            int done=0;while(done<raw.Length){int n=deflate.Read(raw,done,raw.Length-done);if(n==0)throw new IOException("Truncated terrain brick.");done+=n;}
            if(deflate.ReadByte()!=-1)throw new IOException("Terrain brick exceeds its declared decoded length.");
        }
        catch(InvalidDataException error){throw new IOException("Invalid compressed terrain brick.",error);}
        if(TerrainEditIdentity.Digest(raw)!=rawDigest)throw new IOException("Decoded terrain checksum failed.");
        if(encoding==0)
        {
            short code=(short)(raw[0]|raw[1]<<8);if(code==short.MinValue)throw new IOException("Reserved terrain density code.");
            return new TerrainDensityBrick(code);
        }
        var samples=new short[TerrainDensityProfile.SamplesPerBrick];
        for(int i=0;i<samples.Length;i++) {samples[i]=(short)(raw[i*2]|raw[i*2+1]<<8);if(samples[i]==short.MinValue)throw new IOException("Reserved terrain density code.");}
        return new TerrainDensityBrick(samples);
    }
    private void ReadBricks(Dictionary<int3,Entry> entries)
    {foreach(var entry in entries.Values)entry.Brick=ReadBrick(entry);}
    private byte[] EncodeManifest(Dictionary<int3,Entry> entries,long revision)
    {
        using var body=new MemoryStream();
        using(var writer=new BinaryWriter(body,Encoding.UTF8,true))
        {
            writer.Write(ManifestMagic);writer.Write(FormatVersion);WriteDigest(writer,identity.WorldId);WriteDigest(writer,identity.Fingerprint);
            writer.Write(identity.Profile.Spacing);writer.Write(identity.Profile.Band);writer.Write(revision);writer.Write(entries.Count);
            var keys=new List<int3>(entries.Keys);keys.Sort(Compare);
            foreach(var coord in keys){var entry=entries[coord];WriteCoord(writer,coord);writer.Write(entry.Revision);writer.Write(entry.Encoding);WriteDigest(writer,entry.Digest);}
            writer.Write(Encoding.ASCII.GetBytes(TerrainEditIdentity.Digest(body.ToArray())));
        }
        if(body.Length>MaxManifestBytes)throw new IOException("Terrain manifest size limit exceeded.");
        return body.ToArray();
    }
    private Dictionary<int3,Entry> ReadManifest(string path,out long revision)
    {
        byte[] file=ReadBounded(path,MaxManifestBytes);
        if(file.Length<228)throw new IOException("Truncated terrain manifest.");
        int bodyLength=file.Length-64;var body=new byte[bodyLength];Buffer.BlockCopy(file,0,body,0,bodyLength);
        if(TerrainEditIdentity.Digest(body)!=Encoding.ASCII.GetString(file,bodyLength,64))throw new IOException("Terrain manifest checksum failed.");
        using var input=new MemoryStream(body,false);using var reader=new BinaryReader(input);
        if(reader.ReadUInt32()!=ManifestMagic)throw new IOException("Invalid terrain manifest.");
        if(reader.ReadInt32()!=FormatVersion)throw new TerrainEditCompatibilityException("Unknown terrain save format; no save files were changed.");
        if(ReadDigest(reader)!=identity.WorldId||ReadDigest(reader)!=identity.Fingerprint)throw new TerrainEditCompatibilityException("Terrain world/generation fingerprint mismatch; no save files were changed.");
        if(reader.ReadDouble()!=identity.Profile.Spacing||reader.ReadDouble()!=identity.Profile.Band)throw new TerrainEditCompatibilityException("Terrain sample profile mismatch.");
        revision=reader.ReadInt64();int count=reader.ReadInt32();
        if(revision<0||count<0||count>MaxEntries||input.Length-input.Position!=(long)count*85)throw new IOException("Invalid terrain manifest revision/length.");
        var entries=new Dictionary<int3,Entry>();long bytes=64*1024;
        for(int i=0;i<count;i++)
        {
            var entry=new Entry{Coord=ReadCoord(reader),Revision=reader.ReadInt64(),Encoding=reader.ReadByte(),Digest=ReadDigest(reader)};
            if(entry.Revision<=0||entry.Revision>revision||entry.Encoding>1||!ValidCoord(entry.Coord)||entries.ContainsKey(entry.Coord))throw new IOException("Invalid/duplicate terrain manifest entry.");
            bytes=checked(bytes+(entry.Encoding==0?2:TerrainDensityProfile.DetailedBrickBytes));
            if(bytes>budget)throw new TerrainDensityCapacityException();
            entries.Add(entry.Coord,entry);
        }
        return entries;
    }
    private static bool ValidCoord(int3 coord) => math.all(coord >= new int3((int.MinValue+32)/16))&&math.all(coord <= new int3((int.MaxValue-32)/16));
    private static void WriteCoord(BinaryWriter writer,int3 p){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
    private static int3 ReadCoord(BinaryReader reader)=>new int3(reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32());
    private static void WriteDigest(BinaryWriter writer,string digest)=>writer.Write(Encoding.ASCII.GetBytes(digest));
    private static string ReadDigest(BinaryReader reader)
    {string value=Encoding.ASCII.GetString(reader.ReadBytes(64));if(!TerrainEditIdentity.IsDigest(value))throw new IOException("Invalid terrain digest.");return value;}
    private static int Compare(int3 a,int3 b)=>a.x!=b.x?a.x.CompareTo(b.x):a.y!=b.y?a.y.CompareTo(b.y):a.z.CompareTo(b.z);
    private static byte[] ReadBounded(string path,int maximum)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(input.Length>maximum||input.Length<8)throw new IOException("Terrain file size limit/length failed.");
        var result=new byte[(int)input.Length];int done=0;
        while(done<result.Length){int n=input.Read(result,done,result.Length-done);if(n==0)throw new IOException("Terrain file was truncated.");done+=n;}
        return result;
    }
    private static void WriteDurable(string path,byte[] bytes)
    {using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);output.Write(bytes,0,bytes.Length);output.Flush(true);}
    public void Dispose(){if(disposed)return;disposed=true;writerLock?.Dispose();writerLock=null;committed.Clear();}
}
