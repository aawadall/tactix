using System;

namespace Tactix.Core
{
    /// <summary>
    /// Static per-type combat stats. Distances are Chebyshev (diagonals count as 1).
    /// Sight has no gameplay effect yet (no fog of war in v1) — it is defined,
    /// displayed, and logged so fog can be added later without a schema change.
    /// </summary>
    public sealed class UnitStats
    {
        public int MoveRange { get; }
        public int AttackRange { get; }
        public int AttackPower { get; }
        public int MaxHp { get; }
        public int Sight { get; }
        /// <summary>Whether attacks require line-of-sight (blocked by forest and impassable tiles).</summary>
        public bool RequiresLineOfSight { get; }

        private UnitStats(int moveRange, int attackRange, int attackPower, int maxHp, int sight, bool requiresLineOfSight)
        {
            MoveRange = moveRange;
            AttackRange = attackRange;
            AttackPower = attackPower;
            MaxHp = maxHp;
            Sight = sight;
            RequiresLineOfSight = requiresLineOfSight;
        }

        public static readonly UnitStats Infantry = new UnitStats(
            moveRange: 2, attackRange: 1, attackPower: 2, maxHp: 5, sight: 3, requiresLineOfSight: false);

        public static readonly UnitStats MechInfantry = new UnitStats(
            moveRange: 3, attackRange: 1, attackPower: 2, maxHp: 6, sight: 3, requiresLineOfSight: false);

        public static readonly UnitStats Armor = new UnitStats(
            moveRange: 3, attackRange: 1, attackPower: 4, maxHp: 8, sight: 2, requiresLineOfSight: false);

        public static readonly UnitStats Artillery = new UnitStats(
            moveRange: 1, attackRange: 3, attackPower: 3, maxHp: 3, sight: 2, requiresLineOfSight: true);

        public static readonly UnitStats Recon = new UnitStats(
            moveRange: 4, attackRange: 1, attackPower: 1, maxHp: 3, sight: 5, requiresLineOfSight: false);

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
