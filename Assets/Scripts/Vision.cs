// using System.Collections;
// using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;

// https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/GettingStarted.html
public class Vision : MonoBehaviour
{
    public NNModel modelAsset;
    private Model model;
    private IWorker worker;

    private bool working = false;

    void Start()
    {
        model = ModelLoader.Load(modelAsset);
        // See worker types here: https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/Worker.html
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);
    }

    public void Detect(Texture2D tex)
    {
        if (working)
            return;
        working = true;

        // https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/TensorHandling.html
        Tensor input = new Tensor(tex); // new Tensor(1, 640, 480, 3);
        worker.Execute(input);

        Tensor output = worker.PeekOutput();
        Debug.unityLogger.Log("mytag", output);
        Debug.unityLogger.Log("mytag", output.ToString());
        
        input.Dispose();
        output.Dispose();

        working = false;
    }

    // Outputs for 640x480 images have dimensions 1,1,6300,84.
    // Need to run non-max suppression manually. https://github.com/ultralytics/ultralytics/blob/main/examples/YOLOv8-OpenCV-ONNX-Python/main.py


    public void Dispose()
    {
        worker.Dispose();
    }
}