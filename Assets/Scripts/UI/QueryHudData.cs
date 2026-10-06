/// <summary>Only the values needed to draw the query label. Gameplay records stay with their providers.</summary>
public readonly struct QueryHudData
{
    public string Name { get; }
    public string Description { get; }

    public QueryHudData(QueryTargetInfo target)
    {
        Name = target.DisplayName;
        Description = target.Description;
    }
}
