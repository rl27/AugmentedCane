using System.Runtime.InteropServices;

namespace AStar.Collections.PathFinder
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct PathFinderNode
    {
        /// <summary>
        /// The position of the node
        /// </summary>
        public Position Position { get; }
        
        /// <summary>
        /// Distance from home
        /// </summary>
        public float G { get; }
        
        /// <summary>
        /// Heuristic
        /// </summary>
        public float H { get; }

        /// <summary>
        /// This nodes parent
        /// </summary>
        public Position ParentNodePosition { get; }

        /// <summary>
        /// Gone + Heuristic (H)
        /// </summary>
        public float F { get; }

        /// <summary>
        /// If the node has been considered yet
        /// </summary>
        public bool HasBeenVisited => F > 0;

        public PathFinderNode(Position position, float g, float h, Position parentNodePosition)
        {
            Position = position;
            G = g;
            H = h;
            ParentNodePosition = parentNodePosition;
            
            F = g + h;
        }
    }
}
