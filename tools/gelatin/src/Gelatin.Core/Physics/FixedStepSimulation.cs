namespace Gelatin.Core.Physics;

public sealed class FixedStepSimulation
{
    private double _accumulator;
    public GelSolver Solver { get; }
    public QualitySettings Quality { get; }
    public double Speed { get; set; } = 1;
    public bool Paused { get; set; }

    public FixedStepSimulation(GelSolver solver, QualitySettings quality)
    {
        Solver = solver;
        Quality = quality;
    }

    public int Advance(double elapsedSeconds)
    {
        if (Paused || elapsedSeconds <= 0 || !double.IsFinite(elapsedSeconds)) return 0;
        var step = 1d / Quality.PhysicsHz;
        _accumulator = Math.Min(_accumulator + Math.Min(elapsedSeconds, 0.1) * Math.Clamp(Speed, 0.1, 1), 0.1);
        var steps = 0;
        while (_accumulator >= step)
        {
            Solver.Step((float)step);
            _accumulator -= step;
            steps++;
        }
        return steps;
    }

    public void ClearBacklog() => _accumulator = 0;
}
