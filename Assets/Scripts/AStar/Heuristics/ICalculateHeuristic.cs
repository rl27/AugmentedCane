namespace AStar.Heuristics
{
    public interface ICalculateHeuristic
    {
        float Calculate(Position source, Position destination);
    }
}