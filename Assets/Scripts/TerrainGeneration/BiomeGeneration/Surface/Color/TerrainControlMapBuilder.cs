using UnityEngine;

public static class TerrainControlMapBuilder
{
    public static ControlMapPixelData BuildRaw(SurfaceType[,] surfaceTypeMap)
    {
        int width = surfaceTypeMap.GetLength(0);
        ControlMapPixelData controlMap = new ControlMapPixelData(width, width, 2);

        int pixelCount = width * width;

        for (int z = 0; z < width; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = z * width + x;
                SurfaceType surfaceType = surfaceTypeMap[x, z];

                if (UsesFirstControlMap(surfaceType))
                {
                    controlMap.Maps[0][pixelIndex] = SurfaceTypeToIndex(surfaceType);
                }
                else
                {
                    controlMap.Maps[1][pixelIndex] = SurfaceTypeToIndex(surfaceType);
                }
            }
        }

        return controlMap;
    }

    private static bool UsesFirstControlMap(SurfaceType surfaceType)
    {
        switch (surfaceType)
        {
            case SurfaceType.Sand:
            case SurfaceType.Mud:
            case SurfaceType.Grass:
            case SurfaceType.Rock:
                return true;

            case SurfaceType.Snow:
            case SurfaceType.Cliff:
            case SurfaceType.Riverbed:
                return false;

            default:
                return true;
        }
    }

    private static Color32 SurfaceTypeToIndex(SurfaceType surfaceType, byte value = 255)
    {
        switch (surfaceType)
        {
            case SurfaceType.Sand: return new Color32(value, 0, 0, 0);
            case SurfaceType.Mud: return new Color32(0, value, 0, 0);
            case SurfaceType.Grass: return new Color32(0, 0, value, 0);
            case SurfaceType.Rock: return new Color32(0, 0, 0, value);

            case SurfaceType.Snow: return new Color32(value, 0, 0, 0);
            case SurfaceType.Cliff: return new Color32(0, value, 0, 0);
            case SurfaceType.Riverbed: return new Color32(0, 0, value, 0);

            default: return new Color32(0, 0, 0, 0);
        }
    }
}
