using System.Collections.Generic;
using Tactix.Core;

namespace Tactix.Game
{
    /// <summary>
    /// Content for the Field Manual screen: a small demonstration board per unit
    /// type, plus the written notes shown beside it.
    ///
    /// The board is a real <see cref="GameState"/> and every overlay drawn on it
    /// comes from the rules engine, so the manual cannot drift out of step with
    /// the actual rules — if movement or ranges change, this screen changes with
    /// them.
    /// </summary>
    public static class FieldManual
    {
        public const int BoardWidth = 20;
        public const int BoardHeight = 13;

        /// <summary>The unit being demonstrated is always id 0.</summary>
        public const int ShowcaseUnitId = 0;

        // Kept clear of the firing line east of the subject, so range demos aren't
        // confused by a blocked shot.
        private static readonly (int x, int y)[] ForestPatch =
        {
            (10, 9), (11, 9), (11, 10), (12, 9),
        };

        // Close enough to clip the movement region of anything faster than a gun.
        private static readonly (int x, int y)[] Rocks =
        {
            (5, 8), (6, 8),
        };

        /// <summary>
        /// Builds the demonstration board: open ground around the subject, a
        /// forest patch and rock outcrop to clip its movement, and a ridge to the
        /// west whose far side is a cliff. A sample enemy and a wounded comrade
        /// are placed so the unit's real attack and support options light up.
        /// </summary>
        public static GameState BuildDemoState(UnitType type, Echelon echelon = Echelon.Company)
        {
            var terrain = new TerrainType[BoardHeight][];
            var elevation = new int[BoardHeight][];
            for (int y = 0; y < BoardHeight; y++)
            {
                terrain[y] = new TerrainType[BoardWidth];
                elevation[y] = new int[BoardWidth];
            }

            foreach (var (x, y) in ForestPatch) terrain[y][x] = TerrainType.Forest;
            foreach (var (x, y) in Rocks) terrain[y][x] = TerrainType.Impassable;

            // A ridge west of the subject. The step from x=5 (ground level) up to
            // x=4 is two levels, which makes it a cliff: impassable in both
            // directions, so it clips the movement region hard.
            for (int y = 1; y < BoardHeight - 1; y++)
            {
                elevation[y][4] = 2;
                elevation[y][3] = 2;
                elevation[y][2] = 3;
                elevation[y][1] = 3;
                elevation[y][0] = 3;
            }

            var state = new GameState
            {
                Terrain = terrain,
                Elevation = elevation,
                Units = new List<Unit>(),
                CurrentPlayer = 0,
                TurnPhase = TurnPhase.Move,
                TurnNumber = 1,
            };

            var stats = UnitStats.For(type, echelon);
            AddUnit(state, ShowcaseUnitId, 0, type, echelon, 7.0, 6.0);

            if (stats.CanAttack)
            {
                // Inside reach, so the target ring shows on the demo board.
                AddUnit(state, 1, 1, UnitType.Infantry, Echelon.Company, 7.0 + stats.AttackRange * 0.8, 6.0);
                // Outside reach, to make the range circle's meaning obvious.
                AddUnit(state, 2, 1, UnitType.Infantry, Echelon.Company, 7.0 + stats.AttackRange + 1.6, 6.9);
            }

            if (stats.CanSupport)
            {
                var casualtyType = stats.Supports == SupportTarget.Vehicles
                    ? UnitType.Armor
                    : UnitType.Infantry;
                AddUnit(state, 3, 0, casualtyType, Echelon.Company,
                    7.0 + stats.SupportRange * 0.7, 6.0 + stats.SupportRange * 0.4,
                    hp: 1);
                AddUnit(state, 4, 1, UnitType.Infantry, Echelon.Company, 13.5, 3.2); // a nearby threat
            }

            return state;
        }

        private static void AddUnit(GameState state, int id, int owner, UnitType type, Echelon echelon, double x, double y, int? hp = null)
        {
            state.Units.Add(new Unit
            {
                Id = id,
                Owner = owner,
                Type = type,
                Echelon = echelon,
                X = x,
                Y = y,
                Hp = hp ?? UnitStats.For(type, echelon).MaxHp,
            });
        }

        /// <summary>Role and handling notes shown next to the demonstration.</summary>
        public static string Description(UnitType type)
        {
            switch (type)
            {
                case UnitType.Infantry:
                    return "The line of the army. Slow but durable, and the only common unit " +
                           "that trades well while sitting still.\n\n" +
                           "Fights at arm's length, so it must close the distance to matter. " +
                           "Holds forest well: the +1 defence there cuts an enemy rifle company's " +
                           "damage in half, and turns an armour attack from 4 down to 3.\n\n" +
                           "Use it to take and keep ground. Treated by the Medical Section.";

                case UnitType.MechInfantry:
                    return "Infantry with transport. Same punch and reach as foot infantry, one " +
                           "more hit point, and half again the speed.\n\n" +
                           "The extra 1.5 of movement is the whole point: it arrives where a fight " +
                           "is already happening, rather than walking toward where one might be. " +
                           "Good for taking a flank before the enemy can shift to meet it.\n\n" +
                           "Counts as dismounted, so the Medical Section — not the Service " +
                           "Company — patches it up.";

                case UnitType.Armor:
                    return "The hammer. Double the damage of infantry, the most hit points on the " +
                           "board, and fast enough to choose its fight.\n\n" +
                           "Short-ranged and short-sighted, so it is easily led into an ambush and " +
                           "cannot see far enough to protect itself. Keep infantry or recon in " +
                           "front of it and it will win almost any exchange it enters.\n\n" +
                           "A vehicle: repaired by the Service Company, never by a medic.";

                case UnitType.Artillery:
                    return "Reach at the cost of everything else. Three times the range of a rifle " +
                           "company, but barely mobile and killed by a single solid hit.\n\n" +
                           "Needs line of sight to fire, so terrain and relief dictate where it is " +
                           "useful: a gun on high ground shoots over the forests below it, while a " +
                           "gun in a valley is blind to anything past the nearest ridge.\n\n" +
                           "Position it early — with only 2.0 of movement it cannot reposition " +
                           "once a battle starts. A vehicle; repaired by the Service Company.";

                case UnitType.Recon:
                    return "Eyes and speed. Sees twice as far as anything else and covers 6.0 in a " +
                           "single turn, but hits for 1 and dies to almost anything.\n\n" +
                           "Not a fighting unit. Its job is to find the enemy before your main body " +
                           "walks into them, and to reach ground others cannot get to in time.\n\n" +
                           "Note: sight currently has no mechanical effect — see ROADMAP.md, where " +
                           "artillery spotting and fog of war would both make this stat matter.";

                case UnitType.Medic:
                    return "Keeps the infantry standing. Restores 2 HP to any dismounted unit " +
                           "(infantry, mechanized, recon, or another medic) within 1.5.\n\n" +
                           "Unarmed and fragile, so it needs to sit just behind the line — close " +
                           "enough to reach casualties, far enough not to become one.\n\n" +
                           "Healing uses its own slot: a medic can move and treat in the same turn, " +
                           "and treating someone does not end the movement phase for the rest of " +
                           "your army. It cannot treat itself.";

                case UnitType.Service:
                    return "Keeps the vehicles running. Restores 3 HP — the largest single heal in " +
                           "the game — to armour, artillery, or another service company within 1.2.\n\n" +
                           "The shortest reach and slowest speed of any unit, so it must be " +
                           "positioned deliberately rather than chased after a damaged tank.\n\n" +
                           "Repairing an 8 HP armour company back up twice is worth more than most " +
                           "attacks. Unarmed, and cannot repair itself.";

                default:
                    return string.Empty;
            }
        }

        /// <summary>One-line summary of what the overlays on the demo board mean.</summary>
        public const string OverlayKey =
            "Cyan area = everywhere this unit can move in one turn — for anything faster than a gun it is cut short " +
            "by the rock outcrop and by the cliff along the western ridge.   " +
            "Red circle = attack range, green circle = support range, white circle = sight.   " +
            "Rings mark the units it could act on right now.";
    }
}
