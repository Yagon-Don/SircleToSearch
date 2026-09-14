using System;
using System.Windows.Media;
using System.Windows.Shapes;
using Point = System.Windows.Point;

namespace SircleToSearch;

/// <summary>
/// Drives a WPF Polygon to render the real Material 3 loading indicator — the shape-morphing
/// blob (SoftBurst → Cookie9 → Pentagon → Pill → Sunny → Cookie4 → Oval) that replaced the
/// classic circular spinner, ported from androidx.compose.material3's actual shape data and
/// animation constants (see M3Shapes/M3Spring/M3Animator) instead of an approximation.
/// </summary>
public sealed class MorphingLoader
{
    // Indicator occupies ~79% of its box (38dp / 48dp in the Material spec).
    private const double SizeRatio = 0.79;

    private readonly Polygon _polygon;
    private readonly double _boxRadius;
    private readonly M3Animator _animator = new();
    private DateTime _startedAt;
    private bool _running;

    public MorphingLoader(Polygon polygon, double radius)
    {
        _polygon = polygon;
        _boxRadius = radius;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _startedAt = DateTime.UtcNow;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var elapsedSeconds = (DateTime.UtcNow - _startedAt).TotalSeconds;
        _animator.Update(elapsedSeconds);

        var shape = M3Shapes.GetMorphedShape(_animator.Morph);
        var rotationRad = _animator.Rotation * Math.PI / 180.0;
        var cos = Math.Cos(rotationRad);
        var sin = Math.Sin(rotationRad);
        var scale = _boxRadius * SizeRatio;
        var center = _boxRadius;

        var points = new PointCollection(shape.Length);
        foreach (var p in shape)
        {
            // Points are pre-normalized to [-1, 1]; rotate, then scale+center into the box.
            var rx = p.X * cos - p.Y * sin;
            var ry = p.X * sin + p.Y * cos;
            points.Add(new Point(center + rx * scale, center + ry * scale));
        }
        _polygon.Points = points;
    }
}
