public struct BerryBushState
{
    public bool isBroken;
    public bool fruitAvailable;
    public long nextRegrowCycle;
    public int hp;

    public BerryBushState(bool isBroken, bool fruitAvailable, long nextRegrowCycle, int hp)
    {
        this.isBroken = isBroken;
        this.fruitAvailable = fruitAvailable;
        this.nextRegrowCycle = nextRegrowCycle;
        this.hp = hp;
    }

    public bool IsDefault(BerryBushDefinition definition)
    {
        if (definition == null)
            return false;

        return !isBroken &&
               fruitAvailable &&
               nextRegrowCycle <= 0 &&
               hp == definition.maxHP;
    }
}
