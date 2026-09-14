namespace SircleToSearch;

/// <summary>
/// Drives the Material 3 loading-indicator morph/rotation — ported from
/// LoadingIndicatorAnimatorDelegate.java's actual constants: 650ms per shape,
/// spring k=200/ζ=0.6, dual rotation (50° constant + 90° spring-driven per cycle).
/// </summary>
public sealed class M3Animator
{
    public const double DurationPerShapeMs = 650;
    public const double ConstantRotationDeg = 50;
    public const double ExtraRotationDeg = 90;
    public const double DefaultSpringStiffness = 200;
    public const double DefaultSpringDamping = 0.6;

    private readonly M3Spring _spring;
    private double _morphTarget = 1;
    private double _fraction;
    private double _elapsedMs;
    private double _lastTs = -1;
    private int _prevCycle;

    public double Rotation { get; private set; }
    public double Morph { get; private set; }

    public M3Animator(double stiffness = DefaultSpringStiffness, double damping = DefaultSpringDamping)
    {
        _spring = new M3Spring(stiffness, damping);
        _spring.Target = 1;
    }

    /// <summary>Advance the animation. <paramref name="tsSeconds"/> is a monotonic clock in seconds.</summary>
    public void Update(double tsSeconds)
    {
        if (_lastTs < 0) _lastTs = tsSeconds;
        var dt = System.Math.Min(tsSeconds - _lastTs, 0.1);
        _lastTs = tsSeconds;
        if (dt <= 0) return;

        _elapsedMs += dt * 1000;
        var cycle = (int)(_elapsedMs / DurationPerShapeMs);

        if (cycle > _prevCycle)
        {
            _morphTarget += cycle - _prevCycle;
            _spring.Target = _morphTarget;
            _prevCycle = cycle;
        }

        _fraction = _elapsedMs % DurationPerShapeMs / DurationPerShapeMs;
        _spring.Step(dt);

        var basePart = _morphTarget - 1;
        var perShape = _spring.Pos - basePart;
        Rotation = ((ConstantRotationDeg + ExtraRotationDeg) * basePart
                    + ConstantRotationDeg * _fraction
                    + ExtraRotationDeg * perShape) % 360;

        Morph = _spring.Pos;
    }
}
