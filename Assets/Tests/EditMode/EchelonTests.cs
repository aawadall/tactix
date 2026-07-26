using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Tactix.Core.Tests
{
    /// <summary>
    /// Unit size scales behaviour monotonically, and the uncertainty that comes
    /// with size stays bounded, seeded, and exactly replayable.
    /// </summary>
    public class EchelonTests
    {
        [Test]
        public void Scaling_IsMonotonic_AcrossTheWholeLadder()
        {
            UnitStats previous = null;
            foreach (var echelon in EchelonScale.All)
            {
                var stats = UnitStats.For(UnitType.Infantry, echelon);
                if (previous != null)
                {
                    Assert.GreaterOrEqual(stats.MaxHp, previous.MaxHp, $"{echelon} is not tougher");
                    Assert.GreaterOrEqual(stats.AttackPower, previous.AttackPower, $"{echelon} does not hit harder");
                    Assert.Less(stats.MoveRange, previous.MoveRange, $"{echelon} is not slower");
                    Assert.Greater(stats.Radius, previous.Radius, $"{echelon} does not occupy more ground");
                    Assert.GreaterOrEqual(stats.AttackRange, previous.AttackRange, $"{echelon} does not reach further");
                    Assert.GreaterOrEqual(stats.DamageSpread, previous.DamageSpread, $"{echelon} is not less predictable");
                    Assert.GreaterOrEqual(stats.MovementFriction, previous.MovementFriction, $"{echelon} has less friction");
                }
                previous = stats;
            }
        }

        [Test]
        public void CompanyScale_IsTheUnchangedReferenceProfile()
        {
            foreach (var type in UnitStats.AllTypes)
            {
                var baseline = UnitStats.For(type);
                var company = UnitStats.For(type, Echelon.Company);
                Assert.AreEqual(baseline.MaxHp, company.MaxHp);
                Assert.AreEqual(baseline.AttackPower, company.AttackPower);
                Assert.AreEqual(baseline.MoveRange, company.MoveRange, 1e-9);
                Assert.AreEqual(baseline.AttackRange, company.AttackRange, 1e-9);
                Assert.AreEqual(baseline.Radius, company.Radius, 1e-9);
            }
        }

        [Test]
        public void Scaling_NeverRoundsARealCapabilityToNothing()
        {
            foreach (var type in UnitStats.AllTypes)
            {
                var baseline = UnitStats.For(type);
                foreach (var echelon in EchelonScale.All)
                {
                    var stats = UnitStats.For(type, echelon);
                    Assert.Greater(stats.MaxHp, 0, $"{type} {echelon} has no hit points");
                    Assert.AreEqual(baseline.CanAttack, stats.CanAttack, $"{type} {echelon} changed armed status");
                    Assert.AreEqual(baseline.CanSupport, stats.CanSupport, $"{type} {echelon} changed support status");
                    Assert.AreEqual(baseline.IsVehicle, stats.IsVehicle);
                    Assert.AreEqual(echelon, stats.Echelon);
                }
            }
        }

        [Test]
        public void SmallFormations_ResolveExactly_LargeOnesVary()
        {
            Assert.AreEqual(0, UnitStats.For(UnitType.Infantry, Echelon.FireTeam).DamageSpread);
            Assert.AreEqual(0, UnitStats.For(UnitType.Infantry, Echelon.FireTeam).MovementFriction, 1e-9);
            Assert.Greater(UnitStats.For(UnitType.Infantry, Echelon.Division).DamageSpread, 0);
            Assert.Greater(UnitStats.For(UnitType.Infantry, Echelon.Division).MovementFriction, 0);
        }

        [Test]
        public void DamageRoll_StaysWithinSpread_AndActuallyVaries()
        {
            var outcomes = new HashSet<int>();
            var rng = new SeededRandom(17);

            for (int trial = 0; trial < 300; trial++)
            {
                var state = TestBoards.OpenBoard(10, 5)
                    .WithRuleset(Ruleset.Standard)
                    .WithUnit(0, 0, UnitType.Infantry, 4, 2, echelon: Echelon.Division)
                    .WithUnit(1, 1, UnitType.Infantry, 5, 2, hp: 500, echelon: Echelon.Division);

                var attacker = state.GetUnit(0);
                int before = state.GetUnit(1).Hp;
                var after = Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 1 }, rng);
                outcomes.Add(before - after.GetUnit(1).Hp);
            }

            var stats = UnitStats.For(UnitType.Infantry, Echelon.Division);
            Assert.Greater(outcomes.Count, 1, "a division's damage should not be a fixed number");
            foreach (int damage in outcomes)
            {
                Assert.GreaterOrEqual(damage, stats.AttackPower - stats.DamageSpread);
                Assert.LessOrEqual(damage, stats.AttackPower + stats.DamageSpread);
            }
        }

        [Test]
        public void MovementFriction_FallsShort_ButNeverOvershootsOrLandsIllegally()
        {
            var rng = new SeededRandom(29);
            bool sawShortfall = false;

            for (int trial = 0; trial < 120; trial++)
            {
                var state = TestBoards.OpenBoard(20, 20)
                    .WithRuleset(Ruleset.Standard)
                    .WithUnit(0, 0, UnitType.Infantry, 10, 10, echelon: Echelon.Corps);

                var unit = state.GetUnit(0);
                double ordered = unit.Stats.MoveRange;
                var after = Rules.Apply(state, TestBoards.Move(0, 10 + ordered, 10), rng);
                var moved = after.GetUnit(0);

                double covered = Rules.Distance(10, 10, moved.X, moved.Y);
                Assert.LessOrEqual(covered, ordered + 1e-9, "a formation covered more ground than ordered");
                Assert.GreaterOrEqual(covered, ordered * (1 - unit.Stats.MovementFriction) - 1e-9,
                    "shortfall exceeded the friction bound");
                Assert.AreEqual(10.0, moved.Y, 1e-9, "friction should not deflect the heading");
                if (covered < ordered - 1e-6) sawShortfall = true;
            }

            Assert.IsTrue(sawShortfall, "a corps-sized formation never fell short of its orders");
        }

        [Test]
        public void DeterministicRuleset_NeedsNoRandomSource_AndAlwaysResolvesTheSame()
        {
            var state = TestBoards.OpenBoard(10, 5)
                .WithRuleset(Ruleset.Deterministic)
                .WithUnit(0, 0, UnitType.Infantry, 4, 2, echelon: Echelon.Division)
                .WithUnit(1, 1, UnitType.Infantry, 5, 2, hp: 500, echelon: Echelon.Division);

            int expected = UnitStats.For(UnitType.Infantry, Echelon.Division).AttackPower;
            for (int i = 0; i < 5; i++)
            {
                var after = Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 1 });
                Assert.AreEqual(500 - expected, after.GetUnit(1).Hp);
            }
        }

        [Test]
        public void StochasticRuleset_RefusesToResolveWithoutARandomSource()
        {
            var state = TestBoards.OpenBoard(10, 5)
                .WithRuleset(Ruleset.Standard)
                .WithUnit(0, 0, UnitType.Infantry, 4, 2, echelon: Echelon.Division)
                .WithUnit(1, 1, UnitType.Infantry, 5, 2, echelon: Echelon.Division);

            Assert.Throws<InvalidOperationException>(
                () => Rules.Apply(state, new AttackAction { UnitId = 0, TargetUnitId = 1 }));
        }

        [Test]
        public void RecordedDraws_ReplayAGameExactly()
        {
            // Play a game, recording every draw the engine consumes...
            var bot = new RandomBot(seed: 5);
            var recorder = new RecordingRandom(bot.OutcomeRandom);
            var state = LevelConfig.CreateStandardGame();

            var actions = new List<GameAction>();
            var drawsPerStep = new List<double[]>();
            int steps = 0;
            while (state.Winner == null && steps < 250)
            {
                var action = bot.ChooseAction(state);
                recorder.Reset();
                state = Rules.Apply(state, action, recorder);
                actions.Add(action);
                drawsPerStep.Add(recorder.Draws.ToArray());
                steps++;
            }
            string expected = state.ToJson();

            // ...then replay those actions against those draws.
            var replayed = LevelConfig.CreateStandardGame();
            for (int i = 0; i < actions.Count; i++)
                replayed = Rules.Apply(replayed, actions[i], new ReplayRandom(drawsPerStep[i]));

            Assert.AreEqual(expected, replayed.ToJson(), "replaying the logged draws did not reproduce the game");
        }

        [Test]
        public void MixedEchelons_CoexistAndAreSerialized()
        {
            var state = LevelConfig.CreateStandardGame();
            var echelons = state.Units.Select(u => u.Echelon).Distinct().ToList();
            Assert.Greater(echelons.Count, 1, "the standard order of battle should mix formation sizes");

            // A brigade of armour should plainly outclass a platoon of infantry.
            var brigade = UnitStats.For(UnitType.Armor, Echelon.Brigade);
            var platoon = UnitStats.For(UnitType.Infantry, Echelon.Platoon);
            Assert.Greater(brigade.MaxHp, platoon.MaxHp * 3);
            Assert.Less(brigade.MoveRange, UnitStats.For(UnitType.Armor, Echelon.Platoon).MoveRange);

            string json = state.ToJson();
            StringAssert.Contains("\"echelon\":\"brigade\"", json);
            StringAssert.Contains("\"echelon\":\"section\"", json);
            Assert.AreEqual(json, GameState.FromJson(json).ToJson());
        }

        [Test]
        public void EveryEchelon_HasADistinctMarking()
        {
            var markings = EchelonScale.All.Select(EchelonScale.Marking).ToList();
            Assert.AreEqual(EchelonScale.All.Length, markings.Distinct().Count(),
                "two echelons share a marking");
            foreach (var echelon in EchelonScale.All)
                Assert.IsNotEmpty(EchelonScale.DisplayName(echelon));
        }
    }
}
