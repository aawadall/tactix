using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Tactix.Core.Tests
{
    public class RulesTests
    {
        // ---------- movement: continuous dash ----------

        [Test]
        public void Move_AnyPointWithinRange_IsLegal_BeyondRangeIsNot()
        {
            var state = TestBoards.OpenBoard(12, 12).WithUnit(0, 0, UnitType.Infantry, 5, 5); // move 3.0

            Assert.IsTrue(Rules.IsLegalMoveTarget(state, 0, 7.4, 6.1));   // 2.6 away
            Assert.IsTrue(Rules.IsLegalMoveTarget(state, 0, 5.03, 5.02)); // tiny nudge is a real move
            Assert.IsFalse(Rules.IsLegalMoveTarget(state, 0, 9.0, 5.0));  // 4.0 away
            Assert.IsFalse(Rules.IsLegalMoveTarget(state, 0, 5.0, 5.0));  // no-op
        }

        [Test]
        public void Move_OffBoard_OrOntoImpassable_IsIllegal()
        {
            var state = TestBoards.OpenBoard(12, 12)
                .WithTerrain(TerrainType.Impassable, (7, 5))
                .WithUnit(0, 0, UnitType.Infantry, 5, 5);

            Assert.IsFalse(Rules.IsLegalMoveTarget(state, 0, 7.0, 5.0));   // into the rock
            Assert.IsFalse(Rules.IsLegalMoveTarget(state, 0, -0.9, 5.0));  // off the west edge
            Assert.IsTrue(Rules.IsLegalMoveTarget(state, 0, 5.0, 7.5));    // clear ground
        }

        [Test]
        public void Move_StraightLine_MustBeClear_ObstacleShadowsThePointBehindIt()
        {
            var state = TestBoards.OpenBoard(12, 3)
                .WithTerrain(TerrainType.Impassable, (6, 1))
                .WithUnit(0, 0, UnitType.Infantry, 5, 1);

            // Directly behind the rock is in range but not on a clear line.
            Assert.IsFalse(Rules.IsLegalMoveTarget(state, 0, 7.5, 1.0));
            // A grazing line that clips the rock's tile is blocked too.
            Assert.IsFalse(Rules.IsLegalMoveTarget(state, 0, 6.5, 2.0));
            // Sidestepping clear of the rock's tile is fine.
            Assert.IsTrue(Rules.IsLegalMoveTarget(state, 0, 5.6, 2.3));
        }

        [Test]
        public void Move_CliffsBlockThePath_RampsAllowIt()
        {
            var cliff = TestBoards.OpenBoard(12, 3)
                .WithElevation(2, (6, 1))
                .WithUnit(0, 0, UnitType.Infantry, 5, 1);
            Assert.IsFalse(Rules.IsLegalMoveTarget(cliff, 0, 6.0, 1.0)); // 0 -> 2 is a cliff

            var ramp = TestBoards.OpenBoard(12, 3)
                .WithElevation(1, (6, 1))
                .WithElevation(2, (7, 1))
                .WithUnit(0, 0, UnitType.Infantry, 5, 1);
            Assert.IsTrue(Rules.IsLegalMoveTarget(ramp, 0, 7.0, 1.0)); // climbs 0 -> 1 -> 2
        }

        [Test]
        public void Move_DestinationMustNotOverlapAnotherUnit()
        {
            var state = TestBoards.OpenBoard(12, 12)
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 0, UnitType.Infantry, 7, 5);  // friendly, radius 0.35 each

            Assert.IsFalse(Rules.IsLegalMoveTarget(state, 0, 7.0, 5.0));  // exactly on top
            Assert.IsFalse(Rules.IsLegalMoveTarget(state, 0, 6.75, 5.0)); // 0.25 apart < 0.7
            Assert.IsTrue(Rules.IsLegalMoveTarget(state, 0, 6.2, 5.0));   // 0.8 apart
        }

        [Test]
        public void Move_IllegalForWrongOwner_MovedUnit_AndAttackPhase()
        {
            var state = TestBoards.OpenBoard(12, 12)
                .WithUnit(0, 0, UnitType.Infantry, 3, 3)
                .WithUnit(1, 1, UnitType.Infantry, 9, 9);

            Assert.IsFalse(Rules.IsLegalMoveTarget(state, 1, 8.0, 8.0)); // not their turn

            var moved = Rules.Apply(state, TestBoards.Move(0, 4.0, 4.0));
            Assert.IsFalse(Rules.IsLegalMoveTarget(moved, 0, 5.0, 4.0)); // already moved

            var attackPhase = state.Clone();
            attackPhase.TurnPhase = TurnPhase.Attack;
            Assert.IsFalse(Rules.IsLegalMoveTarget(attackPhase, 0, 4.0, 4.0)); // no movement after attacking
        }

        [Test]
        public void MoveRegion_IsBoundedByRange_AndByTerrain()
        {
            var open = TestBoards.OpenBoard(20, 20).WithUnit(0, 0, UnitType.Infantry, 10, 10);
            var reach = Rules.GetMoveRegion(open, 0);
            Assert.AreEqual(Rules.MoveRegionRays, reach.Length);
            foreach (double r in reach) Assert.AreEqual(3.0, r, 1e-6); // open ground: full range everywhere

            var walled = TestBoards.OpenBoard(20, 20)
                .WithTerrain(TerrainType.Impassable, (12, 10))
                .WithUnit(0, 0, UnitType.Infantry, 10, 10);
            var walledReach = Rules.GetMoveRegion(walled, 0);
            Assert.Less(walledReach[0], 1.6); // due east is stopped at the rock's near edge
            Assert.AreEqual(3.0, walledReach[Rules.MoveRegionRays / 2], 1e-6); // due west is clear
        }

        [Test]
        public void ProjectMove_ClampsRequestOntoTheLegalRegion()
        {
            var state = TestBoards.OpenBoard(20, 20).WithUnit(0, 0, UnitType.Infantry, 10, 10);

            Assert.IsTrue(Rules.ProjectMove(state, 0, 20.0, 10.0, out double x, out double y));
            Assert.AreEqual(13.0, x, 1e-3); // clamped to the 3.0 move range
            Assert.AreEqual(10.0, y, 1e-3);
            Assert.IsTrue(Rules.IsLegalMoveTarget(state, 0, x, y));

            var blocked = TestBoards.OpenBoard(20, 20)
                .WithTerrain(TerrainType.Impassable, (12, 10))
                .WithUnit(0, 0, UnitType.Infantry, 10, 10);
            Assert.IsTrue(Rules.ProjectMove(blocked, 0, 20.0, 10.0, out double bx, out double by));
            Assert.Less(bx, 11.55); // stopped short of the rock
            Assert.IsTrue(Rules.IsLegalMoveTarget(blocked, 0, bx, by));
        }

        [Test]
        public void SampledMoves_AreAlwaysLegal()
        {
            var state = TestBoards.OpenBoard(20, 20)
                .WithTerrain(TerrainType.Impassable, (11, 10), (11, 11), (9, 9))
                .WithElevation(3, (12, 12), (12, 13))
                .WithUnit(0, 0, UnitType.MechInfantry, 10, 10)
                .WithUnit(1, 0, UnitType.Armor, 8, 10);

            var rng = new Random(42);
            var samples = Rules.SampleLegalMoves(state, 0, 200, rng);
            Assert.IsNotEmpty(samples);
            foreach (var move in samples)
            {
                Assert.IsTrue(Rules.IsLegalMoveTarget(state, 0, move.TargetX, move.TargetY),
                    $"sampler produced an illegal move: {move}");
                Assert.DoesNotThrow(() => Rules.Apply(state, move));
            }
        }

        // ---------- attacks ----------

        [Test]
        public void Attack_UsesEuclideanRange()
        {
            var state = TestBoards.OpenBoard(12, 12)
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)      // range 1.2
                .WithUnit(1, 1, UnitType.Infantry, 5.8, 5.6)  // 0.98 away
                .WithUnit(2, 1, UnitType.Infantry, 6.5, 6.5); // 2.12 away
            CollectionAssert.AreEquivalent(new[] { 1 }, TestBoards.AttackTargets(state, 0));
        }

        [Test]
        public void Artillery_Attack_RespectsRange_AndLineOfSight()
        {
            var clear = TestBoards.OpenBoard(12, 3)
                .WithUnit(0, 0, UnitType.Artillery, 2, 1)     // range 5.0
                .WithUnit(1, 1, UnitType.Infantry, 6, 1)      // 4.0 away, clear
                .WithUnit(2, 1, UnitType.Infantry, 9, 1);     // 7.0 away
            CollectionAssert.AreEquivalent(new[] { 1 }, TestBoards.AttackTargets(clear, 0));

            var throughForest = TestBoards.OpenBoard(12, 3)
                .WithTerrain(TerrainType.Forest, (4, 1))
                .WithUnit(0, 0, UnitType.Artillery, 2, 1)
                .WithUnit(1, 1, UnitType.Infantry, 6, 1);
            Assert.IsEmpty(TestBoards.AttackTargets(throughForest, 0));

            var throughRock = TestBoards.OpenBoard(12, 3)
                .WithTerrain(TerrainType.Impassable, (4, 1))
                .WithUnit(0, 0, UnitType.Artillery, 2, 1)
                .WithUnit(1, 1, UnitType.Infantry, 6, 1);
            Assert.IsEmpty(TestBoards.AttackTargets(throughRock, 0));
        }

        [Test]
        public void Artillery_TargetInForest_IsVisible_ForestOnlyBlocksInBetween()
        {
            var state = TestBoards.OpenBoard(12, 3)
                .WithTerrain(TerrainType.Forest, (6, 1))
                .WithUnit(0, 0, UnitType.Artillery, 2, 1)
                .WithUnit(1, 1, UnitType.Infantry, 6, 1); // standing in the forest
            CollectionAssert.AreEquivalent(new[] { 1 }, TestBoards.AttackTargets(state, 0));
        }

        [Test]
        public void LineOfSight_IsSymmetric_AcrossFractionalPositions()
        {
            var state = TestBoards.OpenBoard(8, 8)
                .WithTerrain(TerrainType.Forest, (3, 2), (2, 3))
                .WithElevation(2, (5, 5));

            var points = new[] { 0.0, 1.25, 2.5, 3.75, 5.0, 6.5, 7.0 };
            foreach (double x0 in points)
                foreach (double y0 in points)
                    foreach (double x1 in points)
                        foreach (double y1 in points)
                            Assert.AreEqual(
                                LineOfSight.HasLineOfSight(state, x0, y0, x1, y1),
                                LineOfSight.HasLineOfSight(state, x1, y1, x0, y0),
                                $"asymmetric LOS between ({x0},{y0}) and ({x1},{y1})");
        }

        [Test]
        public void Attack_DamageForestDefenseAndHighGround()
        {
            var plain = TestBoards.OpenBoard(6, 3)
                .WithUnit(0, 0, UnitType.Infantry, 2, 1)
                .WithUnit(1, 1, UnitType.Infantry, 3, 1);
            Assert.AreEqual(3, Rules.Apply(plain, Attack(0, 1)).GetUnit(1).Hp); // 5 - 2

            var inForest = TestBoards.OpenBoard(6, 3)
                .WithTerrain(TerrainType.Forest, (3, 1))
                .WithUnit(0, 0, UnitType.Infantry, 2, 1)
                .WithUnit(1, 1, UnitType.Infantry, 3, 1);
            Assert.AreEqual(4, Rules.Apply(inForest, Attack(0, 1)).GetUnit(1).Hp); // 5 - (2 - 1)

            var downhill = TestBoards.OpenBoard(6, 3)
                .WithElevation(1, (2, 1))
                .WithUnit(0, 0, UnitType.Infantry, 2, 1)
                .WithUnit(1, 1, UnitType.Infantry, 3, 1);
            Assert.AreEqual(2, Rules.Apply(downhill, Attack(0, 1)).GetUnit(1).Hp); // 5 - (2 + 1)

            var uphill = TestBoards.OpenBoard(6, 3)
                .WithElevation(1, (3, 1))
                .WithUnit(0, 0, UnitType.Infantry, 2, 1)
                .WithUnit(1, 1, UnitType.Infantry, 3, 1);
            Assert.AreEqual(3, Rules.Apply(uphill, Attack(0, 1)).GetUnit(1).Hp); // no bonus attacking up
        }

        [Test]
        public void Attack_SwitchesPhase_KillRemovesUnit_LastKillWins()
        {
            var state = TestBoards.OpenBoard(8, 3)
                .WithUnit(0, 0, UnitType.Artillery, 1, 1)
                .WithUnit(1, 0, UnitType.Infantry, 2, 1)
                .WithUnit(2, 1, UnitType.Recon, 5, 1); // hp 3, dies to power 3

            var after = Rules.Apply(state, Attack(0, 2));
            Assert.AreEqual(TurnPhase.Attack, after.TurnPhase);
            Assert.IsNull(after.GetUnit(2));
            Assert.AreEqual(0, after.Winner);
            Assert.IsEmpty(Rules.GetAllLegalActions(after));
        }

        [Test]
        public void Attack_GrantsXp_MoreForKills_AndNeverCounterattacks()
        {
            var state = TestBoards.OpenBoard(8, 3)
                .WithUnit(0, 0, UnitType.Armor, 2, 1)
                .WithUnit(1, 1, UnitType.Armor, 3, 1)   // 8 hp, survives
                .WithUnit(2, 1, UnitType.Recon, 6, 1);
            var after = Rules.Apply(state, Attack(0, 1));
            Assert.AreEqual(1, after.GetUnit(0).Xp);
            Assert.AreEqual(8, after.GetUnit(0).Hp); // attacker untouched

            var kill = TestBoards.OpenBoard(8, 3)
                .WithUnit(0, 0, UnitType.Armor, 2, 1)
                .WithUnit(1, 1, UnitType.Recon, 3, 1)  // 3 hp, dies to power 4
                .WithUnit(2, 1, UnitType.Armor, 6, 1);
            var afterKill = Rules.Apply(kill, Attack(0, 1));
            Assert.AreEqual(3, afterKill.GetUnit(0).Xp); // +1 attack, +2 kill
            Assert.IsNull(afterKill.GetUnit(1));
        }

        [Test]
        public void EndTurn_FlipsPlayer_ResetsFlags_IncrementsTurn()
        {
            var state = TestBoards.OpenBoard(12, 12)
                .WithUnit(0, 0, UnitType.Infantry, 3, 3)
                .WithUnit(1, 1, UnitType.Infantry, 9, 9);
            var moved = Rules.Apply(state, TestBoards.Move(0, 4.0, 4.0));
            var ended = Rules.Apply(moved, new EndTurnAction());

            Assert.AreEqual(1, ended.CurrentPlayer);
            Assert.AreEqual(TurnPhase.Move, ended.TurnPhase);
            Assert.AreEqual(2, ended.TurnNumber);
            Assert.IsFalse(ended.GetUnit(0).HasMoved);
            Assert.IsTrue(Rules.CanMove(ended, 1));
        }

        [Test]
        public void Apply_ThrowsOnIllegal_AndDoesNotMutateInput()
        {
            var state = TestBoards.OpenBoard(12, 12)
                .WithUnit(0, 0, UnitType.Infantry, 3, 3)
                .WithUnit(1, 1, UnitType.Infantry, 9, 9);
            string before = state.ToJson();

            Assert.Throws<IllegalActionException>(() => Rules.Apply(state, TestBoards.Move(0, 9.0, 9.0))); // out of range
            Assert.Throws<IllegalActionException>(() => Rules.Apply(state, Attack(0, 1)));                 // out of range
            Assert.Throws<IllegalActionException>(() => Rules.Apply(state, TestBoards.Move(1, 8.0, 8.0))); // not their turn

            var next = Rules.Apply(state, TestBoards.Move(0, 4.5, 3.0));
            Assert.AreEqual(before, state.ToJson()); // input untouched
            Assert.AreEqual(4.5, next.GetUnit(0).X, 1e-9);
        }

        // ---------- support units ----------

        [Test]
        public void Medic_HealsDismountedOnly_ServiceRepairsVehiclesOnly()
        {
            var state = TestBoards.OpenBoard(12, 6)
                .WithUnit(0, 0, UnitType.Medic, 5, 3)
                .WithUnit(1, 0, UnitType.Infantry, 6, 3, hp: 2)   // dismounted, hurt
                .WithUnit(2, 0, UnitType.Armor, 4, 3, hp: 4)      // vehicle, hurt
                .WithUnit(3, 0, UnitType.Service, 5, 1)
                .WithUnit(4, 0, UnitType.Artillery, 6, 1, hp: 1)  // vehicle, hurt
                .WithUnit(5, 0, UnitType.Recon, 4, 1, hp: 1)      // dismounted, hurt
                .WithUnit(6, 1, UnitType.Infantry, 5.8, 3.8, hp: 1); // enemy

            var medicTargets = Rules.GetLegalHeals(state, 0).Select(h => h.TargetUnitId).ToList();
            CollectionAssert.AreEquivalent(new[] { 1 }, medicTargets); // not the armour, not the enemy

            var serviceTargets = Rules.GetLegalHeals(state, 3).Select(h => h.TargetUnitId).ToList();
            CollectionAssert.AreEquivalent(new[] { 4 }, serviceTargets); // not the recon
        }

        [Test]
        public void Heal_RestoresHp_CappedAtMax_AndSkipsHealthyUnits()
        {
            var state = TestBoards.OpenBoard(12, 6)
                .WithUnit(0, 0, UnitType.Medic, 5, 3)
                .WithUnit(1, 0, UnitType.Infantry, 6, 3, hp: 2)  // 5 max, +2 -> 4
                .WithUnit(2, 0, UnitType.Recon, 4, 3, hp: 2);    // 3 max, +2 caps at 3

            var afterInfantry = Rules.Apply(state, Heal(0, 1));
            Assert.AreEqual(4, afterInfantry.GetUnit(1).Hp);
            Assert.AreEqual(1, afterInfantry.GetUnit(0).Xp);

            var afterRecon = Rules.Apply(state, Heal(0, 2));
            Assert.AreEqual(3, afterRecon.GetUnit(2).Hp); // capped, not 4

            // A unit at full strength is not a legal target.
            var healthy = TestBoards.OpenBoard(12, 6)
                .WithUnit(0, 0, UnitType.Medic, 5, 3)
                .WithUnit(1, 0, UnitType.Infantry, 6, 3);
            Assert.IsEmpty(Rules.GetLegalHeals(healthy, 0));
        }

        [Test]
        public void Support_UsesItsOwnSlot_NeverEndsTheMovementPhase()
        {
            var state = TestBoards.OpenBoard(12, 6)
                .WithUnit(0, 0, UnitType.Medic, 5, 3)
                .WithUnit(1, 0, UnitType.Infantry, 6, 3, hp: 2)
                .WithUnit(2, 1, UnitType.Infantry, 10, 3);

            var after = Rules.Apply(state, Heal(0, 1));
            Assert.AreEqual(TurnPhase.Move, after.TurnPhase);                 // army can still advance
            Assert.IsTrue(Rules.IsLegalMoveTarget(after, 1, 6.0, 4.0));       // the patient too
            Assert.IsTrue(after.GetUnit(0).HasSupported);
            Assert.IsFalse(after.GetUnit(0).HasAttacked);
            Assert.IsTrue(Rules.CanMove(after, 0));                           // medic may still move

            Assert.IsEmpty(Rules.GetLegalHeals(after, 0));                    // but only supports once
            Assert.Throws<IllegalActionException>(() => Rules.Apply(after, Heal(0, 1)));

            var nextTurn = Rules.Apply(Rules.Apply(after, new EndTurnAction()), new EndTurnAction());
            Assert.IsFalse(nextTurn.GetUnit(0).HasSupported);                 // slot refreshes
        }

        [Test]
        public void SupportUnits_AreUnarmed_AndCannotHealThemselves()
        {
            var state = TestBoards.OpenBoard(12, 6)
                .WithUnit(0, 0, UnitType.Medic, 5, 3, hp: 1)
                .WithUnit(1, 0, UnitType.Service, 5.6, 3, hp: 1)
                .WithUnit(2, 1, UnitType.Infantry, 5.5, 3.6);

            Assert.IsEmpty(TestBoards.AttackTargets(state, 0)); // medic cannot attack an adjacent enemy
            Assert.IsEmpty(TestBoards.AttackTargets(state, 1)); // nor can the service company
            Assert.IsEmpty(Rules.GetLegalHeals(state, 0));      // medic can't treat itself or the vehicle
            Assert.IsEmpty(Rules.GetLegalHeals(state, 1));      // service can't repair itself or the medic
        }

        [Test]
        public void Heal_OutOfRange_IsIllegal()
        {
            var state = TestBoards.OpenBoard(12, 6)
                .WithUnit(0, 0, UnitType.Medic, 5, 3)            // support range 1.5
                .WithUnit(1, 0, UnitType.Infantry, 7.5, 3, hp: 1);
            Assert.IsEmpty(Rules.GetLegalHeals(state, 0));
            Assert.Throws<IllegalActionException>(() => Rules.Apply(state, Heal(0, 1)));
        }

        // ---------- level config ----------

        [Test]
        public void StandardLevel_IsRotationallySymmetric()
        {
            var state = LevelConfig.CreateStandardGame();
            int w = state.Width, h = state.Height;
            Assert.AreEqual(24, w);
            Assert.AreEqual(24, h);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Assert.AreEqual(state.TerrainAt(x, y), state.TerrainAt(w - 1 - x, h - 1 - y),
                        $"terrain not symmetric at ({x},{y})");
                    Assert.AreEqual(state.ElevationAt(x, y), state.ElevationAt(w - 1 - x, h - 1 - y),
                        $"elevation not symmetric at ({x},{y})");
                }

            foreach (var unit in state.Units.Where(u => u.Owner == 0))
            {
                var mirror = state.Units.FirstOrDefault(m =>
                    m.Owner == 1 &&
                    Math.Abs(m.X - (w - 1 - unit.X)) < 1e-9 &&
                    Math.Abs(m.Y - (h - 1 - unit.Y)) < 1e-9);
                Assert.IsNotNull(mirror, $"no mirrored unit for {unit.Id}");
                Assert.AreEqual(unit.Type, mirror.Type);
            }

            Assert.AreEqual(30, state.Units.Count);
            foreach (int owner in new[] { 0, 1 })
            {
                Assert.AreEqual(3, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.Infantry));
                Assert.AreEqual(3, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.MechInfantry));
                Assert.AreEqual(2, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.Armor));
                Assert.AreEqual(3, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.Artillery));
                Assert.AreEqual(1, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.Recon));
                Assert.AreEqual(1, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.Medic));
                Assert.AreEqual(1, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.Service));
                Assert.AreEqual(1, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.Headquarters));
            }

            // Both sides must start with identical combat strength.
            Assert.AreEqual(state.StrengthOf(0), state.StrengthOf(1));
            Assert.AreEqual(state.StartingStrength[0], state.StartingStrength[1]);
        }

        [Test]
        public void StandardLevel_UnitsStartLegallyPlaced_AndCanMove()
        {
            var state = LevelConfig.CreateStandardGame();
            foreach (var unit in state.Units)
            {
                Assert.AreNotEqual(TerrainType.Impassable, state.TerrainAtPoint(unit.X, unit.Y),
                    $"unit {unit.Id} starts in a rock");
                Assert.IsTrue(Geometry.IsInsideBoard(state, unit.X, unit.Y), $"unit {unit.Id} starts off board");

                foreach (var other in state.Units)
                {
                    if (other.Id == unit.Id) continue;
                    double separation = Rules.Distance(unit.X, unit.Y, other.X, other.Y);
                    Assert.GreaterOrEqual(separation, unit.Stats.Radius + other.Stats.Radius,
                        $"units {unit.Id} and {other.Id} start overlapping");
                }
            }

            foreach (var unit in state.Units.Where(u => u.Owner == 0))
                Assert.IsTrue(Rules.CanMove(state, unit.Id), $"unit {unit.Id} is boxed in at the start");
        }

        [Test]
        public void StandardLevel_ArmiesCanReachEachOther_DespiteCliffsAndRocks()
        {
            // Coarse reachability sweep at half-tile resolution using the real
            // path rule, from player 0's deployment to player 1's.
            var state = LevelConfig.CreateStandardGame();
            const double step = 0.5;
            var start = (x: Math.Round(state.Units[0].X / step) * step, y: Math.Round(state.Units[0].Y / step) * step);
            var seen = new HashSet<(double, double)> { start };
            var queue = new Queue<(double x, double y)>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        double nx = cx + dx * step, ny = cy + dy * step;
                        if (!Geometry.IsInsideBoard(state, nx, ny) || seen.Contains((nx, ny))) continue;
                        if (state.TerrainAtPoint(nx, ny) == TerrainType.Impassable) continue;
                        if (!Geometry.IsPathWalkable(state, cx, cy, nx, ny)) continue;
                        seen.Add((nx, ny));
                        queue.Enqueue((nx, ny));
                    }
            }

            var enemy = state.Units.First(u => u.Owner == 1);
            var goal = (Math.Round(enemy.X / step) * step, Math.Round(enemy.Y / step) * step);
            Assert.IsTrue(seen.Contains(goal), "player 1's deployment is unreachable from player 0's");
        }

        // ---------- serialization ----------

        [Test]
        public void GameState_JsonRoundTrip_IsLossless()
        {
            var state = LevelConfig.CreateStandardGame();
            state.Ruleset = Ruleset.Deterministic; // so the unit lands exactly where ordered
            state = Rules.Apply(state, TestBoards.Move(0, 7.25, 2.5));
            string json = state.ToJson();
            var restored = GameState.FromJson(json);

            Assert.AreEqual(json, restored.ToJson());
            Assert.AreEqual(7.25, restored.GetUnit(0).X, 1e-12);
            StringAssert.Contains("\"infantry\"", json);
            StringAssert.Contains("\"mechInfantry\"", json);
            StringAssert.Contains("\"xp\"", json);
            StringAssert.Contains("\"elevation\"", json);
            StringAssert.Contains("\"echelon\"", json);
            StringAssert.Contains("\"ruleset\"", json);
        }

        [Test]
        public void GameAction_PolymorphicJsonRoundTrip_PreservesFloatTargets()
        {
            GameAction[] actions =
            {
                TestBoards.Move(3, 12.75, 4.125),
                Attack(1, 6),
                new EndTurnAction(),
            };
            foreach (var action in actions)
            {
                string json = TactixJson.Serialize(action);
                var restored = TactixJson.Deserialize<GameAction>(json);
                Assert.AreEqual(action.GetType(), restored.GetType());
                Assert.AreEqual(json, TactixJson.Serialize(restored));
            }
            StringAssert.Contains("\"actionType\":\"move\"", TactixJson.Serialize(actions[0]));
            StringAssert.Contains("12.75", TactixJson.Serialize(actions[0]));
        }

        private static AttackAction Attack(int unitId, int targetId) =>
            new AttackAction { UnitId = unitId, TargetUnitId = targetId };

        private static HealAction Heal(int unitId, int targetId) =>
            new HealAction { UnitId = unitId, TargetUnitId = targetId };
    }
}
