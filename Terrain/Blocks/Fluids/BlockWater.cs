using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockWater : Block
{
    public override BlockType Type => BlockType.Water;
    public override string Name => "Water";

    public override bool IsSolid => false;
    public override bool IsBreakable => false;
    public override bool IsFluid => true;
    public override bool IsTransparent => true;
    public override int LightOpacity => 3;
    public override bool ShowInInventory => true;
    public override int TickRate => 5;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    public override void ScheduledTick(World world, int x, int y, int z, Random random)
    {
        // Flow down: fill the entire column below until blocked
        for (int ny = y - 1; ny >= 0; ny--)
        {
            var below = world.GetBlock(x, ny, z);

            if (below == BlockType.Water || BlockRegistry.IsSolid(below) || IsNearSponge(world, x, ny, z))
                break;

            if (below == BlockType.Lava)
            {
                Game.Instance.AudioManager.PlayAudio("Resources/Audio/SteamHiss.ogg", Game.Instance.AudioManager.SfxVol);
                world.SetBlock(x, ny, z, BlockType.CobbleStone);
                break;
            }

            if (below != BlockType.Air)
                Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, ny, z), below);

            world.SetBlock(x, ny, z, BlockType.Water);
        }

        // Horizontal spread: shuffled directions
        var dirs = new (int dx, int dz)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        for (int i = dirs.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
        }
        foreach (var (dx, dz) in dirs)
            TrySpread(world, x + dx, y, z + dz);
    }

    private static void TrySpread(World world, int x, int y, int z)
    {
        var neighbor = world.GetBlock(x, y, z);
        if (neighbor == BlockType.Water || BlockRegistry.IsSolid(neighbor))
            return;

        // Water + lava = cobblestone
        if (neighbor == BlockType.Lava)
        {
            Game.Instance.AudioManager.PlayAudio("Resources/Audio/SteamHiss.ogg", Game.Instance.AudioManager.SfxVol);
            world.SetBlock(x, y, z, BlockType.CobbleStone);
            return;
        }

        if (IsNearSponge(world, x, y, z))
            return;

        // Water washes away non-solid blocks (torches, flowers, etc.)
        if (neighbor != BlockType.Air)
            Game.Instance?.ParticleSystem?.SpawnBlockBreakParticles(new Vector3(x, y, z), neighbor);

        world.SetBlock(x, y, z, BlockType.Water);
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
