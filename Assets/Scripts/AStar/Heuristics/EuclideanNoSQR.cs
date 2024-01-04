using System;

namespace AStar.Heuristics
{
    public class EuclideanNoSQR : ICalculateHeuristic
    {
        public float Calculate(Position source, Position destination)
        {
            var heuristicEstimate = 2;
            float h = heuristicEstimate * (MathF.Pow((source.Row - destination.Row), 2) + MathF.Pow((source.Column - destination.Column), 2));
            return h;
        }
    }
}