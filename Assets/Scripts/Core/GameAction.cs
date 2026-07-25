using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tactix.Core
{
    /// <summary>
    /// Base class for all player actions. Actions are structured, pointer-based
    /// objects (unit ids and target ids/coordinates), never fixed-size board
    /// enumerations, so the action space is board-size-agnostic.
    /// Serialized with an "actionType" discriminator: "move" | "attack" | "endTurn".
    /// </summary>
    public abstract class GameAction
    {
        [JsonProperty("actionType", Order = -2)]
        public abstract string ActionType { get; }
    }

    public sealed class MoveAction : GameAction
    {
        public const string TypeName = "move";
        public override string ActionType => TypeName;

        [JsonProperty("unitId")]
        public int UnitId { get; set; }

        [JsonProperty("targetX")]
        public int TargetX { get; set; }

        [JsonProperty("targetY")]
        public int TargetY { get; set; }

        public override string ToString() => $"Move(unit {UnitId} -> {TargetX},{TargetY})";
    }

    public sealed class AttackAction : GameAction
    {
        public const string TypeName = "attack";
        public override string ActionType => TypeName;

        [JsonProperty("unitId")]
        public int UnitId { get; set; }

        [JsonProperty("targetUnitId")]
        public int TargetUnitId { get; set; }

        public override string ToString() => $"Attack(unit {UnitId} -> unit {TargetUnitId})";
    }

    public sealed class EndTurnAction : GameAction
    {
        public const string TypeName = "endTurn";
        public override string ActionType => TypeName;

        public override string ToString() => "EndTurn";
    }

    /// <summary>Polymorphic (de)serialization of <see cref="GameAction"/> via the "actionType" field.</summary>
    public sealed class GameActionConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(GameAction);

        // Writing uses the default object serialization of the concrete type.
        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var obj = JObject.Load(reader);
            string type = obj["actionType"]?.Value<string>();
            switch (type)
            {
                case MoveAction.TypeName: return obj.ToObject<MoveAction>();
                case AttackAction.TypeName: return obj.ToObject<AttackAction>();
                case EndTurnAction.TypeName: return obj.ToObject<EndTurnAction>();
                default: throw new JsonSerializationException($"Unknown actionType '{type}'");
            }
        }
    }
}
