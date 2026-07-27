using System;
using NUnit.Framework;

namespace Tactix.Core.Tests
{
    public class UnitAutonomyTests
    {
        private static readonly Random Rng = new Random(42);

        [Test]
        public void EmptyQueue_HealsWoundedAllyInRange()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Medic, 5, 5)
                .WithUnit(1, 0, UnitType.Infantry, 5.5, 5, hp: 2)
                .WithUnit(2, 1, UnitType.Infantry, 20, 20);

            var action = UnitAutonomy.TryStep(state, 0, Rng);
            Assert.IsInstanceOf<HealAction>(action);
            Assert.AreEqual(1, ((HealAction)action).TargetUnitId);
        }

        [Test]
        public void EmptyQueue_AttacksEnemyInRange()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Armor, 5, 5)
                .WithUnit(1, 1, UnitType.Recon, 6.2, 5, echelon: Echelon.Platoon)
                .WithUnit(2, 1, UnitType.Infantry, 20, 20);

            GameAction action = null;
            for (int i = 0; i < 20 && action == null; i++)
                action = UnitAutonomy.TryStep(state, 0, new Random(100 + i));

            Assert.IsInstanceOf<AttackAction>(action);
            Assert.AreEqual(1, ((AttackAction)action).TargetUnitId);
        }

        [Test]
        public void EmptyQueue_AdvancesTowardEnemy()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 18, 5);

            var action = UnitAutonomy.TryStep(state, 0, new Random(7), advanceBias: 1.0);
            Assert.IsInstanceOf<MoveAction>(action);
            Assert.Greater(((MoveAction)action).TargetX, 5.0);
        }

        [Test]
        public void HoldOrder_UsesExecutor_NotAutonomyMove()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 20, 20);

            var hold = new HoldOrder(5, 5, radius: 2);
            var orderAction = OrderExecutor.TryStep(state, 0, hold, out bool complete);
            Assert.IsNull(orderAction);
            Assert.IsFalse(complete);

            var auto = UnitAutonomy.TryStep(state, 0, Rng, advanceBias: 1.0);
            Assert.IsNotNull(auto, "without a queued hold, autonomy would move or fight");
            Assert.IsInstanceOf<MoveAction>(auto);
        }

        [Test]
        public void RandomBot_StillWorksAfterRefactor()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 18, 5);

            var bot = new RandomBot(seed: 12345);
            var action = bot.ChooseAction(state);
            Assert.IsNotNull(action);
            Assert.IsFalse(action is EndTurnAction);
        }

        [Test]
        public void PickBestHeal_PrefersMostWounded()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Medic, 5, 5)
                .WithUnit(1, 0, UnitType.Infantry, 6, 5, hp: 4)
                .WithUnit(2, 0, UnitType.Infantry, 6, 6, hp: 1)
                .WithUnit(3, 1, UnitType.Infantry, 20, 20);

            var heal = UnitAutonomy.PickBestHeal(state, Rng);
            Assert.IsInstanceOf<HealAction>(heal);
            Assert.AreEqual(2, ((HealAction)heal).TargetUnitId);
        }

        private static GameState Board() =>
            TestBoards.OpenBoard(24, 24).WithRuleset(Ruleset.Deterministic);
    }
}
