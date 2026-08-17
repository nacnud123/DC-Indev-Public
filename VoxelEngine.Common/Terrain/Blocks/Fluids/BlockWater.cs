
using VoxelEngine.Core;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

/// <summary>
/// Water. The flow simulation itself lives in <see cref="BlockFluid"/>; water only differs from
/// lava in how fast it ticks, how far it reaches, and what it does on contact.
///
/// Losing one level per block gives water its familiar 7-block reach from a source, and because
/// water forms new sources between two existing ones, a 2x2 hole can be made infinite.
/// </summary>
public class BlockWater : BlockFluid
{
    public override BlockType Type => BlockType.Water;
    public override string Name => "Water";

    public override int LightOpacity => 3;
    public override bool ShowInInventory => true;

    // Fast tick rate relative to lava's 25 - water flows/spreads much more quickly.
    public override int TickRate => 5;

    protected override int DecayPerBlock => 1;
    protected override bool FormsInfiniteSources => true;
    protected override BlockType OpposingFluid => BlockType.Lava;

    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(0, 4);
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
    public override TextureCoords SideTextureCoords => TopTextureCoords;

    /// <summary>
    /// Water hitting lava sets it. A lava <em>source</em> becomes obsidian and a flowing one
    /// becomes cobblestone, which is what makes a lava lake worth pouring water onto but a lava
    /// stream merely worth blocking.
    /// </summary>
    protected override void OnMeetOpposingFluid(World world, int x, int y, int z)
    {
        bool isSource = world.GetMetadata(x, y, z) == 0;

        world.SetBlock(x, y, z, isSource ? BlockType.Obsidian : BlockType.CobbleStone);
        world.SetMetadata(x, y, z, 0);
        world.SetChunkAsModified(x, y, z);

        GameContext.Current?.AudioManager?.PlayAudio(
            "Resources/Audio/SteamHiss.ogg", GameContext.Current.AudioManager.SfxVol);
    }
}
