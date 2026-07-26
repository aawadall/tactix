namespace Tactix.Core
{
    /// <summary>
    /// Unit archetypes. Serialized as camelCase strings ("infantry",
    /// "mechInfantry", "armor", "artillery", "recon") in the state JSON.
    /// </summary>
    public enum UnitType
    {
        Infantry = 0,
        MechInfantry = 1,
        Armor = 2,
        Artillery = 3,
        Recon = 4,
    }
}
