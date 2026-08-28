using System.Numerics;

namespace Gelatin.Core.Physics;

public sealed class FixedStepSimulation
{
    private double _accumulator;
    private bool _wallContactActive;
    public GelSolver Solver { get; }
    public QualitySettings Quality { get; }
    public double Speed { get; set; } = 1;
    public bool Paused { get; set; }
    public double RuntimeSpeedPixelsPerSecond { get; set; }
    public double ViewportWidthPixels { get; set; } = 1;
    public double ViewportHeightPixels { get; set; } = 1;
    public int LastAdvanceBounceCount { get; private set; }

    public FixedStepSimulation(GelSolver solver, QualitySettings quality)
    {
        Solver = solver;
        Quality = quality;
    }

    public int Advance(double elapsedSeconds)
    {
        LastAdvanceBounceCount = 0;
        if (Paused || elapsedSeconds <= 0 || !double.IsFinite(elapsedSeconds)) return 0;
        var step = 1d / Quality.PhysicsHz;
        _accumulator = Math.Min(_accumulator + Math.Min(elapsedSeconds, 0.1) * Math.Clamp(Speed, 0.1, 1), 0.1);
        var steps = 0;
        while (_accumulator >= step)
        {
            Solver.Step((float)step);
            if (Solver.BouncedThisStep && !_wallContactActive) LastAdvanceBounceCount++;
            _wallContactActive = Solver.HasWallContactThisStep;
            MaintainRuntimeTranslationSpeed();
            _accumulator -= step;
            steps++;
        }
        return steps;
    }

    public void ResetToRest()
    {
        Solver.Reset(Vector2.Zero);
        _wallContactActive = false;
        LastAdvanceBounceCount = 0;
        ClearBacklog();
    }

    public void ClearBacklog() => _accumulator = 0;

    private void MaintainRuntimeTranslationSpeed()
    {
        if (!(RuntimeSpeedPixelsPerSecond > 0) || !double.IsFinite(RuntimeSpeedPixelsPerSecond) ||
            !(ViewportWidthPixels > 0) || !(ViewportHeightPixels > 0)) return;

        var worldVelocity = Solver.AverageVelocity();
        var pixelVelocity = new Vector2(
            worldVelocity.X * (float)ViewportWidthPixels,
            worldVelocity.Y * (float)ViewportHeightPixels);
        var length = pixelVelocity.Length();
        if (!(length > 1e-5f) || !float.IsFinite(length)) return;

        var desiredPixels = pixelVelocity / length * (float)RuntimeSpeedPixelsPerSecond;
        var desiredWorld = new Vector2(
            desiredPixels.X / (float)ViewportWidthPixels,
            desiredPixels.Y / (float)ViewportHeightPixels);
        Solver.AddUniformVelocity(desiredWorld - worldVelocity);
    }
}
