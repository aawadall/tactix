using System;
using Newtonsoft.Json;

namespace Tactix.Core
{
    /// <summary>
    /// Serializable description of a battlefield for the pre-game map workshop
    /// and game-log header. Deterministic for a given generated seed + size.
    /// </summary>
    public sealed class MapSpec
    {
        public const string SourceStandard = "standard";
        public const string SourceGenerated = "generated";

        [JsonProperty("source")]
        public string Source { get; set; } = SourceGenerated;

        [JsonProperty("width")]
        public int Width { get; set; } = 24;

        [JsonProperty("height")]
        public int Height { get; set; } = 24;

        /// <summary>Seed for procedural maps; null for the baked standard map.</summary>
        [JsonProperty("seed", NullValueHandling = NullValueHandling.Ignore)]
        public int? Seed { get; set; }

        [JsonProperty("turnLimit")]
        public int TurnLimit { get; set; } = LevelConfig.DefaultTurnLimit;

        public bool IsStandard =>
            string.Equals(Source, SourceStandard, StringComparison.OrdinalIgnoreCase);

        public static MapSpec Standard() => new MapSpec
        {
            Source = SourceStandard,
            Width = 24,
            Height = 24,
            Seed = null,
            TurnLimit = LevelConfig.DefaultTurnLimit,
        };

        public static MapSpec Generated(int width, int height, int seed, int? turnLimit = null)
        {
            if (width < MapGenerator.MinimumSize || height < MapGenerator.MinimumSize)
                throw new ArgumentException(
                    $"Maps must be at least {MapGenerator.MinimumSize}x{MapGenerator.MinimumSize}");
            return new MapSpec
            {
                Source = SourceGenerated,
                Width = width,
                Height = height,
                Seed = seed,
                TurnLimit = turnLimit ?? LevelConfig.DefaultTurnLimit,
            };
        }

        /// <summary>Square generated map (workshop size presets).</summary>
        public static MapSpec Generated(int size, int seed, int? turnLimit = null) =>
            Generated(size, size, seed, turnLimit);

        public void Validate()
        {
            if (IsStandard) return;
            if (Width < MapGenerator.MinimumSize || Height < MapGenerator.MinimumSize)
                throw new ArgumentException(
                    $"Generated maps must be at least {MapGenerator.MinimumSize}x{MapGenerator.MinimumSize}");
            if (!Seed.HasValue)
                throw new ArgumentException("Generated maps require a seed");
        }

        public MapSpec Clone() => new MapSpec
        {
            Source = Source,
            Width = Width,
            Height = Height,
            Seed = Seed,
            TurnLimit = TurnLimit,
        };
    }
}
