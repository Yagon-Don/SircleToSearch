using System;

namespace SircleToSearch;

/// <summary>Spring physics matching Android's SpringForce — semi-implicit Euler with sub-stepping.</summary>
public sealed class M3Spring
{
    private readonly double _k;
    private readonly double _c;

    public double Pos;
    public double Vel;
    public double Target;

    public M3Spring(double stiffness, double dampingRatio)
    {
        _k = stiffness;
        _c = dampingRatio * 2 * Math.Sqrt(stiffness); // critical damping, mass = 1
    }

    public void Step(double dt)
    {
        const int subSteps = 12;
        var sub = dt / subSteps;
        for (var i = 0; i < subSteps; i++)
        {
            var accel = -_k * (Pos - Target) - _c * Vel;
            Vel += accel * sub;
            Pos += Vel * sub;
        }
    }

    public void Reset()
    {
        Pos = 0;
        Vel = 0;
        Target = 0;
    }
}
