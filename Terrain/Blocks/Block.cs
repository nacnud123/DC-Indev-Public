// Main block parent class, holds a lot of block properties, some of which are not used yet | DA | 2/5/26
using OpenTK.Mathematics;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public abstract class Block
{
    private static readonly TextureCoords KDefaultCoords = UvHelper.FromTileCoords(0, 0);
    public abstract BlockType Type { get; }
    public virtual RenderingType RenderType => RenderingType.Normal;
    public virtual BlockBreakMaterial BreakMaterial => BlockBreakMaterial.None;
    public abstract string Name { get; }

    public virtual bool IsSolid => true;
    public virtual bool GravityBlock => false;
    public virtual bool IsBreakable => true;
    public virtual bool IsReplaceable => false;
    public virtual bool ShowInInventory => true;
    public virtual bool BlocksLight => LightOpacity >= 15;
    public virtual bool IsTransparent => !BlocksLight;
    public virtual bool SuffocatesBeneath => false;
    public virtual bool NeedsSupportBelow => false;
    public virtual bool CrossHasOffset => true;
    public virtual int LightEmission => 0;
    public virtual int LightOpacity => 15;
    public virtual float Hardness => 1.0f;
    public virtual bool TicksRandomly => false;
    public virtual bool IsFlamable => false;
    public virtual int RandomTickDivisor => 1;
    public virtual bool IsFluid => false;
    public virtual bool SlowsEntities => false;
    public virtual List<BlockType> BlocksThatCanSupport => new List<BlockType>() { BlockType.All };

    public virtual Vector3 BoundsMin => Vector3.Zero;
    public virtual Vector3 BoundsMax => Vector3.One;

    public virtual TextureCoords TopTextureCoords => KDefaultCoords;
    public virtual TextureCoords GetTopTexture(byte metadata) => TopTextureCoords;
    public virtual TextureCoords BottomTextureCoords => KDefaultCoords;

    public virtual TextureCoords FrontTextureCoords => SideTextureCoords;
    public virtual TextureCoords BackTextureCoords => SideTextureCoords;
    public virtual TextureCoords LeftTextureCoords => SideTextureCoords;
    public virtual TextureCoords RightTextureCoords => SideTextureCoords;



    public virtual TextureCoords SideTextureCoords => KDefaultCoords;
    public virtual TextureCoords InventoryTextureCoords => TopTextureCoords;

    public virtual int TickRate => 0;  // 0 = not scheduled

    public virtual void RandomDisplayTick(int x, int y, int z, Random random) { }
    public virtual void RandomTick(World world, int x, int y, int z, Random random) { }
    public virtual void ScheduledTick(World world, int x, int y, int z, Random random) { }
    public virtual void OnPlaced(World world, int x, int y, int z) { }
    public virtual void OnRemoved(World world, int x, int y, int z) { }
    public virtual void OnEntityWalking(World world, int x, int y, int z, Random random) { }

    public virtual bool CanBlockSupport(BlockType beneath)
    {
        var needed = BlocksThatCanSupport;
        if (needed.Contains(BlockType.All))
            return true;

        return needed.Contains(beneath);
    }

    public virtual ItemStack? GetDrop(byte metadata) => ItemStack.FromBlock(this.Type);
    public virtual ToolType PreferredTool => ToolType.None;
    public virtual ToolTier MinimumTier => ToolTier.None;
    public virtual int DropCount => 1;
    public virtual int MaxStackSize => 64;
}
