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

        public void ShowTelemetry(Unit unit, GameState state)
        {
            var s = unit.Stats;
            var sb = new StringBuilder();
            sb.AppendLine($"{VisualAssets.UnitDisplayName(unit.Type)}  —  Player {unit.Owner + 1} ({PlayerName(unit.Owner)})");
            sb.AppendLine($"Health {unit.Hp}/{s.MaxHp}    XP {unit.Xp}    Elevation {state.ElevationAt(unit.X, unit.Y)}");
            sb.AppendLine($"Damage {s.AttackPower}    Range {s.AttackRange}{(s.RequiresLineOfSight ? " (needs LOS)" : "")}    Move {s.MoveRange}    Sight {s.Sight}");

            var notes = new StringBuilder();
            if (state.TerrainAt(unit.X, unit.Y) == TerrainType.Forest) notes.Append("In forest: +1 defense.  ");
            if (state.ElevationAt(unit.X, unit.Y) > 0) notes.Append("High ground: +1 damage vs lower targets.  ");
            if (unit.Owner == state.CurrentPlayer)
                notes.Append($"Moved: {(unit.HasMoved ? "yes" : "no")}   Attacked: {(unit.HasAttacked ? "yes" : "no")}");
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

            _modePanel.SetActive(false);
            _winPanel.SetActive(false);
            _legendPanel.SetActive(false);
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
            rect.sizeDelta = new Vector2(430, 118);

            _telemetryText = MakeText(_telemetryPanel.transform, "Body", "", 17, TextAnchor.UpperLeft);
            var textRect = _telemetryText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14, 10);
            textRect.offsetMax = new Vector2(-14, -10);
        }

        private void BuildModePanel(Transform canvas)
        {
            _modePanel = MakePanel(canvas, "ModePanel");
            var title = MakeText(_modePanel.transform, "Title", "TACTIX", 48, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 185), new Vector2(600, 70));
            var subtitle = MakeText(_modePanel.transform, "Subtitle", "turn-based tactics — pick a mode", 20, TextAnchor.MiddleCenter);
            Anchor(subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 130), new Vector2(600, 30));

            string[] labels = { "Hotseat (2 players)", "Vs Random Bot", "Bot vs Bot (self-play)" };
            GameMode[] modes = { GameMode.Hotseat, GameMode.VsBot, GameMode.BotVsBot };
            for (int i = 0; i < labels.Length; i++)
            {
                var mode = modes[i];
                var button = MakeButton(_modePanel.transform, labels[i], () => _game.StartGame(mode),
                    new Color(0.22f, 0.42f, 0.32f));
                Anchor(button.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, 60 - i * 72), new Vector2(320, 58));
            }

            var legend = MakeButton(_modePanel.transform, "Unit Legend", ToggleLegend, new Color(0.32f, 0.32f, 0.38f));
            Anchor(legend.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -156), new Vector2(320, 58));

            var quit = MakeButton(_modePanel.transform, "Quit", () => _game.QuitGame(), new Color(0.5f, 0.24f, 0.22f));
            Anchor(quit.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -228), new Vector2(320, 58));

            var hint = MakeText(_modePanel.transform, "Hint", "Esc: back / quit   •   L: legend   •   right-click: deselect", 16, TextAnchor.MiddleCenter);
            Anchor(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -300), new Vector2(700, 30));
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
            var title = MakeText(_legendPanel.transform, "Title", "UNIT LEGEND", 34, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 272), new Vector2(600, 50));

            for (int i = 0; i < UnitStats.AllTypes.Length; i++)
            {
                var type = UnitStats.AllTypes[i];
                var s = UnitStats.For(type);
                float y = 210 - i * 62;

                var iconGo = new GameObject($"Icon {type}");
                iconGo.transform.SetParent(_legendPanel.transform, false);
                var icon = iconGo.AddComponent<Image>();
                icon.sprite = VisualAssets.UnitSymbol(type, 0);
                icon.preserveAspect = true;
                Anchor(iconGo.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(-330, y), new Vector2(96, 80));

                string los = s.RequiresLineOfSight ? " (needs line of sight)" : "";
                var row = MakeText(_legendPanel.transform, $"Row {type}",
                    $"{VisualAssets.UnitDisplayName(type)}\nMove {s.MoveRange}  •  Range {s.AttackRange}{los}  •  Damage {s.AttackPower}  •  HP {s.MaxHp}  •  Sight {s.Sight}",
                    18, TextAnchor.MiddleLeft);
                Anchor(row.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(60, y), new Vector2(660, 60));
            }

            (Color color, string text)[] terrain =
            {
                (VisualAssets.OpenColor, "Open — no effect"),
                (VisualAssets.ForestColor, "Forest — +1 defense to occupant, blocks artillery line of sight"),
                (VisualAssets.ImpassableColor, "Impassable — blocks movement and line of sight"),
                (VisualAssets.ContourColor, "Contour lines mark elevation changes (corner digit = height 1-3).\nThick contour = cliff, impassable; high ground +1 dmg; hills shape sight lines"),
            };
            for (int i = 0; i < terrain.Length; i++)
            {
                float y = -122 - i * 38;
                var chipGo = new GameObject($"Chip {i}");
                chipGo.transform.SetParent(_legendPanel.transform, false);
                chipGo.AddComponent<Image>().color = terrain[i].color;
                Anchor(chipGo.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(-330, y), new Vector2(30, 30));

                var row = MakeText(_legendPanel.transform, $"TerrainRow {i}", terrain[i].text, 18, TextAnchor.MiddleLeft);
                Anchor(row.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(60, y), new Vector2(660, 34));
            }

            var note = MakeText(_legendPanel.transform, "Note",
                "Diagonal movement allowed. Move any units, then attack — the first attack ends movement for the whole turn.\nXP: +1 per attack, +2 bonus per kill (no gameplay effect yet).",
                16, TextAnchor.MiddleCenter);
            Anchor(note.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -292), new Vector2(900, 50));

            var close = MakeButton(_legendPanel.transform, "Close", CloseLegend, new Color(0.32f, 0.32f, 0.38f));
            Anchor(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -336), new Vector2(200, 44));
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
