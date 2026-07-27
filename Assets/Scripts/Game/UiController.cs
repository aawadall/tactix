using System.Collections.Generic;
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
        private GameObject _workshopSection;
        private GameObject _commandSection;
        private Text _workshopSeed;
        private Text _workshopMode;
        private Text _dockStatus;
        private Text _dockTelemetry;
        private GameObject _dockEndTurn;
        private Image _cameoIcon;
        private Image _cameoHpFill;
        private Text _cameoName;
        private readonly Dictionary<InputController.OrderTool, Image> _orderToolImages =
            new Dictionary<InputController.OrderTool, Image>();
        private readonly Dictionary<InputController.OrderTool, Button> _orderToolButtons =
            new Dictionary<InputController.OrderTool, Button>();
        private Button _dockSplitButton;
        private Button _dockClearButton;
        private Image _dockSplitImage;
        private Image _dockClearImage;
        private GameObject _mapBezel;
        private GameObject _orderStrip;
        private Text _orderSummary;
        private Text _orderHint;
        private readonly GameObject[] _slotButtons = new GameObject[OrderBook.MaxDepth];
        private readonly GameObject[] _slotRemoveButtons = new GameObject[OrderBook.MaxDepth];

        public bool LegendOpen => _legendPanel != null && _legendPanel.activeSelf;

        public void Init(GameController game)
        {
            _game = game;
            BuildCanvas();
        }

        // ---------- panel switching ----------

        public void ShowModeSelect()
        {
            // Shell replaces the old full-screen mode wall — reopen workshop dock.
            _modePanel.SetActive(false);
            _winPanel.SetActive(false);
            _legendPanel.SetActive(false);
            _endTurnButton.SetActive(false);
            _legendButton.SetActive(true);
            HideTelemetry();
            HideOrderStrip();
            HideContextMenu();
            _banner.text = "";
            if (_game != null && !_game.InMapWorkshop)
                _game.OpenMapWorkshop(GameMode.Hotseat);
        }

        public void HidePanels()
        {
            ShowCommandDock();
        }

        /// <summary>In-match C&amp;C dock: status, telemetry, orders, End Turn.</summary>
        public void ShowCommandDock()
        {
            _modePanel.SetActive(false);
            _winPanel.SetActive(false);
            _legendPanel.SetActive(false);
            _workshopPanel.SetActive(true);
            if (_workshopSection != null) _workshopSection.SetActive(false);
            if (_commandSection != null) _commandSection.SetActive(true);
            _endTurnButton.SetActive(false);
            _legendButton.SetActive(true);
            if (_dockEndTurn != null) _dockEndTurn.SetActive(true);
            HideContextMenu();
            _banner.text = "";
        }

        public void ShowWinScreen(GameState state)
        {
            _winPanel.SetActive(true);
            _endTurnButton.SetActive(false);
            if (_dockEndTurn != null) _dockEndTurn.SetActive(false);
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
            _legendButton.SetActive(true);
            HideTelemetry();
            HideOrderStrip();
            HideContextMenu();
            if (_manualPanel != null) _manualPanel.SetActive(false);

            _workshopPanel.SetActive(true);
            if (_workshopSection != null) _workshopSection.SetActive(true);
            if (_commandSection != null) _commandSection.SetActive(false);

            string source = spec.IsStandard
                ? "Standard map (24×24)"
                : $"Generated {spec.Width}×{spec.Height}";
            string seed = spec.IsStandard || !spec.Seed.HasValue
                ? "—"
                : spec.Seed.Value.ToString();
            _workshopSeed.text = $"{source}\nSeed: {seed}";
            _workshopMode.text = ModeLabel(mode);
            _banner.text = "";
        }

        public void HideMapWorkshop()
        {
            if (_workshopSection != null) _workshopSection.SetActive(false);
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
                $"Turn {state.TurnNumber}{limit}  •  {PlayerName(state.CurrentPlayer)}  •  {phase}{clock}{actor}";
            if (_dockStatus != null)
            {
                _dockStatus.color = VisualAssets.HudAccentGreen;
                _dockStatus.text =
                    $"TURN {state.TurnNumber}{limit}\n" +
                    $"{PlayerName(state.CurrentPlayer).ToUpperInvariant()}  ·  {phase.ToUpperInvariant()}{clock}\n" +
                    $"SCORE  {state.Score[0]} – {state.Score[1]}{autonomy}{actor}";
            }
            if (_dockEndTurn != null)
                _dockEndTurn.GetComponent<Button>().interactable = isHumanTurn;
            if (_endTurnButton != null)
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
            // Queue strip lives under the order grid inside the command dock.
            if (_commandSection != null && _commandSection.activeSelf
                && _orderStrip.transform.parent != _commandSection.transform)
            {
                _orderStrip.transform.SetParent(_commandSection.transform, false);
                var r = _orderStrip.GetComponent<RectTransform>();
                r.anchorMin = new Vector2(0f, 1f);
                r.anchorMax = new Vector2(1f, 1f);
                r.pivot = new Vector2(0.5f, 1f);
                r.anchoredPosition = new Vector2(0, -356);
                r.sizeDelta = new Vector2(-24, 96);
                var bg = _orderStrip.GetComponent<Image>();
                if (bg != null) bg.color = VisualAssets.HudPanelInner;
            }

            var sb = new StringBuilder();
            sb.Append($"QUEUE · {VisualAssets.UnitDisplayName(unit.Type, unit.Echelon).ToUpperInvariant()}");
            if (selectionCount > 1) sb.Append($"  ×{selectionCount}");
            if (tool != InputController.OrderTool.Auto) sb.Append($"  [{tool}]");

            var queue = book.PeekAll(unitId);
            if (queue.Count == 0)
                sb.Append("\n<size=12><color=#8a8e72>Empty — unit acts on its own</color></size>");
            _orderSummary.text = sb.ToString();
            _orderSummary.color = VisualAssets.HudAccent;

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
                            ? VisualAssets.HudButtonHot
                            : occupied
                                ? VisualAssets.HudPanelInner
                                : VisualAssets.HudButton;
                    }
                }
                if (_slotRemoveButtons[i] != null)
                    _slotRemoveButtons[i].SetActive(occupied);
            }

            // Highlight active order tool on the C&C dock grid (skip disabled).
            foreach (var kv in _orderToolImages)
            {
                if (kv.Value == null) continue;
                bool enabled = _orderToolButtons.TryGetValue(kv.Key, out var btn) && btn != null && btn.interactable;
                if (!enabled) continue;
                kv.Value.color = kv.Key == tool ? VisualAssets.HudButtonHot : VisualAssets.HudButton;
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
            UpdateCameo(unit);

            var sb = new StringBuilder();
            if (inspectOnly)
                sb.AppendLine("<color=#888>Inspect only</color>");
            if (splitMode) sb.AppendLine("[ DETACH ]");
            sb.AppendLine($"HP {unit.Hp}/{s.MaxHp}   XP {unit.Xp}");
            sb.Append($"({unit.X:0.0},{unit.Y:0.0})  elev {state.ElevationAtPoint(unit.X, unit.Y)}");
            ApplyTelemetryText(sb.ToString());
        }

        private void UpdateCameo(Unit unit)
        {
            if (_cameoName != null)
                _cameoName.text = VisualAssets.UnitDisplayName(unit.Type, unit.Echelon).ToUpperInvariant();
            if (_cameoIcon != null)
            {
                _cameoIcon.sprite = VisualAssets.UnitSymbol(unit.Type, unit.Owner, unit.Echelon);
                _cameoIcon.color = Color.white;
            }
            if (_cameoHpFill != null)
            {
                float pct = unit.Stats.MaxHp <= 0 ? 0f : (float)unit.Hp / unit.Stats.MaxHp;
                var r = _cameoHpFill.rectTransform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = new Vector2(Mathf.Clamp01(pct), 1f);
                r.offsetMin = Vector2.zero;
                r.offsetMax = Vector2.zero;
            }
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
            UpdateCameo(list[0]);
            if (_cameoName != null)
                _cameoName.text = $"{list.Count} UNITS SELECTED";
            ApplyTelemetryText(sb.ToString());
        }

        private void ApplyTelemetryText(string body)
        {
            if (_telemetryText != null)
            {
                _telemetryText.supportRichText = true;
                _telemetryText.text = body;
            }
            if (_dockTelemetry != null)
            {
                _dockTelemetry.supportRichText = true;
                _dockTelemetry.text = body;
            }
            bool useDock = _commandSection != null && _commandSection.activeSelf;
            if (_telemetryPanel != null) _telemetryPanel.SetActive(!useDock);
        }

        /// <summary>No floating menu — dock owns orders. Clears tool enablement when nothing is selected.</summary>
        public void HideContextMenu()
        {
            UpdateDockOrderTools(null, canOrder: false, canSplit: false);
        }

        /// <summary>
        /// Enable/grey dock order buttons for the selection. Actions live only on the side panel.
        /// </summary>
        public void UpdateDockOrderTools(Unit unit, bool canOrder, bool canSplit,
            int selectionCount = 1, bool anyAttack = false, bool anySupport = false)
        {
            bool attack = unit != null && (selectionCount > 1 ? anyAttack : unit.Stats.CanAttack);
            bool support = unit != null && (selectionCount > 1 ? anySupport : unit.Stats.CanSupport);
            bool splitOk = canOrder && canSplit && selectionCount <= 1;

            SetDockTool(InputController.OrderTool.Move, canOrder);
            SetDockTool(InputController.OrderTool.Engage, canOrder && attack);
            SetDockTool(InputController.OrderTool.Support, canOrder && support);
            SetDockTool(InputController.OrderTool.Hold, canOrder);
            SetDockButton(_dockSplitButton, _dockSplitImage, splitOk);
            SetDockButton(_dockClearButton, _dockClearImage, canOrder);
        }

        private void SetDockTool(InputController.OrderTool tool, bool enabled)
        {
            _orderToolButtons.TryGetValue(tool, out var btn);
            _orderToolImages.TryGetValue(tool, out var img);
            SetDockButton(btn, img, enabled);
        }

        private static void SetDockButton(Button btn, Image img, bool enabled)
        {
            if (btn != null) btn.interactable = enabled;
            if (img != null)
                img.color = enabled ? VisualAssets.HudButton : VisualAssets.HudDisabled;
        }

        public void HideTelemetry()
        {
            if (_telemetryPanel != null) _telemetryPanel.SetActive(false);
            if (_dockTelemetry != null) _dockTelemetry.text = "";
            if (_cameoName != null) _cameoName.text = "NO UNIT SELECTED";
            if (_cameoIcon != null) { _cameoIcon.sprite = null; _cameoIcon.color = VisualAssets.HudMuted; }
            if (_cameoHpFill != null)
            {
                var r = _cameoHpFill.rectTransform;
                r.anchorMax = new Vector2(0f, 1f);
            }
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

            _banner = MakeText(canvasGo.transform, "Banner", "", 16, TextAnchor.UpperLeft);
            _banner.supportRichText = true;
            _banner.color = VisualAssets.HudAccent;
            Anchor(_banner.rectTransform, new Vector2(0f, 1f), new Vector2(24, -14), new Vector2(760, 36));

            BuildMapBezel(canvasGo.transform);

            _endTurnButton = MakeHudButton(canvasGo.transform, "End Turn", () => _game.SubmitEndTurn(),
                VisualAssets.HudButtonPrimary);
            Anchor(_endTurnButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-110, 45), new Vector2(180, 55));

            _legendButton = MakeHudButton(canvasGo.transform, "Legend (L)", ToggleLegend, VisualAssets.HudButton);
            Anchor(_legendButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(90, -52), new Vector2(140, 32));
            var legendLabel = _legendButton.GetComponentInChildren<Text>();
            if (legendLabel != null) legendLabel.fontSize = 14;

            BuildTelemetryPanel(canvasGo.transform);
            BuildOrderStrip(canvasGo.transform);
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
            _endTurnButton.SetActive(false);
            _legendButton.SetActive(false);
            UpdateDockOrderTools(null, canOrder: false, canSplit: false);
        }

        private void BuildOrderStrip(Transform canvas)
        {
            // Slim three-slot queue; dock owns Move/Engage/etc. buttons.
            _orderStrip = MakePanel(canvas, "OrderStrip");
            var rect = _orderStrip.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(12, 12);
            rect.sizeDelta = new Vector2(400, 96);

            var bg = _orderStrip.GetComponent<Image>();
            bg.color = VisualAssets.HudPanelInner;

            _orderSummary = MakeText(_orderStrip.transform, "Summary", "", 13, TextAnchor.UpperLeft);
            _orderSummary.supportRichText = true;
            _orderSummary.color = VisualAssets.HudAccent;
            Anchor(_orderSummary.rectTransform, new Vector2(0f, 1f), new Vector2(10, -6), new Vector2(380, 20));

            for (int i = 0; i < OrderBook.MaxDepth; i++)
            {
                int slot = i;
                var slotGo = MakeButton(_orderStrip.transform, $"{i + 1}. (empty)",
                    () => FindInput()?.SetFocusedSlot(slot),
                    VisualAssets.HudButton);
                slotGo.name = $"Slot{i}";
                Anchor(slotGo.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                    new Vector2(10 + i * 128, -30), new Vector2(110, 26));
                var slotText = slotGo.GetComponentInChildren<Text>();
                if (slotText != null)
                {
                    slotText.fontSize = 12;
                    slotText.color = VisualAssets.HudBody;
                }
                _slotButtons[i] = slotGo;

                var removeGo = MakeButton(_orderStrip.transform, "×",
                    () => FindInput()?.RemoveOrderAtSlot(slot),
                    VisualAssets.HudButtonDanger);
                removeGo.name = $"SlotRemove{i}";
                Anchor(removeGo.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                    new Vector2(112 + i * 128, -30), new Vector2(22, 26));
                var removeText = removeGo.GetComponentInChildren<Text>();
                if (removeText != null) removeText.fontSize = 14;
                _slotRemoveButtons[i] = removeGo;
            }

            _orderHint = MakeText(_orderStrip.transform, "Hint",
                "Slot = edit  ·  Shift+click append  ·  Z undo  ·  RMB cancel",
                11, TextAnchor.LowerLeft);
            _orderHint.color = VisualAssets.HudMuted;
            Anchor(_orderHint.rectTransform, new Vector2(0f, 0f), new Vector2(10, 6), new Vector2(380, 16));
        }

        private InputController FindInput() =>
            _game != null ? _game.GetComponent<InputController>() : null;

        private void BuildTelemetryPanel(Transform canvas)
        {
            _telemetryPanel = new GameObject("TelemetryPanel");
            _telemetryPanel.transform.SetParent(canvas, false);
            var image = _telemetryPanel.AddComponent<Image>();
            image.color = VisualAssets.HudMenuPanel;
            AddBevel(_telemetryPanel.transform);
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
            AddBevel(_winPanel.transform);
            _winText = MakeText(_winPanel.transform, "WinText", "", 36, TextAnchor.MiddleCenter);
            _winText.supportRichText = true;
            _winText.color = VisualAssets.HudAccent;
            Anchor(_winText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(800, 60));
            var newGame = MakeHudButton(_winPanel.transform, "NEW GAME", () => _game.BackToMenu(),
                VisualAssets.HudButtonPrimary);
            Anchor(newGame.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(260, 58));
            var quit = MakeHudButton(_winPanel.transform, "ABORT", () => _game.QuitGame(), VisualAssets.HudButtonDanger);
            Anchor(quit.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -82), new Vector2(260, 58));
        }

        private void BuildLegendPanel(Transform canvas)
        {
            _legendPanel = MakePanel(canvas, "LegendPanel");
            AddBevel(_legendPanel.transform);
            var title = MakeText(_legendPanel.transform, "Title", "UNIT LEGEND", 28, TextAnchor.MiddleCenter);
            title.color = VisualAssets.HudAccent;
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

            var close = MakeHudButton(_legendPanel.transform, "CLOSE", CloseLegend, VisualAssets.HudButton);
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
            card.AddComponent<Image>().color = VisualAssets.HudPanel;
            Anchor(card.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-16, 0), new Vector2(420, 640));
            AddBevel(card.transform);

            var header = MakeText(card.transform, "Header", "FIELD MANUAL", 14, TextAnchor.UpperLeft);
            header.color = VisualAssets.HudAccent;
            Anchor(header.rectTransform, new Vector2(0f, 1f), new Vector2(22, -18), new Vector2(380, 22));

            _manualTitle = MakeText(card.transform, "Title", "", 22, TextAnchor.UpperLeft);
            _manualTitle.color = VisualAssets.HudBody;
            Anchor(_manualTitle.rectTransform, new Vector2(0f, 1f), new Vector2(22, -44), new Vector2(380, 34));

            _manualStats = MakeText(card.transform, "Stats", "", 15, TextAnchor.UpperLeft);
            _manualStats.color = VisualAssets.HudAccentGreen;
            Anchor(_manualStats.rectTransform, new Vector2(0f, 1f), new Vector2(22, -86), new Vector2(380, 90));

            _manualBody = MakeText(card.transform, "Body", "", 15, TextAnchor.UpperLeft);
            _manualBody.color = VisualAssets.HudBody;
            _manualBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            Anchor(_manualBody.rectTransform, new Vector2(0f, 1f), new Vector2(22, -186), new Vector2(376, 330));

            var prev = MakeHudButton(card.transform, "< PREV", () => _game.CycleFieldManual(-1), VisualAssets.HudButton);
            Anchor(prev.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(22, 74), new Vector2(120, 44));

            var next = MakeHudButton(card.transform, "NEXT >", () => _game.CycleFieldManual(1), VisualAssets.HudButton);
            Anchor(next.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(150, 74), new Vector2(120, 44));

            var back = MakeHudButton(card.transform, "BACK", () => _game.CloseFieldManual(), VisualAssets.HudButton);
            Anchor(back.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(278, 74), new Vector2(120, 44));

            var hint = MakeText(card.transform, "Hint", "← → branch   ·   ↑ ↓ formation size   ·   Esc back", 13, TextAnchor.UpperLeft);
            hint.color = VisualAssets.HudMuted;
            Anchor(hint.rectTransform, new Vector2(0f, 0f), new Vector2(22, 44), new Vector2(380, 22));

            _manualKey = MakeText(_manualPanel.transform, "OverlayKey", "", 14, TextAnchor.LowerCenter);
            _manualKey.color = VisualAssets.HudBody;
            _manualKey.horizontalOverflow = HorizontalWrapMode.Wrap;
            Anchor(_manualKey.rectTransform, new Vector2(0.5f, 0f), new Vector2(-120, 16), new Vector2(880, 60));
        }

        /// <summary>
        /// Persistent right Command Dock — C&amp;C industrial sidebar.
        /// Workshop section pre-match; command section in-match.
        /// </summary>
        private void BuildWorkshopPanel(Transform canvas)
        {
            _workshopPanel = new GameObject("CommandDock");
            _workshopPanel.transform.SetParent(canvas, false);
            StretchFull(_workshopPanel.AddComponent<RectTransform>());

            var card = new GameObject("Card");
            card.transform.SetParent(_workshopPanel.transform, false);
            card.AddComponent<Image>().color = VisualAssets.HudPanel;
            var cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(1f, 0f);
            cardRect.anchorMax = new Vector2(1f, 1f);
            cardRect.pivot = new Vector2(1f, 0.5f);
            cardRect.anchoredPosition = new Vector2(-4, 0);
            cardRect.sizeDelta = new Vector2(420, -8);
            AddBevel(card.transform);

            // ---- Workshop / briefing ----
            _workshopSection = new GameObject("WorkshopSection");
            _workshopSection.transform.SetParent(card.transform, false);
            StretchFull(_workshopSection.AddComponent<RectTransform>());

            var header = MakeText(_workshopSection.transform, "Header", "BRIEFING  ·  MAP SELECT", 14, TextAnchor.UpperLeft);
            header.color = VisualAssets.HudAccent;
            Anchor(header.rectTransform, new Vector2(0f, 1f), new Vector2(16, -14), new Vector2(380, 20));

            var modeLabel = MakeText(_workshopSection.transform, "ModeLabel", "FORCE MODE", 12, TextAnchor.UpperLeft);
            modeLabel.color = VisualAssets.HudMuted;
            Anchor(modeLabel.rectTransform, new Vector2(0f, 1f), new Vector2(16, -42), new Vector2(380, 16));

            string[] modeNames = { "Hotseat", "Vs Bot", "Bot vs Bot" };
            GameMode[] modes = { GameMode.Hotseat, GameMode.VsBot, GameMode.BotVsBot };
            for (int i = 0; i < modeNames.Length; i++)
            {
                var m = modes[i];
                var btn = MakeHudButton(_workshopSection.transform, modeNames[i],
                    () => _game.SetPendingMode(m), VisualAssets.HudButton);
                Anchor(btn.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                    new Vector2(16 + i * 128, -64), new Vector2(120, 34));
                var t = btn.GetComponentInChildren<Text>();
                if (t != null) { t.fontSize = 13; t.color = VisualAssets.HudBody; }
            }

            _workshopMode = MakeText(_workshopSection.transform, "Mode", "", 18, TextAnchor.UpperLeft);
            _workshopMode.color = VisualAssets.HudAccentGreen;
            Anchor(_workshopMode.rectTransform, new Vector2(0f, 1f), new Vector2(16, -110), new Vector2(380, 24));

            _workshopSeed = MakeText(_workshopSection.transform, "Seed", "", 14, TextAnchor.UpperLeft);
            _workshopSeed.color = VisualAssets.HudBody;
            Anchor(_workshopSeed.rectTransform, new Vector2(0f, 1f), new Vector2(16, -142), new Vector2(380, 48));

            var sizeLabel = MakeText(_workshopSection.transform, "SizeLabel", "THEATER SIZE", 12, TextAnchor.UpperLeft);
            sizeLabel.color = VisualAssets.HudMuted;
            Anchor(sizeLabel.rectTransform, new Vector2(0f, 1f), new Vector2(16, -200), new Vector2(380, 16));

            int[] sizes = { 16, 20, 24, 28 };
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                var sizeBtn = MakeHudButton(_workshopSection.transform, size.ToString(),
                    () => _game.WorkshopSetSize(size), VisualAssets.HudButton);
                Anchor(sizeBtn.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                    new Vector2(16 + i * 94, -224), new Vector2(86, 36));
                var st = sizeBtn.GetComponentInChildren<Text>();
                if (st != null) { st.fontSize = 14; st.color = VisualAssets.HudBody; }
            }

            var reroll = MakeHudButton(_workshopSection.transform, "REROLL", () => _game.WorkshopReroll(), VisualAssets.HudButton);
            Anchor(reroll.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(16, -278), new Vector2(180, 40));
            var standard = MakeHudButton(_workshopSection.transform, "STANDARD", () => _game.WorkshopUseStandard(), VisualAssets.HudButton);
            Anchor(standard.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(208, -278), new Vector2(180, 40));

            var start = MakeHudButton(_workshopSection.transform, "DEPLOY  ·  START", () => _game.WorkshopStartMatch(),
                VisualAssets.HudButtonPrimary);
            Anchor(start.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(16, 68), new Vector2(250, 48));
            var quit = MakeHudButton(_workshopSection.transform, "ABORT", () => _game.QuitGame(), VisualAssets.HudButtonDanger);
            Anchor(quit.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(278, 68), new Vector2(110, 48));

            var manual = MakeHudButton(_workshopSection.transform, "FIELD MANUAL", () => _game.ShowFieldManual(), VisualAssets.HudButton);
            Anchor(manual.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(16, 16), new Vector2(180, 36));
            var legend = MakeHudButton(_workshopSection.transform, "LEGEND", ToggleLegend, VisualAssets.HudButton);
            Anchor(legend.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(208, 16), new Vector2(180, 36));

            // ---- Command section ----
            _commandSection = new GameObject("CommandSection");
            _commandSection.transform.SetParent(card.transform, false);
            StretchFull(_commandSection.AddComponent<RectTransform>());
            _commandSection.SetActive(false);

            var cmdHeader = MakeText(_commandSection.transform, "Header", "COMMAND", 14, TextAnchor.UpperLeft);
            cmdHeader.color = VisualAssets.HudAccent;
            Anchor(cmdHeader.rectTransform, new Vector2(0f, 1f), new Vector2(16, -12), new Vector2(380, 18));

            _dockStatus = MakeText(_commandSection.transform, "Status", "", 13, TextAnchor.UpperLeft);
            _dockStatus.supportRichText = true;
            _dockStatus.color = VisualAssets.HudAccentGreen;
            Anchor(_dockStatus.rectTransform, new Vector2(0f, 1f), new Vector2(16, -36), new Vector2(380, 72));

            // Cameo frame
            var cameoFrame = new GameObject("Cameo");
            cameoFrame.transform.SetParent(_commandSection.transform, false);
            cameoFrame.AddComponent<Image>().color = VisualAssets.HudPanelInner;
            Anchor(cameoFrame.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(16, -118), new Vector2(88, 72));
            AddBevel(cameoFrame.transform);

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(cameoFrame.transform, false);
            _cameoIcon = iconGo.AddComponent<Image>();
            _cameoIcon.preserveAspect = true;
            _cameoIcon.color = Color.white;
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.1f, 0.1f);
            iconRect.anchorMax = new Vector2(0.9f, 0.9f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            _cameoName = MakeText(_commandSection.transform, "CameoName", "NO UNIT SELECTED", 14, TextAnchor.UpperLeft);
            _cameoName.color = VisualAssets.HudBody;
            Anchor(_cameoName.rectTransform, new Vector2(0f, 1f), new Vector2(116, -118), new Vector2(270, 22));

            var hpBack = new GameObject("HpBack");
            hpBack.transform.SetParent(_commandSection.transform, false);
            hpBack.AddComponent<Image>().color = VisualAssets.HudHpBack;
            Anchor(hpBack.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(116, -146), new Vector2(270, 12));
            var hpFill = new GameObject("HpFill");
            hpFill.transform.SetParent(hpBack.transform, false);
            _cameoHpFill = hpFill.AddComponent<Image>();
            _cameoHpFill.color = VisualAssets.HudHpFill;
            var hpRect = hpFill.GetComponent<RectTransform>();
            hpRect.anchorMin = Vector2.zero;
            hpRect.anchorMax = new Vector2(1f, 1f);
            hpRect.pivot = new Vector2(0f, 0.5f);
            hpRect.offsetMin = Vector2.zero;
            hpRect.offsetMax = Vector2.zero;

            _dockTelemetry = MakeText(_commandSection.transform, "Telemetry", "", 12, TextAnchor.UpperLeft);
            _dockTelemetry.supportRichText = true;
            _dockTelemetry.color = VisualAssets.HudMuted;
            _dockTelemetry.horizontalOverflow = HorizontalWrapMode.Wrap;
            Anchor(_dockTelemetry.rectTransform, new Vector2(0f, 1f), new Vector2(116, -164), new Vector2(270, 40));

            var ordersLabel = MakeText(_commandSection.transform, "OrdersLabel", "ORDERS", 12, TextAnchor.UpperLeft);
            ordersLabel.color = VisualAssets.HudMuted;
            Anchor(ordersLabel.rectTransform, new Vector2(0f, 1f), new Vector2(16, -208), new Vector2(380, 16));

            // Order tool grid 2x3
            (string label, InputController.OrderTool tool)[] tools =
            {
                ("MOVE", InputController.OrderTool.Move),
                ("ENGAGE", InputController.OrderTool.Engage),
                ("SUPPORT", InputController.OrderTool.Support),
                ("GARRISON", InputController.OrderTool.Hold),
                ("SPLIT", InputController.OrderTool.Auto), // split handled separately
            };
            for (int i = 0; i < 4; i++)
            {
                int col = i % 2, row = i / 2;
                var tool = tools[i].tool;
                var label = tools[i].label;
                var btn = MakeHudButton(_commandSection.transform, label, () => FindInput()?.SetTool(tool), VisualAssets.HudButton);
                Anchor(btn.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                    new Vector2(16 + col * 190, -232 - row * 40), new Vector2(180, 36));
                var lt = btn.GetComponentInChildren<Text>();
                if (lt != null) { lt.fontSize = 13; lt.color = VisualAssets.HudBody; }
                _orderToolImages[tool] = btn.GetComponent<Image>();
                _orderToolButtons[tool] = btn.GetComponent<Button>();
            }
            var splitBtn = MakeHudButton(_commandSection.transform, "SPLIT", () => FindInput()?.ToggleSplitMode(), VisualAssets.HudButton);
            Anchor(splitBtn.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(16, -312), new Vector2(180, 36));
            var clearBtn = MakeHudButton(_commandSection.transform, "CLEAR Q", () => FindInput()?.ClearSelectedOrders(), VisualAssets.HudButton);
            Anchor(clearBtn.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(206, -312), new Vector2(180, 36));
            foreach (var b in new[] { splitBtn, clearBtn })
            {
                var lt = b.GetComponentInChildren<Text>();
                if (lt != null) { lt.fontSize = 13; lt.color = VisualAssets.HudBody; }
            }
            _dockSplitButton = splitBtn.GetComponent<Button>();
            _dockClearButton = clearBtn.GetComponent<Button>();
            _dockSplitImage = splitBtn.GetComponent<Image>();
            _dockClearImage = clearBtn.GetComponent<Image>();
            UpdateDockOrderTools(null, canOrder: false, canSplit: false);

            _dockEndTurn = MakeHudButton(_commandSection.transform, "END TURN", () => _game.SubmitEndTurn(),
                VisualAssets.HudButtonPrimary);
            Anchor(_dockEndTurn.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(16, 16), new Vector2(250, 48));
            var endLabel = _dockEndTurn.GetComponentInChildren<Text>();
            if (endLabel != null) { endLabel.fontSize = 18; endLabel.color = VisualAssets.HudAccent; }

            var backMatch = MakeHudButton(_commandSection.transform, "MENU", () => _game.BackToMenu(), VisualAssets.HudButton);
            Anchor(backMatch.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(278, 16), new Vector2(110, 48));
        }

        private void BuildMapBezel(Transform canvas)
        {
            _mapBezel = new GameObject("MapBezel");
            _mapBezel.transform.SetParent(canvas, false);
            _mapBezel.transform.SetAsFirstSibling();
            var rect = _mapBezel.AddComponent<RectTransform>();
            // Frame the left ~66% of the screen (map area), leaving the dock clear.
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0.66f, 1f);
            rect.offsetMin = new Vector2(4, 4);
            rect.offsetMax = new Vector2(-4, -4);
            // Hollow frame: four edge strips
            void Edge(string name, Vector2 aMin, Vector2 aMax)
            {
                var go = new GameObject(name);
                go.transform.SetParent(_mapBezel.transform, false);
                var img = go.AddComponent<Image>();
                img.color = VisualAssets.HudMapBezel;
                img.raycastTarget = false;
                var r = go.GetComponent<RectTransform>();
                r.anchorMin = aMin;
                r.anchorMax = aMax;
                r.offsetMin = Vector2.zero;
                r.offsetMax = Vector2.zero;
            }
            const float t = 0.012f;
            Edge("Top", new Vector2(0, 1f - t), Vector2.one);
            Edge("Bottom", Vector2.zero, new Vector2(1, t));
            Edge("Left", Vector2.zero, new Vector2(t, 1));
            Edge("Right", new Vector2(1f - t, 0), Vector2.one);
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void AddBevel(Transform parent)
        {
            void Strip(string name, Vector2 aMin, Vector2 aMax, Color c)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                var img = go.AddComponent<Image>();
                img.color = c;
                img.raycastTarget = false;
                var r = go.GetComponent<RectTransform>();
                r.anchorMin = aMin;
                r.anchorMax = aMax;
                r.offsetMin = Vector2.zero;
                r.offsetMax = Vector2.zero;
            }
            const float b = 0.008f;
            Strip("BevelL", new Vector2(0, 0), new Vector2(b, 1), VisualAssets.HudBevelLight);
            Strip("BevelT", new Vector2(0, 1f - b), new Vector2(1, 1), VisualAssets.HudBevelLight);
            Strip("BevelR", new Vector2(1f - b, 0), new Vector2(1, 1), VisualAssets.HudBevelDark);
            Strip("BevelB", new Vector2(0, 0), new Vector2(1, b), VisualAssets.HudBevelDark);
        }

        private static GameObject MakeHudButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color color)
        {
            var go = MakeButton(parent, label, onClick, color);
            AddBevel(go.transform);
            var text = go.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.fontSize = 14;
                text.color = VisualAssets.HudBody;
            }
            return go;
        }

        private static GameObject MakePanel(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = VisualAssets.HudPanel;
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
            text.color = VisualAssets.HudBody;
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
