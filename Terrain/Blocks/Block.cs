// Main block parent class, holds a lot of block properties, some of which are not used yet | DA | 2/5/26
using OpenTK.Mathematics;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.Terrain.Blocks;

public abstract class Block
{
    private static readonly TextureCoords KDefaultCoords = UvHelper.FromTileCoords(0, 0);
    public abstract BlockType Type { get; }
    public virtual RenderingType RenderType => RenderingType.Normal;
    public abstract string Name { get; }

    public virtual bool IsSolid => true;
    public virtual bool GravityBlock => false;
    public virtual bool IsBreakable => true;
    public virtual bool ShowInInventory => true;
    public virtual bool BlocksLight => LightOpacity >= 15;
    public virtual bool IsTransparent => !BlocksLight;
    public virtual bool SuffocatesBeneath => false;
    public virtual int LightEmission => 0;
    public virtual int LightOpacity => 15;
    public virtual float Hardness => 1.0f;
    public virtual bool TicksRandomly => false;

    public virtual Vector3 BoundsMin => Vector3.Zero;
    public virtual Vector3 BoundsMax => Vector3.One;

    public virtual TextureCoords TopTextureCoords => KDefaultCoords;
    public virtual TextureCoords BottomTextureCoords => KDefaultCoords;
    public virtual TextureCoords SideTextureCoords => KDefaultCoords;
    public virtual TextureCoords InventoryTextureCoords => TopTextureCoords;

    public virtual void RandomDisplayTick(int x, int y, int z, Random random) { }
    public virtual void RandomTick(World world, int x, int y, int z, Random random) { }
}
