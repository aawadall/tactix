using System.Text;
using Tactix.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tactix.Game
{
    /// <summary>
    /// All uGUI built in code: status banner, End Turn button, mode-select panel,
    /// win screen, unit legend (NATO symbols + capabilities), and selected-unit
    /// telemetry (health / XP / sight / damage). Placeholder styling only.
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
            HideTelemetry();
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
        }

        public void ShowWinScreen(int winner)
        {
            _winPanel.SetActive(true);
            _endTurnButton.SetActive(false);
            HideTelemetry();
            _winText.text = $"Player {winner + 1} ({PlayerName(winner)}) wins!";
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
            HideTelemetry();
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

        /// <summary>Keeps the map-source button's label in step with the setting.</summary>
        public void RefreshMapButton()
        {
            if (_mapButtonLabel == null) return;
            _mapButtonLabel.text = _game.UseRandomMap ? "Map: Random (generated)" : "Map: Standard (fixed)";
        }

        public void ToggleLegend()
        {
            _legendPanel.SetActive(!_legendPanel.activeSelf);
        }

        public void CloseLegend()
        {
            _legendPanel.SetActive(false);
        }

        public void UpdateStatus(GameState state, GameMode mode, bool isHumanTurn)
        {
            if (state.Winner != null) return;
            string phase = state.TurnPhase == TurnPhase.Move ? "Move phase" : "Attack phase";
            string actor = isHumanTurn ? "" : "  [bot thinking...]";
            _banner.text = $"Turn {state.TurnNumber}  •  Player {state.CurrentPlayer + 1} ({PlayerName(state.CurrentPlayer)})  •  {phase}{actor}";
            _endTurnButton.GetComponent<Button>().interactable = isHumanTurn;
        }

        // ---------- telemetry ----------

        public void ShowTelemetry(Unit unit, GameState state, bool splitMode = false)
        {
            var s = unit.Stats;
            var sb = new StringBuilder();
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

            _telemetryText.text = sb.ToString();
            _telemetryPanel.SetActive(true);
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

            _banner = MakeText(canvasGo.transform, "Banner", "", 22, TextAnchor.MiddleCenter);
            Anchor(_banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -28), new Vector2(1000, 40));

            _endTurnButton = MakeButton(canvasGo.transform, "End Turn", () => _game.SubmitEndTurn(),
                new Color(0.25f, 0.35f, 0.5f));
            Anchor(_endTurnButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-110, 45), new Vector2(180, 55));

            _legendButton = MakeButton(canvasGo.transform, "Legend (L)", ToggleLegend,
                new Color(0.32f, 0.32f, 0.38f));
            Anchor(_legendButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-85, -30), new Vector2(140, 42));

            BuildTelemetryPanel(canvasGo.transform);
            BuildModePanel(canvasGo.transform);
            BuildWinPanel(canvasGo.transform);
            BuildLegendPanel(canvasGo.transform);
            BuildFieldManualPanel(canvasGo.transform);

            _modePanel.SetActive(false);
            _winPanel.SetActive(false);
            _legendPanel.SetActive(false);
            _manualPanel.SetActive(false);
            _telemetryPanel.SetActive(false);
            _endTurnButton.SetActive(false);
            _legendButton.SetActive(false);
        }

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
            rect.anchoredPosition = new Vector2(12, 12);
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
                var button = MakeButton(_modePanel.transform, labels[i], () => _game.StartGame(mode),
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

            var hint = MakeText(_modePanel.transform, "Hint", "Esc: back / quit   •   L: legend   •   F11: fullscreen   •   right-click: deselect", 15, TextAnchor.MiddleCenter);
            Anchor(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 122), new Vector2(800, 28));
        }

        private void BuildWinPanel(Transform canvas)
        {
            _winPanel = MakePanel(canvas, "WinPanel");
            _winText = MakeText(_winPanel.transform, "WinText", "", 40, TextAnchor.MiddleCenter);
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

            (Color color, string text)[] terrain =
            {
                (VisualAssets.OpenColor, "Open — no effect"),
                (VisualAssets.ForestColor, "Forest — +1 defense to occupant, blocks artillery line of sight"),
                (VisualAssets.ImpassableColor, "Impassable — blocks movement and line of sight"),
                (VisualAssets.ContourColor, "Contours mark elevation (digit = summit height); thick = cliff, blocks movement"),
            };
            for (int i = 0; i < terrain.Length; i++)
            {
                float y = -100 - i * 34;
                var chipGo = new GameObject($"Chip {i}");
                chipGo.transform.SetParent(_legendPanel.transform, false);
                chipGo.AddComponent<Image>().color = terrain[i].color;
                Anchor(chipGo.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(-390, y), new Vector2(26, 26));

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
