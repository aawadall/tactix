using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace Tactix.Core
{
    /// <summary>
    /// Writes one JSON-lines file per game to a local directory. Every line is a
    /// self-contained JSON object with a "type" field: one "header" line, then a
    /// "step" line per applied action ((state, action, resulting state) tuple),
    /// then exactly one final "result" line. Lines are flushed as written, so a
    /// crash loses at most the line in progress. This is training data for a
    /// future imitation-learning pipeline: schema stability is paramount.
    /// </summary>
    public sealed class GameLogger : IDisposable
    {
        public const int SchemaVersion = 8;

        private readonly StreamWriter _writer;
        private int _stepCount;
        private bool _resultWritten;

        public string FilePath { get; }

        /// <summary>
        /// Opens a log for one game. <paramref name="mapSeed"/> is the seed used to
        /// generate the map, or null for the fixed standard map — recording it keeps
        /// every logged game reproducible.
        /// </summary>
        public GameLogger(string directory, string mode, GameState initialState, int? mapSeed = null, int? rngSeed = null)
        {
            Directory.CreateDirectory(directory);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            FilePath = Path.Combine(directory, $"game_{stamp}_{suffix}.jsonl");

            _writer = new StreamWriter(FilePath, append: false) { AutoFlush = true };
            WriteLine(new HeaderLine
            {
                SchemaVersion = SchemaVersion,
                CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Mode = mode,
                MapSource = mapSeed.HasValue ? "generated" : "standard",
                MapSeed = mapSeed,
                RngSeed = rngSeed,
                InitialState = initialState,
            });
        }

        /// <summary>
        /// Records one applied action. <paramref name="rngDraws"/> are the random
        /// values the rules engine consumed resolving it, in order — replaying
        /// them through <see cref="ReplayRandom"/> reproduces the step exactly.
        /// </summary>
        public void LogStep(GameState stateBefore, GameAction action, GameState stateAfter, IReadOnlyList<double> rngDraws = null)
        {
            if (_resultWritten) throw new InvalidOperationException("Game already ended");
            WriteLine(new StepLine
            {
                StepIndex = _stepCount++,
                Player = stateBefore.CurrentPlayer,
                StateBefore = stateBefore,
                Action = action,
                RngDraws = rngDraws != null && rngDraws.Count > 0 ? new List<double>(rngDraws) : null,
                StateAfter = stateAfter,
            });
        }

        /// <summary>Writes the final result line. Pass null winner for an aborted (unfinished) game.</summary>
        public void LogResult(int? winner)
        {
            if (_resultWritten) return;
            _resultWritten = true;
            WriteLine(new ResultLine
            {
                Winner = winner,
                Completed = winner.HasValue,
                TotalSteps = _stepCount,
            });
        }

        public void Dispose()
        {
            if (!_resultWritten) LogResult(null); // game abandoned mid-way (e.g. app quit)
            _writer.Dispose();
        }

        private void WriteLine(object entry)
        {
            _writer.WriteLine(JsonConvert.SerializeObject(entry, Formatting.None, TactixJson.Settings));
        }

        private sealed class HeaderLine
        {
            [JsonProperty("type")] public string Type => "header";
            [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; }
            [JsonProperty("createdUtc")] public string CreatedUtc { get; set; }
            [JsonProperty("mode")] public string Mode { get; set; }
            [JsonProperty("mapSource")] public string MapSource { get; set; }
            [JsonProperty("mapSeed")] public int? MapSeed { get; set; }
            [JsonProperty("rngSeed")] public int? RngSeed { get; set; }
            [JsonProperty("initialState")] public GameState InitialState { get; set; }
        }

        private sealed class StepLine
        {
            [JsonProperty("type")] public string Type => "step";
            [JsonProperty("stepIndex")] public int StepIndex { get; set; }
            [JsonProperty("player")] public int Player { get; set; }
            [JsonProperty("stateBefore")] public GameState StateBefore { get; set; }
            [JsonProperty("action")] public GameAction Action { get; set; }
            [JsonProperty("rngDraws", NullValueHandling = NullValueHandling.Ignore)] public List<double> RngDraws { get; set; }
            [JsonProperty("stateAfter")] public GameState StateAfter { get; set; }
        }

        private sealed class ResultLine
        {
            [JsonProperty("type")] public string Type => "result";
            [JsonProperty("winner")] public int? Winner { get; set; }
            [JsonProperty("completed")] public bool Completed { get; set; }
            [JsonProperty("totalSteps")] public int TotalSteps { get; set; }
        }
    }
}
