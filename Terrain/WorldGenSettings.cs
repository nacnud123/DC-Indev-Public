// Holds setting for the world generation types and themes. Has stuff like reference to clouds colors, world colors, and other stuff needed for terrain generation. | DA | 2/21/26

using VoxelEngine.Terrain.Blocks;

namespace VoxelEngine.Terrain;

/// <summary>
/// The overall shape/layout strategy used when building the heightmap in TerrainGen. Island: edge falloff sinks terrain toward world borders, producing an island surrounded by ocean. Inland: no edge falloff - terrain continues to the world border. Floating: heightmap + a second carve noise produce disconnected floating landmasses stacked in layers. Flat: heightmap generation is skipped entirely (superflat world).
/// </summary>
public enum WorldTye { Island, Inland, Floating, Flat }

/// <summary>
/// Cosmetic/environmental theme applied on top of the world type - controls ocean fluid, sky/fog colors, cloud appearance, and light level ranges. See WorldGenSettings.Build.
/// </summary>
public enum WorldTheme { Normal, Hell, Paradise, Woods }

/// <summary>
/// Bundles all the tunable, per-world-type/theme constants that TerrainGen and the renderer consume - ocean fluid choice, coastline behavior, tree density, sky/fog/cloud colors, and light level clamps. Built once via <see cref="Build"/> from the selected type/theme indices and then read-only for the lifetime of the world.
/// </summary>
public struct WorldGenSettings
{
    public WorldTye Type = WorldTye.Island;
    public WorldTheme Theme = WorldTheme.Normal;

    // Fluid used to fill the ocean/floor at sea level (Water normally, Lava for the Hell theme).
    public BlockType OceanFluid;
    // Surface height range (relative to sea level) that gets converted to a coastal block (sand/gravel) in GenerateGrowing.
    public int SandNoiseThreshold;
    public int SandBorderYOffset;
    // If true, coastlines are gravel instead of sand (used by themes without sandy beaches).
    public bool CoastIsGrass;
    // Number of independent tree-placement passes TerrainGen.GenerateTrees runs per chunk - more passes = denser forests.
    public int TreePasses;
    // Multiplier applied to daylight brightness for this theme (1.0 = normal).
    public float SkyBrightness;

    // World-space Y at which the cloud plane is rendered.
    public int CloudHeight;
    public Vector3 CloudColor;

    public Vector3 DaySkyColor;
    public Vector3 DayFogColor;
    // Clamp range for block-emitted light (torches, lava, etc.) used by the lighting engine for this theme.
    public int MinBlockLightLevel;
    public int MaxBlockLightLevel;

    // Clamp range for sky/sunlight propagation for this theme (e.g. Hell caps sunlight low to stay dim).
    public int MinSunLightLevel;
    public int MaxSunLightLevel;

    /// <summary>Default parameterless constructor required because the struct has field initializers.</summary>
    public WorldGenSettings() { }

    /// <summary>
    /// Constructs a fully-populated WorldGenSettings for the given world type/theme combination. Each theme case hardcodes ocean fluid, coastline, tree density, sky/fog/cloud colors, and light level ranges; <paramref name="typeIndex"/> maps to <see cref="WorldTye"/> and <paramref name="themeIndex"/> maps to <see cref="WorldTheme"/> (both cast from raw ints, e.g. from UI dropdowns or saved world metadata).
    /// </summary>
    public static WorldGenSettings Build(int typeIndex, int themeIndex)
    {
        var s = new WorldGenSettings
        {
            Type = (WorldTye)typeIndex,
            Theme = (WorldTheme)themeIndex,
        };

        switch (s.Theme)
        {
            // Nether-like theme: lava "ocean", dark red-black sky, dim capped sunlight, small clouds hugging the world ceiling.
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
                s.MaxBlockLightLevel = Chunk.MAX_LIGHT;

                s.MinSunLightLevel = 0;
                s.MaxSunLightLevel = 7;

                s.CloudColor = new Vector3(0x21 / 255f, 0x0E / 255f, 0x00 / 255f);
                s.CloudHeight = Chunk.HEIGHT + 2;
                break;

            // Bright, high-light theme: full daylight brightness, pale blue sky/fog, tall clouds, no dark caves (min sunlight raised to 12).
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
                s.MaxBlockLightLevel = Chunk.MAX_LIGHT;

                s.MinSunLightLevel = 12;
                s.MaxSunLightLevel = Chunk.MAX_LIGHT;

                s.CloudColor = new Vector3(0xEE / 255f, 0xBF / 255f, 0xFF / 255f);
                s.CloudHeight = Chunk.HEIGHT + 64;
                break;

            // Dense forest theme: green-tinted sky/fog and quadruple tree passes (TreePasses = 4) for thick woods.
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

            // Baseline Overworld-style theme: standard water ocean, normal sky/fog colors, full light ranges.
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
                s.MaxBlockLightLevel = Chunk.MAX_LIGHT;

                s.MinSunLightLevel = 0;
                s.MaxSunLightLevel = Chunk.MAX_LIGHT;

                s.CloudColor = new Vector3(1.0f, 1.0f, 1.0f);
                s.CloudHeight = Chunk.HEIGHT + 2;
                break;
        }

        return s;
    }
}