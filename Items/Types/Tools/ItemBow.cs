using OpenTK.Mathematics;
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

public class ItemBow : Item
{
    public override ItemType Type => ItemType.Bow;
    public override string Name => "Bow";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 2);
    public override ToolType ToolType => ToolType.Bow;
    public override int MaxDurability => 384;
    public override int MaxStackSize => 1;
    public override bool SkipBlockRaycast => true;

    public override bool OnUse(World world, Vector3i blockPos, Vector3i? placePos)
    {
        var inv = Game.Instance.PlayerInventory;
        var player = Game.Instance.GetPlayer;

        if (inv == null)
            return false;

        int arrowSlot = inv.FindItem(ItemType.Arrow);
        if (arrowSlot == -1)
            return false;

        inv.ConsumeOne(arrowSlot);
        world.AddEntity(new ArrowEntity(player));
        Game.Instance.AudioManager.PlayAudio("Resources/Audio/Bow/BowRelease.ogg", Game.Instance.AudioManager.SfxVol);
        return true;
    }
}
