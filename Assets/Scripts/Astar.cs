using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;  

namespace AStarSharp
{
    public class Node
    {
        public Node Parent;
        public Vector2 Position;
        public float DistanceToTarget;
        public float Cost;
        public float Weight;
        public float F
        {
            get
            {
                if (DistanceToTarget != -1 && Cost != -1)
                    return DistanceToTarget + Cost;
                else
                    return -1;
            }
        }
        public float Sum;
        public static float SumThreshold = 5f;

        public Node(Vector2 pos, float weight = 1)
        {
            Parent = null;
            Position = pos;
            DistanceToTarget = -1;
            Cost = 1;
            Weight = weight;
            Sum = 0;
        }
    }

    public class Astar
    {
        List<List<Node>> Grid;
        int numNodes;

        public Astar(List<List<Node>> grid, int num)
        {
            Grid = grid;
            numNodes = num;
        }

        public Stack<Node> FindPath(Vector2 Start, Vector2 End)
        {
            Node start = new Node(Start);
            Node end = new Node(End);

            Stack<Node> Path = new Stack<Node>();
            PriorityQueue<Node, float> OpenList = new PriorityQueue<Node,float>();
            List<Node> ClosedList = new List<Node>();
            List<Node> adjacencies;
            Node current = start;
           
            // add start node to Open List
            OpenList.Enqueue(start, start.F);

            while(OpenList.Count != 0 && !ClosedList.Exists(x => x.Position == end.Position))
            {
                current = OpenList.Dequeue();
                ClosedList.Add(current);
                adjacencies = GetAdjacentNodes(current);

                foreach(Node n in adjacencies)
                {
                    if (!ClosedList.Contains(n) && n.Sum < Node.SumThreshold)
                    {
                        bool isFound = false;
                        foreach (var oLNode in OpenList.UnorderedItems)
                        {
                            if (oLNode.Element == n)
                            {
                                isFound = true;
                            }
                        }
                        if (!isFound)
                        {
                            n.Parent = current;
                            n.DistanceToTarget = Math.Abs(n.Position.x - end.Position.x) + Math.Abs(n.Position.y - end.Position.y);
                            n.Cost = n.Weight + n.Parent.Cost;
                            OpenList.Enqueue(n, n.F);
                        }
                    }
                }
            }
            
            // construct path, if end was not closed return null
            if(!ClosedList.Exists(x => x.Position == end.Position))
            {
                return null;
            }

            // if all good, return path
            Node temp = ClosedList[ClosedList.IndexOf(current)];
            if (temp == null) return null;
            do
            {
                Path.Push(temp);
                temp = temp.Parent;
            } while (temp != start && temp != null) ;
            return Path;
        }
		
        private List<Node> GetAdjacentNodes(Node n)
        {
            List<Node> temp = new List<Node>();

            int row = (int)n.Position.y;
            int col = (int)n.Position.x;

            if(row + 1 < 2*numNodes)
            {
                temp.Add(Grid[col][row + 1]);
            }
            if(row - 1 >= 0)
            {
                temp.Add(Grid[col][row - 1]);
            }
            if(col - 1 >= 0)
            {
                temp.Add(Grid[col - 1][row]);
            }
            if(col + 1 < 2*numNodes)
            {
                temp.Add(Grid[col + 1][row]);
            }

            return temp;
        }
    }
}
