using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public class BlockWheatStage0 : BlockWheatBase
{
    public override BlockType Type => BlockType.WheatStage0;
    public override int Stage => 0;
    public override BlockType NextStage => BlockType.WheatStage1;
    public override bool IsReplaceable => true;
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(8, 0);
    public override TextureCoords SideTextureCoords => TopTextureCoords;
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
}

public class BlockWheatStage1 : BlockWheatBase
{
    public override BlockType Type => BlockType.WheatStage1;
    public override int Stage => 1;
    public override BlockType NextStage => BlockType.WheatStage2;
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(9, 0);
    public override TextureCoords SideTextureCoords => TopTextureCoords;
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
}

public class BlockWheatStage2 : BlockWheatBase
{
    public override BlockType Type => BlockType.WheatStage2;
    public override int Stage => 2;
    public override BlockType NextStage => BlockType.WheatStage3;
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(10, 0);
    public override TextureCoords SideTextureCoords => TopTextureCoords;
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
}

public class BlockWheatStage3 : BlockWheatBase
{
    public override BlockType Type => BlockType.WheatStage3;
    public override int Stage => 3;
    public override BlockType NextStage => BlockType.WheatStage4;
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(11, 0);
    public override TextureCoords SideTextureCoords => TopTextureCoords;
    public override TextureCoords BottomTextureCoords => TopTextureCoords;
}

public class BlockWheatStage4 : BlockWheatBase
{
    public override BlockType Type => BlockType.WheatStage4;
    public override int Stage => 4;
    public override BlockType NextStage => BlockType.WheatStage4; // fully grown
    public override TextureCoords TopTextureCoords => UvHelper.FromTileCoords(12, 0);
    public override TextureCoords SideTextureCoords => TopTextureCoords;
    public override TextureCoords BottomTextureCoords => TopTextureCoords;

    // OnRemoved handles drops directly - return null so GetDrop doesn't also spawn items
    public override ItemStack? GetDrop(byte metadata) => null;

    public override void OnRemoved(World world, int x, int y, int z)
    {
        SpawnDrop(world, x, y, z, ItemStack.FromItem(ItemType.Wheat));

        int seedCount = Game.Instance.GameRandom.Next(1, 4);

        for (int i = 0; i < seedCount; i++)
            SpawnDrop(world, x, y, z, ItemStack.FromItem(ItemType.Seeds));
    }
}
