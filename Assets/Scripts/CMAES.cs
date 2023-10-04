using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;

// https://github.com/yn-cloud/CMAES.NET/blob/master/CMAES.NET/CMAESOptimizer.cs
namespace CMAESnet
{
    public class CMAES
    {
        private readonly CMA cma; // https://github.com/yn-cloud/CMAES.NET/blob/master/CMAES.NET/CMA.cs
        private Vector<double> parameters;

        // Inputs: initial mean, step size for CMA-ES, (optional) seed number
        public CMAES(IList<double> initial, double sigma, int randSeed = 0)
        {
            cma = new CMA(initial, sigma, seed: randSeed, tol_sigma: 1e-1, tol_C: 1e-1);
        }

        // Inputs: initial mean, step size for CMA-ES, lower/upper bounds of search range, (optional) seed number
        public CMAES(IList<double> initial, double sigma, IList<double> lowerBounds, IList<double> upperBounds, int randSeed = 0)
        {
            if (initial.Count != lowerBounds.Count)
                throw new ArgumentException("Length of lowerBounds must be equal to that of initial.");
            if (initial.Count != upperBounds.Count)
                throw new ArgumentException("Length of upperBounds must be equal to that of initial");

            Matrix<double> bounds = Matrix<double>.Build.Dense(initial.Count, 2);
            bounds.SetColumn(0, lowerBounds.ToArray());
            bounds.SetColumn(1, upperBounds.ToArray());

            cma = new CMA(initial, sigma, bounds, seed: randSeed, tol_sigma: 1e-1, tol_C: 1e-1);
        }

        List<Tuple<Vector<double>, double>> solutions = new List<Tuple<Vector<double>, double>>();

        public (double[], bool) Optimize(double[] x, double output)
        {
            Vector<double> vx = Vector<double>.Build.Dense(x);
            solutions.Add(new Tuple<Vector<double>, double>(vx, output));

            // If reached 4 + floor(3ln(N)), update covariance matrix and step size
            if (solutions.Count == cma.PopulationSize) {
                cma.Tell(solutions);
                solutions.Clear();
            }

            return (cma.Ask().ToArray(), cma.IsConverged());
        }
    }
}
