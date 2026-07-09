
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Rendering;
using VoxelEngine.Terrain;
using VoxelEngine.Utils;

namespace VoxelEngine.Items;

/// <summary>
/// Ranged weapon item. Extends Item directly (not ItemTool) since it doesn't fit the tier-based durability table — it has its own fixed MaxDurability instead. Firing consumes one Arrow from the inventory and spawns a physical ArrowEntity in the world.
/// </summary>
public class ItemBow : Item
{
    public override ItemType Type => ItemType.Bow;
    public override string Name => "Bow";
    public override TextureCoords ItemCoords => UvHelper.FromTileCoords(0, 2);
    public override ToolType ToolType => ToolType.Bow;
    public override int MaxDurability => 384;
    public override int MaxStackSize => 1;

    // Bows are used by aiming/clicking rather than targeting a block, so the normal block-under-cursor raycast used for OnUse's blockPos/placePos should be skipped.
    public override bool SkipBlockRaycast => true;

    /// <summary>Fires an arrow: requires at least one Arrow in inventory, consumes it, spawns an ArrowEntity, and plays the release sound.</summary>
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
