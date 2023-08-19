using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace TensorFlowLite
{
public class YOLO : BaseImagePredictor<float>
{
    private float scoreThreshold = 0.5f;

    [System.Serializable]
    public class Options
    {
        [FilePopup("*.tflite")]
        public string modelPath = string.Empty;
        public AspectMode aspectMode = AspectMode.Fit;
        public Accelerator accelerator = Accelerator.GPU;
    }

    public readonly struct Result
    {
        public readonly int classID;
        public readonly float score;
        public readonly Rect rect;

        public Result(int classID, float score, Rect rect)
        {
            this.classID = classID;
            this.score = score;
            this.rect = rect;
        }
    }

    private float[,] output;
    private List<Result> results;

    public YOLO(Options options)
        : base(options.modelPath, options.accelerator)
    {
        resizeOptions.aspectMode = options.aspectMode;
        resizeOptions.rotationDegree = 90f; // Adding rotation degree to compensate for ARFoundation CPU images being rotated.

        int[] odim = interpreter.GetOutputTensorInfo(0).shape;
        output = new float[odim[1], odim[2]]; // Should be 84 x something
    }

    public YOLO(Options options, InterpreterOptions interpreterOptions)
        : base(options.modelPath, interpreterOptions)
    {
        resizeOptions.aspectMode = options.aspectMode;
        resizeOptions.rotationDegree = 90f;

        int[] odim = interpreter.GetOutputTensorInfo(0).shape;
        output = new float[odim[1], odim[2]];
    }

    public override void Invoke(Texture inputTex)
    {
        ToTensor(inputTex, inputTensor);

        interpreter.SetInputTensorData(0, inputTensor);
        interpreter.Invoke();
        interpreter.GetOutputTensorData(0, output);
    }

    public async UniTask<List<Result>> InvokeAsync(Texture inputTex, CancellationToken cancellationToken)
    {
        await ToTensorAsync(inputTex, inputTensor, cancellationToken);
        await UniTask.SwitchToThreadPool();

        interpreter.SetInputTensorData(0, inputTensor);
        interpreter.Invoke();
        interpreter.GetOutputTensorData(0, output);

        var results = GetResults();

        await UniTask.SwitchToMainThread(cancellationToken);
        return results;
    }

    public List<Result> GetResults()
    {
        results = new List<Result>();

        List<YOLOUtils.Box> boxes = YOLOUtils.ObjectDetection(output, scoreThreshold);
        foreach (var box in boxes) {
            // Invert Y to adapt Unity UI space
            float top = 1f - box.bbox[1];
            float left = box.bbox[0];
            float bottom = 1f - box.bbox[3];
            float right = box.bbox[2];

            results.Add(new Result(
                classID: box.cls,
                score: box.score,
                rect: new Rect(left, top, right - left, top - bottom)));

            Debug.unityLogger.Log("mytag", box.cls);
            Debug.unityLogger.Log("mytag", box.score);
        }

        return results;
    }
}
}
