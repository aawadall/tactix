using System.Collections;
using System.IO;
using Tactix.Core;
using UnityEngine;

namespace Tactix.Game
{
    public enum GameMode
    {
        Hotseat,
        VsBot,
        BotVsBot,
    }

    /// <summary>
    /// Owns the authoritative <see cref="GameState"/>, routes every action through
    /// <see cref="Rules.Apply"/>, drives bot turns, and logs every step.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        private const float BotActionDelay = 0.35f;

        public GameState State { get; private set; }
        public GameMode Mode { get; private set; }
        public bool GameStarted => State != null;

        private BoardRenderer _board;
        private InputController _input;
        private UiController _ui;
        private GameLogger _logger;
        private RandomBot _bot;
        private bool _autoplay;
        private int _autoplayRemaining;

        public bool IsHumanTurn =>
            GameStarted && State.Winner == null &&
            (Mode == GameMode.Hotseat || (Mode == GameMode.VsBot && State.CurrentPlayer == 0));

        public bool CanAcceptInput => IsHumanTurn;

        public UiController Ui => _ui;

        private void Awake()
        {
            CreateCamera();
            _board = new GameObject("Board").AddComponent<BoardRenderer>();
            _input = gameObject.AddComponent<InputController>();
            _ui = new GameObject("Ui").AddComponent<UiController>();
            _input.Init(this, _board);
            _ui.Init(this);
        }

        private void Start()
        {
            StartCoroutine(BotLoop()); // one persistent loop for the whole session

            // "Tactix.exe -autoplay [N]" runs N bot-vs-bot games unattended (for
            // self-play data generation, works with -batchmode -nographics) and quits.
            var args = System.Environment.GetCommandLineArgs();

            // "Tactix.exe -shots <dir>" (debug): start a bot game, capture two
            // screenshots (board + legend), then quit. Requires a rendering run.
            int shotsIndex = System.Array.IndexOf(args, "-shots");
            if (shotsIndex >= 0)
            {
                string dir = shotsIndex + 1 < args.Length ? args[shotsIndex + 1] : ".";
                StartCoroutine(ScreenshotSequence(dir));
                return;
            }

            int autoplayIndex = System.Array.IndexOf(args, "-autoplay");
            if (autoplayIndex >= 0)
            {
                _autoplay = true;
                _autoplayRemaining = 1;
                if (autoplayIndex + 1 < args.Length && int.TryParse(args[autoplayIndex + 1], out int games) && games > 0)
                    _autoplayRemaining = games;
                Debug.Log($"Autoplay: running {_autoplayRemaining} bot-vs-bot game(s), logging to {LogDirectory}");
                StartGame(GameMode.BotVsBot);
            }
            else
            {
                _ui.ShowModeSelect();
            }
        }

        private static void CreateCamera()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
        }

        private void FrameBoard()
        {
            var cam = Camera.main;
            float cx = (State.Width - 1) / 2f;
            float cy = (State.Height - 1) / 2f;
            cam.transform.position = new Vector3(cx, cy, -10f);
            float halfH = State.Height / 2f + 1.2f;
            float halfW = (State.Width / 2f + 1.2f) / cam.aspect;
            cam.orthographicSize = Mathf.Max(halfH, halfW);
        }

        public void StartGame(GameMode mode)
        {
            EndLoggerIfOpen();
            Mode = mode;
            State = LevelConfig.CreateStandardGame();
            _bot = mode == GameMode.Hotseat ? null : new RandomBot();
            _logger = new GameLogger(LogDirectory, ModeString(mode), State);

            FrameBoard();
            _board.BuildTerrain(State);
            _input.ClearSelection();
            RefreshViews();
            _ui.HidePanels();
        }

        public void BackToMenu()
        {
            EndLoggerIfOpen();
            State = null;
            _input.ClearSelection();
            _board.Clear();
            _ui.ShowModeSelect();
        }

        /// <summary>Applies an action if legal. Returns false (and changes nothing) otherwise.</summary>
        public bool TrySubmitAction(GameAction action)
        {
            if (!GameStarted || State.Winner != null) return false;

            GameState next;
            try
            {
                next = Rules.Apply(State, action);
            }
            catch (IllegalActionException e)
            {
                Debug.LogWarning($"Rejected action {action}: {e.Message}");
                return false;
            }

            _logger.LogStep(State, action, next);
            State = next;

            if (State.Winner != null)
            {
                _logger.LogResult(State.Winner);
                EndLoggerIfOpen();
                _input.ClearSelection();
                _ui.ShowWinScreen(State.Winner.Value);

                if (_autoplay)
                {
                    _autoplayRemaining--;
                    if (_autoplayRemaining > 0)
                    {
                        StartGame(GameMode.BotVsBot);
                    }
                    else
                    {
                        Debug.Log("Autoplay finished, quitting");
                        Application.Quit();
                    }
                }
            }

            RefreshViews();
            return true;
        }

        public void SubmitEndTurn()
        {
            if (CanAcceptInput) TrySubmitAction(new EndTurnAction());
        }

        public void RefreshViews()
        {
            if (!GameStarted) return;
            _board.RenderUnits(State);
            _ui.UpdateStatus(State, Mode, IsHumanTurn);
        }

        private IEnumerator BotLoop()
        {
            var delay = new WaitForSeconds(BotActionDelay);
            while (true)
            {
                bool botTurn = GameStarted && State.Winner == null && !IsHumanTurn;
                if (!botTurn)
                {
                    yield return null;
                    continue;
                }

                // Autoplay runs at full speed; interactive bot turns are paced to be watchable.
                if (_autoplay) yield return null;
                else yield return delay;

                if (GameStarted && State.Winner == null && !IsHumanTurn)
                    TrySubmitAction(_bot.ChooseAction(State));
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.L)) _ui.ToggleLegend();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_ui.LegendOpen) _ui.CloseLegend();
                else if (GameStarted) BackToMenu(); // aborts the game (logger writes an incomplete-result line)
                else QuitGame();
            }
        }

        public void QuitGame()
        {
            EndLoggerIfOpen();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private IEnumerator ScreenshotSequence(string dir)
        {
            StartGame(GameMode.BotVsBot);
            yield return new WaitForSeconds(1.4f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_game.png"));
            yield return new WaitForSeconds(0.5f);
            _ui.ToggleLegend();
            yield return new WaitForSeconds(0.5f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_legend.png"));
            yield return new WaitForSeconds(0.5f);
            QuitGame();
        }

        private void EndLoggerIfOpen()
        {
            _logger?.Dispose(); // writes an aborted-result line if the game didn't finish
            _logger = null;
        }

        private void OnDestroy()
        {
            EndLoggerIfOpen();
        }

        private static string ModeString(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Hotseat: return "hotseat";
                case GameMode.VsBot: return "vsBot";
                default: return "botVsBot";
            }
        }

        /// <summary>logs/ next to the project root in the editor, next to the executable in builds.</summary>
        public static string LogDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "logs"));
    }
}
