namespace Tactix.Core
{
    /// <summary>
    /// Unit archetypes. Serialized as camelCase strings ("infantry",
    /// "mechInfantry", "armor", "artillery", "recon", "medic", "service").
    /// </summary>
    public enum UnitType
    {
        Infantry = 0,
        MechInfantry = 1,
        Armor = 2,
        Artillery = 3,
        Recon = 4,
        /// <summary>Medical section: heals dismounted units, unarmed.</summary>
        Medic = 5,
        /// <summary>Maintenance/service company: repairs vehicles, unarmed.</summary>
        Service = 6,
        /// <summary>
        /// A formation of mixed branches, produced by amalgamating units of
        /// different types. Its profile is a blend rather than a record of what
        /// went into it — provenance is deliberately not tracked, so that a
        /// unit's stats stay a pure function of its type and echelon.
        /// </summary>
        CombinedArms = 7,
    }
}
