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
        ToColor(0xFF00_0000), // Black
        ToColor(0xFF80_3E75), // Strong Purple
    };

    private int[,] outputs0; // 256, 256

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

        var oShape0 = interpreter.GetOutputTensorInfo(0).shape;

        Debug.Assert(oShape0[1] == height);
        Debug.Assert(oShape0[2] == width);

        outputs0 = new int[oShape0[1], oShape0[2]];

        // Init compute shader resources
        labelTex = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        labelTex.enableRandomWrite = true;
        labelTex.Create();
        labelBuffer = new ComputeBuffer(height * width, sizeof(float) * 2);
        colorTableBuffer = new ComputeBuffer(2, sizeof(float) * 4);

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
        labelBuffer.SetData(outputs0);
        compute.SetBuffer(labelToTexKernel, "LabelBuffer", labelBuffer);
        compute.SetBuffer(labelToTexKernel, "ColorTable", colorTableBuffer);
        compute.SetTexture(labelToTexKernel, "Result", labelTex);

        compute.Dispatch(labelToTexKernel, 256 / 8, 256 / 8, 1);

        return labelTex;
    }

    private static Color32 ToColor(uint c)
    {
        return Color32Extension.FromHex(c);
    }
}
}
