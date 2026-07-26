using System;

namespace Tactix.Core
{
    /// <summary>
    /// Unit size, from a handful of soldiers up to a whole theatre of war.
    /// Serialized as camelCase strings ("fireTeam", "company", "armyGroup", …).
    /// <see cref="UnitType"/> says what a unit does; Echelon says how much of it
    /// there is, and the two are independent — any type may exist at any size.
    ///
    /// Numeric values are part of the logged schema and must not be reordered.
    /// <see cref="Echelon.Company"/> is the reference scale: a company-sized unit
    /// uses the unscaled stats in <see cref="UnitStats"/>.
    /// </summary>
    public enum Echelon
    {
        FireTeam = 0,
        Squad = 1,
        Section = 2,
        Platoon = 3,
        /// <summary>
        /// The four-dot level of APP-6, which the standard names variously
        /// "echelon", "half-squadron", "troop (major)", or "department". Called
        /// Detachment here to avoid colliding with the name of this axis.
        /// </summary>
        Detachment = 4,
        Company = 5,
        Battalion = 6,
        Regiment = 7,
        Brigade = 8,
        Division = 9,
        Corps = 10,
        Army = 11,
        ArmyGroup = 12,
        Theater = 13,
    }

    /// <summary>
    /// How unit size scales behaviour. Larger formations hit harder and absorb
    /// far more punishment, but are slower, occupy more ground, and — the point
    /// of the whole ladder — behave less predictably: a fire team does exactly
    /// what it is told, while a theatre command is an abstraction over so many
    /// subordinate actions that its outcomes are only statistical.
    /// </summary>
    public static class EchelonScale
    {
        public static readonly Echelon[] All =
        {
            Echelon.FireTeam, Echelon.Squad, Echelon.Section, Echelon.Platoon, Echelon.Detachment,
            Echelon.Company, Echelon.Battalion, Echelon.Regiment, Echelon.Brigade,
            Echelon.Division, Echelon.Corps, Echelon.Army, Echelon.ArmyGroup, Echelon.Theater,
        };

        /// <summary>
        /// Multiplier on attack power, support power, and hit points: each step up
        /// doubles. Powers of two are what make amalgamation conserve — two units
        /// of one size carry exactly the strength of one unit a size larger, so
        /// merging is a decision about tempo and precision rather than a way to
        /// gain or lose mass. (Below company scale, integer rounding means the
        /// smallest rungs floor at 1 HP and conservation becomes approximate.)
        /// </summary>
        private static readonly double[] Strength =
        {
            1.0 / 32, 1.0 / 16, 1.0 / 8, 1.0 / 4, 1.0 / 2,
            1.0,
            2, 4, 8, 16, 32, 64, 128, 256,
        };

        /// <summary>Multiplier on movement range — big formations are ponderous.</summary>
        private static readonly double[] Mobility =
        { 1.35, 1.29, 1.22, 1.15, 1.08, 1.00, 0.90, 0.82, 0.74, 0.66, 0.58, 0.52, 0.46, 0.40 };

        /// <summary>Multiplier on attack and support range — heavier formations reach further.</summary>
        private static readonly double[] Reach =
        { 0.80, 0.85, 0.89, 0.93, 0.97, 1.00, 1.10, 1.20, 1.30, 1.42, 1.55, 1.68, 1.82, 1.95 };

        /// <summary>Multiplier on sight — larger formations field more reconnaissance.</summary>
        private static readonly double[] Vision =
        { 0.75, 0.80, 0.85, 0.90, 0.95, 1.00, 1.08, 1.16, 1.24, 1.33, 1.42, 1.52, 1.62, 1.72 };

        /// <summary>Multiplier on collision radius — the ground a formation physically occupies.</summary>
        private static readonly double[] Footprint =
        { 0.55, 0.61, 0.68, 0.76, 0.87, 1.00, 1.18, 1.38, 1.60, 1.85, 2.10, 2.40, 2.70, 3.00 };

        /// <summary>
        /// Damage spread as a fraction of attack power. Zero for the smallest
        /// formations (they resolve exactly), widening with size.
        /// </summary>
        private static readonly double[] DamageSpreadFraction =
        { 0.00, 0.00, 0.08, 0.13, 0.19, 0.25, 0.30, 0.33, 0.36, 0.38, 0.40, 0.42, 0.44, 0.45 };

        /// <summary>
        /// Worst-case shortfall on a movement order, as a fraction of the distance
        /// ordered. A fire team goes exactly where it is sent; a theatre command
        /// may fall well short of it.
        /// </summary>
        private static readonly double[] MovementFrictionFraction =
        { 0.00, 0.00, 0.00, 0.01, 0.03, 0.05, 0.08, 0.11, 0.14, 0.18, 0.21, 0.24, 0.27, 0.30 };

        public static double StrengthMultiplier(Echelon e) => Strength[Index(e)];

        /// <summary>The next size up, or null at the top of the ladder.</summary>
        public static Echelon? Larger(Echelon echelon)
        {
            int i = Index(echelon);
            return i + 1 < Strength.Length ? (Echelon?)(Echelon)(i + 1) : null;
        }

        /// <summary>The next size down, or null at the bottom of the ladder.</summary>
        public static Echelon? Smaller(Echelon echelon)
        {
            int i = Index(echelon);
            return i > 0 ? (Echelon?)(Echelon)(i - 1) : null;
        }
        public static double MobilityMultiplier(Echelon e) => Mobility[Index(e)];
        public static double ReachMultiplier(Echelon e) => Reach[Index(e)];
        public static double VisionMultiplier(Echelon e) => Vision[Index(e)];
        public static double FootprintMultiplier(Echelon e) => Footprint[Index(e)];
        public static double DamageSpreadOf(Echelon e) => DamageSpreadFraction[Index(e)];
        public static double MovementFrictionOf(Echelon e) => MovementFrictionFraction[Index(e)];

        /// <summary>Human-readable size name ("Fire Team", "Army Group").</summary>
        public static string DisplayName(Echelon echelon)
        {
            switch (echelon)
            {
                case Echelon.FireTeam: return "Fire Team";
                case Echelon.ArmyGroup: return "Army Group";
                case Echelon.Theater: return "Theatre";
                default: return echelon.ToString();
            }
        }

        /// <summary>
        /// The NATO echelon marking drawn above a unit's frame, described as a
        /// count of a repeated glyph.
        /// </summary>
        public static (EchelonMark mark, int count) Marking(Echelon echelon)
        {
            switch (echelon)
            {
                case Echelon.FireTeam: return (EchelonMark.Ring, 1);
                case Echelon.Squad: return (EchelonMark.Dot, 1);
                case Echelon.Section: return (EchelonMark.Dot, 2);
                case Echelon.Platoon: return (EchelonMark.Dot, 3);
                case Echelon.Detachment: return (EchelonMark.Dot, 4);
                case Echelon.Company: return (EchelonMark.Bar, 1);
                case Echelon.Battalion: return (EchelonMark.Bar, 2);
                case Echelon.Regiment: return (EchelonMark.Bar, 3);
                case Echelon.Brigade: return (EchelonMark.Cross, 1);
                case Echelon.Division: return (EchelonMark.Cross, 2);
                case Echelon.Corps: return (EchelonMark.Cross, 3);
                case Echelon.Army: return (EchelonMark.Cross, 4);
                case Echelon.ArmyGroup: return (EchelonMark.Cross, 5);
                case Echelon.Theater: return (EchelonMark.Cross, 6);
                default: throw new ArgumentOutOfRangeException(nameof(echelon));
            }
        }

        private static int Index(Echelon echelon)
        {
            int i = (int)echelon;
            if (i < 0 || i >= Strength.Length)
                throw new ArgumentOutOfRangeException(nameof(echelon), echelon, "Unknown echelon");
            return i;
        }
    }

    /// <summary>The glyph repeated to form an echelon marking.</summary>
    public enum EchelonMark
    {
        /// <summary>A small open circle (fire team).</summary>
        Ring,
        /// <summary>A filled dot (squad through platoon).</summary>
        Dot,
        /// <summary>A vertical bar (company through regiment).</summary>
        Bar,
        /// <summary>An X (brigade and above).</summary>
        Cross,
    }
}
