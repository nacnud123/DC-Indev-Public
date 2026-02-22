using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockLava : Block
{
    public override BlockType Type => BlockType.Lava;
    public override string Name => "Lava";

    public override bool IsSolid => false;
    public override bool IsBreakable => false;
    public override bool IsFluid => true;
    public override bool IsTransparent => true;
    public override int LightEmission => 15;
    public override int LightOpacity => 3;
    public override bool ShowInInventory => true;
    public override int TickRate => 25;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    public override void ScheduledTick(World world, int x, int y, int z, Random random)
    {
        // Try to flow down one block (lava descends one step at a time)
        bool flowedDown = false;
        var below = world.GetBlock(x, y - 1, z);
        if (below != BlockType.Lava && !BlockRegistry.IsSolid(below) && !IsNearSponge(world, x, y - 1, z))
        {
            if (below == BlockType.Water)
            {
                Game.Instance.AudioManager.PlayAudio("Resources/Audio/SteamHiss.ogg", Game.Instance.AudioManager.SfxVol);
                world.SetBlock(x, y - 1, z, BlockType.CobbleStone);
            }
            else
            {
                if (below != BlockType.Air)
                    Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, y - 1, z), below);

                world.SetBlock(x, y - 1, z, BlockType.Lava);
                flowedDown = true;
            }
        }

        // Only spread horizontally if downward flow did not occur
        if (!flowedDown)
        {
            var dirs = new (int dx, int dz)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            for (int i = dirs.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
            }
            foreach (var (dx, dz) in dirs)
                TrySpread(world, x + dx, y, z + dz);
        }
    }

    private static void TrySpread(World world, int x, int y, int z)
    {
        var neighbor = world.GetBlock(x, y, z);

        if (neighbor == BlockType.Lava || BlockRegistry.IsSolid(neighbor))
            return;

        if (neighbor == BlockType.Water)
        {
            Game.Instance.AudioManager.PlayAudio("Resources/Audio/SteamHiss.ogg", Game.Instance.AudioManager.SfxVol);
            world.SetBlock(x, y, z, BlockType.CobbleStone);
            return;
        }

        if (IsNearSponge(world, x, y, z))
            return;

        if (neighbor != BlockType.Air)
            Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, y, z), neighbor);

        world.SetBlock(x, y, z, BlockType.Lava);
    }

    private static bool IsNearSponge(World world, int x, int y, int z)
    {
        int r = BlockSponge.ABSORB_RADIUS;
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        for (int dz = -r; dz <= r; dz++)
        {
            if (world.GetBlock(x + dx, y + dy, z + dz) == BlockType.Sponge)
                return true;
        }
        return false;
    }
}
