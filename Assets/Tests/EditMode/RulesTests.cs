using System.Linq;
using NUnit.Framework;

namespace Tactix.Core.Tests
{
    public class RulesTests
    {
        // ---------- Movement ----------

        [Test]
        public void Infantry_OnOpenBoard_ReachesAllTilesWithinChebyshev2()
        {
            var state = TestBoards.OpenBoard(5, 5).WithUnit(0, 0, UnitType.Infantry, 2, 2);
            var targets = TestBoards.MoveTargets(state, 0);
            Assert.AreEqual(24, targets.Count); // full 5x5 minus own tile
            Assert.IsFalse(targets.Contains((2, 2)));
        }

        [Test]
        public void Artillery_HasMoveRange1()
        {
            var state = TestBoards.OpenBoard(5, 5).WithUnit(0, 0, UnitType.Artillery, 2, 2);
            var targets = TestBoards.MoveTargets(state, 0);
            Assert.AreEqual(8, targets.Count); // the 8 neighbors
        }

        [Test]
        public void Recon_HasMoveRange4()
        {
            var state = TestBoards.OpenBoard(9, 9).WithUnit(0, 0, UnitType.Recon, 4, 4);
            var targets = TestBoards.MoveTargets(state, 0);
            Assert.AreEqual(80, targets.Count); // full 9x9 minus own tile
        }

        [Test]
        public void Movement_BlockedByImpassable_NoPathAround()
        {
            var state = TestBoards.OpenBoard(5, 5)
                .WithTerrain(TerrainType.Impassable, (1, 0), (1, 1), (0, 1))
                .WithUnit(0, 0, UnitType.Infantry, 0, 0);
            Assert.IsEmpty(TestBoards.MoveTargets(state, 0));
        }

        [Test]
        public void Movement_ImpassableIsNeverATarget_ButCanBeRouted_Around()
        {
            // Wall with a gap: infantry can route around within range 2.
            var state = TestBoards.OpenBoard(5, 5)
                .WithTerrain(TerrainType.Impassable, (1, 1))
                .WithUnit(0, 0, UnitType.Infantry, 0, 1);
            var targets = TestBoards.MoveTargets(state, 0);
            Assert.IsFalse(targets.Contains((1, 1)));
            Assert.IsTrue(targets.Contains((2, 1))); // reachable via (1,0) or (1,2) diagonal-ish path
        }

        [Test]
        public void Movement_CanPassThroughFriendly_ButNotStopOnThem()
        {
            var state = TestBoards.OpenBoard(5, 1)
                .WithUnit(0, 0, UnitType.Infantry, 0, 0)
                .WithUnit(1, 0, UnitType.Infantry, 1, 0);

            var targets = TestBoards.MoveTargets(state, 0);
            Assert.IsFalse(targets.Contains((1, 0))); // occupied by friendly
            Assert.IsTrue(targets.Contains((2, 0)));  // reached through the friendly
        }

        [Test]
        public void Movement_EnemiesBlockPathing()
        {
            var state = TestBoards.OpenBoard(3, 1)
                .WithUnit(0, 0, UnitType.Infantry, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 1, 0);
            var targets = TestBoards.MoveTargets(state, 0);
            Assert.IsEmpty(targets); // enemy occupies the only corridor
        }

        [Test]
        public void Movement_ForestCostsSameAsOpen()
        {
            var state = TestBoards.OpenBoard(5, 1)
                .WithTerrain(TerrainType.Forest, (1, 0))
                .WithUnit(0, 0, UnitType.Infantry, 0, 0);
            var targets = TestBoards.MoveTargets(state, 0);
            Assert.IsTrue(targets.Contains((1, 0)));
            Assert.IsTrue(targets.Contains((2, 0)));
        }

        [Test]
        public void Movement_IllegalForWrongOwner_MovedUnit_AndAttackPhase()
        {
            var state = TestBoards.OpenBoard(5, 5)
                .WithUnit(0, 0, UnitType.Infantry, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 4, 4);

            Assert.IsEmpty(Rules.GetLegalMoves(state, 1)); // not their turn

            var moved = Rules.Apply(state, new MoveAction { UnitId = 0, TargetX = 1, TargetY = 1 });
            Assert.IsEmpty(Rules.GetLegalMoves(moved, 0)); // already moved

            var inAttackPhase = state.Clone();
            inAttackPhase.TurnPhase = TurnPhase.Attack;
            Assert.IsEmpty(Rules.GetLegalMoves(inAttackPhase, 0)); // no movement after attacking
        }

        // ---------- Attacks ----------

        [Test]
        public void Infantry_AttacksAll8Neighbors_NotRange2()
        {
            var state = TestBoards.OpenBoard(5, 5)
                .WithUnit(0, 0, UnitType.Infantry, 2, 2)
                .WithUnit(1, 1, UnitType.Infantry, 3, 3)  // diagonal, range 1
                .WithUnit(2, 1, UnitType.Infantry, 2, 4); // range 2
            var targets = TestBoards.AttackTargets(state, 0);
            CollectionAssert.AreEquivalent(new[] { 1 }, targets);
        }

        [Test]
        public void Artillery_Attack_RespectsRange3_AndLineOfSight()
        {
            var state = TestBoards.OpenBoard(8, 1)
                .WithUnit(0, 0, UnitType.Artillery, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 3, 0)  // in range, clear
                .WithUnit(2, 1, UnitType.Infantry, 7, 0); // out of range
            CollectionAssert.AreEquivalent(new[] { 1 }, TestBoards.AttackTargets(state, 0));

            var blocked = TestBoards.OpenBoard(8, 1)
                .WithTerrain(TerrainType.Forest, (1, 0))
                .WithUnit(0, 0, UnitType.Artillery, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 3, 0);
            Assert.IsEmpty(TestBoards.AttackTargets(blocked, 0));

            var blockedByRock = TestBoards.OpenBoard(8, 1)
                .WithTerrain(TerrainType.Impassable, (2, 0))
                .WithUnit(0, 0, UnitType.Artillery, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 3, 0);
            Assert.IsEmpty(TestBoards.AttackTargets(blockedByRock, 0));
        }

        [Test]
        public void Artillery_TargetInForest_IsVisible_ForestOnlyBlocksInBetween()
        {
            var state = TestBoards.OpenBoard(8, 1)
                .WithTerrain(TerrainType.Forest, (3, 0))
                .WithUnit(0, 0, UnitType.Artillery, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 3, 0); // stands in the forest
            CollectionAssert.AreEquivalent(new[] { 1 }, TestBoards.AttackTargets(state, 0));
        }

        [Test]
        public void LineOfSight_CornerTouch_DoesNotBlock_InteriorCrossing_Does()
        {
            // Diagonal shot (0,0)->(2,2): corners of (1,0)/(0,1) are only touched,
            // but (1,1) is crossed through its interior.
            var cornerOnly = TestBoards.OpenBoard(4, 4)
                .WithTerrain(TerrainType.Forest, (1, 0), (0, 1))
                .WithUnit(0, 0, UnitType.Artillery, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 2, 2);
            CollectionAssert.AreEquivalent(new[] { 1 }, TestBoards.AttackTargets(cornerOnly, 0));

            var interior = TestBoards.OpenBoard(4, 4)
                .WithTerrain(TerrainType.Forest, (1, 1))
                .WithUnit(0, 0, UnitType.Artillery, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 2, 2);
            Assert.IsEmpty(TestBoards.AttackTargets(interior, 0));
        }

        [Test]
        public void LineOfSight_IsSymmetric()
        {
            var state = TestBoards.OpenBoard(5, 5)
                .WithTerrain(TerrainType.Forest, (2, 1), (1, 2));
            for (int x0 = 0; x0 < 5; x0++)
                for (int y0 = 0; y0 < 5; y0++)
                    for (int x1 = 0; x1 < 5; x1++)
                        for (int y1 = 0; y1 < 5; y1++)
                            Assert.AreEqual(
                                LineOfSight.HasLineOfSight(state, x0, y0, x1, y1),
                                LineOfSight.HasLineOfSight(state, x1, y1, x0, y0),
                                $"asymmetric LOS between ({x0},{y0}) and ({x1},{y1})");
        }

        [Test]
        public void Attack_DamageAndForestDefense()
        {
            var state = TestBoards.OpenBoard(3, 1)
                .WithUnit(0, 0, UnitType.Infantry, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 1, 0);
            var after = Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 1 });
            Assert.AreEqual(3, after.GetUnit(1).Hp); // 5 - 2

            var inForest = TestBoards.OpenBoard(3, 1)
                .WithTerrain(TerrainType.Forest, (1, 0))
                .WithUnit(0, 0, UnitType.Infantry, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 1, 0);
            var afterForest = Rules.Apply(inForest, new AttackAction { UnitId = 0, TargetUnitId = 1 });
            Assert.AreEqual(4, afterForest.GetUnit(1).Hp); // 5 - (2 - 1)
        }

        [Test]
        public void Attack_SwitchesPhase_KillRemovesUnit_LastKillWins()
        {
            var state = TestBoards.OpenBoard(4, 1)
                .WithUnit(0, 0, UnitType.Artillery, 0, 0)
                .WithUnit(1, 0, UnitType.Infantry, 1, 0)
                .WithUnit(2, 1, UnitType.Artillery, 3, 0); // hp 3, dies to pow 3

            var after = Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 2 });
            Assert.AreEqual(TurnPhase.Attack, after.TurnPhase);
            Assert.IsNull(after.GetUnit(2));
            Assert.AreEqual(0, after.Winner);
            Assert.IsEmpty(Rules.GetAllLegalActions(after)); // game over: nothing legal
        }

        [Test]
        public void Attack_NoCounterattack()
        {
            var state = TestBoards.OpenBoard(3, 1)
                .WithUnit(0, 0, UnitType.Infantry, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 1, 0);
            var after = Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 1 });
            Assert.AreEqual(5, after.GetUnit(0).Hp); // attacker untouched
        }

        [Test]
        public void Attack_GrantsXp_MoreForKills()
        {
            var state = TestBoards.OpenBoard(4, 1)
                .WithUnit(0, 0, UnitType.Armor, 0, 0)
                .WithUnit(1, 1, UnitType.Armor, 1, 0)      // survives (8 hp - 4)
                .WithUnit(2, 1, UnitType.Recon, 3, 0);

            var after = Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 1 });
            Assert.AreEqual(1, after.GetUnit(0).Xp); // hit, no kill

            var killState = TestBoards.OpenBoard(4, 1)
                .WithUnit(0, 0, UnitType.Armor, 0, 0)
                .WithUnit(1, 1, UnitType.Recon, 1, 0)      // dies (3 hp vs pow 4)
                .WithUnit(2, 1, UnitType.Armor, 3, 0);
            var afterKill = Rules.Apply(killState, new AttackAction { UnitId = 0, TargetUnitId = 1 });
            Assert.AreEqual(3, afterKill.GetUnit(0).Xp); // +1 attack, +2 kill
            Assert.IsNull(afterKill.GetUnit(1));
        }

        [Test]
        public void EndTurn_FlipsPlayer_ResetsFlags_IncrementsTurn()
        {
            var state = TestBoards.OpenBoard(5, 5)
                .WithUnit(0, 0, UnitType.Infantry, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 4, 4);
            var s1 = Rules.Apply(state, new MoveAction { UnitId = 0, TargetX = 1, TargetY = 1 });
            var s2 = Rules.Apply(s1, new EndTurnAction());

            Assert.AreEqual(1, s2.CurrentPlayer);
            Assert.AreEqual(TurnPhase.Move, s2.TurnPhase);
            Assert.AreEqual(2, s2.TurnNumber);
            Assert.IsFalse(s2.GetUnit(0).HasMoved);
            Assert.IsNotEmpty(Rules.GetLegalMoves(s2, 1));
        }

        [Test]
        public void Apply_ThrowsOnIllegal_AndDoesNotMutateInput()
        {
            var state = TestBoards.OpenBoard(5, 5)
                .WithUnit(0, 0, UnitType.Infantry, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 4, 4);
            string before = state.ToJson();

            Assert.Throws<IllegalActionException>(() =>
                Rules.Apply(state, new MoveAction { UnitId = 0, TargetX = 4, TargetY = 4 })); // out of range + occupied
            Assert.Throws<IllegalActionException>(() =>
                Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 1 })); // out of range
            Assert.Throws<IllegalActionException>(() =>
                Rules.Apply(state, new MoveAction { UnitId = 1, TargetX = 3, TargetY = 3 })); // not their turn

            var legalNext = Rules.Apply(state, new MoveAction { UnitId = 0, TargetX = 1, TargetY = 1 });
            Assert.AreEqual(before, state.ToJson()); // input state untouched
            Assert.AreEqual((1, 1), (legalNext.GetUnit(0).X, legalNext.GetUnit(0).Y));
        }

        // ---------- Elevation (topographic layer) ----------

        [Test]
        public void Movement_CliffsBlock_StepsOfOneClimb()
        {
            var state = TestBoards.OpenBoard(5, 1)
                .WithElevation(2, (1, 0))
                .WithUnit(0, 0, UnitType.Infantry, 0, 0);
            var targets = TestBoards.MoveTargets(state, 0);
            Assert.IsEmpty(targets); // 0 -> 2 is a cliff and there is no way around

            var ramp = TestBoards.OpenBoard(5, 1)
                .WithElevation(1, (1, 0))
                .WithElevation(2, (2, 0))
                .WithUnit(0, 0, UnitType.Infantry, 0, 0);
            var rampTargets = TestBoards.MoveTargets(ramp, 0);
            Assert.IsTrue(rampTargets.Contains((1, 0))); // climb 0 -> 1
            Assert.IsTrue(rampTargets.Contains((2, 0))); // then 1 -> 2
        }

        [Test]
        public void Attack_HighGround_AddsOneDamage_OnlyDownhill()
        {
            var downhill = TestBoards.OpenBoard(3, 1)
                .WithElevation(1, (0, 0))
                .WithUnit(0, 0, UnitType.Infantry, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 1, 0);
            var after = Rules.Apply(downhill, new AttackAction { UnitId = 0, TargetUnitId = 1 });
            Assert.AreEqual(2, after.GetUnit(1).Hp); // 5 - (2 + 1)

            var uphill = TestBoards.OpenBoard(3, 1)
                .WithElevation(1, (1, 0))
                .WithUnit(0, 0, UnitType.Infantry, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 1, 0);
            var afterUp = Rules.Apply(uphill, new AttackAction { UnitId = 0, TargetUnitId = 1 });
            Assert.AreEqual(3, afterUp.GetUnit(1).Hp); // 5 - 2, no bonus attacking up
        }

        [Test]
        public void LineOfSight_HillShootsOverForest_HillBlocksValleyShot()
        {
            // Artillery on a hill (elev 2) fires over a valley forest that would
            // block a flat shot.
            var fromHill = TestBoards.OpenBoard(4, 1)
                .WithElevation(2, (0, 0))
                .WithTerrain(TerrainType.Forest, (1, 0))
                .WithUnit(0, 0, UnitType.Artillery, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 3, 0);
            CollectionAssert.AreEquivalent(new[] { 1 }, TestBoards.AttackTargets(fromHill, 0));

            var flat = TestBoards.OpenBoard(4, 1)
                .WithTerrain(TerrainType.Forest, (1, 0))
                .WithUnit(0, 0, UnitType.Artillery, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 3, 0);
            Assert.IsEmpty(TestBoards.AttackTargets(flat, 0));

            // An open elev-2 hill between two valley units blocks the shot.
            var ridge = TestBoards.OpenBoard(4, 1)
                .WithElevation(2, (2, 0))
                .WithUnit(0, 0, UnitType.Artillery, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 3, 0);
            Assert.IsEmpty(TestBoards.AttackTargets(ridge, 0));
        }

        // ---------- Level config ----------

        [Test]
        public void StandardLevel_IsRotationallySymmetric()
        {
            var state = LevelConfig.CreateStandardGame();
            int w = state.Width, h = state.Height;
            Assert.AreEqual(16, w);
            Assert.AreEqual(16, h);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Assert.AreEqual(state.TerrainAt(x, y), state.TerrainAt(w - 1 - x, h - 1 - y),
                        $"terrain not symmetric at ({x},{y})");
                    Assert.AreEqual(state.ElevationAt(x, y), state.ElevationAt(w - 1 - x, h - 1 - y),
                        $"elevation not symmetric at ({x},{y})");
                }

            foreach (var unit in state.Units)
            {
                var mirror = state.GetUnitAt(w - 1 - unit.X, h - 1 - unit.Y);
                Assert.IsNotNull(mirror, $"no mirrored unit for {unit.Id}");
                Assert.AreEqual(unit.Type, mirror.Type);
                Assert.AreEqual(1 - unit.Owner, mirror.Owner);
            }

            Assert.AreEqual(16, state.Units.Count);
            foreach (int owner in new[] { 0, 1 })
            {
                Assert.AreEqual(2, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.Infantry));
                Assert.AreEqual(2, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.MechInfantry));
                Assert.AreEqual(1, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.Armor));
                Assert.AreEqual(2, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.Artillery));
                Assert.AreEqual(1, state.Units.Count(u => u.Owner == owner && u.Type == UnitType.Recon));
            }
        }

        [Test]
        public void StandardLevel_UnitsStartOnOpenOrForest_NeverImpassable()
        {
            var state = LevelConfig.CreateStandardGame();
            foreach (var unit in state.Units)
            {
                Assert.AreNotEqual(TerrainType.Impassable, state.TerrainAt(unit.X, unit.Y));
                Assert.IsFalse(state.Units.Any(o => o != unit && o.X == unit.X && o.Y == unit.Y),
                    $"units stacked at ({unit.X},{unit.Y})");
            }
        }

        [Test]
        public void StandardLevel_SpawnsAreMutuallyReachable_DespiteCliffs()
        {
            // Flood fill with the movement rule (8-dir, no impassable, |Δelev| <= 1)
            // from player 0's spawn must reach player 1's spawn.
            var state = LevelConfig.CreateStandardGame();
            var start = (x: state.Units[0].X, y: state.Units[0].Y);
            var goalUnit = state.Units.First(u => u.Owner == 1);
            var goal = (x: goalUnit.X, y: goalUnit.Y);

            var seen = new System.Collections.Generic.HashSet<(int, int)> { start };
            var queue = new System.Collections.Generic.Queue<(int x, int y)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (!state.IsInBounds(nx, ny) || seen.Contains((nx, ny))) continue;
                        if (state.TerrainAt(nx, ny) == TerrainType.Impassable) continue;
                        if (System.Math.Abs(state.ElevationAt(nx, ny) - state.ElevationAt(cx, cy)) > 1) continue;
                        seen.Add((nx, ny));
                        queue.Enqueue((nx, ny));
                    }
            }
            Assert.IsTrue(seen.Contains(goal), "player spawns are not connected under the cliff rule");
        }

        // ---------- Serialization ----------

        [Test]
        public void GameState_JsonRoundTrip_IsLossless()
        {
            var state = LevelConfig.CreateStandardGame();
            state = Rules.Apply(state, Rules.GetAllLegalActions(state).First());
            string json = state.ToJson();
            var restored = GameState.FromJson(json);
            Assert.AreEqual(json, restored.ToJson());
            StringAssert.Contains("\"infantry\"", json);
            StringAssert.Contains("\"mechInfantry\"", json);
            StringAssert.Contains("\"xp\"", json);
            StringAssert.Contains("\"elevation\"", json);
            StringAssert.Contains("\"turnPhase\"", json);
            Assert.AreEqual(restored.ElevationAt(7, 8), state.ElevationAt(7, 8));
        }

        [Test]
        public void GameAction_PolymorphicJsonRoundTrip()
        {
            GameAction[] actions =
            {
                new MoveAction { UnitId = 3, TargetX = 2, TargetY = 5 },
                new AttackAction { UnitId = 1, TargetUnitId = 6 },
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
        }
    }
}

