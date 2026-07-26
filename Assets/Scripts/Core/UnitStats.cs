using System;
using System.Collections.Generic;

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
        /// <summary>Maximum centre-to-centre distance to a target. Zero for unarmed units.</summary>
        public double AttackRange { get; }
        public int AttackPower { get; }
        public int MaxHp { get; }
        public double Sight { get; }
        /// <summary>Collision radius: two units must stay at least 2x this apart.</summary>
        public double Radius { get; }
        /// <summary>Whether attacks require line of sight (blocked by terrain and relief).</summary>
        public bool RequiresLineOfSight { get; }

        /// <summary>HP restored per support action; zero for units that cannot support.</summary>
        public int SupportPower { get; }
        /// <summary>Range of the support action.</summary>
        public double SupportRange { get; }
        /// <summary>Which units this one can support: medics treat crews, service repairs vehicles.</summary>
        public SupportTarget Supports { get; }
        /// <summary>Vehicles are repaired by service units; dismounted units are treated by medics.</summary>
        public bool IsVehicle { get; }

        public bool CanAttack => AttackPower > 0 && AttackRange > 0;
        public bool CanSupport => SupportPower > 0 && Supports != SupportTarget.None;

        /// <summary>
        /// Half-width of this unit's damage roll: damage lands uniformly in
        /// [power - spread, power + spread]. Zero for small formations, which
        /// resolve exactly.
        /// </summary>
        public int DamageSpread { get; private set; }

        /// <summary>
        /// Worst-case fraction of an ordered move this unit may fail to cover.
        /// Zero for small formations, which go exactly where they are sent.
        /// </summary>
        public double MovementFriction { get; private set; }

        /// <summary>The size this profile has been scaled to.</summary>
        public Echelon Echelon { get; private set; } = Echelon.Company;

        private UnitStats(
            double moveRange, double attackRange, int attackPower, int maxHp, double sight, double radius,
            bool requiresLineOfSight, bool isVehicle,
            int supportPower = 0, double supportRange = 0, SupportTarget supports = SupportTarget.None)
        {
            MoveRange = moveRange;
            AttackRange = attackRange;
            AttackPower = attackPower;
            MaxHp = maxHp;
            Sight = sight;
            Radius = radius;
            RequiresLineOfSight = requiresLineOfSight;
            IsVehicle = isVehicle;
            SupportPower = supportPower;
            SupportRange = supportRange;
            Supports = supports;
        }

        public static readonly UnitStats Infantry = new UnitStats(
            moveRange: 3.0, attackRange: 1.2, attackPower: 2, maxHp: 5, sight: 4.0, radius: 0.35,
            requiresLineOfSight: false, isVehicle: false);

        public static readonly UnitStats MechInfantry = new UnitStats(
            moveRange: 4.5, attackRange: 1.2, attackPower: 2, maxHp: 6, sight: 4.0, radius: 0.35,
            requiresLineOfSight: false, isVehicle: false);

        public static readonly UnitStats Armor = new UnitStats(
            moveRange: 4.0, attackRange: 1.5, attackPower: 4, maxHp: 8, sight: 3.0, radius: 0.40,
            requiresLineOfSight: false, isVehicle: true);

        public static readonly UnitStats Artillery = new UnitStats(
            moveRange: 2.0, attackRange: 5.0, attackPower: 3, maxHp: 3, sight: 3.0, radius: 0.35,
            requiresLineOfSight: true, isVehicle: true);

        public static readonly UnitStats Recon = new UnitStats(
            moveRange: 6.0, attackRange: 1.0, attackPower: 1, maxHp: 3, sight: 8.0, radius: 0.30,
            requiresLineOfSight: false, isVehicle: false);

        public static readonly UnitStats Medic = new UnitStats(
            moveRange: 4.0, attackRange: 0, attackPower: 0, maxHp: 4, sight: 4.0, radius: 0.30,
            requiresLineOfSight: false, isVehicle: false,
            supportPower: 2, supportRange: 1.5, supports: SupportTarget.Dismounted);

        public static readonly UnitStats Service = new UnitStats(
            moveRange: 2.5, attackRange: 0, attackPower: 0, maxHp: 6, sight: 3.0, radius: 0.40,
            requiresLineOfSight: false, isVehicle: true,
            supportPower: 3, supportRange: 1.2, supports: SupportTarget.Vehicles);

        /// <summary>
        /// A blend of manoeuvre branches, formed by amalgamating unlike units.
        /// Tougher and harder-hitting than infantry, slower and shorter-sighted
        /// than armour, and dismounted enough for a medic to treat.
        /// </summary>
        public static readonly UnitStats CombinedArms = new UnitStats(
            moveRange: 3.5, attackRange: 1.3, attackPower: 3, maxHp: 6, sight: 3.5, radius: 0.38,
            requiresLineOfSight: false, isVehicle: false);

        /// <summary>All unit types, in display order (for legends, tools, iteration).</summary>
        public static readonly UnitType[] AllTypes =
        {
            UnitType.Infantry, UnitType.MechInfantry, UnitType.Armor, UnitType.Artillery,
            UnitType.Recon, UnitType.Medic, UnitType.Service, UnitType.CombinedArms,
        };

        /// <summary>The company-scale reference profile for a unit type.</summary>
        public static UnitStats For(UnitType type)
        {
            switch (type)
            {
                case UnitType.Infantry: return Infantry;
                case UnitType.MechInfantry: return MechInfantry;
                case UnitType.Armor: return Armor;
                case UnitType.Artillery: return Artillery;
                case UnitType.Recon: return Recon;
                case UnitType.Medic: return Medic;
                case UnitType.Service: return Service;
                case UnitType.CombinedArms: return CombinedArms;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown unit type");
            }
        }

        private static readonly Dictionary<(UnitType, Echelon), UnitStats> ScaledCache =
            new Dictionary<(UnitType, Echelon), UnitStats>();

        /// <summary>
        /// The profile for a unit type at a given size. Company scale returns the
        /// reference profile unchanged; every other size scales it through
        /// <see cref="EchelonScale"/>. Results are cached — profiles are immutable.
        /// </summary>
        public static UnitStats For(UnitType type, Echelon echelon)
        {
            lock (ScaledCache)
            {
                if (ScaledCache.TryGetValue((type, echelon), out var cached)) return cached;
                var scaled = Scale(For(type), echelon);
                ScaledCache[(type, echelon)] = scaled;
                return scaled;
            }
        }

        private static UnitStats Scale(UnitStats baseStats, Echelon echelon)
        {
            double strength = EchelonScale.StrengthMultiplier(echelon);
            int attackPower = ScaleWhole(baseStats.AttackPower, strength);

            var scaled = new UnitStats(
                moveRange: baseStats.MoveRange * EchelonScale.MobilityMultiplier(echelon),
                attackRange: baseStats.AttackRange * EchelonScale.ReachMultiplier(echelon),
                attackPower: attackPower,
                maxHp: ScaleWhole(baseStats.MaxHp, strength),
                sight: baseStats.Sight * EchelonScale.VisionMultiplier(echelon),
                radius: baseStats.Radius * EchelonScale.FootprintMultiplier(echelon),
                requiresLineOfSight: baseStats.RequiresLineOfSight,
                isVehicle: baseStats.IsVehicle,
                supportPower: ScaleWhole(baseStats.SupportPower, strength),
                supportRange: baseStats.SupportRange * EchelonScale.ReachMultiplier(echelon),
                supports: baseStats.Supports)
            {
                Echelon = echelon,
                MovementFriction = EchelonScale.MovementFrictionOf(echelon),
            };
            scaled.DamageSpread = (int)Math.Round(
                attackPower * EchelonScale.DamageSpreadOf(echelon), MidpointRounding.AwayFromZero);
            return scaled;
        }

        /// <summary>Scales a whole-number stat, never rounding a real capability away to nothing.</summary>
        private static int ScaleWhole(int value, double multiplier)
        {
            if (value <= 0) return 0;
            return Math.Max(1, (int)Math.Round(value * multiplier, MidpointRounding.AwayFromZero));
        }

        /// <summary>True when a unit of <paramref name="targetType"/> is treatable by this supporter.</summary>
        public bool CanSupportType(UnitType targetType)
        {
            switch (Supports)
            {
                case SupportTarget.Dismounted: return !For(targetType).IsVehicle;
                case SupportTarget.Vehicles: return For(targetType).IsVehicle;
                default: return false;
            }
        }
    }

    /// <summary>What a support unit is able to work on.</summary>
    public enum SupportTarget
    {
        None = 0,
        /// <summary>Foot and mounted-infantry units (medics).</summary>
        Dismounted = 1,
        /// <summary>Armour, artillery, and other vehicles (service/maintenance).</summary>
        Vehicles = 2,
    }
}
