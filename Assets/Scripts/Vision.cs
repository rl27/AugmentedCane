using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Barracuda;

// https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/GettingStarted.html
public class Vision : MonoBehaviour
{
    public NNModel modelAsset;
    private Model model;
    private IWorker worker;

    private bool working = false;

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

    public List<Box> boxes = new List<Box>();

    void Start()
    {
        model = ModelLoader.Load(modelAsset);
        // See worker types here: https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/Worker.html
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);

        // Tensor input = new Tensor(1, 640, 480, 3);
        // worker.Execute(input);
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
        Tensor output = worker.PeekOutput(); // (n:1, h:1, w:6300, c:84)

        // Find boxes with high confidence
        // https://github.com/ultralytics/ultralytics/blob/main/examples/YOLOv8-OpenCV-ONNX-Python/main.py#L41
        boxes = new List<Box>();
        for (int i = 0; i < output.width; i++) {
            float maxScore = -1f;
            int maxIndex = -1;
            for (int j = 4; j < output.channels; j++) {
                float score = output[0, 0, i, j];
                if (score > maxScore) {
                    maxScore = score;
                    maxIndex = j - 4;
                }
            }
            if (maxScore >= 0.5f) {
                Vector4 bbox = YOLOtoBbox(output[0, 0, i, 0], output[0, 0, i, 1], output[0, 0, i, 2], output[0, 0, i, 3]);
                boxes.Add(new Box(bbox, maxScore, maxIndex));
            }
        }

        if (boxes.Count > 0) {
            Debug.unityLogger.Log("mytag", "x");
            NMS(boxes, 0.45f);
            foreach (var b in boxes) {
                Debug.unityLogger.Log("mytag", b.cls);
                Debug.unityLogger.Log("mytag", b.score);
            }
        }

        input.Dispose();
        output.Dispose();

        working = false;
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

    void OnDisable()
    {
        worker.Dispose();
    }

    // Returns [x1, y1, x2, y2]
    Vector4 YOLOtoBbox(float x, float y, float w, float h)
    {
        return new Vector4(x-w/2, y-h/2, x+w/2, y+h/2);
    }
}