using Newtonsoft.Json;

namespace Tactix.Core
{
    /// <summary>
    /// A piece of key ground. A player controls an objective by being the only
    /// side with a unit inside it when a turn ends, and earns its
    /// <see cref="Value"/> at the end of each of their turns while they hold it.
    ///
    /// Control persists: an objective stays with whoever took it last until the
    /// other side takes it, so ground does not change hands merely because
    /// everyone walked away.
    /// </summary>
    public sealed class Objective
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        /// <summary>Centre in world coordinates.</summary>
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        /// <summary>A unit whose centre is within this distance is inside the objective.</summary>
        [JsonProperty("radius")]
        public double Radius { get; set; }

        /// <summary>Points earned per turn held.</summary>
        [JsonProperty("value")]
        public int Value { get; set; }

        /// <summary>Player holding it, or null if it has never been taken.</summary>
        [JsonProperty("controlledBy")]
        public int? ControlledBy { get; set; }

        /// <summary>True when both sides had units inside at the last evaluation.</summary>
        [JsonProperty("contested")]
        public bool Contested { get; set; }

        public Objective Clone() => (Objective)MemberwiseClone();
    }

    /// <summary>How a finished game ended. Null while a game is still running.</summary>
    public enum GameOutcome
    {
        /// <summary>Every enemy formation was destroyed.</summary>
        Elimination = 0,
        /// <summary>The enemy headquarters was destroyed.</summary>
        Decapitation = 1,
        /// <summary>The enemy lost so much of its strength that it broke.</summary>
        Rout = 2,
        /// <summary>The turn limit was reached and one side was ahead on points.</summary>
        Score = 3,
        /// <summary>The turn limit was reached with the scores level.</summary>
        Draw = 4,
    }
}
