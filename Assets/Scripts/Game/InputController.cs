using System.Collections.Generic;
using System.Linq;
using Tactix.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Tactix.Game
{
    /// <summary>
    /// Mouse interaction: click a friendly unit to select it, click a highlighted
    /// tile to move, click a highlighted enemy to attack. Highlights always come
    /// from the Rules legal-action functions, so nothing illegal is clickable.
    /// </summary>
    public sealed class InputController : MonoBehaviour
    {
        private GameController _game;
        private BoardRenderer _board;
        private int? _selectedUnitId;
        private List<MoveAction> _legalMoves = new List<MoveAction>();
        private List<AttackAction> _legalAttacks = new List<AttackAction>();

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
            int x = Mathf.RoundToInt(world.x);
            int y = Mathf.RoundToInt(world.y);
            var state = _game.State;

            if (!state.IsInBounds(x, y))
            {
                ClearSelection();
                return;
            }

            var clickedUnit = state.GetUnitAt(x, y);

            // Attack if the clicked enemy is a legal target of the selection.
            if (_selectedUnitId.HasValue && clickedUnit != null && clickedUnit.Owner != state.CurrentPlayer)
            {
                var attack = _legalAttacks.FirstOrDefault(a => a.TargetUnitId == clickedUnit.Id);
                if (attack != null && _game.TrySubmitAction(attack))
                {
                    RefreshSelection(); // keep unit selected so its attack options update
                    return;
                }
            }

            // Move if the clicked tile is a legal destination of the selection.
            if (_selectedUnitId.HasValue && clickedUnit == null)
            {
                var move = _legalMoves.FirstOrDefault(m => m.TargetX == x && m.TargetY == y);
                if (move != null && _game.TrySubmitAction(move))
                {
                    RefreshSelection(); // unit may attack after moving
                    return;
                }
            }

            // (Re)select a friendly unit, or deselect on empty ground.
            if (clickedUnit != null && clickedUnit.Owner == state.CurrentPlayer)
            {
                _selectedUnitId = clickedUnit.Id;
                RefreshSelection();
            }
            else
            {
                ClearSelection();
            }
        }

        public void ClearSelection()
        {
            _selectedUnitId = null;
            _legalMoves.Clear();
            _legalAttacks.Clear();
            if (_board != null) _board.ClearHighlights();
        }

        private void RefreshSelection()
        {
            var state = _game.State;
            var unit = _selectedUnitId.HasValue ? state?.GetUnit(_selectedUnitId.Value) : null;
            if (unit == null) // died or game ended
            {
                ClearSelection();
                return;
            }

            _legalMoves = Rules.GetLegalMoves(state, unit.Id);
            _legalAttacks = Rules.GetLegalAttacks(state, unit.Id);

            var attackTiles = _legalAttacks
                .Select(a => state.GetUnit(a.TargetUnitId))
                .Where(t => t != null)
                .Select(t => (t.X, t.Y));

            _board.SetHighlights(
                (unit.X, unit.Y),
                _legalMoves.Select(m => (m.TargetX, m.TargetY)),
                attackTiles);
        }
    }
}
