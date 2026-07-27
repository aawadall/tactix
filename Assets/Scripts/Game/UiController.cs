using System.Text;
using Tactix.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tactix.Game
{
    /// <summary>
    /// All uGUI built in code: status banner, End Turn button, mode-select panel,
    /// win screen, unit legend (NATO symbols + capabilities), selected-unit
    /// telemetry, and the orders strip / clock readout. Placeholder styling only.
    /// </summary>
    public sealed class UiController : MonoBehaviour
    {
        private GameController _game;
        private Text _banner;
        private GameObject _endTurnButton;
        private GameObject _legendButton;
        private GameObject _modePanel;
        private GameObject _winPanel;
        private Text _winText;
        private GameObject _legendPanel;
        private GameObject _telemetryPanel;
        private Text _telemetryText;
        private Text _mapButtonLabel;
        private GameObject _manualPanel;
        private Text _manualTitle;
        private Text _manualStats;
        private Text _manualBody;
        private Text _manualKey;
        private GameObject _workshopPanel;
        private Text _workshopSeed;
        private Text _workshopMode;
        private GameObject _orderStrip;
        private Text _orderSummary;
        private Text _orderHint;
        private readonly GameObject[] _slotButtons = new GameObject[OrderBook.MaxDepth];
        private readonly GameObject[] _slotRemoveButtons = new GameObject[OrderBook.MaxDepth];
        private GameObject _contextMenu;

        public bool LegendOpen => _legendPanel != null && _legendPanel.activeSelf;

        public void Init(GameController game)
        {
            _game = game;
            BuildCanvas();
        }

        // ---------- panel switching ----------

        public void ShowModeSelect()
        {
            _modePanel.SetActive(true);
            _winPanel.SetActive(false);
            _legendPanel.SetActive(false);
            _endTurnButton.SetActive(false);
            _legendButton.SetActive(false);
            HideMapWorkshop();
            HideTelemetry();
            HideOrderStrip();
            HideContextMenu();
            _banner.text = "";
        }

        public void HidePanels()
        {
            _modePanel.SetActive(false);
            _winPanel.SetActive(false);
            _legendPanel.SetActive(false);
            _endTurnButton.SetActive(true);
            _legendButton.SetActive(true);
            HideTelemetry();
            HideOrderStrip();
            HideContextMenu();
        }

        public void ShowWinScreen(GameState state)
        {
            _winPanel.SetActive(true);
            _endTurnButton.SetActive(false);
            HideTelemetry();
            HideOrderStrip();
            HideContextMenu();

            string headline = state.Winner.HasValue
                ? $"Player {state.Winner.Value + 1} ({PlayerName(state.Winner.Value)}) wins"
                : "Draw";
            string reason;
            switch (state.Outcome)
            {
                case GameOutcome.Elimination: reason = "enemy force destroyed"; break;
                case GameOutcome.Decapitation: reason = "enemy headquarters destroyed"; break;
                case GameOutcome.Rout: reason = "enemy force broke and routed"; break;
                case GameOutcome.Score: reason = "ahead on points at the turn limit"; break;
                default: reason = "scores level at the turn limit"; break;
            }
            _winText.text = $"{headline}\n<size=22>{reason}   •   {state.Score[0]} – {state.Score[1]}   after {state.TurnNumber - 1} turns</size>";
        }

        // ---------- field manual ----------

        public void ShowFieldManual(UnitType type, Echelon echelon, int page, int pageCount)
        {
            var s = UnitStats.For(type, echelon);
            _modePanel.SetActive(false);
            _winPanel.SetActive(false);
            _legendPanel.SetActive(false);
            _endTurnButton.SetActive(false);
            _legendButton.SetActive(false);
            HideMapWorkshop();
            HideTelemetry();
            HideOrderStrip();
            _banner.text = "";

            _manualPanel.SetActive(true);
            _manualTitle.text = $"{VisualAssets.UnitTypeName(type)} {EchelonScale.DisplayName(echelon)}   ({page}/{pageCount})";

            var stats = new StringBuilder();
            stats.AppendLine($"Move {s.MoveRange:0.##}     HP {s.MaxHp}     Sight {s.Sight:0.##}");
            stats.AppendLine(s.CanAttack
                ? $"Damage {DamageText(s)}     Range {s.AttackRange:0.##}{(s.RequiresLineOfSight ? "  (needs LOS)" : "")}"
                : "Unarmed");
            if (s.CanSupport)
            {
                string treats = s.Supports == SupportTarget.Vehicles ? "vehicles" : "dismounted";
                stats.AppendLine($"Restores +{s.SupportPower} HP to {treats} at range {s.SupportRange:0.##}");
            }
            stats.AppendLine(s.IsVehicle ? "Classed as a vehicle" : "Classed as dismounted");
            stats.Append(s.MovementFriction > 0
                ? $"Order friction: may fall up to {s.MovementFriction * 100:0}% short of a move"
                : "Moves exactly as ordered");
            _manualStats.text = stats.ToString();

            _manualBody.text = FieldManual.Description(type);
            _manualKey.text = FieldManual.OverlayKey;
        }

        /// <summary>Shows a damage roll as "5" when exact, or "5 ±2  (3-7)" when it varies.</summary>
        private static string DamageText(UnitStats s)
        {
            if (s.DamageSpread <= 0) return s.AttackPower.ToString();
            int low = Mathf.Max(0, s.AttackPower - s.DamageSpread);
            return $"{s.AttackPower} ±{s.DamageSpread}  ({low}-{s.AttackPower + s.DamageSpread})";
        }

        public void HideFieldManual()
        {
            if (_manualPanel != null) _manualPanel.SetActive(false);
        }

        // ---------- map workshop ----------

        public void ShowMapWorkshop(MapSpec spec, GameMode mode)
        {
            _modePanel.SetActive(false);
            _winPanel.SetActive(false);
            _legendPanel.SetActive(false);
            _endTurnButton.SetActive(false);
            _legendButton.SetActive(false);
            HideTelemetry();
            HideOrderStrip();
            HideContextMenu();
            if (_manualPanel != null) _manualPanel.SetActive(false);

            _workshopPanel.SetActive(true);
            string source = spec.IsStandard
                ? "Standard map (24×24)"
                : $"Generated {spec.Width}×{spec.Height}";
            string seed = spec.IsStandard || !spec.Seed.HasValue
                ? "—"
                : spec.Seed.Value.ToString();
            _workshopSeed.text = $"{source}\nSeed: {seed}";
            _workshopMode.text = ModeLabel(mode);
            _banner.text = "Preview — Reroll or Start Match";
        }

        public void HideMapWorkshop()
        {
            if (_workshopPanel != null) _workshopPanel.SetActive(false);
        }

        private static string ModeLabel(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Hotseat: return "Hotseat";
                case GameMode.VsBot: return "Vs Bot";
                default: return "Bot vs Bot";
            }
        }

        /// <summary>Keeps the map-source button's label in step with the setting.</summary>
        public void RefreshMapButton()
        {
            if (_mapButtonLabel == null) return;
            _mapButtonLabel.text = _game.UseRandomMap
                ? "Autoplay map: Random"
                : "Autoplay map: Standard";
        }

        public void ToggleLegend()
        {
            _legendPanel.SetActive(!_legendPanel.activeSelf);
        }

        public void CloseLegend()
        {
            _legendPanel.SetActive(false);
        }

        public void UpdateStatus(GameState state, GameMode mode, bool isHumanTurn,
            float clockSeconds = 0f, bool ordersMode = false)
        {
            if (state == null || state.IsOver) return;
            string phase = state.TurnPhase == TurnPhase.Move ? "Move phase" : "Attack phase";
            string actor = isHumanTurn
                ? ""
                : (ordersMode ? "  •  Planning — opponent is moving" : "  •  [bot thinking...]");
            string limit = state.TurnLimit.HasValue ? $"/{state.TurnLimit}" : "";
            string clock = ordersMode && isHumanTurn
                ? $"  •  Clock {clockSeconds:0.0}s"
                : "";
            string autonomy = ordersMode && isHumanTurn
                ? "  •  empty queues act on their own"
                : "";
            _banner.text =
                $"Turn {state.TurnNumber}{limit}  •  Player {state.CurrentPlayer + 1} ({PlayerName(state.CurrentPlayer)})  •  {phase}{clock}{autonomy}{actor}\n" +
                $"<size=17>Score  Blue {state.Score[0]} – {state.Score[1]} Red</size>";
            _endTurnButton.GetComponent<Button>().interactable = isHumanTurn;
        }

        public void ShowOrderStrip(int unitId, GameState state, OrderBook book, InputController.OrderTool tool,
            int focusedSlot, int selectionCount = 1)
        {
            if (_orderStrip == null || state == null) return;
            var unit = state.GetUnit(unitId);
            if (unit == null)
            {
                HideOrderStrip();
                return;
            }

            _orderStrip.SetActive(true);
            var sb = new StringBuilder();
            sb.Append($"Orders · {VisualAssets.UnitDisplayName(unit.Type, unit.Echelon)}");
            if (selectionCount > 1) sb.Append($"  ×{selectionCount}");
            if (tool != InputController.OrderTool.Auto) sb.Append($"  [{tool}]");

            var queue = book.PeekAll(unitId);
            if (queue.Count == 0)
                sb.Append("\n<size=13><color=#aaa>No orders — unit acts on its own</color></size>");
            _orderSummary.text = sb.ToString();

            for (int i = 0; i < OrderBook.MaxDepth; i++)
            {
                bool occupied = i < queue.Count;
                string label = occupied ? $"{i + 1}. {OrderLabel(state, queue[i])}" : $"{i + 1}. (empty)";
                var slotBtn = _slotButtons[i];
                if (slotBtn != null)
                {
                    slotBtn.SetActive(true);
                    var text = slotBtn.GetComponentInChildren<Text>();
                    if (text != null) text.text = label;
                    var img = slotBtn.GetComponent<Image>();
                    if (img != null)
                    {
                        bool focused = i == focusedSlot;
                        img.color = focused
                            ? new Color(0.45f, 0.38f, 0.15f, 0.95f)
                            : occupied
                                ? new Color(0.18f, 0.22f, 0.28f, 0.92f)
                                : new Color(0.12f, 0.13f, 0.16f, 0.75f);
                    }
                }
                if (_slotRemoveButtons[i] != null)
                    _slotRemoveButtons[i].SetActive(occupied);
            }
        }

        public void HideOrderStrip()
        {
            if (_orderStrip != null) _orderStrip.SetActive(false);
        }

        private static string OrderLabel(GameState state, UnitOrder order)
        {
            switch (order)
            {
                case MoveToOrder move: return $"Move({move.X:0.0},{move.Y:0.0})";
                case EngageOrder engage:
                    var enemy = state.GetUnit(engage.TargetUnitId);
                    return enemy != null ? $"Engage {VisualAssets.UnitTypeName(enemy.Type)}" : "Engage ?";
                case HoldOrder hold: return $"Garrison({hold.X:0.0},{hold.Y:0.0})";
                case SupportOrder support:
                    var ally = state.GetUnit(support.TargetUnitId);
                    return ally != null ? $"Support {VisualAssets.UnitTypeName(ally.Type)}" : "Support ?";
                default: return order.Kind;
            }
        }

        // ---------- telemetry ----------

        public void ShowTelemetry(Unit unit, GameState state, bool splitMode = false, bool inspectOnly = false)
        {
            var s = unit.Stats;
            var sb = new StringBuilder();
            if (inspectOnly)
                sb.AppendLine("<color=#aaa>Inspect only — cannot issue orders</color>");
            if (splitMode) sb.AppendLine("[ DETACHING — click where the new unit forms up ]");
            sb.AppendLine($"{VisualAssets.UnitDisplayName(unit.Type, unit.Echelon)}");
            sb.AppendLine($"Player {unit.Owner + 1} ({PlayerName(unit.Owner)})");
            sb.AppendLine($"Health {unit.Hp}/{s.MaxHp}    XP {unit.Xp}");
            sb.AppendLine($"Position ({unit.X:0.0}, {unit.Y:0.0})    Elevation {state.ElevationAtPoint(unit.X, unit.Y)}");
            sb.AppendLine(s.CanAttack
                ? $"Damage {DamageText(s)}    Range {s.AttackRange:0.##}{(s.RequiresLineOfSight ? " (LOS)" : "")}"
                : "Unarmed");
            if (s.CanSupport)
            {
                string treats = s.Supports == SupportTarget.Vehicles ? "vehicles" : "dismounted";
                sb.AppendLine($"Restores +{s.SupportPower} HP    Range {s.SupportRange:0.##}  ({treats})");
            }
            sb.AppendLine($"Move {s.MoveRange:0.##}    Sight {s.Sight:0.##}");

            var notes = new StringBuilder();
            if (state.TerrainAtPoint(unit.X, unit.Y) == TerrainType.Forest) notes.Append("In forest: +1 defense.  ");
            if (state.ElevationAtPoint(unit.X, unit.Y) > 0) notes.Append("High ground: +1 damage vs lower targets.  ");
            if (unit.Owner == state.CurrentPlayer)
            {
                notes.Append($"Moved: {(unit.HasMoved ? "yes" : "no")}   ");
                notes.Append(s.CanSupport
                    ? $"Supported: {(unit.HasSupported ? "yes" : "no")}"
                    : $"Attacked: {(unit.HasAttacked ? "yes" : "no")}");
            }
            sb.Append(notes.Length > 0 ? notes.ToString() : "—");

            _telemetryText.supportRichText = true;
            _telemetryText.text = sb.ToString();
            _telemetryPanel.SetActive(true);
        }

        public void ShowTelemetryGroup(System.Collections.Generic.IEnumerable<Unit> units, GameState state)
        {
            if (units == null || state == null)
            {
                HideTelemetry();
                return;
            }

            var list = new System.Collections.Generic.List<Unit>();
            foreach (var u in units)
                if (u != null) list.Add(u);
            if (list.Count == 0)
            {
                HideTelemetry();
                return;
            }
            if (list.Count == 1)
            {
                ShowTelemetry(list[0], state);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Selected {list.Count} units");
            int hp = 0, maxHp = 0;
            foreach (var u in list)
            {
                hp += u.Hp;
                maxHp += u.Stats.MaxHp;
                sb.AppendLine($"• {VisualAssets.UnitDisplayName(u.Type, u.Echelon)}  HP {u.Hp}/{u.Stats.MaxHp}");
            }
            sb.Append($"Total HP {hp}/{maxHp}");
            _telemetryText.supportRichText = true;
            _telemetryText.text = sb.ToString();
            _telemetryPanel.SetActive(true);
        }

        public void HideContextMenu()
        {
            if (_contextMenu != null) _contextMenu.SetActive(false);
        }

        public void ShowUnitContextMenu(Vector2 screenPos, Unit unit, bool canOrder, bool canSplit,
            int selectionCount = 1, bool anyAttack = false, bool anySupport = false)
        {
            if (_contextMenu == null || unit == null) return;
            _contextMenu.SetActive(true);

            var rect = _contextMenu.GetComponent<RectTransform>();
            float w = rect.sizeDelta.x;
            float h = rect.sizeDelta.y;
            rect.position = new Vector3(
                Mathf.Clamp(screenPos.x + 8f, w * 0.5f + 4f, Screen.width - w * 0.5f - 4f),
                Mathf.Clamp(screenPos.y + 8f, h * 0.5f + 4f, Screen.height - h * 0.5f - 4f),
                0f);

            bool attack = selectionCount > 1 ? anyAttack : unit.Stats.CanAttack;
            bool support = selectionCount > 1 ? anySupport : unit.Stats.CanSupport;
            SetMenuButtonActive("Move", canOrder);
            SetMenuButtonActive("Engage", canOrder && attack);
            SetMenuButtonActive("Support", canOrder && support);
            SetMenuButtonActive("Garrison", canOrder);
            SetMenuButtonActive("Split", canOrder && canSplit && selectionCount <= 1);
            SetMenuButtonActive("ClearQueue", canOrder);
            SetMenuButtonActive("Deselect", true);
        }

        private void SetMenuButtonActive(string name, bool active)
        {
            if (_contextMenu == null) return;
            var t = _contextMenu.transform.Find(name);
            if (t != null) t.gameObject.SetActive(active);
        }

        public void HideTelemetry()
        {
            if (_telemetryPanel != null) _telemetryPanel.SetActive(false);
        }

        private static string PlayerName(int player) => player == 0 ? "Blue" : "Red";

        // ---------- construction ----------

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            canvasGo.AddComponent<GraphicRaycaster>();

            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.transform.SetParent(transform, false);
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<StandaloneInputModule>();

            _banner = MakeText(canvasGo.transform, "Banner", "", 22, TextAnchor.UpperCenter);
            _banner.supportRichText = true;
            Anchor(_banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -12), new Vector2(1000, 56));

            _endTurnButton = MakeButton(canvasGo.transform, "End Turn", () => _game.SubmitEndTurn(),
                new Color(0.25f, 0.35f, 0.5f));
            Anchor(_endTurnButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-110, 45), new Vector2(180, 55));

            _legendButton = MakeButton(canvasGo.transform, "Legend (L)", ToggleLegend,
                new Color(0.32f, 0.32f, 0.38f));
            Anchor(_legendButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-85, -30), new Vector2(140, 42));

            BuildTelemetryPanel(canvasGo.transform);
            BuildOrderStrip(canvasGo.transform);
            BuildContextMenu(canvasGo.transform);
            BuildModePanel(canvasGo.transform);
            BuildWinPanel(canvasGo.transform);
            BuildLegendPanel(canvasGo.transform);
            BuildFieldManualPanel(canvasGo.transform);
            BuildWorkshopPanel(canvasGo.transform);

            _modePanel.SetActive(false);
            _winPanel.SetActive(false);
            _legendPanel.SetActive(false);
            _manualPanel.SetActive(false);
            _workshopPanel.SetActive(false);
            _telemetryPanel.SetActive(false);
            _orderStrip.SetActive(false);
            _contextMenu.SetActive(false);
            _endTurnButton.SetActive(false);
            _legendButton.SetActive(false);
        }

        private void BuildOrderStrip(Transform canvas)
        {
            _orderStrip = MakePanel(canvas, "OrderStrip");
            var rect = _orderStrip.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(12, 12);
            rect.sizeDelta = new Vector2(520, 148);

            var bg = _orderStrip.GetComponent<Image>();
            bg.color = new Color(0.08f, 0.09f, 0.12f, 0.88f);

            _orderSummary = MakeText(_orderStrip.transform, "Summary", "", 15, TextAnchor.UpperLeft);
            _orderSummary.supportRichText = true;
            Anchor(_orderSummary.rectTransform, new Vector2(0f, 1f), new Vector2(12, -8), new Vector2(496, 22));

            for (int i = 0; i < OrderBook.MaxDepth; i++)
            {
                int slot = i;
                var slotGo = MakeButton(_orderStrip.transform, $"{i + 1}. (empty)",
                    () => FindInput()?.SetFocusedSlot(slot),
                    new Color(0.12f, 0.13f, 0.16f, 0.75f));
                slotGo.name = $"Slot{i}";
                Anchor(slotGo.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                    new Vector2(12 + i * 170, -34), new Vector2(158, 28));
                var slotText = slotGo.GetComponentInChildren<Text>();
                if (slotText != null) slotText.fontSize = 13;
                _slotButtons[i] = slotGo;

                var removeGo = MakeButton(_orderStrip.transform, "×",
                    () => FindInput()?.RemoveOrderAtSlot(slot),
                    new Color(0.45f, 0.22f, 0.22f, 0.9f));
                removeGo.name = $"SlotRemove{i}";
                Anchor(removeGo.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                    new Vector2(158 + i * 170, -34), new Vector2(24, 28));
                var removeText = removeGo.GetComponentInChildren<Text>();
                if (removeText != null) removeText.fontSize = 18;
                _slotRemoveButtons[i] = removeGo;
            }

            float x = 12f;
            void Tool(string label, InputController.OrderTool tool, Color color)
            {
                var captured = tool;
                var btn = MakeButton(_orderStrip.transform, label, () => FindInput()?.SetTool(captured), color);
                Anchor(btn.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(x + 40, 38), new Vector2(80, 32));
                x += 86f;
            }

            Tool("Move", InputController.OrderTool.Move, new Color(0.2f, 0.4f, 0.5f));
            Tool("Engage", InputController.OrderTool.Engage, new Color(0.5f, 0.22f, 0.2f));
            Tool("Garrison", InputController.OrderTool.Hold, new Color(0.35f, 0.35f, 0.28f));
            Tool("Support", InputController.OrderTool.Support, new Color(0.2f, 0.45f, 0.28f));

            var undo = MakeButton(_orderStrip.transform, "Undo", () => FindInput()?.UndoLastOrder(),
                new Color(0.32f, 0.32f, 0.38f));
            Anchor(undo.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(x + 36, 38), new Vector2(72, 32));
            x += 80f;
            var clear = MakeButton(_orderStrip.transform, "Clear", () => FindInput()?.ClearSelectedOrders(),
                new Color(0.32f, 0.32f, 0.38f));
            Anchor(clear.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(x + 36, 38), new Vector2(72, 32));

            _orderHint = MakeText(_orderStrip.transform, "Hint",
                "Click slot to edit  •  Shift+click append  •  Z undo  •  Ctrl+click instant  •  RMB cancel",
                12, TextAnchor.LowerLeft);
            _orderHint.color = new Color(0.7f, 0.72f, 0.78f);
            Anchor(_orderHint.rectTransform, new Vector2(0f, 0f), new Vector2(12, 4), new Vector2(496, 18));
        }

        private void BuildContextMenu(Transform canvas)
        {
            _contextMenu = new GameObject("ContextMenu");
            _contextMenu.transform.SetParent(canvas, false);
            var image = _contextMenu.AddComponent<Image>();
            image.color = new Color(0.06f, 0.07f, 0.1f, 0.94f);
            var rect = _contextMenu.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(140, 220);

            float y = -8f;
            void Item(string name, string label, System.Action action)
            {
                var btn = MakeButton(_contextMenu.transform, label, () =>
                {
                    action?.Invoke();
                    HideContextMenu();
                }, new Color(0.22f, 0.24f, 0.3f));
                btn.name = name;
                Anchor(btn.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0, y - 18), new Vector2(124, 30));
                var t = btn.GetComponentInChildren<Text>();
                if (t != null) t.fontSize = 15;
                y -= 34f;
            }

            Item("Move", "Move", () => FindInput()?.SetTool(InputController.OrderTool.Move));
            Item("Engage", "Engage", () => FindInput()?.SetTool(InputController.OrderTool.Engage));
            Item("Support", "Support", () => FindInput()?.SetTool(InputController.OrderTool.Support));
            Item("Garrison", "Garrison", () => FindInput()?.SetTool(InputController.OrderTool.Hold));
            Item("Split", "Split", () => FindInput()?.ToggleSplitMode());
            Item("ClearQueue", "Clear Queue", () => FindInput()?.ClearSelectedOrders());
            Item("Deselect", "Deselect", () => FindInput()?.ClearSelection());

            rect.sizeDelta = new Vector2(140, -y + 8f);
            _contextMenu.SetActive(false);
        }

        private InputController FindInput() =>
            _game != null ? _game.GetComponent<InputController>() : null;

        private void BuildTelemetryPanel(Transform canvas)
        {
            _telemetryPanel = new GameObject("TelemetryPanel");
            _telemetryPanel.transform.SetParent(canvas, false);
            var image = _telemetryPanel.AddComponent<Image>();
            image.color = new Color(0.05f, 0.05f, 0.07f, 0.82f);
            var rect = _telemetryPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(12, 168);
            rect.sizeDelta = new Vector2(300, 190);

            _telemetryText = MakeText(_telemetryPanel.transform, "Body", "", 15, TextAnchor.UpperLeft);
            _telemetryText.horizontalOverflow = HorizontalWrapMode.Wrap; // stay inside the side margin
            var textRect = _telemetryText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14, 10);
            textRect.offsetMax = new Vector2(-14, -10);
        }

        private void BuildModePanel(Transform canvas)
        {
            _modePanel = MakePanel(canvas, "ModePanel");
            var title = MakeText(_modePanel.transform, "Title", "TACTIX", 46, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 210), new Vector2(600, 66));
            var subtitle = MakeText(_modePanel.transform, "Subtitle", "turn-based tactics — pick a mode", 20, TextAnchor.MiddleCenter);
            Anchor(subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 162), new Vector2(600, 30));

            string[] labels = { "Hotseat (2 players)", "Vs Random Bot", "Bot vs Bot (self-play)" };
            GameMode[] modes = { GameMode.Hotseat, GameMode.VsBot, GameMode.BotVsBot };
            for (int i = 0; i < labels.Length; i++)
            {
                var mode = modes[i];
                var button = MakeButton(_modePanel.transform, labels[i], () => _game.OpenMapWorkshop(mode),
                    new Color(0.22f, 0.42f, 0.32f));
                Anchor(button.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, 70 - i * 68), new Vector2(320, 56));
            }

            var mapButton = MakeButton(_modePanel.transform, "", () => _game.ToggleRandomMap(), new Color(0.30f, 0.30f, 0.46f));
            Anchor(mapButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -142), new Vector2(320, 56));
            _mapButtonLabel = mapButton.GetComponentInChildren<Text>();
            RefreshMapButton();

            var manual = MakeButton(_modePanel.transform, "Field Manual", () => _game.ShowFieldManual(), new Color(0.32f, 0.32f, 0.38f));
            Anchor(manual.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -210), new Vector2(320, 56));

            var legend = MakeButton(_modePanel.transform, "Quick Legend", ToggleLegend, new Color(0.32f, 0.32f, 0.38f));
            Anchor(legend.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -278), new Vector2(320, 56));

            var quit = MakeButton(_modePanel.transform, "Quit", () => _game.QuitGame(), new Color(0.5f, 0.24f, 0.22f));
            Anchor(quit.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -346), new Vector2(320, 56));

            var hint = MakeText(_modePanel.transform, "Hint",
                "Pick a mode → Map Workshop → Start Match   •   Esc: back / quit   •   L: legend",
                15, TextAnchor.MiddleCenter);
            Anchor(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 122), new Vector2(800, 28));
        }

        private void BuildWinPanel(Transform canvas)
        {
            _winPanel = MakePanel(canvas, "WinPanel");
            _winText = MakeText(_winPanel.transform, "WinText", "", 40, TextAnchor.MiddleCenter);
            _winText.supportRichText = true;
            Anchor(_winText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(800, 60));
            var newGame = MakeButton(_winPanel.transform, "New Game", () => _game.BackToMenu(),
                new Color(0.22f, 0.42f, 0.32f));
            Anchor(newGame.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(260, 58));
            var quit = MakeButton(_winPanel.transform, "Quit", () => _game.QuitGame(), new Color(0.5f, 0.24f, 0.22f));
            Anchor(quit.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -82), new Vector2(260, 58));
        }

        private void BuildLegendPanel(Transform canvas)
        {
            _legendPanel = MakePanel(canvas, "LegendPanel");
            var title = MakeText(_legendPanel.transform, "Title", "UNIT LEGEND", 30, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 300), new Vector2(600, 44));

            for (int i = 0; i < UnitStats.AllTypes.Length; i++)
            {
                var type = UnitStats.AllTypes[i];
                var s = UnitStats.For(type);
                float y = 250 - i * 48;

                var iconGo = new GameObject($"Icon {type}");
                iconGo.transform.SetParent(_legendPanel.transform, false);
                var icon = iconGo.AddComponent<Image>();
                icon.sprite = VisualAssets.UnitSymbol(type, 0);
                icon.preserveAspect = true;
                Anchor(iconGo.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(-390, y), new Vector2(74, 62));

                string role;
                if (s.CanSupport)
                {
                    string treats = s.Supports == SupportTarget.Vehicles ? "vehicles" : "dismounted";
                    role = $"Unarmed  •  restores +{s.SupportPower} HP to {treats} at range {s.SupportRange:0.#}";
                }
                else
                {
                    string los = s.RequiresLineOfSight ? " (needs LOS)" : "";
                    role = $"Range {s.AttackRange:0.#}{los}  •  Damage {s.AttackPower}";
                }
                var row = MakeText(_legendPanel.transform, $"Row {type}",
                    $"{VisualAssets.UnitTypeName(type)}  (company scale)\nMove {s.MoveRange:0.#}  •  {role}  •  HP {s.MaxHp}  •  Sight {s.Sight:0.#}",
                    16, TextAnchor.MiddleLeft);
                Anchor(row.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(20, y), new Vector2(740, 48));
            }

            (Sprite sprite, Color tint, string text)[] terrain =
            {
                (VisualAssets.Square, VisualAssets.PaperColor, "Open ground — parchment base (no tile squares)"),
                (VisualAssets.ForestSymbol, VisualAssets.ForestInk, "Forest mark — +1 defense to occupant, blocks artillery line of sight"),
                (VisualAssets.RockSymbol, VisualAssets.RockInk, "Rock / impassable — blocks movement and line of sight"),
                (VisualAssets.ObjectiveSymbol, VisualAssets.ObjectiveInk, "Objective — hold the ring for score points"),
                (VisualAssets.Square, VisualAssets.ContourColor, "Contours mark elevation (digit = summit height); thick = cliff, blocks movement"),
            };
            for (int i = 0; i < terrain.Length; i++)
            {
                float y = -100 - i * 34;
                var chipGo = new GameObject($"Chip {i}");
                chipGo.transform.SetParent(_legendPanel.transform, false);
                var chip = chipGo.AddComponent<Image>();
                chip.sprite = terrain[i].sprite;
                chip.color = terrain[i].tint;
                chip.preserveAspect = true;
                Anchor(chipGo.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(-390, y), new Vector2(28, 28));

                var row = MakeText(_legendPanel.transform, $"TerrainRow {i}", terrain[i].text, 16, TextAnchor.MiddleLeft);
                Anchor(row.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(20, y), new Vector2(740, 32));
            }

            var note = MakeText(_legendPanel.transform, "Note",
                "Dismounted = infantry, mech, recon, medic.   Vehicles = armor, artillery, service.\n" +
                "Free movement: a unit dashes in a straight line anywhere inside its shaded region; a click outside it is clamped to the nearest reachable point.\n" +
                "Move any units, then attack — the first attack ends movement for the whole turn. Support is the exception: healing has its own slot and never locks movement.\n" +
                "Red rings = attackable enemies.   Green rings = friendlies you can treat or amalgamate with.   XP: +1 per attack or heal, +2 bonus per kill.\n" +
                "Amalgamate: select a unit, click a green-ringed friendly of the same size — two formations become one a size larger. Press S to detach one back off.",
                15, TextAnchor.MiddleCenter);
            Anchor(note.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -258), new Vector2(960, 86));

            var close = MakeButton(_legendPanel.transform, "Close", CloseLegend, new Color(0.32f, 0.32f, 0.38f));
            Anchor(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -322), new Vector2(200, 42));
        }

        /// <summary>
        /// Side panel for the Field Manual. The demonstration board renders in the
        /// world behind it, so this only occupies the right-hand margin.
        /// </summary>
        private void BuildFieldManualPanel(Transform canvas)
        {
            _manualPanel = new GameObject("FieldManualPanel");
            _manualPanel.transform.SetParent(canvas, false);
            var rect = _manualPanel.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var card = new GameObject("Card");
            card.transform.SetParent(_manualPanel.transform, false);
            card.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 0.93f);
            Anchor(card.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-16, 0), new Vector2(420, 640));

            var header = MakeText(card.transform, "Header", "FIELD MANUAL", 16, TextAnchor.UpperLeft);
            header.color = new Color(0.65f, 0.72f, 0.85f);
            Anchor(header.rectTransform, new Vector2(0f, 1f), new Vector2(22, -18), new Vector2(380, 22));

            _manualTitle = MakeText(card.transform, "Title", "", 24, TextAnchor.UpperLeft);
            Anchor(_manualTitle.rectTransform, new Vector2(0f, 1f), new Vector2(22, -44), new Vector2(380, 34));

            _manualStats = MakeText(card.transform, "Stats", "", 16, TextAnchor.UpperLeft);
            _manualStats.color = new Color(0.85f, 0.90f, 1f);
            Anchor(_manualStats.rectTransform, new Vector2(0f, 1f), new Vector2(22, -86), new Vector2(380, 90));

            _manualBody = MakeText(card.transform, "Body", "", 16, TextAnchor.UpperLeft);
            _manualBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            Anchor(_manualBody.rectTransform, new Vector2(0f, 1f), new Vector2(22, -186), new Vector2(376, 330));

            var prev = MakeButton(card.transform, "< Prev", () => _game.CycleFieldManual(-1), new Color(0.30f, 0.30f, 0.46f));
            Anchor(prev.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(22, 74), new Vector2(120, 44));

            var next = MakeButton(card.transform, "Next >", () => _game.CycleFieldManual(1), new Color(0.30f, 0.30f, 0.46f));
            Anchor(next.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(150, 74), new Vector2(120, 44));

            var back = MakeButton(card.transform, "Back", () => _game.CloseFieldManual(), new Color(0.32f, 0.32f, 0.38f));
            Anchor(back.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(278, 74), new Vector2(120, 44));

            var hint = MakeText(card.transform, "Hint", "← → branch   •   ↑ ↓ formation size   •   Esc to go back", 14, TextAnchor.UpperLeft);
            hint.color = new Color(0.7f, 0.7f, 0.75f);
            Anchor(hint.rectTransform, new Vector2(0f, 0f), new Vector2(22, 44), new Vector2(380, 22));

            _manualKey = MakeText(_manualPanel.transform, "OverlayKey", "", 15, TextAnchor.LowerCenter);
            _manualKey.horizontalOverflow = HorizontalWrapMode.Wrap;
            Anchor(_manualKey.rectTransform, new Vector2(0.5f, 0f), new Vector2(-120, 16), new Vector2(880, 60));
        }

        /// <summary>
        /// Side panel for the Map Workshop. Preview board renders in the world;
        /// this card holds size / reroll / start controls.
        /// </summary>
        private void BuildWorkshopPanel(Transform canvas)
        {
            _workshopPanel = new GameObject("WorkshopPanel");
            _workshopPanel.transform.SetParent(canvas, false);
            var rect = _workshopPanel.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var card = new GameObject("Card");
            card.transform.SetParent(_workshopPanel.transform, false);
            card.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 0.93f);
            Anchor(card.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-16, 0), new Vector2(420, 560));

            var header = MakeText(card.transform, "Header", "MAP WORKSHOP", 16, TextAnchor.UpperLeft);
            header.color = new Color(0.65f, 0.72f, 0.85f);
            Anchor(header.rectTransform, new Vector2(0f, 1f), new Vector2(22, -18), new Vector2(380, 22));

            _workshopMode = MakeText(card.transform, "Mode", "", 22, TextAnchor.UpperLeft);
            Anchor(_workshopMode.rectTransform, new Vector2(0f, 1f), new Vector2(22, -48), new Vector2(380, 30));

            _workshopSeed = MakeText(card.transform, "Seed", "", 16, TextAnchor.UpperLeft);
            _workshopSeed.color = new Color(0.85f, 0.90f, 1f);
            Anchor(_workshopSeed.rectTransform, new Vector2(0f, 1f), new Vector2(22, -90), new Vector2(380, 56));

            var sizeLabel = MakeText(card.transform, "SizeLabel", "Board size", 15, TextAnchor.UpperLeft);
            sizeLabel.color = new Color(0.7f, 0.7f, 0.75f);
            Anchor(sizeLabel.rectTransform, new Vector2(0f, 1f), new Vector2(22, -156), new Vector2(380, 22));

            int[] sizes = { 16, 20, 24, 28 };
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                var sizeBtn = MakeButton(card.transform, size.ToString(),
                    () => _game.WorkshopSetSize(size), new Color(0.30f, 0.30f, 0.46f));
                Anchor(sizeBtn.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                    new Vector2(22 + i * 94, -190), new Vector2(86, 42));
            }

            var reroll = MakeButton(card.transform, "Reroll", () => _game.WorkshopReroll(),
                new Color(0.30f, 0.30f, 0.46f));
            Anchor(reroll.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(22, -250), new Vector2(180, 48));

            var standard = MakeButton(card.transform, "Standard map", () => _game.WorkshopUseStandard(),
                new Color(0.30f, 0.30f, 0.46f));
            Anchor(standard.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(214, -250), new Vector2(180, 48));

            var start = MakeButton(card.transform, "Start Match", () => _game.WorkshopStartMatch(),
                new Color(0.22f, 0.42f, 0.32f));
            Anchor(start.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(22, 74), new Vector2(240, 52));

            var back = MakeButton(card.transform, "Back", () => _game.CloseMapWorkshop(),
                new Color(0.32f, 0.32f, 0.38f));
            Anchor(back.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(278, 74), new Vector2(116, 52));

            var hint = MakeText(card.transform, "Hint",
                "Preview only — Esc to go back. Autoplay still bypasses the workshop.",
                14, TextAnchor.UpperLeft);
            hint.color = new Color(0.7f, 0.7f, 0.75f);
            Anchor(hint.rectTransform, new Vector2(0f, 0f), new Vector2(22, 40), new Vector2(380, 28));
        }

        private static GameObject MakePanel(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.05f, 0.05f, 0.07f, 0.92f);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        private static Text MakeText(Transform parent, string name, string content, int size, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = VisualAssets.UiFont;
            text.text = content;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static GameObject MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color color)
        {
            var go = new GameObject($"Button {label}");
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var text = MakeText(go.transform, "Label", label, 20, TextAnchor.MiddleCenter);
            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }
    }
}
