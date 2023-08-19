using System;
using System.Collections;
using System.Collections.Generic;
using TensorFlowLite;
using UnityEngine;
using Unity.Barracuda;

public class YOLOUtils
{
    private static IOps ops = new BurstCPUOps();

    public struct Box {
        public Box(Vector4 b, float s, int c) {
            bbox = b;
            score = s;
            cls = c;
            area = (bbox[2] - bbox[0]) * (bbox[3] - bbox[1]);
            mask = new float[0];
        }

        public Box(Vector4 b, float s, int c, float[] m) {
            bbox = b;
            score = s;
            cls = c;
            area = (bbox[2] - bbox[0]) * (bbox[3] - bbox[1]);
            mask = m;
        }
        public Vector4 bbox;
        public float score;
        public int cls;
        public float area;
        public float[] mask;
    }

    // output0 should be [84, 2100] for a 320x320 input. 2100 will vary based on input size.
    // output0 is the output from a yolov8n object detection model.
    public static List<Box> ObjectDetection(float[,] output0, float scoreThreshold)
    {
        List<Box> boxes = new List<Box>();
        for (int j = 0; j < output0.GetLength(1); j++) {
            float maxScore = -1f;
            int maxIndex = -1;
            for (int i = 4; i < output0.GetLength(0); i++) {
                float score = output0[i, j];
                if (score > maxScore) {
                    maxScore = score;
                    maxIndex = i - 4;
                }
            }
            if (maxScore >= scoreThreshold) {
                Vector4 bbox = YOLOtoBbox(output0[0, j], output0[1, j], output0[2, j], output0[3, j]);
                boxes.Add(new Box(bbox, maxScore, maxIndex));
            }
        }

        if (boxes.Count > 0)
            NMS(boxes, 0.45f);
        return boxes;
    }

    // https://github.com/ultralytics/ultralytics/issues/2953#issuecomment-1573030054
    // https://github.com/ibaiGorordo/ONNX-YOLOv8-Instance-Segmentation/blob/4fe21d04cb4d42465fbc9191ceb89d0e0d9cd50d/yoloseg/YOLOSeg.py#L91
    // https://dev.to/andreygermanov/how-to-implement-instance-segmentation-using-yolov8-neural-network-3if9#process_output
    // output0 should be [116, 2100] for a 320x320 input. 2100 will vary based on input size.
    // output1 should be [80, 80, 32] for a 320x320 input. 80,80 will vary based on input size.
    // output0 and output1 are the outputs from a yolov8n-seg model.
    public static List<Box> Segmentation(float[,] output0, float[,,] output1, float scoreThreshold)
    {
        Tensor o0 = new Tensor(output0.GetLength(0), output0.GetLength(1), output0);
        o0 = ops.StridedSlice(o0, new int[]{84,0,0,0}, new int[]{output0.GetLength(0),0,0,output0.GetLength(1)}, new int[]{1,1,1,1}); // 32, 1, 1, 2100

        float[,] flattened = new float[output1.GetLength(0) * output1.GetLength(1), output1.GetLength(2)];
        Buffer.BlockCopy(output1, 0, flattened, 0, sizeof(float) * flattened.GetLength(0) * flattened.GetLength(1));
        Tensor masks = new Tensor(flattened.GetLength(0), flattened.GetLength(1), flattened); // 6400, 1, 1, 32
        masks = ops.Sigmoid(ops.Transpose(ops.MatMul(masks, false, o0, false))); // 2100, 1, 1, 6400
        // NOTE: DO NOT USE "true" IN MATMUL. IT DOES NOT WORK!

        List<Box> boxes = new List<Box>();
        for (int j = 0; j < output0.GetLength(1); j++) {
            float maxScore = -1f;
            int maxIndex = -1;
            for (int i = 4; i < 84; i++) {
                float score = output0[i, j];
                if (score > maxScore) {
                    maxScore = score;
                    maxIndex = i - 4;
                }
            }
            if (maxScore >= scoreThreshold) {
                Vector4 bbox = YOLOtoBbox(output0[0, j], output0[1, j], output0[2, j], output0[3, j]);
                float[] mask = TensorExtensions.AsFloats(ops.StridedSlice(masks, new int[]{j,0,0,0}, new int[]{j+1,0,0,masks.channels}, new int[]{1,1,1,1}));
                boxes.Add(new Box(bbox, maxScore, maxIndex, mask));
            }
        }
        
        o0.Dispose();
        masks.Dispose();

        if (boxes.Count > 0)
            NMS(boxes, 0.45f);
        return boxes;
    }

    // 1. Select box with highest confidence
    // 2. Find IoU with all other boxes - if IoU is greater than some threshold, e.g. 0.45, remove box
    // 3. Select box with next-highest confidence and repeat
    private static void NMS(List<Box> boxes, float iouThreshold)
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

    private static float IoU(Box box1, Box box2)
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
    private static int CompareBoxes(Box x, Box y)
    {
        if (x.score > y.score)
            return -1;
        else if (y.score > x.score)
            return 1;
        return 0;
    }

    // Returns [x1, y1, x2, y2]
    private static Vector4 YOLOtoBbox(float x, float y, float w, float h)
    {
        return new Vector4(x-w/2, y-h/2, x+w/2, y+h/2);
    }
}
