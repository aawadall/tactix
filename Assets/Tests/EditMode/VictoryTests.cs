using System.Linq;
using NUnit.Framework;

namespace Tactix.Core.Tests
{
    /// <summary>
    /// The four ways a game can end, and the scoring that decides the last of
    /// them. A draw is a *finished* game with no winner, which is why
    /// <see cref="GameState.IsOver"/> rather than <c>Winner</c> is the test for
    /// whether play continues.
    /// </summary>
    public class VictoryTests
    {
        // ---------- objectives and scoring ----------

        [Test]
        public void HoldingAnObjective_EarnsItsValueEachTurn()
        {
            var state = Board()
                .WithObjective(0, 5, 5, radius: 2, value: 3)
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 18, 18);

            var afterFirst = Rules.Apply(state, new EndTurnAction());
            Assert.AreEqual(3, afterFirst.Score[0]);
            Assert.AreEqual(0, afterFirst.Score[1]);
            Assert.AreEqual(0, afterFirst.Objectives[0].ControlledBy);

            // Player 1 ends their turn holding nothing, then player 0 scores again.
            var afterSecond = Rules.Apply(afterFirst, new EndTurnAction());
            Assert.AreEqual(3, afterSecond.Score[0], "player 1 should not earn player 0's ground");
            var afterThird = Rules.Apply(afterSecond, new EndTurnAction());
            Assert.AreEqual(6, afterThird.Score[0], "holding should pay every turn");
        }

        [Test]
        public void ControlPersistsWhenVacated_ButNotWhenContested()
        {
            var state = Board()
                .WithObjective(0, 5, 5, radius: 2, value: 2)
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 18, 18);

            var held = Rules.Apply(state, new EndTurnAction());
            Assert.AreEqual(0, held.Objectives[0].ControlledBy);

            // Walking away does not surrender the ground.
            held.GetUnit(0).X = 15;
            Rules.UpdateObjectiveControl(held);
            Assert.AreEqual(0, held.Objectives[0].ControlledBy);
            Assert.IsFalse(held.Objectives[0].Contested);

            // But both sides present freezes it, and pays nobody.
            held.GetUnit(0).X = 5;
            held.GetUnit(1).X = 5.8;
            held.GetUnit(1).Y = 5;
            Rules.UpdateObjectiveControl(held);
            Assert.IsTrue(held.Objectives[0].Contested);

            int before = held.Score[0];
            var contested = Rules.Apply(held, new EndTurnAction());
            Assert.AreEqual(before, contested.Score[0], "a contested objective should pay nobody");
        }

        [Test]
        public void DestroyingAFormation_ScoresItsStrength()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Armor, 5, 5)                                  // power 4
                .WithUnit(1, 1, UnitType.Recon, 6, 5, echelon: Echelon.Platoon)        // 1 hp, dies
                .WithUnit(2, 1, UnitType.Infantry, 18, 18);

            int strength = state.GetUnit(1).Stats.MaxHp;
            var after = Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 1 });

            Assert.IsNull(after.GetUnit(1));
            Assert.AreEqual(strength, after.Score[0], "a kill should be worth the formation's strength");

            // A battalion is worth twice a company, because it is twice the force.
            Assert.AreEqual(
                UnitStats.For(UnitType.Infantry, Echelon.Company).MaxHp * 2,
                UnitStats.For(UnitType.Infantry, Echelon.Battalion).MaxHp);
        }

        // ---------- the four endings ----------

        [Test]
        public void Elimination_EndsTheGame()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Armor, 5, 5)
                .WithUnit(1, 1, UnitType.Recon, 6, 5, echelon: Echelon.Platoon);

            var after = Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 1 });
            Assert.IsTrue(after.IsOver);
            Assert.AreEqual(0, after.Winner);
            Assert.AreEqual(GameOutcome.Elimination, after.Outcome);
            Assert.IsEmpty(Rules.GetAllLegalActions(after));
        }

        [Test]
        public void KillingTheHeadquarters_EndsTheGameEvenWithForcesIntact()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Armor, 5, 5)
                .WithUnit(1, 1, UnitType.Headquarters, 6, 5, hp: 1)
                .WithUnit(2, 1, UnitType.Infantry, 18, 18, echelon: Echelon.Battalion)
                .WithUnit(3, 1, UnitType.Armor, 17, 18, echelon: Echelon.Battalion);
            state.StartingStrength = new[] { state.StrengthOf(0), state.StrengthOf(1) };

            var after = Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 1 });
            Assert.IsTrue(after.IsOver);
            Assert.AreEqual(0, after.Winner);
            Assert.AreEqual(GameOutcome.Decapitation, after.Outcome);
            Assert.AreEqual(2, after.Units.Count(u => u.Owner == 1), "the rest of the army survives");
        }

        [Test]
        public void LosingMostOfTheArmy_CausesARout()
        {
            // Player 1 fields a battalion and a doomed platoon; losing the
            // battalion drops them under the threshold.
            var state = Board()
                .WithUnit(0, 0, UnitType.Armor, 5, 5, echelon: Echelon.Division)
                .WithUnit(1, 1, UnitType.Infantry, 6, 5, hp: 1, echelon: Echelon.Battalion)
                .WithUnit(2, 1, UnitType.Recon, 18, 18, echelon: Echelon.Platoon)
                .WithUnit(3, 1, UnitType.Headquarters, 17, 18);
            state.StartingStrength = new[] { state.StrengthOf(0), state.StrengthOf(1) };
            state.RoutThreshold = 0.5;

            var after = Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 1 });
            Assert.IsTrue(after.IsOver);
            Assert.AreEqual(0, after.Winner);
            Assert.AreEqual(GameOutcome.Rout, after.Outcome);
        }

        [Test]
        public void ReachingTheTurnLimit_DecidesOnPoints()
        {
            var state = Board()
                .WithObjective(0, 5, 5, radius: 2, value: 5)
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 18, 18);
            state.TurnLimit = 4;

            while (!state.IsOver) state = Rules.Apply(state, new EndTurnAction());

            Assert.AreEqual(GameOutcome.Score, state.Outcome);
            Assert.AreEqual(0, state.Winner, "the side holding ground should win on points");
            Assert.Greater(state.Score[0], state.Score[1]);
        }

        [Test]
        public void LevelScoresAtTheTurnLimit_IsADraw()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 18, 18);
            state.TurnLimit = 4;

            while (!state.IsOver) state = Rules.Apply(state, new EndTurnAction());

            Assert.IsTrue(state.IsOver, "a drawn game is still a finished game");
            Assert.IsNull(state.Winner);
            Assert.AreEqual(GameOutcome.Draw, state.Outcome);
            Assert.IsEmpty(Rules.GetAllLegalActions(state));
        }

        // ---------- interactions ----------

        [Test]
        public void HeadquartersNeitherAmalgamatesNorDetaches()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Headquarters, 5, 5)
                .WithUnit(1, 0, UnitType.Headquarters, 5.9, 5)
                .WithUnit(2, 0, UnitType.Infantry, 5, 5.9);

            Assert.IsEmpty(Rules.GetLegalMerges(state, 0), "command elements should not combine");
            Assert.IsEmpty(Rules.GetSplitRegion(state, 0), "command elements should not detach");
            Assert.IsEmpty(Rules.GetLegalMerges(state, 2), "and nothing should absorb one");
        }

        [Test]
        public void AFinishedGameRefusesFurtherActions()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Armor, 5, 5)
                .WithUnit(1, 1, UnitType.Recon, 6, 5, echelon: Echelon.Platoon);
            var over = Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 1 });

            Assert.Throws<IllegalActionException>(() => Rules.Apply(over, new EndTurnAction()));
            Assert.IsFalse(Rules.IsLegalMoveTarget(over, 0, 6, 6));
        }

        [Test]
        public void VictoryStateRoundTripsThroughJson()
        {
            var state = LevelConfig.CreateStandardGame();
            state.Score = new[] { 12, 7 };
            state.Objectives[0].ControlledBy = 1;

            string json = state.ToJson();
            StringAssert.Contains("\"objectives\"", json);
            StringAssert.Contains("\"score\":[12,7]", json);
            StringAssert.Contains("\"turnLimit\":60", json);
            StringAssert.Contains("\"outcome\":null", json);
            Assert.AreEqual(json, GameState.FromJson(json).ToJson());
        }

        [Test]
        public void StandardGameHasSymmetricObjectives()
        {
            var state = LevelConfig.CreateStandardGame();
            Assert.IsNotEmpty(state.Objectives);

            double cx = (state.Width - 1) / 2.0, cy = (state.Height - 1) / 2.0;
            foreach (var objective in state.Objectives)
            {
                var mirror = state.Objectives.FirstOrDefault(o =>
                    System.Math.Abs(o.X - (2 * cx - objective.X)) < 1e-6 &&
                    System.Math.Abs(o.Y - (2 * cy - objective.Y)) < 1e-6);
                Assert.IsNotNull(mirror, $"objective {objective.Id} has no mirror");
                Assert.AreEqual(objective.Value, mirror.Value, "mirrored objectives must be worth the same");
                Assert.IsNull(objective.ControlledBy, "nobody starts holding ground");
            }
        }

        private static GameState Board() =>
            TestBoards.OpenBoard(24, 24).WithRuleset(Ruleset.Deterministic);
    }
}
