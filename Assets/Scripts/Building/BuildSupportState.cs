using System.Collections.Generic;

/// <summary>Compatibility API for existing tools. Building pieces no longer require physical support.</summary>
public static class BuildSupportState
{
    public static bool CandidateSupported(BuildPieceRecord candidate,IReadOnlyList<BuildPieceRecord> neighbours,Dictionary<ulong,BuildResolvedState> states)=>true;
    public static HashSet<ulong> Resolve(IReadOnlyList<BuildPieceRecord> pieces,Dictionary<ulong,BuildResolvedState> states,
        System.Func<BuildPieceRecord,IReadOnlyList<BuildPieceRecord>> nearby=null,
        System.Func<BuildPieceRecord,IEnumerable<ulong>> connections=null)
    {
        var result=new HashSet<ulong>();foreach(var piece in pieces)result.Add(piece.Id);return result;
    }
}
