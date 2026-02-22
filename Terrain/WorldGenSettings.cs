// Holds setting for the world generation types and themes. Has stuff like reference to clouds colors, world colors, and other stuff needed for terrain generation. | DA | 2/21/26
using OpenTK.Mathematics;
using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

public enum WorldTye { Island, Inland, Floating, Flat }

public enum WorldTheme { Normal, Hell, Paradise, Woods }

public struct WorldGenSettings
{
    public WorldTye Type = WorldTye.Island;
    public WorldTheme Theme = WorldTheme.Normal;

    public BlockType OceanFluid;
    public int SandNoiseThreshold;
    public int SandBorderYOffset;
    public bool CoastIsGrass;
    public int TreePasses;
    public float SkyBrightness;

    public int CloudHeight;
    public Vector3 CloudColor;

    public Vector3 DaySkyColor;
    public Vector3 DayFogColor;
    public int MinBlockLightLevel;
    public int MaxBlockLightLevel;

    public int MinSunLightLevel;
    public int MaxSunLightLevel;

    public WorldGenSettings() { }

    public static WorldGenSettings Build(int typeIndex, int themeIndex)
    {
        var s = new WorldGenSettings
        {
            Type = (WorldTye)typeIndex,
            Theme = (WorldTheme)themeIndex,
        };

        switch (s.Theme)
        {
            case WorldTheme.Hell:
                s.OceanFluid = BlockType.Lava;
                s.SandNoiseThreshold = -2;
                s.SandBorderYOffset = 0;
                s.CoastIsGrass = true;
                s.TreePasses = 1;
                s.SkyBrightness = 0.467f;
                
                s.DaySkyColor = new Vector3(0.0627f, 0.0235f, 0.000f);
                s.DayFogColor = new Vector3(0.0627f, 0.0235f, 0.000f);
                
                s.MinBlockLightLevel = 0;
                s.MaxBlockLightLevel = 16;

                s.MinSunLightLevel = 0;
                s.MaxSunLightLevel = 7;

                s.CloudColor = new Vector3(0x21 / 255f, 0x0E / 255f, 0x00 / 255f);
                s.CloudHeight = Chunk.HEIGHT + 2;
                break;

            case WorldTheme.Paradise:
                s.OceanFluid = BlockType.Water;
                s.SandNoiseThreshold = 0;
                s.SandBorderYOffset = 3;
                s.CoastIsGrass = false;
                s.TreePasses = 2;
                s.SkyBrightness = 1.0f;
                
                s.DaySkyColor = new Vector3(0.780f, 0.780f, 1.000f);
                s.DayFogColor = new Vector3(0.780f, 0.780f, 1.000f);
                
                s.MinBlockLightLevel = 0;
                s.MaxBlockLightLevel = 16;

                s.MinSunLightLevel = 12;
                s.MaxSunLightLevel = 16;

                s.CloudColor = new Vector3(0xEE / 255f, 0xBF / 255f, 0xFF / 255f);
                s.CloudHeight = Chunk.HEIGHT + 64;
                break;

            case WorldTheme.Woods:
                s.OceanFluid = BlockType.Water;
                s.SandNoiseThreshold = -2;
                s.SandBorderYOffset = 1;
                s.CoastIsGrass = false;
                s.TreePasses = 4;
                s.SkyBrightness = 0.8f;
                
                s.DaySkyColor = new Vector3(0.459f, 0.533f, 0.278f);
                s.DayFogColor = new Vector3(0.302f, 0.420f, 0.106f);
                
                s.MinBlockLightLevel = 0;
                s.MaxBlockLightLevel = 12;

                s.MinSunLightLevel = 0;
                s.MaxSunLightLevel = 12;

                s.CloudColor = new Vector3(0x4D / 255f, 0x6B / 255f, 0x1B / 255f);
                s.CloudHeight = Chunk.HEIGHT + 2;
                break;

            default: // Normal
                s.OceanFluid = BlockType.Water;
                s.SandNoiseThreshold = -2;
                s.SandBorderYOffset = 1;
                s.CoastIsGrass = false;
                s.TreePasses = 1;
                s.SkyBrightness = 1.0f;
                
                s.DaySkyColor = new Vector3(0.600f, 0.698f, 1.000f);
                s.DayFogColor = new Vector3(1.000f, 1.000f, 1.000f);

                s.MinBlockLightLevel = 0;
                s.MaxBlockLightLevel = 16;

                s.MinSunLightLevel = 0;
                s.MaxSunLightLevel = 16;

                s.CloudColor = new Vector3(1.0f, 1.0f, 1.0f);
                s.CloudHeight = Chunk.HEIGHT + 2;
                break;
        }

        return s;
    }
}