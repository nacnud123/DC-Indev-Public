using System.Numerics;
using ImGuiNET;
using VoxelEngine.Items;

namespace VoxelEngine.UI;

/// <summary>
/// Small collection of shared, stateless drawing helpers used by multiple inventory/hotbar screens (durability bars, UI scale constant, etc.) so each screen doesn't reimplement them.
/// </summary>
internal static class UIHelper
{
    // Change this to scale all in-game UI (hotbar, inventory screens, HUD icons).
    public const float UI_SCALE = 1f;

    // Cached background color (translucent black) for the durability bar track, computed once rather than converted from a Vector4 every frame/slot.
    private static readonly uint ColorDurBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.80f));

    /// <summary>
    /// Draws a small red-to-green durability bar under an item slot's icon (Minecraft-style), using the raw ImGui draw list so it can be layered directly on top of slot contents. No-ops for stacks that don't track durability or that are still at full durability.
    /// </summary>
    /// <param name="drawList">Draw list of the window currently being rendered (foreground layer).</param>
    /// <param name="stack">The item stack whose durability should be visualized.</param>
    /// <param name="sx">Screen-space X of the slot's top-left corner.</param>
    /// <param name="sy">Screen-space Y of the slot's top-left corner.</param>
    /// <param name="slotWidth">Width of the slot in pixels, used to size the bar.</param>
    internal static void DrawDurabilityBar(ImDrawListPtr drawList, ItemStack stack, float sx, float sy, float slotWidth)
    {
        if (!stack.HasDurability)
            return;

        var def = ItemRegistry.Get(stack.Item);

        if (def.MaxDurability <= 0)
            return;

        if (stack.Durability == def.MaxDurability)
            return;


        float frac = (float)stack.Durability / def.MaxDurability;

        // Hue shifts from red (frac=0) to green (frac=1) by ramping each channel independently; each clamps at 1 once frac crosses the 0.5 midpoint so the color never dips to brown.
        float r = Math.Clamp(2f * (1f - frac), 0f, 1f);
        float g = Math.Clamp(2f * frac, 0f, 1f);
        var barColor = ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, 0f, 1f));

        // Bar sits just above the slot's bottom edge; background track drawn first, then a foreground rect scaled by frac on top to show the filled portion.
        float barY = sy - 3f;
        drawList.AddRectFilled(new Vector2(sx + 2f, barY), new Vector2(sx + slotWidth - 2f, barY + 2f), ColorDurBg);
        drawList.AddRectFilled(new Vector2(sx + 2f, barY), new Vector2(sx + 2f + (slotWidth - 4f) * frac, barY + 2f),
            barColor);
    }
}