// Registry of all painting art definitions | DA | 2/27/26
using System.Linq;

namespace VoxelEngine.Items;

/// <summary>
/// Describes one painting artwork's size and its pixel offset within the painting texture sheet. SizeX/SizeY are in pixels (each block face is a 16px tile), while OffsetX/OffsetY locate the artwork's top-left corner in that sheet.
/// </summary>
public record PaintingDef(string Name, int SizeX, int SizeY, int OffsetX, int OffsetY)
{
    /// <summary>Painting width in world blocks, derived from pixel size (16px per block tile).</summary>
    public int WidthBlocks  => SizeX / 16;

    /// <summary>Painting height in world blocks, derived from pixel size (16px per block tile).</summary>
    public int HeightBlocks => SizeY / 16;
}

/// <summary>Static list of all placeable painting variants (used by ItemPainting when placing a painting entity).</summary>
public static class PaintingRegistry
{
    public static readonly List<PaintingDef> All = new()
    {
        new("Small",  16,  16,  0,  0),
        new("Rogue",    32,  16,  0, 16),
        new("Big",    32,  32,  0, 32),
    };

    /// <summary>Finds a painting definition by name, falling back to the first entry ("Small") if not found.</summary>
    public static PaintingDef GetByName(string name) => All.FirstOrDefault(p => p.Name == name) ?? All[0];
}
