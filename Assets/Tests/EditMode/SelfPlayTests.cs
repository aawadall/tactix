using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Tactix.Core.Tests
{
    /// <summary>
    /// Full random-bot self-play through the real engine and logger: proves the
    /// engine terminates, never produces an illegal action, and writes log files
    /// that conform to the JSONL schema.
    /// </summary>
    public class SelfPlayTests
    {
        private const int Games = 10;
        private const int MaxStepsPerGame = 20000;

        private string _logDir;

        [SetUp]
        public void SetUp()
        {
            _logDir = Path.Combine(Path.GetTempPath(), "tactix_selfplay_tests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_logDir)) Directory.Delete(_logDir, recursive: true);
        }

        [Test]
        public void RandomSelfPlay_TerminatesAndLogsValidJsonl()
        {
            for (int game = 0; game < Games; game++)
            {
                var bot = new RandomBot(seed: 1000 + game);
                var state = LevelConfig.CreateStandardGame();
                string logFile;

                using (var logger = new GameLogger(_logDir, "botVsBot", state))
                {
                    logFile = logger.FilePath;
                    int steps = 0;
                    while (state.Winner == null)
                    {
                        Assert.Less(steps, MaxStepsPerGame, $"game {game} did not terminate");
                        var action = bot.ChooseAction(state);

                        // The bot only constructs from GetAllLegalActions; Apply must accept it.
                        GameState next = null;
                        Assert.DoesNotThrow(() => next = Rules.Apply(state, action),
                            $"legal action rejected: {action}");

                        logger.LogStep(state, action, next);
                        state = next;
                        steps++;
                    }
                    logger.LogResult(state.Winner);
                }

                ValidateLogFile(logFile);
            }
        }

        [Test]
        public void EveryActionFromLegalSet_IsApplicable_DeepCheck()
        {
            // Stronger authority check: at random points of a game, *every* action in
            // the legal set must apply cleanly, not just the sampled one.
            var bot = new RandomBot(seed: 7);
            var rng = new Random(7);
            var state = LevelConfig.CreateStandardGame();
            int steps = 0;

            while (state.Winner == null && steps < 500)
            {
                if (rng.Next(10) == 0)
                {
                    foreach (var candidate in Rules.GetAllLegalActions(state))
                        Assert.DoesNotThrow(() => Rules.Apply(state, candidate),
                            $"action in legal set rejected: {candidate}");
                }
                state = Rules.Apply(state, bot.ChooseAction(state));
                steps++;
            }
        }

        [Test]
        public void AbortedGame_GetsIncompleteResultLine()
        {
            var state = LevelConfig.CreateStandardGame();
            string logFile;
            using (var logger = new GameLogger(_logDir, "hotseat", state))
            {
                logFile = logger.FilePath;
                var action = Rules.GetAllLegalActions(state).First();
                logger.LogStep(state, action, Rules.Apply(state, action));
                // Disposed without LogResult -> abort line expected.
            }

            var lines = File.ReadAllLines(logFile);
            var result = JObject.Parse(lines.Last());
            Assert.AreEqual("result", (string)result["type"]);
            Assert.IsFalse((bool)result["completed"]);
            Assert.AreEqual(JTokenType.Null, result["winner"].Type);
        }

        private static void ValidateLogFile(string path)
        {
            var lines = File.ReadAllLines(path);
            Assert.GreaterOrEqual(lines.Length, 3, "expected header + steps + result");

            var header = JObject.Parse(lines[0]);
            Assert.AreEqual("header", (string)header["type"]);
            Assert.AreEqual(GameLogger.SchemaVersion, (int)header["schemaVersion"]);
            Assert.AreEqual("botVsBot", (string)header["mode"]);
            Assert.IsNotNull(header["initialState"]?["terrain"]);
            Assert.AreEqual(8, header["initialState"]["units"].Count());

            var result = JObject.Parse(lines[lines.Length - 1]);
            Assert.AreEqual("result", (string)result["type"]);
            Assert.IsTrue((bool)result["completed"]);
            int winner = (int)result["winner"];
            Assert.IsTrue(winner == 0 || winner == 1);
            Assert.AreEqual(lines.Length - 2, (int)result["totalSteps"]);

            string previousAfter = null;
            for (int i = 1; i < lines.Length - 1; i++)
            {
                var step = JObject.Parse(lines[i]);
                Assert.AreEqual("step", (string)step["type"]);
                Assert.AreEqual(i - 1, (int)step["stepIndex"]);

                var actionType = (string)step["action"]["actionType"];
                CollectionAssert.Contains(new[] { "move", "attack", "endTurn" }, actionType);

                // The acting player recorded on the line matches the pre-state.
                Assert.AreEqual((int)step["stateBefore"]["currentPlayer"], (int)step["player"]);

                // States chain: stateAfter of step n == stateBefore of step n+1.
                string beforeJson = step["stateBefore"].ToString(Newtonsoft.Json.Formatting.None);
                string afterJson = step["stateAfter"].ToString(Newtonsoft.Json.Formatting.None);
                if (previousAfter != null)
                    Assert.AreEqual(previousAfter, beforeJson, $"state chain broken at step {i - 1}");
                previousAfter = afterJson;

                // Every state must round-trip through the typed model.
                Assert.DoesNotThrow(() => GameState.FromJson(beforeJson));
            }

            // Final state of the last step carries the winner.
            var lastStep = JObject.Parse(lines[lines.Length - 2]);
            Assert.AreEqual(winner, (int)lastStep["stateAfter"]["winner"]);
        }
    }
}
