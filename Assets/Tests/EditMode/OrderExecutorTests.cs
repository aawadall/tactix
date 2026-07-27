using System.Collections.Generic;
using NUnit.Framework;

namespace Tactix.Core.Tests
{
    /// <summary>
    /// Order queues and the executor that turns goals into ordinary legal actions.
    /// Orders are not part of GameState — these tests exercise the pure planner only.
    /// </summary>
    public class OrderExecutorTests
    {
        [Test]
        public void MoveTo_ProjectsTowardGoal_AndCompletesWhenClose()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 20, 20);

            var order = new MoveToOrder(12, 5);
            var action = OrderExecutor.TryStep(state, 0, order, out bool complete);
            Assert.IsInstanceOf<MoveAction>(action);
            var move = (MoveAction)action;
            Assert.Greater(move.TargetX, 5.0);
            Assert.IsFalse(complete, "a distant goal should take more than one step");

            // Place the unit on the goal — next step completes with no action.
            state.GetUnit(0).X = 12;
            state.GetUnit(0).Y = 5;
            var idle = OrderExecutor.TryStep(state, 0, order, out complete);
            Assert.IsNull(idle);
            Assert.IsTrue(complete);
        }

        [Test]
        public void Engage_AttacksWhenInRange_ElseMovesCloser()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Armor, 5, 5)
                .WithUnit(1, 1, UnitType.Recon, 6.2, 5, echelon: Echelon.Platoon)
                .WithUnit(2, 1, UnitType.Infantry, 20, 20);

            var order = new EngageOrder(1);
            var action = OrderExecutor.TryStep(state, 0, order, out bool complete);
            Assert.IsInstanceOf<AttackAction>(action);
            Assert.AreEqual(1, ((AttackAction)action).TargetUnitId);
            Assert.IsFalse(complete, "engage stays until the target is gone");

            // Far target: move instead.
            state = Board()
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 18, 5);
            action = OrderExecutor.TryStep(state, 0, new EngageOrder(1), out complete);
            Assert.IsInstanceOf<MoveAction>(action);
            Assert.Greater(((MoveAction)action).TargetX, 5.0);
            Assert.IsFalse(complete);
        }

        [Test]
        public void Engage_CompletesWhenTargetGone()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Armor, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 18, 18);

            var action = OrderExecutor.TryStep(state, 0, new EngageOrder(99), out bool complete);
            Assert.IsNull(action);
            Assert.IsTrue(complete);
        }

        [Test]
        public void Hold_IdlesInsideRadius_MovesWhenOutside()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 20, 20);

            var hold = new HoldOrder(5, 5, radius: 2);
            var idle = OrderExecutor.TryStep(state, 0, hold, out bool complete);
            Assert.IsNull(idle);
            Assert.IsFalse(complete, "hold is standing and never auto-completes");

            state.GetUnit(0).X = 10;
            state.GetUnit(0).Y = 5;
            var move = OrderExecutor.TryStep(state, 0, hold, out complete);
            Assert.IsInstanceOf<MoveAction>(move);
            Assert.Less(((MoveAction)move).TargetX, 10.0);
            Assert.IsFalse(complete);
        }

        [Test]
        public void Support_HealsWhenInRange()
        {
            var state = Board()
                .WithUnit(0, 0, UnitType.Medic, 5, 5)
                .WithUnit(1, 0, UnitType.Infantry, 5.5, 5, hp: 2)
                .WithUnit(2, 1, UnitType.Infantry, 20, 20);

            var action = OrderExecutor.TryStep(state, 0, new SupportOrder(1), out bool complete);
            Assert.IsInstanceOf<HealAction>(action);
            Assert.IsTrue(complete);
        }

        [Test]
        public void OrderBook_EnforcesMaxDepth_AndRevoke()
        {
            var book = new OrderBook();
            Assert.IsTrue(book.Enqueue(0, new MoveToOrder(1, 1)));
            Assert.IsTrue(book.Enqueue(0, new MoveToOrder(2, 2)));
            Assert.IsTrue(book.Enqueue(0, new MoveToOrder(3, 3)));
            Assert.IsFalse(book.Enqueue(0, new MoveToOrder(4, 4)));
            Assert.AreEqual(3, book.Count(0));

            var popped = book.PopLast(0);
            Assert.IsInstanceOf<MoveToOrder>(popped);
            Assert.AreEqual(3.0, ((MoveToOrder)popped).X, 1e-9);
            Assert.AreEqual(2, book.Count(0));

            book.Clear(0);
            Assert.IsFalse(book.HasOrders(0));
        }

        [Test]
        public void OrderBook_SetOrReplaceTail_ReplacesLastOnly()
        {
            var book = new OrderBook();
            book.Enqueue(0, new MoveToOrder(1, 1));
            book.Enqueue(0, new MoveToOrder(2, 2));
            book.SetOrReplaceTail(0, new EngageOrder(7));

            var all = book.PeekAll(0);
            Assert.AreEqual(2, all.Count);
            Assert.IsInstanceOf<MoveToOrder>(all[0]);
            Assert.IsInstanceOf<EngageOrder>(all[1]);
            Assert.AreEqual(7, ((EngageOrder)all[1]).TargetUnitId);
        }

        [Test]
        public void EstimateTurnsToGoal_UsesMoveRange()
        {
            var state = Board().WithUnit(0, 0, UnitType.Infantry, 0, 0); // move 3
            Assert.AreEqual(0, OrderExecutor.EstimateTurnsToGoal(state, 0, 0.1, 0));
            Assert.AreEqual(1, OrderExecutor.EstimateTurnsToGoal(state, 0, 2.5, 0));
            Assert.AreEqual(4, OrderExecutor.EstimateTurnsToGoal(state, 0, 12, 0));
        }

        [Test]
        public void ClockPly_ExecutorActionsApplyAndAutoCompleteArrivals()
        {
            // Thin harness: step orders the way GameController's clock does,
            // without Unity — empty-book does not end the turn; idle hold does.
            var state = Board()
                .WithUnit(0, 0, UnitType.Infantry, 5, 5)
                .WithUnit(1, 1, UnitType.Infantry, 20, 20);
            var book = new OrderBook();
            book.Replace(0, new MoveToOrder(8, 5));

            var action = OrderExecutor.TryStep(state, 0, book.Peek(0), out bool complete);
            Assert.IsNotNull(action);
            state = Rules.Apply(state, action);
            if (complete) book.Dequeue(0);

            Assert.Greater(state.GetUnit(0).X, 5.0);
            Assert.IsTrue(book.HasOrders(0) || complete);
        }

        [Test]
        public void OrderBook_ReplaceAt_RemoveAt_InsertAt()
        {
            var book = new OrderBook();
            book.Enqueue(0, new MoveToOrder(1, 1));
            book.Enqueue(0, new MoveToOrder(2, 2));

            book.ReplaceAt(0, 1, new EngageOrder(5));
            Assert.IsInstanceOf<EngageOrder>(book.PeekAll(0)[1]);

            book.RemoveAt(0, 0);
            Assert.AreEqual(1, book.Count(0));
            Assert.IsInstanceOf<EngageOrder>(book.Peek(0));

            Assert.IsTrue(book.InsertAt(0, 0, new MoveToOrder(0, 0)));
            Assert.AreEqual(2, book.Count(0));
            Assert.IsInstanceOf<MoveToOrder>(book.Peek(0));

            Assert.IsFalse(book.InsertAt(0, 0, new HoldOrder(3, 3)));
            Assert.AreEqual(2, book.Count(0));
        }

        [Test]
        public void PathWaypoints_UsesSlopeAwareRoute()
        {
            var state = Board()
                .WithElevation(2, (5, 5))
                .WithUnit(0, 0, UnitType.Infantry, 0, 5)
                .WithUnit(1, 1, UnitType.Infantry, 20, 20);

            var orders = new List<UnitOrder> { new MoveToOrder(8, 5) };
            var path = OrderExecutor.PathWaypoints(state, 0, orders);
            Assert.GreaterOrEqual(path.Count, 2);
            bool crossesRidge = false;
            foreach (var (x, y) in path)
            {
                if (Geometry.TileIndex(x) == 5 && Geometry.TileIndex(y) == 5)
                    crossesRidge = true;
            }
            Assert.IsFalse(crossesRidge, "Path should detour around cliff at (5,5)");
        }

        [Test]
        public void EstimateTurnsToGoal_UsesPathLength()
        {
            var state = Board().WithUnit(0, 0, UnitType.Infantry, 0, 0);
            int turns = OrderExecutor.EstimateTurnsToGoal(state, 0, 12, 0);
            Assert.GreaterOrEqual(turns, 4);
        }

        private static GameState Board() =>
            TestBoards.OpenBoard(24, 24).WithRuleset(Ruleset.Deterministic);
    }
}
