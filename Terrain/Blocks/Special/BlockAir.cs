namespace VoxelEngine.Terrain.Blocks;

public class BlockAir : Block
{
    public override BlockType Type => BlockType.Air;
    public override string Name => "Air";
    public override int LightOpacity => 0;

    public override bool IsSolid => false;
    public override bool ShowInInventory => false;
}
