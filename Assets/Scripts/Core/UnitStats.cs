using System;

namespace Tactix.Core
{
    /// <summary>
    /// Static per-type combat stats. Distances are Chebyshev (diagonals count as 1).
    /// </summary>
    public sealed class UnitStats
    {
        public int MoveRange { get; }
        public int AttackRange { get; }
        public int AttackPower { get; }
        public int MaxHp { get; }
        /// <summary>Whether attacks require line-of-sight (blocked by forest and impassable tiles).</summary>
        public bool RequiresLineOfSight { get; }

        private UnitStats(int moveRange, int attackRange, int attackPower, int maxHp, bool requiresLineOfSight)
        {
            MoveRange = moveRange;
            AttackRange = attackRange;
            AttackPower = attackPower;
            MaxHp = maxHp;
            RequiresLineOfSight = requiresLineOfSight;
        }

        public static readonly UnitStats Infantry = new UnitStats(
            moveRange: 2, attackRange: 1, attackPower: 2, maxHp: 5, requiresLineOfSight: false);

        public static readonly UnitStats Ranged = new UnitStats(
            moveRange: 1, attackRange: 3, attackPower: 3, maxHp: 3, requiresLineOfSight: true);

        public static UnitStats For(UnitType type)
        {
            switch (type)
            {
                case UnitType.Infantry: return Infantry;
                case UnitType.Ranged: return Ranged;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown unit type");
            }
        }
    }
}
