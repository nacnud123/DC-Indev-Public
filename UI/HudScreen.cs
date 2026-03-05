// HUD overlay — health, armor, and air bubbles drawn above the hotbar, Minecraft-style | DA
using System.Numerics;
using ImGuiNET;
using VoxelEngine.Core;
using VoxelEngine.GameEntity;
using VoxelEngine.Items;
using VoxelEngine.Rendering;
using VoxelEngine.Utils;

namespace VoxelEngine.UI;

public class HudScreen
{
    private const int   ICON_COUNT = 10;
    private const float ICON_DRAW  = 18f * UIHelper.UI_SCALE;
    private const float ICON_STEP  = ICON_DRAW - 1f * UIHelper.UI_SCALE; // slight overlap like Minecraft
    private const float ROW_GAP    = 2f  * UIHelper.UI_SCALE;

    // Icons.png tile positions (tileX, tileY)
    // Row 0: health — empty(0,0), full(1,0), half(2,0)
    // Row 1: armor  — empty(0,1), full(1,1), half(2,1)
    // Row 2: air    — full bubble(0,2), popped bubble(1,2)
    private static readonly TextureCoords UvHealthEmpty  = UvHelper.FromTileCoords(0, 0);
    private static readonly TextureCoords UvHealthFull   = UvHelper.FromTileCoords(1, 0);
    private static readonly TextureCoords UvHealthHalf   = UvHelper.FromTileCoords(2, 0);
    private static readonly TextureCoords UvArmorEmpty   = UvHelper.FromTileCoords(0, 1);
    private static readonly TextureCoords UvArmorFull    = UvHelper.FromTileCoords(1, 1);
    private static readonly TextureCoords UvArmorHalf    = UvHelper.FromTileCoords(2, 1);
    private static readonly TextureCoords UvBubbleFull   = UvHelper.FromTileCoords(0, 2);
    private static readonly TextureCoords UvBubblePopped = UvHelper.FromTileCoords(1, 2);

    private readonly Texture mIconsTexture;

    public HudScreen(Texture iconsTexture)
    {
        mIconsTexture = iconsTexture;
    }

    public void Render(float hotbarX, float hotbarY, float hotbarWidth)
    {
        var player = Game.Instance.GetPlayer;
        var inv    = Game.Instance.PlayerInventory;
        if (player == null || inv == null) return;

        var drawList = ImGui.GetBackgroundDrawList();

        float rowWidth   = (ICON_COUNT - 1) * ICON_STEP + ICON_DRAW;
        float healthX    = hotbarX;
        float armorX     = hotbarX + hotbarWidth - rowWidth;
        float statusRowY = hotbarY - ICON_DRAW - ROW_GAP;

        // Health on the left, armor on the right, same row
        DrawHealthRow(drawList, healthX, statusRowY, player);
        if (HasAnyArmor(inv))
            DrawArmorRow(drawList, armorX, statusRowY, inv);

        // Air bubbles above health when underwater
        if (player.IsUnderWater)
        {
            float bubbleY = statusRowY - ICON_DRAW - ROW_GAP;
            DrawBubbleRow(drawList, healthX, bubbleY, player);
        }
    }

    private void DrawHealthRow(ImDrawListPtr drawList, float x, float y, Player player)
    {
        int hp = Math.Clamp(player.Health, 0, Player.PLAYER_MAX_HEALTH);

        for (int i = 0; i < ICON_COUNT; i++)
        {
            int hpThisIcon = hp - i * 2;
            var uv = hpThisIcon <= 0 ? UvHealthEmpty
                   : hpThisIcon == 1 ? UvHealthHalf
                                     : UvHealthFull;
            DrawIcon(drawList, x + i * ICON_STEP, y, uv);
        }
    }

    // Protection points contributed by each armor slot (helmet, chest, legs, boots)
    private static readonly int[] SlotProtection = { 3, 8, 6, 3 };

    private void DrawArmorRow(ImDrawListPtr drawList, float x, float y, PlayerInventory inv)
    {
        int totalProtection = 0;
        int totalDur        = 0;
        int totalMax        = 0;

        for (int s = 0; s < 4; s++)
        {
            var slot = inv.GetArmorSlot((ArmorSlot)s);
            if (!slot.HasValue)
                continue;

            var def = ItemRegistry.Get(slot.Value.Item);
            if (def.MaxDurability <= 0)
                continue;

            totalProtection += SlotProtection[s];
            totalDur        += slot.Value.Durability;
            totalMax        += def.MaxDurability;
        }

        if (totalMax == 0) return;

        float durFrac   = (float)totalDur / totalMax;
        float armorValue = totalProtection > 1
            ? (totalProtection - 1) * durFrac + 1
            : totalProtection * durFrac;

        int armorInt = (int)Math.Round(armorValue);

        for (int i = 0; i < ICON_COUNT; i++)
        {
            int threshold = i * 2 + 1; // 1, 3, 5, 7, 9, 11, 13, 15, 17, 19
            var uv = threshold < armorInt  ? UvArmorFull
                   : threshold == armorInt ? UvArmorHalf
                                           : UvArmorEmpty;
            DrawIcon(drawList, x + (ICON_COUNT - 1 - i) * ICON_STEP, y, uv);
        }
    }

    private void DrawBubbleRow(ImDrawListPtr drawList, float x, float y, Player player)
    {
        // breath fraction: 1 = full air, 0 = no air. Each bubble = 1/ICON_COUNT of total.
        float frac      = Math.Clamp(player.BreathFraction, 0f, 1f);
        int   fullCount = (int)Math.Ceiling(frac * ICON_COUNT);

        for (int i = 0; i < ICON_COUNT; i++)
        {
            // Draw right-to-left so bubbles pop from the right as air depletes
            var uv = i < fullCount ? UvBubbleFull : UvBubblePopped;
            DrawIcon(drawList, x + i * ICON_STEP, y, uv);
        }
    }

    private void DrawIcon(ImDrawListPtr drawList, float x, float y, TextureCoords uv)
    {
        var min = new Vector2(x, y);
        var max = new Vector2(x + ICON_DRAW, y + ICON_DRAW);
        drawList.AddImage(
            new IntPtr(mIconsTexture.Handle),
            min, max,
            new Vector2(uv.TopLeft.X,     uv.BottomRight.Y),
            new Vector2(uv.BottomRight.X, uv.TopLeft.Y));
    }

    private static bool HasAnyArmor(PlayerInventory inv)
    {
        for (int s = 0; s < 4; s++)
        {
            if (inv.GetArmorSlot((ArmorSlot)s).HasValue)
                return true;
        }
        return false;
    }
}
