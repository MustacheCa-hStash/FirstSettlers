using UnityEngine;

public class ControlMapPixelData
{
    public int Width;
    public int Height;
    public Color32[][] Maps;

    public ControlMapPixelData(int width, int height, int mapCount)
    {
        Width = width;
        Height = height;
        Maps = new Color32[mapCount][];
        int pixelCount = width * height;

        for (int i = 0; i < mapCount; i++)
        {
            Maps[i] = new Color32[pixelCount];
        }
    }
}

