namespace Tactix.Core
{
    /// <summary>
    /// Terrain codes. Serialized as integers in the state JSON — the numeric
    /// values are part of the logged schema and must not be reordered.
    /// </summary>
    public enum TerrainType
    {
        Open = 0,
        Forest = 1,
        Impassable = 2,
    }
}
