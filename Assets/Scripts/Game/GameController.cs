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
    /// Human turns also run an orders clock that steps queued intents into ordinary
    /// actions — the book itself is never part of state or the JSONL schema.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        private const float BotActionDelay = 0.35f;
        private const float TickInterval = 0.5f;

        /// <summary>Board size used when generating a random map.</summary>
        private const int RandomMapSize = 24;

        public GameState State { get; private set; }
        public GameMode Mode { get; private set; }
        public bool GameStarted => State != null;

        /// <summary>When set, each new game is played on a freshly generated map.</summary>
        public bool UseRandomMap { get; private set; }

        /// <summary>True while the Field Manual is showing instead of a game.</summary>
        public bool InFieldManual { get; private set; }

        /// <summary>True while the Map Workshop preview is open (no match yet).</summary>
        public bool InMapWorkshop { get; private set; }

        /// <summary>Current workshop map description (preview / locked for Start Match).</summary>
        public MapSpec WorkshopSpec { get; private set; }

        /// <summary>Per-unit command queues for the human (presentation layer only).</summary>
        public OrderBook Orders { get; } = new OrderBook();

        /// <summary>Seconds until the next orders-clock tick (0 when not ticking).</summary>
        public float ClockSecondsRemaining { get; private set; }

        private int? _mapSeed;
        private MapSpec _matchSpec;
        private GameMode _pendingMode;
        private int _manualTypeIndex;
        private int _manualEchelonIndex = (int)Echelon.Company;

        /// <summary>Records the draws the rules engine consumes, so each step can log them.</summary>
        private RecordingRandom _outcomes;
        private int _rngSeed;

        private BoardRenderer _board;
        private InputController _input;
        private UiController _ui;
        private GameLogger _logger;
        private RandomBot _bot;
        private System.Random _autonomyRng;
        private bool _autoplay;
        private int _autoplayRemaining;
        private int _framedWidth;
        private int _framedHeight;
        private float _clockAccum;

        public bool IsHumanTurn =>
            GameStarted && !InFieldManual && !InMapWorkshop && !State.IsOver &&
            (Mode == GameMode.Hotseat || (Mode == GameMode.VsBot && State.CurrentPlayer == 0));

        /// <summary>Instant actions (Ctrl-click, End Turn, split) — only on your ply.</summary>
        public bool CanAcceptInput => IsHumanTurn;

        /// <summary>
        /// Order queuing and path preview stay live even while the opponent
        /// (or the other hotseat player) is resolving their turn.
        /// </summary>
        public bool CanPlanOrders =>
            GameStarted && !InFieldManual && !InMapWorkshop && !State.IsOver && OrdersModeActive;

        /// <summary>True when human turns use the orders clock (not bot-vs-bot / autoplay).</summary>
        public bool OrdersModeActive =>
            GameStarted && !InFieldManual && !InMapWorkshop && !_autoplay && Mode != GameMode.BotVsBot;

        /// <summary>
        /// Units the local player may give standing orders to. Vs-bot: Blue only.
        /// Hotseat: either side (so the waiting player can keep planning).
        /// </summary>
        public bool IsOrderableUnit(Unit unit) =>
            unit != null && CanPlanOrders && (Mode == GameMode.Hotseat || unit.Owner == 0);

        public UiController Ui => _ui;
        public BoardRenderer Board => _board;

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
            StartCoroutine(ClockLoop());

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
                OpenMapWorkshop(GameMode.Hotseat);
            }
        }

        private static void CreateCamera()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.14f, 0.15f, 0.12f);
        }

        // World-unit margins kept clear around the board for the overlay UI.
        private const float TopMargin = 2.4f;
        private const float BottomMargin = 1.6f;
        private const float SideMargin = 0.8f;

        /// <summary>Share of the viewport reserved for the persistent C&amp;C command dock.</summary>
        private const float ManualPanelShare = 0.34f;

        private void FrameBoard()
        {
            var cam = Camera.main;
            float cx = (State.Width - 1) / 2f;
            float cy = (State.Height - 1) / 2f;

            // Always reserve the right dock (workshop, match, or field manual).
            float usableWidthShare = 1f - ManualPanelShare;
            float halfHeight = (State.Height + TopMargin + BottomMargin) / 2f;
            float halfWidth = (State.Width + 2f * SideMargin) / (2f * cam.aspect * usableWidthShare);
            cam.orthographicSize = Mathf.Max(halfHeight, halfWidth);

            float viewportWorldWidth = 2f * cam.orthographicSize * cam.aspect;
            float shift = viewportWorldWidth * (ManualPanelShare / 2f);
            cam.transform.position = new Vector3(cx + shift, cy + (TopMargin - BottomMargin) / 2f, -10f);

            _framedWidth = Screen.width;
            _framedHeight = Screen.height;
        }

        /// <summary>
        /// Starts a match immediately (autoplay / -shots / CLI). Interactive play
        /// goes through <see cref="OpenMapWorkshop"/> instead.
        /// </summary>
        public void StartGame(GameMode mode)
        {
            MapSpec spec = UseRandomMap
                ? MapSpec.Generated(RandomMapSize, Random.Range(int.MinValue, int.MaxValue))
                : MapSpec.Standard();
            BeginMatch(mode, spec);
        }

        private void BeginMatch(GameMode mode, MapSpec spec)
        {
            EndLoggerIfOpen();
            InMapWorkshop = false;
            InFieldManual = false;
            Mode = mode;
            _matchSpec = spec.Clone();
            _mapSeed = spec.IsStandard ? (int?)null : spec.Seed;
            State = MapGenerator.Generate(spec);

            _rngSeed = Random.Range(int.MinValue, int.MaxValue);
            _outcomes = new RecordingRandom(new SeededRandom(_rngSeed));

            _bot = mode == GameMode.Hotseat ? null : new RandomBot();
            _autonomyRng = new System.Random(_rngSeed ^ 0x6a09e667);
            _logger = new GameLogger(LogDirectory, ModeString(mode), State, _mapSeed, _rngSeed, _matchSpec);
            Orders.ClearAll();
            _clockAccum = 0f;
            ClockSecondsRemaining = 0f;

            FrameBoard();
            _board.BuildTerrain(State);
            _input.ClearSelection();
            RefreshViews();
            _ui.ShowCommandDock();
        }

        public void BackToMenu()
        {
            EndLoggerIfOpen();
            _matchSpec = null;
            Orders.ClearAll();
            ClockSecondsRemaining = 0f;
            _input.ClearSelection();
            WorkshopSpec = null;
            OpenMapWorkshop(Mode);
        }

        // ---------- map workshop / shell ----------

        /// <summary>
        /// Opens the shell: live map preview + workshop controls in the command dock.
        /// Does not open the game logger until the match begins.
        /// </summary>
        public void OpenMapWorkshop(GameMode mode)
        {
            EndLoggerIfOpen();
            InFieldManual = false;
            InMapWorkshop = true;
            _pendingMode = mode;
            Mode = mode;
            if (WorkshopSpec == null)
                WorkshopSpec = MapSpec.Generated(RandomMapSize, Random.Range(int.MinValue, int.MaxValue));
            _input.ClearSelection();
            _ui.CloseLegend();
            RefreshWorkshopPreview();
        }

        public void SetPendingMode(GameMode mode)
        {
            if (!InMapWorkshop) return;
            _pendingMode = mode;
            Mode = mode;
            _ui.ShowMapWorkshop(WorkshopSpec, _pendingMode);
        }

        public void WorkshopReroll()
        {
            if (!InMapWorkshop) return;
            int seed = Random.Range(int.MinValue, int.MaxValue);
            int size = WorkshopSpec.IsStandard ? RandomMapSize : WorkshopSpec.Width;
            WorkshopSpec = MapSpec.Generated(size, seed, WorkshopSpec.TurnLimit);
            RefreshWorkshopPreview();
        }

        public void WorkshopSetSize(int size)
        {
            if (!InMapWorkshop) return;
            int seed = WorkshopSpec.Seed ?? Random.Range(int.MinValue, int.MaxValue);
            WorkshopSpec = MapSpec.Generated(size, seed, WorkshopSpec.TurnLimit);
            RefreshWorkshopPreview();
        }

        public void WorkshopUseStandard()
        {
            if (!InMapWorkshop) return;
            WorkshopSpec = MapSpec.Standard();
            RefreshWorkshopPreview();
        }

        public void WorkshopStartMatch()
        {
            if (!InMapWorkshop || WorkshopSpec == null) return;
            var locked = WorkshopSpec.Clone();
            InMapWorkshop = false;
            BeginMatch(_pendingMode, locked);
        }

        public void CloseMapWorkshop()
        {
            QuitGame();
        }

        private void RefreshWorkshopPreview()
        {
            State = MapGenerator.Generate(WorkshopSpec);
            FrameBoard();
            _board.BuildTerrain(State);
            _board.RenderUnits(State);
            _input.ClearSelection();
            _ui.ShowMapWorkshop(WorkshopSpec, _pendingMode);
        }

        /// <summary>Applies an action if legal. Returns false (and changes nothing) otherwise.</summary>
        public bool TrySubmitAction(GameAction action)
        {
            if (!GameStarted || InFieldManual || InMapWorkshop || State.IsOver || _logger == null)
                return false;

            GameState next;
            _outcomes.Reset();
            try
            {
                next = Rules.Apply(State, action, _outcomes);
            }
            catch (IllegalActionException e)
            {
                Debug.LogWarning($"Rejected action {action}: {e.Message}");
                return false;
            }

            _logger.LogStep(State, action, next, _outcomes.Draws);
            State = next;
            Orders.Prune(State);

            if (State.IsOver)
            {
                _logger.LogResult(State);
                EndLoggerIfOpen();
                Orders.ClearAll();
                _input.ClearSelection();
                _ui.ShowWinScreen(State);

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
            _board.RenderUnits(State, Orders, IsHumanTurn && OrdersModeActive);
            _board.SetOrderPaths(State, Orders);
            _ui.UpdateStatus(State, Mode, IsHumanTurn, ClockSecondsRemaining, OrdersModeActive);
            _input.RefreshOverlays();
        }

        private IEnumerator BotLoop()
        {
            var delay = new WaitForSeconds(BotActionDelay);
            while (true)
            {
                bool botTurn = GameStarted && !InFieldManual && !InMapWorkshop
                    && !State.IsOver && !IsHumanTurn && _bot != null;
                if (!botTurn)
                {
                    yield return null;
                    continue;
                }

                // Autoplay runs at full speed; interactive bot turns are paced to be watchable.
                if (_autoplay) yield return null;
                else yield return delay;

                if (GameStarted && !InFieldManual && !InMapWorkshop
                    && !State.IsOver && !IsHumanTurn && _bot != null)
                    TrySubmitAction(_bot.ChooseAction(State));
            }
        }

        /// <summary>
        /// Human-turn clock: each tick turns one queued order into a legal action.
        /// When every unit with orders is idle for the ply, auto-ends the turn.
        /// Empty books do not force end-turn — the player can keep planning.
        /// </summary>
        private IEnumerator ClockLoop()
        {
            while (true)
            {
                if (!OrdersModeActive || !IsHumanTurn)
                {
                    _clockAccum = 0f;
                    ClockSecondsRemaining = 0f;
                    yield return null;
                    continue;
                }

                _clockAccum += Time.deltaTime;
                ClockSecondsRemaining = Mathf.Max(0f, TickInterval - _clockAccum);
                if (_clockAccum < TickInterval)
                {
                    _ui.UpdateStatus(State, Mode, IsHumanTurn, ClockSecondsRemaining, OrdersModeActive);
                    yield return null;
                    continue;
                }

                _clockAccum = 0f;
                ClockSecondsRemaining = TickInterval;
                TickOrdersClock();
                yield return null;
            }
        }

        private void TickOrdersClock()
        {
            if (!GameStarted || State.IsOver || !IsHumanTurn) return;
            Orders.Prune(State);

            bool submitted = false;
            bool pendingOrders = false;
            bool anyAutonomousUnit = false;

            foreach (var unit in State.Units.Where(u => u.Owner == State.CurrentPlayer).OrderBy(u => u.Id))
            {
                if (Orders.HasOrders(unit.Id))
                {
                    while (true)
                    {
                        var order = Orders.Peek(unit.Id);
                        if (order == null) break;
                        pendingOrders = true;

                        var action = OrderExecutor.TryStep(State, unit.Id, order, out bool complete);
                        if (action != null)
                        {
                            if (TrySubmitAction(action))
                            {
                                if (complete) Orders.Dequeue(unit.Id);
                                submitted = true;
                            }
                            return;
                        }

                        if (complete)
                        {
                            Orders.Dequeue(unit.Id);
                            continue;
                        }
                        break;
                    }
                }
                else
                {
                    anyAutonomousUnit = true;
                    var action = UnitAutonomy.TryStep(State, unit.Id, _autonomyRng);
                    if (action != null && TrySubmitAction(action))
                    {
                        submitted = true;
                        return;
                    }
                }

                if (submitted) return;
            }

            if (!submitted && (pendingOrders || anyAutonomousUnit))
                TrySubmitAction(new EndTurnAction());
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
                if (Input.GetKeyDown(KeyCode.UpArrow)) CycleFieldManualEchelon(1);
                if (Input.GetKeyDown(KeyCode.DownArrow)) CycleFieldManualEchelon(-1);
                if (Input.GetKeyDown(KeyCode.Escape)) CloseFieldManual();
                return;
            }

            if (InMapWorkshop)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CloseMapWorkshop();
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
            InMapWorkshop = false;
            WorkshopSpec = null;
            _mapSeed = null;
            _input.ClearSelection();
            _ui.HideMapWorkshop();
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

        /// <summary>Steps the demonstrated unit up or down the echelon ladder.</summary>
        public void CycleFieldManualEchelon(int delta)
        {
            if (!InFieldManual) return;
            int count = EchelonScale.All.Length;
            _manualEchelonIndex = Mathf.Clamp(_manualEchelonIndex + delta, 0, count - 1);
            RenderFieldManualPage();
        }

        public void CloseFieldManual()
        {
            InFieldManual = false;
            _ui.HideFieldManual();
            OpenMapWorkshop(Mode);
        }

        private void RenderFieldManualPage()
        {
            var type = UnitStats.AllTypes[_manualTypeIndex];
            var echelon = EchelonScale.All[_manualEchelonIndex];
            State = FieldManual.BuildDemoState(type, echelon);
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

            _ui.ShowFieldManual(type, echelon, _manualTypeIndex + 1, UnitStats.AllTypes.Length);
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
