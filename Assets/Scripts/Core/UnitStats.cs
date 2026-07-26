using System;

namespace Tactix.Core
{
    /// <summary>
    /// Static per-type stats. All distances are Euclidean world units in
    /// continuous space (a tile is 1x1). Sight has no gameplay effect yet
    /// (no fog of war) — it is defined, displayed, and logged so fog can be
    /// added later without a schema change.
    /// </summary>
    public sealed class UnitStats
    {
        /// <summary>Maximum straight-line distance the unit may dash in one turn.</summary>
        public double MoveRange { get; }
        /// <summary>Maximum centre-to-centre distance to a target.</summary>
        public double AttackRange { get; }
        public int AttackPower { get; }
        public int MaxHp { get; }
        public double Sight { get; }
        /// <summary>Collision radius: two units must stay at least 2x this apart.</summary>
        public double Radius { get; }
        /// <summary>Whether attacks require line of sight (blocked by terrain and relief).</summary>
        public bool RequiresLineOfSight { get; }

        private UnitStats(double moveRange, double attackRange, int attackPower, int maxHp, double sight, double radius, bool requiresLineOfSight)
        {
            MoveRange = moveRange;
            AttackRange = attackRange;
            AttackPower = attackPower;
            MaxHp = maxHp;
            Sight = sight;
            Radius = radius;
            RequiresLineOfSight = requiresLineOfSight;
        }

        public static readonly UnitStats Infantry = new UnitStats(
            moveRange: 3.0, attackRange: 1.2, attackPower: 2, maxHp: 5, sight: 4.0, radius: 0.35, requiresLineOfSight: false);

        public static readonly UnitStats MechInfantry = new UnitStats(
            moveRange: 4.5, attackRange: 1.2, attackPower: 2, maxHp: 6, sight: 4.0, radius: 0.35, requiresLineOfSight: false);

        public static readonly UnitStats Armor = new UnitStats(
            moveRange: 4.0, attackRange: 1.5, attackPower: 4, maxHp: 8, sight: 3.0, radius: 0.40, requiresLineOfSight: false);

        public static readonly UnitStats Artillery = new UnitStats(
            moveRange: 2.0, attackRange: 5.0, attackPower: 3, maxHp: 3, sight: 3.0, radius: 0.35, requiresLineOfSight: true);

        public static readonly UnitStats Recon = new UnitStats(
            moveRange: 6.0, attackRange: 1.0, attackPower: 1, maxHp: 3, sight: 8.0, radius: 0.30, requiresLineOfSight: false);

        /// <summary>All unit types, in display order (for legends, tools, iteration).</summary>
        public static readonly UnitType[] AllTypes =
        {
            UnitType.Infantry, UnitType.MechInfantry, UnitType.Armor, UnitType.Artillery, UnitType.Recon,
        };

        public static UnitStats For(UnitType type)
        {
            switch (type)
            {
                case UnitType.Infantry: return Infantry;
                case UnitType.MechInfantry: return MechInfantry;
                case UnitType.Armor: return Armor;
                case UnitType.Artillery: return Artillery;
                case UnitType.Recon: return Recon;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown unit type");
            }
        }
    }
}
