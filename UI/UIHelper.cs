using System.Numerics;
using ImGuiNET;
using VoxelEngine.Items;

namespace VoxelEngine.UI;

internal static class UIHelper
{
    // Change this to scale all in-game UI (hotbar, inventory screens, HUD icons).
    public const float UI_SCALE = 1f;

    private static readonly uint ColorDurBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.80f));

    internal static void DrawDurabilityBar(ImDrawListPtr drawList, ItemStack stack, float sx, float sy, float slotWidth)
    {
        if (!stack.HasDurability) return;

        var def   = ItemRegistry.Get(stack.Item);
        float frac = (float)stack.Durability / def.MaxDurability;

        float r = Math.Clamp(2f * (1f - frac), 0f, 1f);
        float g = Math.Clamp(2f * frac,         0f, 1f);
        var barColor = ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, 0f, 1f));

        float barY = sy - 3f;
        drawList.AddRectFilled(new Vector2(sx + 2f, barY), new Vector2(sx + slotWidth - 2f, barY + 2f), ColorDurBg);
        drawList.AddRectFilled(new Vector2(sx + 2f, barY), new Vector2(sx + 2f + (slotWidth - 4f) * frac, barY + 2f), barColor);
    }
}
