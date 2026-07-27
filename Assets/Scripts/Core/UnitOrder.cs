namespace Tactix.Core
{
    /// <summary>
    /// A standing / queued intent for a unit. Orders live outside
    /// <see cref="GameState"/>; the executor turns the head of each queue into
    /// ordinary <see cref="GameAction"/>s that <see cref="Rules.Apply"/> validates.
    /// </summary>
    public abstract class UnitOrder
    {
        public abstract string Kind { get; }
    }

    /// <summary>Advance toward a world point; completes when the unit is close enough.</summary>
    public sealed class MoveToOrder : UnitOrder
    {
        public override string Kind => "moveTo";
        public double X { get; }
        public double Y { get; }

        public MoveToOrder(double x, double y)
        {
            X = x;
            Y = y;
        }

        public override string ToString() => $"MoveTo({X:0.###},{Y:0.###})";
    }

    /// <summary>Close with and attack a specific enemy until it is gone or unreachable.</summary>
    public sealed class EngageOrder : UnitOrder
    {
        public override string Kind => "engage";
        public int TargetUnitId { get; }

        public EngageOrder(int targetUnitId) => TargetUnitId = targetUnitId;

        public override string ToString() => $"Engage({TargetUnitId})";
    }

    /// <summary>Stay inside a radius of a point; move back in if pushed out.</summary>
    public sealed class HoldOrder : UnitOrder
    {
        public override string Kind => "hold";
        public double X { get; }
        public double Y { get; }
        public double Radius { get; }

        public HoldOrder(double x, double y, double radius = 2.0)
        {
            X = x;
            Y = y;
            Radius = radius;
        }

        public override string ToString() => $"Hold({X:0.###},{Y:0.###},r={Radius:0.###})";
    }

    /// <summary>Heal / repair a friendly when in range; otherwise close with them.</summary>
    public sealed class SupportOrder : UnitOrder
    {
        public override string Kind => "support";
        public int TargetUnitId { get; }

        public SupportOrder(int targetUnitId) => TargetUnitId = targetUnitId;

        public override string ToString() => $"Support({TargetUnitId})";
    }
}
