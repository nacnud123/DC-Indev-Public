
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Lava. Shares <see cref="BlockFluid"/>'s flow simulation with water and differs only in being
/// slower and shorter-reaching: it ticks every 25 game ticks instead of every 5, and loses two
/// levels per block rather than one, so a source spreads three blocks instead of seven. Emits
/// full light, and forms stone where it runs into water.
/// </summary>
public class BlockLava : BlockFluid
{
    public override BlockType Type => BlockType.Lava;
    public override string Name => "Lava";

    public override int LightEmission => 15;
    public override int LightOpacity => 3;
    public override bool ShowInInventory => true;

    // Scheduled ticks fire much less often than water's, making lava flow noticeably thicker.
    public override int TickRate => 25;

    // Two levels per block: a lava source reaches 3 blocks, not water's 7.
    protected override int DecayPerBlock => 2;
    protected override BlockType OpposingFluid => BlockType.Water;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(7, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    /// <summary>Lava running into water sets solid: stone, as in vanilla when lava is the one moving.</summary>
    protected override void OnMeetOpposingFluid(World world, int x, int y, int z)
    {
        world.SetBlock(x, y, z, BlockType.Stone);
        world.SetMetadata(x, y, z, 0);
        world.SetChunkAsModified(x, y, z);

        GameContext.Current?.AudioManager?.PlayAudio(
            "Resources/Audio/SteamHiss.ogg", GameContext.Current.AudioManager.SfxVol);
    }
}
