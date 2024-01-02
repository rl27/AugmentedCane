using AStar;
using AStar.Options;
using AStar.Heuristics;
using UnityEngine;

public class Astar2
{
    public WorldGrid worldGrid;
    PathFinderOptions pathfinderOptions;
    int gridWidth;

    public Astar2(int width)
    {
        gridWidth = width;

        short[,] grid = new short[gridWidth, gridWidth];
        worldGrid = new WorldGrid(grid);

        pathfinderOptions = new PathFinderOptions {
            PunishChangeDirection = true,
            UseDiagonals = true,
            HeuristicFormula = HeuristicFormula.EuclideanNoSQR,
        };
    }

    public Position[] Pathfind(Position start, Position target)
    {
        var pathfinder = new PathFinder(worldGrid, pathfinderOptions);
        return pathfinder.FindPath(start, target);
    }
}