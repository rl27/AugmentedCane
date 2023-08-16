using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

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

        public struct Box {
            public Box(Vector4 b, float s, int c) {
                bbox = b;
                score = s;
                cls = c;
                area = (bbox[2] - bbox[0]) * (bbox[3] - bbox[1]);
            }
            public Vector4 bbox;
            public float score;
            public int cls;
            public float area;
        }

        private int[] odim;
        private float[,] output;
        private List<Result> results;

        public YOLO(Options options)
            : base(options.modelPath, options.accelerator)
        {
            resizeOptions.aspectMode = options.aspectMode;
            resizeOptions.rotationDegree = 90f; // Adding rotation degree to compensate for ARFoundation CPU images being rotated.

            odim = interpreter.GetOutputTensorInfo(0).shape;
            output = new float[odim[1], odim[2]]; // Should be 84 x something
        }

        public YOLO(Options options, InterpreterOptions interpreterOptions)
            : base(options.modelPath, interpreterOptions)
        {
            resizeOptions.aspectMode = options.aspectMode;
            resizeOptions.rotationDegree = 90f;

            odim = interpreter.GetOutputTensorInfo(0).shape;
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
            List<Box> boxes = new List<Box>();
            for (int j = 0; j < odim[2]; j++) {
                float maxScore = -1f;
                int maxIndex = -1;
                for (int i = 4; i < odim[1]; i++) {
                    float score = output[i, j];
                    if (score > maxScore) {
                        maxScore = score;
                        maxIndex = i - 4;
                    }
                }
                if (maxScore >= 0.5f) {
                    Vector4 bbox = YOLOtoBbox(output[0, j], output[1, j], output[2, j], output[3, j]);
                    boxes.Add(new Box(bbox, maxScore, maxIndex));
                }
            }

            if (boxes.Count > 0) {
                Debug.unityLogger.Log("mytag", "x");
                NMS(boxes, 0.45f);
                foreach (var b in boxes) {
                    Debug.unityLogger.Log("mytag", b.cls);
                    Debug.unityLogger.Log("mytag", b.score);
                    Debug.unityLogger.Log("mytag", b.bbox);
                }
            }

            results = new List<Result>();
            for (int i = 0; i < boxes.Count; i++) {
                // Invert Y to adapt Unity UI space
                Box box = boxes[i];
                float top = 1f - box.bbox[1];
                float left = box.bbox[0];
                float bottom = 1f - box.bbox[3];
                float right = box.bbox[2];

                results.Add(new Result(
                    classID: box.cls,
                    score: box.score,
                    rect: new Rect(left, top, right - left, top - bottom)));
            }

            return results;
        }

        // 1. Select box with highest confidence
        // 2. Find IoU with all other boxes - if IoU is greater than some threshold, e.g. 0.45, remove box
        // 3. Select box with next-highest confidence and repeat
        void NMS(List<Box> boxes, float iouThreshold)
        {
            boxes.Sort(CompareBoxes); // Sort in descending order of confidence

            for (int i = 0; i < boxes.Count - 1; i++) {
                for (int j = i + 1; j < boxes.Count; j++) {
                    if (IoU(boxes[i], boxes[j]) > iouThreshold) {
                        boxes.RemoveAt(j);
                        j--;
                    }   
                }
            }
        }

        float IoU(Box box1, Box box2)
        {
            Vector4 b1 = box1.bbox;
            Vector4 b2 = box2.bbox;
            float x1 = Math.Max(b1[0], b2[0]);
            float y1 = Math.Max(b1[1], b2[1]);
            float x2 = Math.Min(b1[2], b2[2]);
            float y2 = Math.Min(b1[3], b2[3]);

            if (x2 > x1 && y2 > y1) {
                float intersection = (x2 - x1) * (y2 - y1);
                float union = box1.area + box2.area - intersection;
                return intersection / union;
            }
            return 0;
        }

        // Returns values such that sorted list is in descending orderr.
        int CompareBoxes(Box x, Box y)
        {
            if (x.score > y.score)
                return -1;
            else if (y.score > x.score)
                return 1;
            return 0;
        }

        // Returns [x1, y1, x2, y2]
        private Vector4 YOLOtoBbox(float x, float y, float w, float h)
        {
            return new Vector4(x-w/2, y-h/2, x+w/2, y+h/2);
        }
    }
}
