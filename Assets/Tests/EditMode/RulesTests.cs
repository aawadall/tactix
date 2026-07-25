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
        public void Ranged_HasMoveRange1()
        {
            var state = TestBoards.OpenBoard(5, 5).WithUnit(0, 0, UnitType.Ranged, 2, 2);
            var targets = TestBoards.MoveTargets(state, 0);
            Assert.AreEqual(8, targets.Count); // the 8 neighbors
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
        public void Ranged_Attack_RespectsRange3_AndLineOfSight()
        {
            var state = TestBoards.OpenBoard(8, 1)
                .WithUnit(0, 0, UnitType.Ranged, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 3, 0)  // in range, clear
                .WithUnit(2, 1, UnitType.Infantry, 7, 0); // out of range
            CollectionAssert.AreEquivalent(new[] { 1 }, TestBoards.AttackTargets(state, 0));

            var blocked = TestBoards.OpenBoard(8, 1)
                .WithTerrain(TerrainType.Forest, (1, 0))
                .WithUnit(0, 0, UnitType.Ranged, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 3, 0);
            Assert.IsEmpty(TestBoards.AttackTargets(blocked, 0));

            var blockedByRock = TestBoards.OpenBoard(8, 1)
                .WithTerrain(TerrainType.Impassable, (2, 0))
                .WithUnit(0, 0, UnitType.Ranged, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 3, 0);
            Assert.IsEmpty(TestBoards.AttackTargets(blockedByRock, 0));
        }

        [Test]
        public void Ranged_TargetInForest_IsVisible_ForestOnlyBlocksInBetween()
        {
            var state = TestBoards.OpenBoard(8, 1)
                .WithTerrain(TerrainType.Forest, (3, 0))
                .WithUnit(0, 0, UnitType.Ranged, 0, 0)
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
                .WithUnit(0, 0, UnitType.Ranged, 0, 0)
                .WithUnit(1, 1, UnitType.Infantry, 2, 2);
            CollectionAssert.AreEquivalent(new[] { 1 }, TestBoards.AttackTargets(cornerOnly, 0));

            var interior = TestBoards.OpenBoard(4, 4)
                .WithTerrain(TerrainType.Forest, (1, 1))
                .WithUnit(0, 0, UnitType.Ranged, 0, 0)
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
                .WithUnit(0, 0, UnitType.Ranged, 0, 0)
                .WithUnit(1, 0, UnitType.Infantry, 1, 0)
                .WithUnit(2, 1, UnitType.Ranged, 3, 0); // hp 3, dies to pow 3

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

        // ---------- Level config ----------

        [Test]
        public void StandardLevel_IsRotationallySymmetric()
        {
            var state = LevelConfig.CreateStandardGame();
            int w = state.Width, h = state.Height;
            Assert.AreEqual(8, w);
            Assert.AreEqual(8, h);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Assert.AreEqual(state.TerrainAt(x, y), state.TerrainAt(w - 1 - x, h - 1 - y),
                        $"terrain not symmetric at ({x},{y})");

            foreach (var unit in state.Units)
            {
                var mirror = state.GetUnitAt(w - 1 - unit.X, h - 1 - unit.Y);
                Assert.IsNotNull(mirror, $"no mirrored unit for {unit.Id}");
                Assert.AreEqual(unit.Type, mirror.Type);
                Assert.AreEqual(1 - unit.Owner, mirror.Owner);
            }

            Assert.AreEqual(8, state.Units.Count);
            Assert.AreEqual(2, state.Units.Count(u => u.Owner == 0 && u.Type == UnitType.Infantry));
            Assert.AreEqual(2, state.Units.Count(u => u.Owner == 0 && u.Type == UnitType.Ranged));
        }

        [Test]
        public void StandardLevel_UnitsStartOnOpenOrForest_NeverImpassable()
        {
            var state = LevelConfig.CreateStandardGame();
            foreach (var unit in state.Units)
                Assert.AreNotEqual(TerrainType.Impassable, state.TerrainAt(unit.X, unit.Y));
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
            StringAssert.Contains("\"turnPhase\"", json);
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
