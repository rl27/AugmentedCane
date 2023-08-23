using System.Threading;
using Cysharp.Threading.Tasks;
using System.Linq;
using UnityEngine;

namespace TensorFlowLite
{
public class DeepLabPlus : BaseImagePredictor<float>
{
    [System.Serializable]
    public class Options
    {
        [FilePopup("*.tflite")]
        public string modelPath = string.Empty;
        public AspectMode aspectMode = AspectMode.Fit;
        public Accelerator accelerator = Accelerator.GPU;
        public ComputeShader compute = null;
    }

    private static readonly Color32[] COLOR_TABLE = new Color32[]
    {
        new Color32(165, 42, 42, 255),
        new Color32(0, 192, 0, 255),
        new Color32(196, 196, 196, 255),
        new Color32(190, 153, 153, 255),
        new Color32(180, 165, 180, 255),
        new Color32(90, 120, 150, 255),
        new Color32(102, 102, 156, 255),
        new Color32(128, 64, 255, 255),
        new Color32(140, 140, 200, 255),
        new Color32(170, 170, 170, 255),
        new Color32(250, 170, 160, 255),
        new Color32(96, 96, 96, 255),
        new Color32(230, 150, 140, 255),
        new Color32(128, 64, 128, 255),
        new Color32(110, 110, 110, 255),
        new Color32(244, 35, 232, 255),
        new Color32(150, 100, 100, 255),
        new Color32(70, 70, 70, 255),
        new Color32(150, 120, 90, 255),
        new Color32(220, 20, 60, 255),
        new Color32(255, 0, 0, 255),
        new Color32(255, 0, 100, 255),
        new Color32(255, 0, 200, 255),
        new Color32(200, 128, 128, 255),
        new Color32(255, 255, 255, 255),
        new Color32(64, 170, 64, 255),
        new Color32(230, 160, 50, 255),
        new Color32(70, 130, 180, 255),
        new Color32(190, 255, 255, 255),
        new Color32(152, 251, 152, 255),
        new Color32(107, 142, 35, 255),
        new Color32(0, 170, 30, 255),
        new Color32(255, 255, 128, 255),
        new Color32(250, 0, 30, 255),
        new Color32(100, 140, 180, 255),
        new Color32(220, 220, 220, 255),
        new Color32(220, 128, 128, 255),
        new Color32(222, 40, 40, 255),
        new Color32(100, 170, 30, 255),
        new Color32(40, 40, 40, 255),
        new Color32(33, 33, 33, 255),
        new Color32(100, 128, 160, 255),
        new Color32(142, 0, 0, 255),
        new Color32(70, 100, 150, 255),
        new Color32(210, 170, 100, 255),
        new Color32(153, 153, 153, 255),
        new Color32(128, 128, 128, 255),
        new Color32(0, 0, 80, 255),
        new Color32(250, 170, 30, 255),
        new Color32(192, 192, 192, 255),
        new Color32(220, 220, 0, 255),
        new Color32(140, 140, 20, 255),
        new Color32(119, 11, 32, 255),
        new Color32(150, 0, 255, 255),
        new Color32(0, 60, 100, 255),
        new Color32(0, 0, 142, 255),
        new Color32(0, 0, 90, 255),
        new Color32(0, 0, 230, 255),
        new Color32(0, 80, 100, 255),
        new Color32(128, 64, 64, 255),
        new Color32(0, 0, 110, 255),
        new Color32(0, 0, 70, 255),
        new Color32(0, 0, 192, 255),
        new Color32(32, 32, 32, 255),
        new Color32(120, 10, 10, 255),
        new Color32(0, 0, 0, 255)
    };

    private float[,,] inputs;
    private long[,] outputs0;

    private ComputeShader compute;
    private ComputeBuffer labelBuffer;
    private ComputeBuffer colorTableBuffer;
    private RenderTexture labelTex;

    private int labelToTexKernel;

    public DeepLabPlus(Options options) : base(options.modelPath, options.accelerator)
    {
        Construct(options);
    }

    public DeepLabPlus(Options options, InterpreterOptions interpreterOptions) : base(options.modelPath, interpreterOptions)
    {
        Construct(options);
    }

    private void Construct(Options options)
    {
        resizeOptions.aspectMode = options.aspectMode;
        resizeOptions.rotationDegree = 90f;

        var oShape0 = interpreter.GetOutputTensorInfo(0).shape; // 1, 1, height, width
        resizeOptions.height = oShape0[2];
        resizeOptions.width = oShape0[3];
        inputs = new float[oShape0[2], oShape0[3], 3]; // 3, height, width
        outputs0 = new long[oShape0[2], oShape0[3]];

        // Init compute shader resources
        labelTex = new RenderTexture(resizeOptions.width, resizeOptions.height, 0, RenderTextureFormat.ARGB32);
        labelTex.enableRandomWrite = true;
        labelTex.Create();
        labelBuffer = new ComputeBuffer(resizeOptions.height * resizeOptions.width, sizeof(long));
        colorTableBuffer = new ComputeBuffer(COLOR_TABLE.Length, sizeof(float) * 4);

        compute = options.compute;
        int initKernel = compute.FindKernel("Init");
        compute.SetInt("Width", resizeOptions.width);
        compute.SetInt("Height", resizeOptions.height);
        compute.SetTexture(initKernel, "Result", labelTex);
        compute.Dispatch(initKernel, resizeOptions.width, resizeOptions.height, 1);

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
        labelBuffer?.Release();
        colorTableBuffer?.Release();
    }

    public override void Invoke(Texture inputTex)
    {
        ToTensor(inputTex, inputs);

        interpreter.SetInputTensorData(0, inputs);
        interpreter.Invoke();
        interpreter.GetOutputTensorData(0, outputs0);
    }

    public async UniTask<RenderTexture> InvokeAsync(Texture inputTex, CancellationToken cancellationToken)
    {
        await ToTensorAsync(inputTex, inputs, cancellationToken);
        await UniTask.SwitchToThreadPool();

        interpreter.SetInputTensorData(0, inputs);
        interpreter.Invoke();
        interpreter.GetOutputTensorData(0, outputs0);

        await UniTask.SwitchToMainThread(cancellationToken);
        return GetResultTexture();
    }

    public RenderTexture GetResultTexture()
    {
        labelBuffer.SetData(outputs0);
        compute.SetBuffer(labelToTexKernel, "LabelBuffer", labelBuffer);
        compute.SetBuffer(labelToTexKernel, "ColorTable", colorTableBuffer);
        compute.SetTexture(labelToTexKernel, "Result", labelTex);
        Debug.unityLogger.Log("mytag", 1);
        compute.Dispatch(labelToTexKernel, resizeOptions.width / 8, resizeOptions.height / 8, 1);
        Debug.unityLogger.Log("mytag", 2);

        return labelTex;
    }

    private static Color32 ToColor(uint c)
    {
        return Color32Extension.FromHex(c);
    }
}
}
