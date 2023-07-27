// using System.Collections;
// using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;

// https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/GettingStarted.html
public class Vision : MonoBehaviour
{
    public NNModel modelAsset;
    private Model model;

    void Start()
    {
        model = ModelLoader.Load(modelAsset);
        var worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);

        Tensor input = new Tensor(1, 640, 480, 3);
        worker.Execute(input);

        Tensor output = worker.PeekOutput();
        Debug.Log(output);
        
        input.Dispose();
        output.Dispose();
        worker.Dispose();
    }
}