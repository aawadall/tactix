using System.Collections;
using System.IO;
using System.Linq;
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

        /// <summary>Board size used when generating a random map.</summary>
        private const int RandomMapSize = 24;

        public GameState State { get; private set; }
        public GameMode Mode { get; private set; }
        public bool GameStarted => State != null;

        /// <summary>When set, each new game is played on a freshly generated map.</summary>
        public bool UseRandomMap { get; private set; }

        /// <summary>True while the Field Manual is showing instead of a game.</summary>
        public bool InFieldManual { get; private set; }

        private int? _mapSeed;
        private int _manualTypeIndex;

        private BoardRenderer _board;
        private InputController _input;
        private UiController _ui;
        private GameLogger _logger;
        private RandomBot _bot;
        private bool _autoplay;
        private int _autoplayRemaining;
        private int _framedWidth;
        private int _framedHeight;

        public bool IsHumanTurn =>
            GameStarted && !InFieldManual && State.Winner == null &&
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
                if (System.Array.IndexOf(args, "-randommaps") >= 0) UseRandomMap = true;
                StartCoroutine(ScreenshotSequence(dir));
                return;
            }

            // "-randommaps" pairs with -autoplay: every self-play game gets a fresh
            // generated map, which is what you want for training-set diversity.
            if (System.Array.IndexOf(args, "-randommaps") >= 0) UseRandomMap = true;

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

        // World-unit margins kept clear around the board for the overlay UI.
        private const float TopMargin = 2.4f;
        private const float BottomMargin = 1.6f;
        private const float SideMargin = 0.8f;

        /// <summary>Share of the viewport reserved for the Field Manual's side panel.</summary>
        private const float ManualPanelShare = 0.34f;

        private void FrameBoard()
        {
            var cam = Camera.main;
            float cx = (State.Width - 1) / 2f;
            float cy = (State.Height - 1) / 2f;

            float usableWidthShare = InFieldManual ? 1f - ManualPanelShare : 1f;
            float halfHeight = (State.Height + TopMargin + BottomMargin) / 2f;
            float halfWidth = (State.Width + 2f * SideMargin) / (2f * cam.aspect * usableWidthShare);
            cam.orthographicSize = Mathf.Max(halfHeight, halfWidth);

            // Shift the view right so the board sits in the free part of the screen.
            float viewportWorldWidth = 2f * cam.orthographicSize * cam.aspect;
            float shift = InFieldManual ? viewportWorldWidth * (ManualPanelShare / 2f) : 0f;
            cam.transform.position = new Vector3(cx + shift, cy + (TopMargin - BottomMargin) / 2f, -10f);

            _framedWidth = Screen.width;
            _framedHeight = Screen.height;
        }

        public void StartGame(GameMode mode)
        {
            EndLoggerIfOpen();
            Mode = mode;

            if (UseRandomMap)
            {
                _mapSeed = Random.Range(int.MinValue, int.MaxValue);
                State = MapGenerator.Generate(RandomMapSize, RandomMapSize, _mapSeed.Value);
            }
            else
            {
                _mapSeed = null;
                State = LevelConfig.CreateStandardGame();
            }

            _bot = mode == GameMode.Hotseat ? null : new RandomBot();
            _logger = new GameLogger(LogDirectory, ModeString(mode), State, _mapSeed);

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
            // Keep the board framed when the window is resized or maximized.
            if (GameStarted && (Screen.width != _framedWidth || Screen.height != _framedHeight))
                FrameBoard();

            if (Input.GetKeyDown(KeyCode.L)) _ui.ToggleLegend();

            if (Input.GetKeyDown(KeyCode.F11) ||
                (Input.GetKeyDown(KeyCode.Return) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))))
            {
                Screen.fullScreen = !Screen.fullScreen;
            }

            if (InFieldManual)
            {
                if (Input.GetKeyDown(KeyCode.RightArrow)) CycleFieldManual(1);
                if (Input.GetKeyDown(KeyCode.LeftArrow)) CycleFieldManual(-1);
                if (Input.GetKeyDown(KeyCode.Escape)) CloseFieldManual();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_ui.LegendOpen) _ui.CloseLegend();
                else if (GameStarted) BackToMenu(); // aborts the game (logger writes an incomplete-result line)
                else QuitGame();
            }
        }

        // ---------- field manual ----------

        /// <summary>
        /// Opens the Field Manual: a demonstration board per unit type with the
        /// unit's real movement region and range envelopes drawn on it.
        /// </summary>
        public void ShowFieldManual()
        {
            EndLoggerIfOpen();
            InFieldManual = true;
            _mapSeed = null;
            _input.ClearSelection();
            _ui.HidePanels();
            _ui.CloseLegend();
            RenderFieldManualPage();
        }

        public void CycleFieldManual(int delta)
        {
            if (!InFieldManual) return;
            int count = UnitStats.AllTypes.Length;
            _manualTypeIndex = ((_manualTypeIndex + delta) % count + count) % count;
            RenderFieldManualPage();
        }

        public void CloseFieldManual()
        {
            InFieldManual = false;
            State = null;
            _board.Clear();
            _ui.HideFieldManual();
            _ui.ShowModeSelect();
        }

        private void RenderFieldManualPage()
        {
            var type = UnitStats.AllTypes[_manualTypeIndex];
            State = FieldManual.BuildDemoState(type);
            FrameBoard();
            _board.BuildTerrain(State);
            _board.RenderUnits(State);

            var subject = State.GetUnit(FieldManual.ShowcaseUnitId);
            _board.SetMoveRegion(subject, Rules.GetMoveRegion(State, subject.Id));

            var attackTargets = Rules.GetLegalAttacks(State, subject.Id)
                .Select(a => State.GetUnit(a.TargetUnitId)).Where(u => u != null).ToList();
            var healTargets = Rules.GetLegalHeals(State, subject.Id)
                .Select(h => State.GetUnit(h.TargetUnitId)).Where(u => u != null).ToList();
            _board.SetSelection(subject, attackTargets, healTargets);
            _board.SetCapabilityRings(subject);

            _ui.ShowFieldManual(type, _manualTypeIndex + 1, UnitStats.AllTypes.Length);
        }

        /// <summary>Toggles between the fixed standard map and freshly generated ones.</summary>
        public void ToggleRandomMap()
        {
            UseRandomMap = !UseRandomMap;
            _ui.RefreshMapButton();
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
            StartGame(GameMode.Hotseat);
            yield return new WaitForSeconds(0.8f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_game.png"));
            yield return new WaitForSeconds(0.5f);

            // Select a unit so the reachable region and telemetry are visible.
            var unit = State.Units.Find(u => u.Owner == 0 && u.Type == Core.UnitType.MechInfantry);
            if (unit != null) _input.SelectUnit(unit.Id);
            yield return new WaitForSeconds(0.5f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_selection.png"));
            yield return new WaitForSeconds(0.5f);

            _ui.ToggleLegend();
            yield return new WaitForSeconds(0.5f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "shot_legend.png"));
            yield return new WaitForSeconds(0.5f);
            _ui.CloseLegend();

            // Field Manual pages, one capture per unit type.
            ShowFieldManual();
            for (int i = 0; i < UnitStats.AllTypes.Length; i++)
            {
                yield return new WaitForSeconds(0.4f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"shot_manual_{i}.png"));
                yield return new WaitForSeconds(0.4f);
                CycleFieldManual(1);
            }
            yield return new WaitForSeconds(0.4f);
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
