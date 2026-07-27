using System.Collections.Generic;

namespace Tactix.Core
{
    /// <summary>
    /// Per-unit command queues (max <see cref="MaxDepth"/> each). Not part of
    /// game state or the log schema — presentation / planner only.
    /// </summary>
    public sealed class OrderBook
    {
        public const int MaxDepth = 3;

        private readonly Dictionary<int, List<UnitOrder>> _queues = new Dictionary<int, List<UnitOrder>>();

        public IEnumerable<int> UnitIds => _queues.Keys;

        public int Count(int unitId) =>
            _queues.TryGetValue(unitId, out var q) ? q.Count : 0;

        public bool HasOrders(int unitId) => Count(unitId) > 0;

        public IReadOnlyList<UnitOrder> PeekAll(int unitId) =>
            _queues.TryGetValue(unitId, out var q) ? q : (IReadOnlyList<UnitOrder>)System.Array.Empty<UnitOrder>();

        public UnitOrder Peek(int unitId)
        {
            if (!_queues.TryGetValue(unitId, out var q) || q.Count == 0) return null;
            return q[0];
        }

        /// <summary>
        /// Append an order. Returns false if the queue is already full.
        /// </summary>
        public bool Enqueue(int unitId, UnitOrder order)
        {
            if (order == null) return false;
            if (!_queues.TryGetValue(unitId, out var q))
            {
                q = new List<UnitOrder>(MaxDepth);
                _queues[unitId] = q;
            }
            if (q.Count >= MaxDepth) return false;
            q.Add(order);
            return true;
        }

        /// <summary>
        /// Replace the entire queue with a single order (or clear if null).
        /// </summary>
        public void Replace(int unitId, UnitOrder order)
        {
            if (order == null)
            {
                Clear(unitId);
                return;
            }
            _queues[unitId] = new List<UnitOrder>(MaxDepth) { order };
        }

        /// <summary>
        /// If the queue is empty, enqueue; otherwise replace the last order.
        /// Used for ordinary clicks. Shift+click should call <see cref="Enqueue"/>.
        /// </summary>
        public void SetOrReplaceTail(int unitId, UnitOrder order)
        {
            if (order == null) return;
            if (!_queues.TryGetValue(unitId, out var q) || q.Count == 0)
            {
                Replace(unitId, order);
                return;
            }
            q[q.Count - 1] = order;
        }

        public UnitOrder PopLast(int unitId)
        {
            if (!_queues.TryGetValue(unitId, out var q) || q.Count == 0) return null;
            int last = q.Count - 1;
            var order = q[last];
            q.RemoveAt(last);
            if (q.Count == 0) _queues.Remove(unitId);
            return order;
        }

        public UnitOrder Dequeue(int unitId)
        {
            if (!_queues.TryGetValue(unitId, out var q) || q.Count == 0) return null;
            var order = q[0];
            q.RemoveAt(0);
            if (q.Count == 0) _queues.Remove(unitId);
            return order;
        }

        public void Clear(int unitId) => _queues.Remove(unitId);

        public void ClearAll() => _queues.Clear();

        /// <summary>Replace the order at <paramref name="index"/> (0-based).</summary>
        public void ReplaceAt(int unitId, int index, UnitOrder order)
        {
            if (order == null) return;
            if (!_queues.TryGetValue(unitId, out var q) || index < 0 || index >= q.Count) return;
            q[index] = order;
        }

        /// <summary>Remove the order at <paramref name="index"/> (0-based).</summary>
        public void RemoveAt(int unitId, int index)
        {
            if (!_queues.TryGetValue(unitId, out var q) || index < 0 || index >= q.Count) return;
            q.RemoveAt(index);
            if (q.Count == 0) _queues.Remove(unitId);
        }

        /// <summary>Insert at <paramref name="index"/>; returns false when full.</summary>
        public bool InsertAt(int unitId, int index, UnitOrder order)
        {
            if (order == null) return false;
            if (!_queues.TryGetValue(unitId, out var q))
            {
                q = new List<UnitOrder>(MaxDepth);
                _queues[unitId] = q;
            }
            if (q.Count >= MaxDepth) return false;
            index = System.Math.Max(0, System.Math.Min(index, q.Count));
            q.Insert(index, order);
            return true;
        }

        /// <summary>Drop queues for units that no longer exist (killed / merged).</summary>
        public void Prune(GameState state)
        {
            if (state == null)
            {
                ClearAll();
                return;
            }
            var dead = new List<int>();
            foreach (var id in _queues.Keys)
            {
                if (state.GetUnit(id) == null) dead.Add(id);
            }
            foreach (var id in dead) _queues.Remove(id);
        }
    }
}
