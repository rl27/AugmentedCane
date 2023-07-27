using System;
using System.Collections;
using UnityEngine;
using Unity.Barracuda;

// https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/GettingStarted.html
public class Vision : MonoBehaviour
{
    public NNModel modelAsset;
    private Model model;
    private IWorker worker;

    private bool working = false;

    private float delay = 0.3f;

    private DateTime startTime;

    void Start()
    {
        model = ModelLoader.Load(modelAsset);
        // See worker types here: https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/Worker.html
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);
    }

    public IEnumerator Detect(Texture2D tex)
    {
        if (working)
            yield break;
        working = true;

        startTime = DateTime.Now;

        // https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/TensorHandling.html
        Tensor input = new Tensor(tex); // new Tensor(1, 640, 480, 3);
        worker.Execute(input);

        Tensor output = worker.PeekOutput();
        
        input.Dispose();
        output.Dispose();

        double timeSpent = (DateTime.Now - startTime).TotalSeconds;
        double newDelay = delay - timeSpent;

        yield return new WaitForSeconds((float) newDelay);
        working = false;
    }

    // Outputs for 640x480 images have dimensions 1,1,6300,84.
    // Need to run non-max suppression manually. https://github.com/ultralytics/ultralytics/blob/main/examples/YOLOv8-OpenCV-ONNX-Python/main.py

    public void Dispose()
    {
        worker.Dispose();
    }
}