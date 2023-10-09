using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MathNet.Numerics.LinearAlgebra;

// Based on: https://github.com/yn-cloud/CMAES.NET/blob/master/CMAES.NET/CMAESOptimizer.cs

[Serializable]
public class CMAES
{
    public CMA2 cma;
    public double[] ask;
    public List<double[]> inputs = new List<double[]>();
    public List<double> outputs = new List<double>();
    public List<double[]> means = new List<double[]>();
    public List<double> sigmas = new List<double>();

    // Inputs: initial mean, step size for CMA-ES, (optional) seed number
    public CMAES(IList<double> initial, double sigma, int randSeed = 0)
    {
        cma = new CMA2(initial, sigma, seed: randSeed, tol_sigma: 1e-1, tol_C: 1e-1);
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

        cma = new CMA2(initial, sigma, bounds, seed: randSeed, tol_sigma: 1e-1, tol_C: 1e-1);
    }

    public List<Tuple<Vector<double>, double>> solutions = new List<Tuple<Vector<double>, double>>();

    public (double[], bool) Optimize(double[] x, double output)
    {
        inputs.Add(x);
        outputs.Add(output);
        means.Add(cma._mean.ToArray());
        sigmas.Add(cma._sigma);

        Vector<double> vx = Vector<double>.Build.Dense(x);
        solutions.Add(new Tuple<Vector<double>, double>(vx, output));

        // If reached 4 + floor(3ln(N)), update covariance matrix and step size
        if (solutions.Count == cma.PopulationSize) {
            cma.Tell(solutions);
            solutions.Clear();
        }

        ask = cma.Ask().ToArray();

        BinarySerialization.WriteToBinaryFile<CMAES>(Path.Combine(Application.persistentDataPath, "cmaes.bin"), this);

        return (ask, cma.IsConverged());
    }
}
