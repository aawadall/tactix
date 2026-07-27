using System.Collections.Generic;
using System.Linq;
using Tactix.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Tactix.Game
{
    /// <summary>
    /// Mouse interaction: select one or many units, enqueue revocable orders
    /// (group Move / Engage / Garrison when multi-selected), or Ctrl for instant
    /// atomic actions on the primary unit.
    /// </summary>
    public sealed class InputController : MonoBehaviour
    {
        public enum OrderTool
        {
            Auto,
            Move,
            Engage,
            Hold,
            Support,
        }

        private const float BoxSelectThresholdPx = 10f;

        private GameController _game;
        private BoardRenderer _board;
        private readonly List<int> _selectedIds = new List<int>();
        private List<AttackAction> _legalAttacks = new List<AttackAction>();
        private List<HealAction> _legalHeals = new List<HealAction>();
        private List<MergeAction> _legalMerges = new List<MergeAction>();
        private Vector3? _aimWorld;

        private Vector2? _boxStartScreen;
        private Vector2? _pendingGround;
        private bool _boxing;

        public bool SplitMode { get; private set; }
        public OrderTool ActiveTool { get; private set; } = OrderTool.Auto;
        public int FocusedSlot { get; private set; }

        /// <summary>Primary (last) selected unit id, or null when empty.</summary>
        public int? SelectedUnitId => _selectedIds.Count > 0 ? _selectedIds[_selectedIds.Count - 1] : (int?)null;

        public IReadOnlyList<int> SelectedUnitIds => _selectedIds;
        public int SelectionCount => _selectedIds.Count;
        public bool IsMultiSelect => _selectedIds.Count > 1;

        public void Init(GameController game, BoardRenderer board)
        {
            _game = game;
            _board = board;
        }

        public void SetTool(OrderTool tool)
        {
            ActiveTool = tool;
            SplitMode = false;
            RefreshSelection();
        }

        public void SetFocusedSlot(int slot)
        {
            FocusedSlot = Mathf.Clamp(slot, 0, OrderBook.MaxDepth - 1);
            RefreshSelection();
        }

        public void RemoveOrderAtSlot(int slot)
        {
            if (_selectedIds.Count == 0 || !_game.CanPlanOrders) return;
            foreach (int id in OrderableSelectedIds())
                _game.Orders.RemoveAt(id, slot);
            SyncFocusedSlot();
            RefreshSelection();
            _game.RefreshViews();
        }

        public void UndoLastOrder()
        {
            if (_selectedIds.Count == 0 || !_game.CanPlanOrders) return;
            foreach (int id in OrderableSelectedIds())
                _game.Orders.PopLast(id);
            SyncFocusedSlot();
            RefreshSelection();
            _game.RefreshViews();
        }

        public void ClearSelectedOrders()
        {
            if (_selectedIds.Count == 0 || !_game.CanPlanOrders) return;
            foreach (int id in OrderableSelectedIds())
                _game.Orders.Clear(id);
            FocusedSlot = 0;
            RefreshSelection();
            _game.RefreshViews();
        }

        public void RefreshOrderUi() => RefreshOverlays();

        public void RefreshOverlays()
        {
            if (_selectedIds.Count > 0)
                RefreshSelection();
            else
                _game.Ui.HideOrderStrip();
        }

        private IEnumerable<int> OrderableSelectedIds()
        {
            var state = _game.State;
            foreach (int id in _selectedIds)
            {
                var unit = state?.GetUnit(id);
                if (unit != null && _game.IsOrderableUnit(unit))
                    yield return id;
            }
        }

        private Unit PrimaryUnit()
        {
            if (_selectedIds.Count == 0 || _game.State == null) return null;
            return _game.State.GetUnit(_selectedIds[_selectedIds.Count - 1]);
        }

        private void Update()
        {
            if (!_game.GameStarted || _game.InFieldManual || _game.InMapWorkshop
                || _game.State == null || _game.State.IsOver)
            {
                if (_selectedIds.Count > 0) ClearSelection();
                _aimWorld = null;
                CancelBox();
                return;
            }

            if (_game.CanPlanOrders)
            {
                if (Input.GetKeyDown(KeyCode.Z))
                {
                    UndoLastOrder();
                    return;
                }

                if (Input.GetKeyDown(KeyCode.H))
                {
                    ActiveTool = ActiveTool == OrderTool.Hold ? OrderTool.Auto : OrderTool.Hold;
                    RefreshSelection();
                    return;
                }
            }

            if (Input.GetMouseButtonDown(1))
            {
                CancelBox();
                if (ActiveTool != OrderTool.Auto)
                {
                    ActiveTool = OrderTool.Auto;
                    _game.Ui.HideContextMenu();
                    RefreshSelection();
                    return;
                }
                _game.Ui.HideContextMenu();
                ClearSelection();
                return;
            }

            var primary = PrimaryUnit();
            if (Input.GetKeyDown(KeyCode.S) && primary != null && _game.CanAcceptInput
                && !IsMultiSelect && _game.IsOrderableUnit(primary))
            {
                SplitMode = !SplitMode;
                ActiveTool = OrderTool.Auto;
                RefreshSelection();
                return;
            }

            UpdateAimPreview();
            UpdateBoxSelect();

            if (!Input.GetMouseButtonDown(0)) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            var world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            double x = world.x, y = world.y;
            var state = _game.State;
            var clickedUnit = state.GetUnitAtPoint(x, y);
            bool atomic = _game.CanAcceptInput
                && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (SplitMode && _game.CanAcceptInput && primary != null && !IsMultiSelect && clickedUnit == null)
            {
                var split = new SplitAction { UnitId = primary.Id, TargetX = x, TargetY = y };
                if (Rules.IsLegalSplitTarget(state, split.UnitId, x, y) && _game.TrySubmitAction(split))
                {
                    SplitMode = false;
                    RefreshSelection();
                }
                return;
            }

            // Shift+click a unit: toggle multi-select (orderable friendlies only).
            if (shift && clickedUnit != null)
            {
                ToggleSelectUnit(clickedUnit, Input.mousePosition);
                return;
            }

            // Ground click may start a drag-box or issue an order on mouse-up.
            if (clickedUnit == null)
            {
                _boxStartScreen = Input.mousePosition;
                _pendingGround = new Vector2((float)x, (float)y);
                _boxing = false;
                return;
            }

            // Atomic / order against current selection before re-selecting.
            if (_selectedIds.Count > 0)
            {
                var self = PrimaryUnit();
                if (self != null && _game.IsOrderableUnit(self))
                {
                    if (atomic && !IsMultiSelect && TryAtomicClick(clickedUnit, x, y)) return;
                    if (_game.CanPlanOrders && TryOrderClick(clickedUnit, x, y, append: false)) return;
                }
            }

            ReplaceSelection(clickedUnit, Input.mousePosition);
        }

        private void UpdateBoxSelect()
        {
            if (!_boxStartScreen.HasValue) return;

            if (Input.GetMouseButton(0))
            {
                float dist = Vector2.Distance(_boxStartScreen.Value, Input.mousePosition);
                if (dist >= BoxSelectThresholdPx)
                {
                    _boxing = true;
                    _board.SetSelectionBox(_boxStartScreen.Value, Input.mousePosition);
                }
                return;
            }

            if (!Input.GetMouseButtonUp(0)) return;

            if (_boxing)
            {
                SelectUnitsInScreenBox(_boxStartScreen.Value, Input.mousePosition);
                CancelBox();
                return;
            }

            // Click (no drag): issue order or clear.
            if (_pendingGround.HasValue)
            {
                double x = _pendingGround.Value.x, y = _pendingGround.Value.y;
                bool append = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool atomic = _game.CanAcceptInput
                    && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));

                var primary = PrimaryUnit();
                if (primary != null && _game.IsOrderableUnit(primary))
                {
                    if (atomic && !IsMultiSelect && TryAtomicClick(null, x, y))
                    {
                        CancelBox();
                        return;
                    }
                    if (_game.CanPlanOrders && TryOrderClick(null, x, y, append))
                    {
                        CancelBox();
                        return;
                    }
                }

                if (_selectedIds.Count == 0)
                    ClearSelection();
            }

            CancelBox();
        }

        private void CancelBox()
        {
            _boxStartScreen = null;
            _pendingGround = null;
            _boxing = false;
            _board?.ClearSelectionBox();
        }

        private void SelectUnitsInScreenBox(Vector2 screenA, Vector2 screenB)
        {
            if (Camera.main == null || _game.State == null) return;

            float minX = Mathf.Min(screenA.x, screenB.x);
            float maxX = Mathf.Max(screenA.x, screenB.x);
            float minY = Mathf.Min(screenA.y, screenB.y);
            float maxY = Mathf.Max(screenA.y, screenB.y);

            var hits = new List<Unit>();
            foreach (var unit in _game.State.Units)
            {
                if (!_game.IsOrderableUnit(unit)) continue;
                var screen = Camera.main.WorldToScreenPoint(new Vector3((float)unit.X, (float)unit.Y, 0f));
                if (screen.x >= minX && screen.x <= maxX && screen.y >= minY && screen.y <= maxY)
                    hits.Add(unit);
            }

            if (hits.Count == 0)
            {
                ClearSelection();
                return;
            }

            bool add = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!add) _selectedIds.Clear();
            foreach (var unit in hits.OrderBy(u => u.Id))
            {
                if (!_selectedIds.Contains(unit.Id))
                    _selectedIds.Add(unit.Id);
            }

            SyncFocusedSlot();
            RefreshSelection();
            ShowContextForSelection(Input.mousePosition);
        }

        private void ToggleSelectUnit(Unit unit, Vector2 screenPos)
        {
            if (!_game.IsOrderableUnit(unit))
            {
                // Inspect-only units replace selection as a single inspect.
                ReplaceSelection(unit, screenPos);
                return;
            }

            if (_selectedIds.Contains(unit.Id))
            {
                _selectedIds.Remove(unit.Id);
                if (_selectedIds.Count == 0)
                {
                    ClearSelection();
                    return;
                }
            }
            else
            {
                // Drop inspect-only units when adding friendlies.
                PruneNonOrderableFromSelection();
                _selectedIds.Add(unit.Id);
            }

            SyncFocusedSlot();
            RefreshSelection();
            ShowContextForSelection(screenPos);
        }

        private void ReplaceSelection(Unit unit, Vector2 screenPos)
        {
            _selectedIds.Clear();
            _selectedIds.Add(unit.Id);
            SyncFocusedSlot();
            RefreshSelection();
            ShowContextForSelection(screenPos);
        }

        private void PruneNonOrderableFromSelection()
        {
            var state = _game.State;
            _selectedIds.RemoveAll(id =>
            {
                var u = state?.GetUnit(id);
                return u == null || !_game.IsOrderableUnit(u);
            });
        }

        private void ShowContextForSelection(Vector2 screenPos)
        {
            var primary = PrimaryUnit();
            if (primary == null)
            {
                _game.Ui.HideContextMenu();
                return;
            }

            bool canOrder = _game.IsOrderableUnit(primary);
            if (!canOrder)
            {
                _game.Ui.HideContextMenu();
                return;
            }

            bool anyAttack = OrderableSelectedIds().Any(id =>
            {
                var u = _game.State.GetUnit(id);
                return u != null && u.Stats.CanAttack;
            });
            bool anySupport = OrderableSelectedIds().Any(id =>
            {
                var u = _game.State.GetUnit(id);
                return u != null && u.Stats.CanSupport;
            });
            bool canSplit = !IsMultiSelect && _game.CanAcceptInput;

            _game.Ui.ShowUnitContextMenu(screenPos, primary, canOrder: true, canSplit,
                selectionCount: _selectedIds.Count, anyAttack: anyAttack, anySupport: anySupport);
        }

        private void SyncFocusedSlot()
        {
            var primary = PrimaryUnit();
            if (primary == null) return;
            int count = _game.Orders.Count(primary.Id);
            if (count == 0)
                FocusedSlot = 0;
            else if (FocusedSlot >= count)
                FocusedSlot = count - 1;
        }

        private void UpdateAimPreview()
        {
            var primary = PrimaryUnit();
            if (primary == null || Camera.main == null || !_game.IsOrderableUnit(primary))
            {
                _aimWorld = null;
                _board.SetAimPreview(null);
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                _aimWorld = null;
                _board.SetAimPreview(null);
                return;
            }

            var world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            _aimWorld = world;
            bool showAim = ActiveTool == OrderTool.Move || ActiveTool == OrderTool.Hold || ActiveTool == OrderTool.Auto;
            if (showAim && _game.State.GetUnitAtPoint(world.x, world.y) == null)
                _board.SetAimPreview(new Vector2(world.x, world.y));
            else
                _board.SetAimPreview(null);

            _board.SetOrderPaths(_game.State, _game.Orders, SelectedUnitId, _aimWorld);
        }

        private bool TryOrderClick(Unit clickedUnit, double x, double y, bool append)
        {
            var orderable = OrderableSelectedIds().ToList();
            if (orderable.Count == 0) return false;

            var state = _game.State;
            var primary = state.GetUnit(orderable[orderable.Count - 1]);
            if (primary == null) return false;

            var tool = ActiveTool;

            // Re-select another friendly in Auto when single-select.
            if (tool == OrderTool.Auto && clickedUnit != null && clickedUnit.Owner == primary.Owner
                && clickedUnit.Id != primary.Id && orderable.Count == 1
                && !(primary.Stats.CanSupport && clickedUnit.Hp < clickedUnit.Stats.MaxHp))
            {
                ReplaceSelection(clickedUnit, Input.mousePosition);
                return true;
            }

            if (orderable.Count > 1)
                return TryGroupOrder(orderable, clickedUnit, x, y, append, tool);

            return TrySingleOrder(primary, clickedUnit, x, y, append, tool);
        }

        private bool TrySingleOrder(Unit self, Unit clickedUnit, double x, double y, bool append, OrderTool tool)
        {
            UnitOrder order = null;
            var state = _game.State;

            if (tool == OrderTool.Auto)
            {
                if (clickedUnit != null && clickedUnit.Owner != self.Owner)
                    order = new EngageOrder(clickedUnit.Id);
                else if (clickedUnit != null && clickedUnit.Owner == self.Owner
                         && self.Stats.CanSupport && clickedUnit.Hp < clickedUnit.Stats.MaxHp)
                    order = new SupportOrder(clickedUnit.Id);
                else if (clickedUnit == null)
                    order = ResolveGroundOrder(state, x, y, forceHold: false);
            }
            else if (tool == OrderTool.Engage && clickedUnit != null && clickedUnit.Owner != self.Owner)
                order = new EngageOrder(clickedUnit.Id);
            else if (tool == OrderTool.Support && clickedUnit != null && clickedUnit.Owner == self.Owner)
                order = new SupportOrder(clickedUnit.Id);
            else if (tool == OrderTool.Hold && clickedUnit == null)
                order = ResolveGroundOrder(state, x, y, forceHold: true);
            else if (tool == OrderTool.Move && clickedUnit == null)
                order = new MoveToOrder(x, y);

            if (order == null) return false;
            IssueOrderToUnits(new[] { self.Id }, order, append);
            return true;
        }

        private bool TryGroupOrder(List<int> unitIds, Unit clickedUnit, double x, double y, bool append, OrderTool tool)
        {
            var state = _game.State;
            var primary = state.GetUnit(unitIds[unitIds.Count - 1]);
            if (primary == null) return false;

            if (tool == OrderTool.Auto || tool == OrderTool.Engage)
            {
                if (clickedUnit != null && clickedUnit.Owner != primary.Owner)
                {
                    foreach (int id in unitIds)
                    {
                        var u = state.GetUnit(id);
                        if (u == null || !u.Stats.CanAttack) continue;
                        IssueOrder(id, new EngageOrder(clickedUnit.Id), append);
                    }
                    FinishGroupIssue();
                    return true;
                }
            }

            if ((tool == OrderTool.Auto || tool == OrderTool.Support)
                && clickedUnit != null && clickedUnit.Owner == primary.Owner
                && clickedUnit.Hp < clickedUnit.Stats.MaxHp)
            {
                bool any = false;
                foreach (int id in unitIds)
                {
                    var u = state.GetUnit(id);
                    if (u == null || !u.Stats.CanSupport) continue;
                    IssueOrder(id, new SupportOrder(clickedUnit.Id), append);
                    any = true;
                }
                if (any)
                {
                    FinishGroupIssue();
                    return true;
                }
            }

            if (clickedUnit != null) return false;

            if (tool == OrderTool.Hold || (tool == OrderTool.Auto && false))
            {
                // Hold only when Hold tool is forced; Auto ground = Move for groups.
            }

            if (tool == OrderTool.Hold)
            {
                var hold = ResolveGroundOrder(state, x, y, forceHold: true);
                IssueOrderToUnits(unitIds, hold, append);
                return true;
            }

            if (tool == OrderTool.Move || tool == OrderTool.Auto)
            {
                IssueOrderToUnits(unitIds, new MoveToOrder(x, y), append);
                return true;
            }

            return false;
        }

        private UnitOrder ResolveGroundOrder(GameState state, double x, double y, bool forceHold)
        {
            if (forceHold || ActiveTool == OrderTool.Hold)
            {
                var objective = NearestObjective(state, x, y);
                if (objective != null
                    && Rules.Distance(x, y, objective.X, objective.Y) <= objective.Radius)
                    return new HoldOrder(objective.X, objective.Y, objective.Radius);
                return new HoldOrder(x, y, 2.0);
            }

            return new MoveToOrder(x, y);
        }

        private static Objective NearestObjective(GameState state, double x, double y)
        {
            Objective best = null;
            double bestDist = double.MaxValue;
            foreach (var objective in state.Objectives)
            {
                double d = Rules.Distance(x, y, objective.X, objective.Y);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = objective;
                }
            }
            return best;
        }

        private bool TryAtomicClick(Unit clickedUnit, double x, double y)
        {
            if (!_game.CanAcceptInput || !SelectedUnitId.HasValue) return false;
            var state = _game.State;
            var self = state.GetUnit(SelectedUnitId.Value);
            if (self == null || self.Owner != state.CurrentPlayer) return false;

            if (clickedUnit != null && clickedUnit.Owner != self.Owner)
            {
                var attack = _legalAttacks.FirstOrDefault(a => a.TargetUnitId == clickedUnit.Id);
                if (attack != null && _game.TrySubmitAction(attack))
                {
                    RefreshSelection();
                    return true;
                }
            }

            if (clickedUnit != null && clickedUnit.Owner == self.Owner)
            {
                var heal = _legalHeals.FirstOrDefault(h => h.TargetUnitId == clickedUnit.Id);
                if (heal != null && _game.TrySubmitAction(heal))
                {
                    RefreshSelection();
                    return true;
                }
                var merge = _legalMerges.FirstOrDefault(m => m.AbsorbedUnitId == clickedUnit.Id);
                if (merge != null && _game.TrySubmitAction(merge))
                {
                    SplitMode = false;
                    RefreshSelection();
                    return true;
                }
                ReplaceSelection(clickedUnit, Input.mousePosition);
                return true;
            }

            if (clickedUnit == null)
            {
                if (Rules.ProjectMove(state, self.Id, x, y, out double targetX, out double targetY))
                {
                    var move = new MoveAction { UnitId = self.Id, TargetX = targetX, TargetY = targetY };
                    if (_game.TrySubmitAction(move))
                    {
                        RefreshSelection();
                        return true;
                    }
                }
                return true;
            }

            return false;
        }

        private void IssueOrderToUnits(IEnumerable<int> unitIds, UnitOrder order, bool append)
        {
            foreach (int id in unitIds)
                IssueOrder(id, order, append);
            FinishGroupIssue();
        }

        private void FinishGroupIssue()
        {
            ActiveTool = OrderTool.Auto;
            SyncFocusedSlot();
            _game.Ui.HideContextMenu();
            RefreshSelection();
            _game.RefreshViews();
        }

        private void IssueOrder(int unitId, UnitOrder order, bool append)
        {
            var book = _game.Orders;
            if (append)
            {
                if (!book.Enqueue(unitId, order))
                    Debug.Log($"Order queue full for unit {unitId} (max {OrderBook.MaxDepth})");
            }
            else
            {
                int count = book.Count(unitId);
                if (FocusedSlot < count)
                    book.ReplaceAt(unitId, FocusedSlot, order);
                else if (count < OrderBook.MaxDepth)
                    book.InsertAt(unitId, FocusedSlot, order);
                else
                    book.SetOrReplaceTail(unitId, order);
            }
        }

        public void SelectUnit(int unitId)
        {
            var unit = _game.State?.GetUnit(unitId);
            if (unit == null) return;
            ReplaceSelection(unit, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        }

        public void ToggleSplitMode()
        {
            if (!_game.CanAcceptInput || IsMultiSelect) return;
            var unit = PrimaryUnit();
            if (unit == null || !_game.IsOrderableUnit(unit)) return;
            SplitMode = !SplitMode;
            ActiveTool = OrderTool.Auto;
            RefreshSelection();
        }

        public void ClearSelection()
        {
            _selectedIds.Clear();
            FocusedSlot = 0;
            SplitMode = false;
            ActiveTool = OrderTool.Auto;
            _legalAttacks.Clear();
            _legalHeals.Clear();
            _legalMerges.Clear();
            _aimWorld = null;
            CancelBox();
            if (_board != null)
            {
                _board.ClearHighlights();
                _board.SetAimPreview(null);
                if (_game != null && _game.GameStarted)
                    _board.SetOrderPaths(_game.State, _game.Orders);
            }
            if (_game != null && _game.Ui != null)
            {
                _game.Ui.HideTelemetry();
                _game.Ui.HideOrderStrip();
                _game.Ui.HideContextMenu();
            }
        }

        private void RefreshSelection()
        {
            var state = _game.State;
            if (state == null)
            {
                ClearSelection();
                return;
            }

            // Drop dead / merged units.
            _selectedIds.RemoveAll(id => state.GetUnit(id) == null);
            if (_selectedIds.Count == 0)
            {
                ClearSelection();
                return;
            }

            var primary = PrimaryUnit();
            if (primary == null)
            {
                ClearSelection();
                return;
            }

            var selectedUnits = _selectedIds
                .Select(id => state.GetUnit(id))
                .Where(u => u != null)
                .ToList();

            bool canOrder = _game.IsOrderableUnit(primary);

            if (canOrder)
            {
                _legalAttacks = Rules.GetLegalAttacks(state, primary.Id);
                _legalHeals = Rules.GetLegalHeals(state, primary.Id);
                _legalMerges = Rules.GetLegalMerges(state, primary.Id);

                var targets = _legalAttacks
                    .Select(a => state.GetUnit(a.TargetUnitId))
                    .Where(t => t != null);
                var friendlies = _legalHeals
                    .Select(h => state.GetUnit(h.TargetUnitId))
                    .Concat(_legalMerges.Select(m => state.GetUnit(m.AbsorbedUnitId)))
                    .Where(t => t != null)
                    .Distinct();

                if (!IsMultiSelect)
                {
                    _board.SetMoveRegion(primary, SplitMode
                        ? Rules.GetSplitRegion(state, primary.Id)
                        : Rules.GetMoveRegion(state, primary.Id));
                }
                else
                    _board.ClearMoveRegionPublic();

                _board.SetSelection(selectedUnits, targets, friendlies);
                _board.SetOrderPaths(state, _game.Orders, SelectedUnitId, _aimWorld);
                _game.Ui.ShowOrderStrip(primary.Id, state, _game.Orders, ActiveTool, FocusedSlot,
                    selectionCount: _selectedIds.Count);
            }
            else
            {
                _legalAttacks.Clear();
                _legalHeals.Clear();
                _legalMerges.Clear();
                _board.ClearHighlights();
                _board.SetSelection(selectedUnits, System.Array.Empty<Unit>(), System.Array.Empty<Unit>());
                _game.Ui.HideOrderStrip();
            }

            if (IsMultiSelect)
            {
                _game.Ui.ShowTelemetryGroup(selectedUnits, state);
            }
            else
            {
                _game.Ui.ShowTelemetry(primary, state, SplitMode, inspectOnly: !canOrder);
            }
        }
    }
}
