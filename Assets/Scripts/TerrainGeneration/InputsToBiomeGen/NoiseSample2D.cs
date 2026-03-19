public readonly struct NoiseSample2D
{
    public readonly float Value;
    public readonly float Dx;
    public readonly float Dz;

    public NoiseSample2D(float value, float dx, float dz)
    {
        Value = value;
        Dx = dx;
        Dz = dz;
    }
}