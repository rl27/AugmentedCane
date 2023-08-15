using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace TensorFlowLite
{
    public class YOLO : BaseImagePredictor<float>
    {
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

        const int MAX_DETECTION = 10;
        private readonly float[,] outputs0;
        private readonly Result[] results = new Result[MAX_DETECTION];

        public YOLO(Options options)
            : base(options.modelPath, options.accelerator)
        {
            resizeOptions.aspectMode = options.aspectMode;
            resizeOptions.rotationDegree = 90f; // Adding rotation degree to compensate for ARFoundation CPU images being rotated.

            int[] odim = interpreter.GetOutputTensorInfo(0).shape;
            outputs0 = new float[odim[1], odim[2]]; // Should be 84 x something
        }

        public YOLO(Options options, InterpreterOptions interpreterOptions)
            : base(options.modelPath, interpreterOptions)
        {
            resizeOptions.aspectMode = options.aspectMode;
            resizeOptions.rotationDegree = 90f;

            int[] odim = interpreter.GetOutputTensorInfo(0).shape;
            outputs0 = new float[odim[1], odim[2]];
        }

        public override void Invoke(Texture inputTex)
        {
            ToTensor(inputTex, inputTensor);

            interpreter.SetInputTensorData(0, inputTensor);
            interpreter.Invoke();
            interpreter.GetOutputTensorData(0, outputs0);

        }

        public async UniTask<Result[]> InvokeAsync(Texture inputTex, CancellationToken cancellationToken)
        {
            await ToTensorAsync(inputTex, inputTensor, cancellationToken);
            await UniTask.SwitchToThreadPool();

            interpreter.SetInputTensorData(0, inputTensor);
            interpreter.Invoke();
            interpreter.GetOutputTensorData(0, outputs0);
            Debug.unityLogger.Log("mytag", "success");

            var results = GetResults();

            await UniTask.SwitchToMainThread(cancellationToken);
            return results;
        }

        public Result[] GetResults()
        {
            // for (int i = 0; i < MAX_DETECTION; i++)
            // {
            //     // Invert Y to adapt Unity UI space
            //     float top = 1f - outputs0[i, 0];
            //     float left = outputs0[i, 1];
            //     float bottom = 1f - outputs0[i, 2];
            //     float right = outputs0[i, 3];

            //     results[i] = new Result(
            //         classID: (int) outputs1[i],
            //         score: outputs2[i],
            //         rect: new Rect(left, top, right - left, top - bottom));
            // }
            return results;
        }
    }
}
