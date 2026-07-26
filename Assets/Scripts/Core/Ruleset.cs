using Newtonsoft.Json;

namespace Tactix.Core
{
    /// <summary>
    /// Which optional, scale-driven uncertainties are switched on. Carried inside
    /// <see cref="GameState"/> so it is logged with every position and visible to
    /// any future model: an agent cannot reason about variance it does not know
    /// exists.
    ///
    /// With both flags off the engine is a pure function of state and action —
    /// the deterministic baseline worth keeping for a first training run.
    /// </summary>
    public sealed class Ruleset
    {
        /// <summary>Damage is drawn from a spread that widens with formation size.</summary>
        [JsonProperty("damageVariance")]
        public bool DamageVariance { get; set; }

        /// <summary>Large formations may fall short of the distance they were ordered to cover.</summary>
        [JsonProperty("movementFriction")]
        public bool MovementFriction { get; set; }

        [JsonIgnore]
        public bool IsStochastic => DamageVariance || MovementFriction;

        /// <summary>Exact outcomes; <see cref="Rules.Apply"/> needs no random source.</summary>
        public static Ruleset Deterministic => new Ruleset
        {
            DamageVariance = false,
            MovementFriction = false,
        };

        /// <summary>The standard game: both uncertainties on, scaled by echelon.</summary>
        public static Ruleset Standard => new Ruleset
        {
            DamageVariance = true,
            MovementFriction = true,
        };

        public Ruleset Clone() => new Ruleset
        {
            DamageVariance = DamageVariance,
            MovementFriction = MovementFriction,
        };
    }
}
