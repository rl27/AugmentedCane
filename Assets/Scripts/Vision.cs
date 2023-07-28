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

    void Start()
    {
        model = ModelLoader.Load(modelAsset);
        // See worker types here: https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/Worker.html
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);
    }

    // https://forum.unity.com/threads/asynchronous-inference-in-barracuda.1370181/
    public IEnumerator Detect(Texture2D tex)
    {
        if (working)
            yield break;
        working = true;

        // https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/TensorHandling.html
        Tensor input = new Tensor(tex);  // Tensor input = new Tensor(1, 640, 480, 3);

        var enumerator = worker.StartManualSchedule(input);
        int step = 0;
        int stepsPerFrame = 20;
        while (enumerator.MoveNext()) {
            if (++step % stepsPerFrame == 0) yield return null;
        }
        Tensor output = worker.PeekOutput();
        Debug.unityLogger.Log("mytag", output.dimensions);

        input.Dispose();
        output.Dispose();

        working = false;
    }

    // Outputs for 640x480 images have dimensions 1,1,6300,84.
    // Need to run non-max suppression manually. https://github.com/ultralytics/ultralytics/blob/main/examples/YOLOv8-OpenCV-ONNX-Python/main.py
    void NMS()
    {

    }

    void OnDisable()
    {
        worker.Dispose();
    }
}