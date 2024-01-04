using System;

namespace AStar.Heuristics
{
    public class Octile : ICalculateHeuristic
    {
        public float Calculate(Position source, Position destination)
        {
            float dx = MathF.Abs(source.Row - destination.Row);
            float dy = MathF.Abs(source.Column - destination.Column);

            return (dx < dy) ? 0.4142f * dx + dy : 4142f * dy + dx;
        }
    }
}