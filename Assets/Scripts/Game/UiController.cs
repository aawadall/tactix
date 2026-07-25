using Tactix.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tactix.Game
{
    /// <summary>
    /// All uGUI built in code: status banner, End Turn button, mode-select panel,
    /// and win screen. Placeholder styling only.
    /// </summary>
    public sealed class UiController : MonoBehaviour
    {
        private GameController _game;
        private Text _banner;
        private GameObject _endTurnButton;
        private GameObject _modePanel;
        private GameObject _winPanel;
        private Text _winText;

        public void Init(GameController game)
        {
            _game = game;
            BuildCanvas();
        }

        public void ShowModeSelect()
        {
            _modePanel.SetActive(true);
            _winPanel.SetActive(false);
            _endTurnButton.SetActive(false);
            _banner.text = "";
        }

        public void HidePanels()
        {
            _modePanel.SetActive(false);
            _winPanel.SetActive(false);
            _endTurnButton.SetActive(true);
        }

        public void ShowWinScreen(int winner)
        {
            _winPanel.SetActive(true);
            _endTurnButton.SetActive(false);
            _winText.text = $"Player {winner + 1} ({PlayerName(winner)}) wins!";
        }

        public void UpdateStatus(GameState state, GameMode mode, bool isHumanTurn)
        {
            if (state.Winner != null) return;
            string phase = state.TurnPhase == TurnPhase.Move ? "Move phase" : "Attack phase";
            string actor = isHumanTurn ? "" : "  [bot thinking...]";
            _banner.text = $"Turn {state.TurnNumber}  •  Player {state.CurrentPlayer + 1} ({PlayerName(state.CurrentPlayer)})  •  {phase}{actor}";
            _endTurnButton.GetComponent<Button>().interactable = isHumanTurn;
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

            _modePanel = MakePanel(canvasGo.transform, "ModePanel");
            var title = MakeText(_modePanel.transform, "Title", "TACTIX", 48, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(600, 70));
            var subtitle = MakeText(_modePanel.transform, "Subtitle", "turn-based tactics — pick a mode", 20, TextAnchor.MiddleCenter);
            Anchor(subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 95), new Vector2(600, 30));

            string[] labels = { "Hotseat (2 players)", "Vs Random Bot", "Bot vs Bot (self-play)" };
            GameMode[] modes = { GameMode.Hotseat, GameMode.VsBot, GameMode.BotVsBot };
            for (int i = 0; i < labels.Length; i++)
            {
                var mode = modes[i];
                var button = MakeButton(_modePanel.transform, labels[i], () => _game.StartGame(mode),
                    new Color(0.22f, 0.42f, 0.32f));
                Anchor(button.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, 20 - i * 75), new Vector2(320, 60));
            }

            _winPanel = MakePanel(canvasGo.transform, "WinPanel");
            _winText = MakeText(_winPanel.transform, "WinText", "", 40, TextAnchor.MiddleCenter);
            Anchor(_winText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(800, 60));
            var newGame = MakeButton(_winPanel.transform, "New Game", () => _game.BackToMenu(),
                new Color(0.22f, 0.42f, 0.32f));
            Anchor(newGame.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(260, 60));

            _modePanel.SetActive(false);
            _winPanel.SetActive(false);
            _endTurnButton.SetActive(false);
        }

        private static GameObject MakePanel(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.05f, 0.05f, 0.07f, 0.88f);
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
