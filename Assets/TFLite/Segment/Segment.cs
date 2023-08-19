using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using System.Linq;
using UnityEngine;

namespace TensorFlowLite
{
public class Segment : BaseImagePredictor<float>
{
    private float scoreThreshold = 0.5f;

    [System.Serializable]
    public class Options
    {
        [FilePopup("*.tflite")]
        public string modelPath = string.Empty;
        public AspectMode aspectMode = AspectMode.Fit;
        public Accelerator accelerator = Accelerator.GPU;
        public ComputeShader compute = null;
    }

    // Port from
    // https://github.com/tensorflow/examples/blob/master/lite/examples/image_segmentation/ios/ImageSegmentation/ImageSegmentator.swift
    private static readonly Color32[] COLOR_TABLE = new Color32[]
    {
        ToColor(0xFF00_0000), // Black
        ToColor(0xFF80_3E75), // Strong Purple
        ToColor(0xFFFF_6800), // Vivid Orange
        ToColor(0xFFA6_BDD7), // Very Light Blue
        ToColor(0xFFC1_0020), // Vivid Red
        ToColor(0xFFCE_A262), // Grayish Yellow
        ToColor(0xFF81_7066), // Medium Gray
        ToColor(0xFF00_7D34), // Vivid Green
        ToColor(0xFFF6_768E), // Strong Purplish Pink
        ToColor(0xFF00_538A), // Strong Blue
        ToColor(0xFFFF_7A5C), // Strong Yellowish Pink
        ToColor(0xFF53_377A), // Strong Violet
        ToColor(0xFFFF_8E00), // Vivid Orange Yellow
        ToColor(0xFFB3_2851), // Strong Purplish Red
        ToColor(0xFFF4_C800), // Vivid Greenish Yellow
        ToColor(0xFF7F_180D), // Strong Reddish Brown
        ToColor(0xFF93_AA00), // Vivid Yellowish Green
        ToColor(0xFF59_3315), // Deep Yellowish Brown
        ToColor(0xFFF1_3A13), // Vivid Reddish Orange
        ToColor(0xFF23_2C16), // Dark Olive Green
        ToColor(0xFF00_A1C2), // Vivid Blue
    };

    // https://www.tensorflow.org/lite/models/segmentation/overview

    // https://github.com/ultralytics/ultralytics/issues/2953#issuecomment-1573030054
    private float[,] outputs0; // [116, 2100] Coordinates of detected objects (4), class confidences (80), mask weights (32)
    private float[,,] outputs1; // [80, 80, 32] Prototype masks

    private ComputeShader compute;
    private ComputeBuffer labelBuffer;
    private ComputeBuffer colorTableBuffer;
    private RenderTexture labelTex;

    private Texture2D labelTex2D;
    private int labelToTexKernel;

    public Segment(Options options) : base(options.modelPath, options.accelerator)
    {
        Construct(options);
    }

    public Segment(Options options, InterpreterOptions interpreterOptions) : base(options.modelPath, interpreterOptions)
    {
        Construct(options);
    }

    private void Construct(Options options)
    {
        resizeOptions.aspectMode = options.aspectMode;
        resizeOptions.rotationDegree = 90f;

        var oShape0 = interpreter.GetOutputTensorInfo(0).shape;
        outputs0 = new float[oShape0[1], oShape0[2]];
        var oShape1 = interpreter.GetOutputTensorInfo(1).shape;
        outputs1 = new float[oShape1[1], oShape1[2], oShape1[3]];

        // Init compute shader resources
        labelTex = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        labelTex.enableRandomWrite = true;
        labelTex.Create();
        labelBuffer = new ComputeBuffer(height * width, sizeof(float) * 21);
        colorTableBuffer = new ComputeBuffer(21, sizeof(float) * 4);

        compute = options.compute;
        int initKernel = compute.FindKernel("Init");
        compute.SetInt("Width", width);
        compute.SetInt("Height", height);
        compute.SetTexture(initKernel, "Result", labelTex);
        compute.Dispatch(initKernel, width, height, 1);

        labelToTexKernel = compute.FindKernel("LabelToTex");

        // Init RGBA color table
        var table = COLOR_TABLE.Select(c => (Color)c).ToArray();
        colorTableBuffer.SetData(table);
    }

    public override void Dispose()
    {
        base.Dispose();

        if (labelTex != null) {
            labelTex.Release();
            Object.Destroy(labelTex);
        }
        if (labelTex2D != null)
            Object.Destroy(labelTex2D);
        labelBuffer?.Release();
        colorTableBuffer?.Release();
    }

    public override void Invoke(Texture inputTex)
    {
        ToTensor(inputTex, inputTensor);

        interpreter.SetInputTensorData(0, inputTensor);
        interpreter.Invoke();
        interpreter.GetOutputTensorData(0, outputs0);
    }

    public async UniTask<RenderTexture> InvokeAsync(Texture inputTex, CancellationToken cancellationToken)
    {
        await ToTensorAsync(inputTex, inputTensor, cancellationToken);
        await UniTask.SwitchToThreadPool();

        interpreter.SetInputTensorData(0, inputTensor);
        interpreter.Invoke();
        interpreter.GetOutputTensorData(0, outputs0);

        await UniTask.SwitchToMainThread(cancellationToken);
        return GetResultTexture();
    }

    public RenderTexture GetResultTexture()
    {
        List<YOLOUtils.Box> boxes = YOLOUtils.Segmentation(outputs0, outputs1, scoreThreshold);
        foreach (var b in boxes) {
            Debug.unityLogger.Log("mytag", b.cls);
            Debug.unityLogger.Log("mytag", b.score);
        }

        // labelBuffer.SetData(outputs0);
        // compute.SetBuffer(labelToTexKernel, "LabelBuffer", labelBuffer);
        // compute.SetBuffer(labelToTexKernel, "ColorTable", colorTableBuffer);
        // compute.SetTexture(labelToTexKernel, "Result", labelTex);

        // compute.Dispatch(labelToTexKernel, 256 / 8, 256 / 8, 1);

        return labelTex;
    }

    private static Color32 ToColor(uint c)
    {
        return Color32Extension.FromHex(c);
    }
}
}
