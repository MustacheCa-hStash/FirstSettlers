/// <summary>One rendered-frame command. Hardware is read only by LocalPlayerInput.</summary>
public readonly struct TerrainSculptInputCommand
{
    public readonly bool DigHeld, DigPressed, CycleShape, Save;
    public readonly int SizeSteps;
    public TerrainSculptInputCommand(bool digHeld, bool digPressed, int sizeSteps = 0, bool cycleShape = false, bool save = false)
    { DigHeld=digHeld;DigPressed=digPressed;SizeSteps=sizeSteps;CycleShape=cycleShape;Save=save; }
}

/// <summary>At most four accepted actions/second, with no catch-up and no saved stroke positions.
/// Suppression disarms held input until a fresh press. A slow transaction naturally reduces rate.</summary>
public struct TerrainSculptRepeat
{
    private bool armed;
    private double next;
    public void Suspend() => armed=false;
    public bool WantsCut(bool held,bool pressed,double now)
    {
        if(!held){armed=false;return false;}
        if(pressed)armed=true;
        return armed&&now>=next;
    }
    public void Accepted(double now) => next=now+.25;
}
