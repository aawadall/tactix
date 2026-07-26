using System.Collections.Generic;
using System.Linq;
using Tactix.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Tactix.Game
{
    /// <summary>
    /// Mouse interaction in continuous space: click a friendly unit to select it,
    /// click anywhere in the shaded region to dash there, click a ringed enemy to
    /// attack. A click outside the reachable region is projected onto it (the
    /// same clamp a continuous policy's raw output would get), so the unit moves
    /// as far along that heading as the rules allow.
    /// </summary>
    public sealed class InputController : MonoBehaviour
    {
        private GameController _game;
        private BoardRenderer _board;
        private int? _selectedUnitId;
        private List<AttackAction> _legalAttacks = new List<AttackAction>();
        private List<HealAction> _legalHeals = new List<HealAction>();

        public void Init(GameController game, BoardRenderer board)
        {
            _game = game;
            _board = board;
        }

        private void Update()
        {
            if (!_game.CanAcceptInput)
            {
                if (_selectedUnitId.HasValue) ClearSelection();
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                ClearSelection();
                return;
            }

            if (!Input.GetMouseButtonDown(0)) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            var world = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            double x = world.x, y = world.y;
            var state = _game.State;
            var clickedUnit = state.GetUnitAtPoint(x, y);

            // Attack a ringed enemy.
            if (_selectedUnitId.HasValue && clickedUnit != null && clickedUnit.Owner != state.CurrentPlayer)
            {
                var attack = _legalAttacks.FirstOrDefault(a => a.TargetUnitId == clickedUnit.Id);
                if (attack != null && _game.TrySubmitAction(attack))
                {
                    RefreshSelection();
                    return;
                }
            }

            // Treat a ringed casualty (medic/service company).
            if (_selectedUnitId.HasValue && clickedUnit != null && clickedUnit.Owner == state.CurrentPlayer)
            {
                var heal = _legalHeals.FirstOrDefault(h => h.TargetUnitId == clickedUnit.Id);
                if (heal != null && _game.TrySubmitAction(heal))
                {
                    RefreshSelection();
                    return;
                }
            }

            // Select or re-select a friendly unit.
            if (clickedUnit != null && clickedUnit.Owner == state.CurrentPlayer)
            {
                _selectedUnitId = clickedUnit.Id;
                RefreshSelection();
                return;
            }

            // Move: exact target if legal, otherwise the projection of the request.
            if (_selectedUnitId.HasValue && clickedUnit == null)
            {
                int unitId = _selectedUnitId.Value;
                if (Rules.ProjectMove(state, unitId, x, y, out double targetX, out double targetY))
                {
                    var move = new MoveAction { UnitId = unitId, TargetX = targetX, TargetY = targetY };
                    if (_game.TrySubmitAction(move))
                    {
                        RefreshSelection(); // the unit may still attack after moving
                        return;
                    }
                }
                return; // keep the selection so the player can try another point
            }

            if (clickedUnit != null)
            {
                // An enemy that isn't a legal target: just inspect it.
                ClearSelection();
                _game.Ui.ShowTelemetry(clickedUnit, state);
            }
            else
            {
                ClearSelection();
            }
        }

        /// <summary>Selects a unit programmatically (used by the -shots capture mode).</summary>
        public void SelectUnit(int unitId)
        {
            _selectedUnitId = unitId;
            RefreshSelection();
        }

        public void ClearSelection()
        {
            _selectedUnitId = null;
            _legalAttacks.Clear();
            _legalHeals.Clear();
            if (_board != null) _board.ClearHighlights();
            if (_game != null && _game.Ui != null) _game.Ui.HideTelemetry();
        }

        private void RefreshSelection()
        {
            var state = _game.State;
            var unit = _selectedUnitId.HasValue ? state?.GetUnit(_selectedUnitId.Value) : null;
            if (unit == null) // died, or the game ended
            {
                ClearSelection();
                return;
            }

            _legalAttacks = Rules.GetLegalAttacks(state, unit.Id);
            _legalHeals = Rules.GetLegalHeals(state, unit.Id);
            var targets = _legalAttacks
                .Select(a => state.GetUnit(a.TargetUnitId))
                .Where(t => t != null);
            var casualties = _legalHeals
                .Select(h => state.GetUnit(h.TargetUnitId))
                .Where(t => t != null);

            _board.SetMoveRegion(unit, Rules.GetMoveRegion(state, unit.Id));
            _board.SetSelection(unit, targets, casualties);
            _game.Ui.ShowTelemetry(unit, state);
        }
    }
}
