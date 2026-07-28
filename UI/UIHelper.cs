using System.Numerics;
using ImGuiNET;
using VoxelEngine.Items;

namespace VoxelEngine.UI;

/// <summary>
/// Small collection of shared, stateless drawing helpers used by multiple inventory/hotbar
/// screens (durability bars, UI scale constant, etc.) so each screen doesn't reimplement them.
/// </summary>
internal static class UIHelper
{
    // Global multiplier applied to every UI pixel constant (hotbar, inventory screens, HUD
    // icons) and to ImGui's own font/style metrics. Computed once at startup from the window's
    // actual resolution (see InitScale) so the same build looks right on a 1080p laptop or a
    // 4K classroom display without per-machine tuning.
    public static float UI_SCALE { get; set; } = 1f;

    // Reference window height (px) the UI's pixel constants were originally tuned at.
    private const float BASELINE_HEIGHT = 1080f;

    /// <summary>
    /// Computes UI_SCALE from the window's actual resolution. Must be called once at startup,
    /// before any UI class (Hotbar, HudScreen, InventoryScreenBase, ImGuiController, etc.)
    /// is constructed, since their pixel-size fields are computed from UI_SCALE at that point.
    /// Clamped to [1, 3] so UI never shrinks below its designed size on a sub-1080p display,
    /// nor balloons to an unusable size on an ultra-high-res one.
    /// </summary>
    public static void InitScale(int windowHeight)
    {
        UI_SCALE = Math.Clamp(windowHeight / BASELINE_HEIGHT, 1f, 3f);
    }

    // Cached background color (translucent black) for the durability bar track, computed once
    // rather than converted from a Vector4 every frame/slot.
    private static readonly uint ColorDurBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.80f));

    /// <summary>
    /// Draws a small red-to-green durability bar under an item slot's icon (Minecraft-style),
    /// using the raw ImGui draw list so it can be layered directly on top of slot contents.
    /// No-ops for stacks that don't track durability or that are still at full durability.
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

        // Hue shifts from red (frac=0) to green (frac=1) by ramping each channel independently;
        // each clamps at 1 once frac crosses the 0.5 midpoint so the color never dips to brown.
        float r = Math.Clamp(2f * (1f - frac), 0f, 1f);
        float g = Math.Clamp(2f * frac, 0f, 1f);
        var barColor = ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, 0f, 1f));

        // Bar sits just above the slot's bottom edge; background track drawn first, then a
        // foreground rect scaled by frac on top to show the filled portion.
        float barY = sy - 3f;
        drawList.AddRectFilled(new Vector2(sx + 2f, barY), new Vector2(sx + slotWidth - 2f, barY + 2f), ColorDurBg);
        drawList.AddRectFilled(new Vector2(sx + 2f, barY), new Vector2(sx + 2f + (slotWidth - 4f) * frac, barY + 2f),
            barColor);
    }
}