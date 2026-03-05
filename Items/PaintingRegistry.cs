// Registry of all painting art definitions | DA | 2/27/26
using System.Linq;

namespace VoxelEngine.Items;

public record PaintingDef(string Name, int SizeX, int SizeY, int OffsetX, int OffsetY)
{
    public int WidthBlocks  => SizeX / 16;
    public int HeightBlocks => SizeY / 16;
}

public static class PaintingRegistry
{
    public static readonly List<PaintingDef> All = new()
    {
        new("Small",  16,  16,  0,  0),
        new("Rogue",    32,  16,  0, 16),
        new("Big",    32,  32,  0, 32),
    };

    public static PaintingDef GetByName(string name) => All.FirstOrDefault(p => p.Name == name) ?? All[0];
}
