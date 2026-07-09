// Used to hold reference to textures coords | DA | 2/5/26


namespace VoxelEngine.Rendering
{
    /// <summary>
    /// Describes a single tile's UV rectangle within the shared texture atlas (Resources/world.png for terrain, Resources/Items.png for items). Both corners are normalized (0..1) atlas coordinates, not pixel coordinates - they're what gets fed directly into a mesh's UV vertex attribute. Instances are produced by <c>UvHelper.FromTileCoords(col, row)</c>, which converts a tile's integer (column, row) position in the fixed tile grid into this normalized rectangle by dividing by the atlas's tile-grid dimensions. <see cref="BlockRegistry"/> stores one of these per block face (top/side/bottom) so <see cref="ChunkMeshBuilder"/> can look up the right UVs per face type.
    /// </summary>
    public class TextureCoords
    {
        // Normalized (0..1) UV of the tile's top-left corner in atlas space.
        public Vector2 TopLeft;
        // Normalized (0..1) UV of the tile's bottom-right corner in atlas space.
        public Vector2 BottomRight;
    }
}