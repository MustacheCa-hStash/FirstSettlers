using UnityEngine;

public static class TerrainControlMapBuilder
{
    private const byte SnowDustingSnowWeight = 64;
    private const byte SnowDustingGrassWeight = 255 - SnowDustingSnowWeight;

    public static ControlMapPixelData BuildRaw(SurfaceType[,] surfaceTypeMap, GroundCoverType[,] groundCoverMap,
        Unity.Mathematics.float2[,] mountainSnow = null)
    {
        int width = surfaceTypeMap.GetLength(0);
        int height = surfaceTypeMap.GetLength(1);
        ControlMapPixelData controlMap = new ControlMapPixelData(width, height, 3);

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = z * width + x;
                SurfaceType surfaceType = surfaceTypeMap[x, z];
                GroundCoverType groundCoverType = groundCoverMap[x, z];

                if (UsesFirstControlMap(surfaceType))
                {
                    controlMap.Maps[0][pixelIndex] = SurfaceTypeToIndex(surfaceType);
                }
                else
                {
                    controlMap.Maps[1][pixelIndex] = SurfaceTypeToIndex(surfaceType);
                }

                if (surfaceType == SurfaceType.Grass && groundCoverType == GroundCoverType.SnowDusting)
                {
                    controlMap.Maps[0][pixelIndex] = SurfaceTypeToIndex(SurfaceType.Grass, SnowDustingGrassWeight);
                    controlMap.Maps[1][pixelIndex] = SurfaceTypeToIndex(SurfaceType.Snow, SnowDustingSnowWeight);
                    controlMap.Maps[2][pixelIndex] = Color.clear;
                }
                else
                {
                    controlMap.Maps[2][pixelIndex] = GroundCoverTypeToIndex(groundCoverType);
                }
            }
        }

        ControlMapPixelData blended = BlendPaddedWeights(controlMap);
        if (mountainSnow != null)
            for (int z = 0; z < blended.Height; z++)
                for (int x = 0; x < blended.Width; x++)
                {
                    int index = z * blended.Width + x;
                    MountainSnow.Apply(ref blended.Maps[0][index], ref blended.Maps[1][index], mountainSnow[x + 1, z + 1]);
                }
        return blended;
    }

    private static ControlMapPixelData BlendPaddedWeights(ControlMapPixelData source)
    {
        // Use the neighboring chunk samples in the one-sample halo, then emit only mesh samples.
        // The separable [1, 2, 1] kernel rounds material boundaries without changing gameplay labels.
        int width = source.Width - 2;
        int height = source.Height - 2;
        var result = new ControlMapPixelData(width, height, source.Maps.Length);
        for (int map = 0; map < source.Maps.Length; map++)
        {
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color weights = Color.clear;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            float weight = (dx == 0 ? 2f : 1f) * (dz == 0 ? 2f : 1f) / 16f;
                            weights += (Color)source.Maps[map][(z + 1 + dz) * source.Width + x + 1 + dx] * weight;
                        }
                    }
                    result.Maps[map][z * width + x] = weights;
                }
            }
        }
        return result;
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

    private static Color32 GroundCoverTypeToIndex(GroundCoverType groundCoverType, byte value = 255)
    {
        switch (groundCoverType)
        {
            case GroundCoverType.DarkGrass:
                return new Color32(value, 0, 0, 0);

            case GroundCoverType.LeafLitter:
            case GroundCoverType.NeedleLitter:
                return new Color32(0, value, 0, 0);

            case GroundCoverType.BareDirt:
            case GroundCoverType.Gravel:
                return new Color32(0, 0, value, 0);

            case GroundCoverType.Moss:
            case GroundCoverType.Lichen:
                return new Color32(0, 0, 0, value);

            default:
                return new Color32(0, 0, 0, 0);
        }
    }
}
