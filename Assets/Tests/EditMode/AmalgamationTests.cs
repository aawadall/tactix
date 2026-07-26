using System;
using System.Linq;
using NUnit.Framework;

namespace Tactix.Core.Tests
{
    /// <summary>
    /// Amalgamation and detachment: two formations of one size combine into one a
    /// size larger, and back again. The power-of-two strength ladder is what makes
    /// this conserve rather than reward or punish the bookkeeping.
    /// </summary>
    public class AmalgamationTests
    {
        [Test]
        public void EchelonLadder_DoublesEachStep_SoTwoUnitsEqualOne()
        {
            foreach (var type in UnitStats.AllTypes)
            {
                foreach (var echelon in EchelonScale.All)
                {
                    var larger = EchelonScale.Larger(echelon);
                    if (larger == null) continue;

                    var small = UnitStats.For(type, echelon);
                    var big = UnitStats.For(type, larger.Value);

                    if (echelon >= Echelon.Company)
                    {
                        // At and above the reference scale the multiplier is a whole
                        // power of two, so doubling is exact and merges conserve.
                        Assert.AreEqual(small.MaxHp * 2, big.MaxHp,
                            $"{type} {echelon}->{larger} does not double hit points");
                        Assert.AreEqual(small.AttackPower * 2, big.AttackPower,
                            $"{type} {echelon}->{larger} does not double attack power");
                    }
                    else
                    {
                        // Below it, halving an odd stat cannot be exact: a 5 HP
                        // company is two 3 HP detachments on paper. Merges there
                        // are capped rather than conserved, which the cap in
                        // ApplyMerge handles.
                        Assert.LessOrEqual(Math.Abs(small.MaxHp * 2 - big.MaxHp), 1,
                            $"{type} {echelon}->{larger} drifts more than rounding explains");
                    }
                }
            }
        }

        [Test]
        public void Merge_CombinesTwoIntoOne_ConservingStrengthAndExperience()
        {
            var state = TwoCompanies(UnitType.Infantry, UnitType.Infantry);
            state.GetUnit(0).Xp = 3;
            state.GetUnit(1).Xp = 4;

            var merge = Rules.GetLegalMerges(state, 0).Single();
            Assert.AreEqual(1, merge.AbsorbedUnitId);

            var after = Rules.Apply(state, merge);
            var merged = after.GetUnit(0);

            Assert.AreEqual(1, after.Units.Count, "the absorbed unit should leave the game");
            Assert.IsNull(after.GetUnit(1));
            Assert.AreEqual(Echelon.Battalion, merged.Echelon);
            Assert.AreEqual(UnitType.Infantry, merged.Type);
            Assert.AreEqual(10, merged.Hp, "two 5 HP companies should make a 10 HP battalion");
            Assert.AreEqual(7, merged.Xp, "experience should pool");
            Assert.IsTrue(merged.HasMoved, "combining is the formation's move");
        }

        [Test]
        public void Merge_OfUnlikeBranches_MakesACombinedArmsFormation()
        {
            var state = TwoCompanies(UnitType.Infantry, UnitType.Armor);
            var after = Rules.Apply(state, Rules.GetLegalMerges(state, 0).Single());
            var merged = after.GetUnit(0);

            Assert.AreEqual(UnitType.CombinedArms, merged.Type);
            Assert.AreEqual(Echelon.Battalion, merged.Echelon);
            // Infantry 5 + Armor 8 = 13, capped at the combined-arms battalion max of 12.
            Assert.AreEqual(UnitStats.For(UnitType.CombinedArms, Echelon.Battalion).MaxHp, merged.Hp);
        }

        [Test]
        public void Merge_RequiresSameSize_SameSide_Contact_AndAnUnmovedPair()
        {
            // Different sizes cannot combine.
            var mismatched = TestBoards.OpenBoard(20, 20)
                .WithUnit(0, 0, UnitType.Infantry, 5, 5, echelon: Echelon.Company)
                .WithUnit(1, 0, UnitType.Infantry, 5.9, 5, echelon: Echelon.Battalion);
            Assert.IsEmpty(Rules.GetLegalMerges(mismatched, 0));

            // Enemies cannot combine.
            var enemies = TestBoards.OpenBoard(20, 20)
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 5.9, 5);
            Assert.IsEmpty(Rules.GetLegalMerges(enemies, 0));

            // Out of contact.
            var distant = TestBoards.OpenBoard(20, 20)
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 0, UnitType.Infantry, 9, 5);
            Assert.IsEmpty(Rules.GetLegalMerges(distant, 0));

            // A formation that has already moved cannot combine this turn.
            var moved = TwoCompanies(UnitType.Infantry, UnitType.Infantry);
            moved.GetUnit(1).HasMoved = true;
            Assert.IsEmpty(Rules.GetLegalMerges(moved, 0));

            // Nothing above theatre scale.
            var top = TestBoards.OpenBoard(30, 30)
                .WithUnit(0, 0, UnitType.Infantry, 10, 10, echelon: Echelon.Theater)
                .WithUnit(1, 0, UnitType.Infantry, 12, 10, echelon: Echelon.Theater);
            Assert.IsEmpty(Rules.GetLegalMerges(top, 0));
        }

        [Test]
        public void Split_BreaksAFormationInTwo_ConservingStrength()
        {
            var state = TestBoards.OpenBoard(20, 20)
                .WithUnit(0, 0, UnitType.Infantry, 10, 10, echelon: Echelon.Battalion);
            state.GetUnit(0).Xp = 6;

            Assert.IsTrue(Rules.IsLegalSplitTarget(state, 0, 11.2, 10));
            var after = Rules.Apply(state, new SplitAction { UnitId = 0, TargetX = 11.2, TargetY = 10 });

            Assert.AreEqual(2, after.Units.Count);
            var parent = after.GetUnit(0);
            var detachment = after.Units.Single(u => u.Id != 0);

            Assert.AreEqual(Echelon.Company, parent.Echelon);
            Assert.AreEqual(Echelon.Company, detachment.Echelon);
            Assert.AreEqual(UnitType.Infantry, detachment.Type);
            Assert.AreEqual(parent.Owner, detachment.Owner);
            Assert.AreEqual(10, parent.Hp + detachment.Hp, "splitting should not lose strength");
            Assert.AreEqual(6, parent.Xp + detachment.Xp, "experience should divide");
            Assert.IsTrue(parent.HasMoved);
            Assert.IsTrue(detachment.HasMoved);
        }

        [Test]
        public void MergeThenSplit_ReturnsToTheStartingStrength()
        {
            var state = TwoCompanies(UnitType.Infantry, UnitType.Infantry);
            int before = state.Units.Sum(u => u.Hp);

            var merged = Rules.Apply(state, Rules.GetLegalMerges(state, 0).Single());
            Assert.AreEqual(before, merged.Units.Sum(u => u.Hp), "merging changed total strength");

            // A fresh turn, so the battalion may act again.
            merged = Rules.Apply(Rules.Apply(merged, new EndTurnAction()), new EndTurnAction());
            var split = Rules.SampleLegalSplits(merged, 0, 1, new Random(4)).Single();
            var after = Rules.Apply(merged, split);

            Assert.AreEqual(before, after.Units.Sum(u => u.Hp), "splitting changed total strength");
            Assert.AreEqual(2, after.Units.Count);
        }

        [Test]
        public void Split_RefusesTargetsThatAreBlocked_OrTooFar_OrOnTopOfSomeone()
        {
            var state = TestBoards.OpenBoard(20, 20)
                .WithTerrain(TerrainType.Impassable, (12, 10))
                .WithUnit(0, 0, UnitType.Infantry, 10, 10, echelon: Echelon.Battalion)
                .WithUnit(1, 0, UnitType.Infantry, 10, 11.2);

            Assert.IsFalse(Rules.IsLegalSplitTarget(state, 0, 12, 10), "formed up inside a rock");
            Assert.IsFalse(Rules.IsLegalSplitTarget(state, 0, 16, 10), "formed up far beyond contact");
            Assert.IsFalse(Rules.IsLegalSplitTarget(state, 0, 10, 11.2), "formed up on top of a friendly");
            Assert.IsFalse(Rules.IsLegalSplitTarget(state, 0, 10, 10), "formed up on itself");

            var fireTeam = TestBoards.OpenBoard(20, 20)
                .WithUnit(0, 0, UnitType.Infantry, 10, 10, echelon: Echelon.FireTeam);
            Assert.IsEmpty(Rules.GetSplitRegion(fireTeam, 0), "nothing splits below a fire team");
        }

        [Test]
        public void SampledSplitsAndMerges_AreAlwaysLegal()
        {
            var rng = new Random(99);
            var state = TestBoards.OpenBoard(24, 24)
                .WithUnit(0, 0, UnitType.Armor, 12, 12, echelon: Echelon.Brigade)
                .WithUnit(1, 0, UnitType.Infantry, 12.9, 12, echelon: Echelon.Company)
                .WithUnit(2, 0, UnitType.Infantry, 13.8, 12, echelon: Echelon.Company);

            foreach (var split in Rules.SampleLegalSplits(state, 0, 40, rng))
            {
                Assert.IsTrue(Rules.IsLegalSplitTarget(state, 0, split.TargetX, split.TargetY));
                Assert.DoesNotThrow(() => Rules.Apply(state, split));
            }
            foreach (var merge in Rules.GetLegalMerges(state, 1))
                Assert.DoesNotThrow(() => Rules.Apply(state, merge));
        }

        [Test]
        public void MergeAndSplit_RoundTripThroughJson()
        {
            GameAction[] actions =
            {
                new MergeAction { UnitId = 2, AbsorbedUnitId = 7 },
                new SplitAction { UnitId = 3, TargetX = 11.25, TargetY = 4.5 },
            };
            foreach (var action in actions)
            {
                string json = TactixJson.Serialize(action);
                var restored = TactixJson.Deserialize<GameAction>(json);
                Assert.AreEqual(action.GetType(), restored.GetType());
                Assert.AreEqual(json, TactixJson.Serialize(restored));
            }
            StringAssert.Contains("\"actionType\":\"merge\"", TactixJson.Serialize(actions[0]));
            StringAssert.Contains("\"actionType\":\"split\"", TactixJson.Serialize(actions[1]));
        }

        /// <summary>Two touching, unmoved friendly companies on open ground.</summary>
        private static GameState TwoCompanies(UnitType first, UnitType second)
        {
            return TestBoards.OpenBoard(20, 20)
                .WithUnit(0, 0, first, 10, 10, echelon: Echelon.Company)
                .WithUnit(1, 0, second, 10.9, 10, echelon: Echelon.Company);
        }
    }
}
