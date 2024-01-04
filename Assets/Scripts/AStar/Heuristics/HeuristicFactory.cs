using System;

namespace AStar.Heuristics
{
    public static class HeuristicFactory
    {
        public static ICalculateHeuristic Create(HeuristicFormula heuristicFormula)
        {
            switch (heuristicFormula)
            {
                case HeuristicFormula.Manhattan:
                    return new Manhattan();
                case HeuristicFormula.EuclideanNoSQR:
                    return new EuclideanNoSQR();
                case HeuristicFormula.Octile:
                    return new Octile();
                default:
                    throw new ArgumentOutOfRangeException(nameof(heuristicFormula), heuristicFormula, null);
            }
        }
    }
}