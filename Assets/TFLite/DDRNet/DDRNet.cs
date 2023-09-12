using System.Threading;
using Cysharp.Threading.Tasks;
using System.Linq;
using UnityEngine;

namespace TensorFlowLite
{
public class DDRNet : BaseImagePredictor<float>
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

    public static readonly Color32[] COLOR_TABLE = new Color32[]
    {
        new Color32(0, 0, 0, 255), // background
        new Color32(0, 0, 255, 255), // road
        new Color32(255, 255, 0, 255), // curb
        new Color32(200, 200, 0, 255), // curb cut
        new Color32(0, 255, 0, 255), // sidewalk
        new Color32(255, 0, 255, 255), // plain crosswalk
        new Color32(255, 0, 255, 255), // zebra crosswalk
        new Color32(255, 0, 0, 255), // grating
        new Color32(255, 0, 0, 255), // manhole
        new Color32(128, 96, 0, 255) // rail track
    };

    private float[,,] inputs;
    private float[,,] inputs2;
    private int[,] outputs0;

    private ComputeShader compute;
    private ComputeBuffer labelBuffer;
    private ComputeBuffer colorTableBuffer;
    private RenderTexture labelTex;

    private int labelToTexKernel;

    public DDRNet(Options options) : base(options.modelPath, options.accelerator)
    {
        Construct(options);
    }

    public DDRNet(Options options, InterpreterOptions interpreterOptions) : base(options.modelPath, interpreterOptions)
    {
        Construct(options);
    }

    private void Construct(Options options)
    {
        resizeOptions.aspectMode = options.aspectMode;
        resizeOptions.rotationDegree = 90f;

        var oShape = interpreter.GetOutputTensorInfo(0).shape; // 1, height, width
        int end = oShape.Length - 1;
        resizeOptions.height = oShape[end-1];
        resizeOptions.width = oShape[end];
        inputs2 = new float[3, oShape[end-1], oShape[end]];
        outputs0 = new int[oShape[end-1], oShape[end]];

        // Init compute shader resources
        labelTex = new RenderTexture(resizeOptions.width, resizeOptions.height, 0, RenderTextureFormat.ARGB32);
        labelTex.enableRandomWrite = true;
        labelTex.Create();
        labelBuffer = new ComputeBuffer(oShape[end-2] * oShape[end-1] * oShape[end], sizeof(int));
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

    public async UniTask<(RenderTexture, RenderTexture)> InvokeAsync(Texture inputTex, CancellationToken cancellationToken)
    {
        resizeOptions.rotationDegree = DepthImage.GetRotation();

        // return resizer.Resize(inputTex, resizeOptions);
        RenderTexture resizedTex = resizer.Resize(inputTex, resizeOptions);
        var pixels = RenderTo2D(resizedTex).GetRawTextureData<Color32>();
        int width = resizedTex.width;
        int height = resizedTex.height - 1;
        
        await UniTask.SwitchToThreadPool();

        const float scale = 255f;
        for (int i = 0; i < pixels.Length; i++) {
            int y = height - i / width;
            int x = i % width;
            // inputs2[0, y, x] = (pixels[i].r / scale - 0.4217f) / 0.2646f;
            // inputs2[1, y, x] = (pixels[i].g / scale - 0.4606f) / 0.2754f;
            // inputs2[2, y, x] = (pixels[i].b / scale - 0.4720f) / 0.3035f;
            inputs2[0, y, x] = (pixels[i].r / scale);
            inputs2[1, y, x] = (pixels[i].g / scale);
            inputs2[2, y, x] = (pixels[i].b / scale);
        }

        interpreter.SetInputTensorData(0, inputs2);
        interpreter.Invoke();
        interpreter.GetOutputTensorData(0, outputs0);

        await UniTask.SwitchToMainThread(cancellationToken);
        return (resizedTex, GetResultTexture());
    }

    Texture2D tex2D;
    private Texture2D RenderTo2D(RenderTexture texture)
    {
        if (tex2D == null || tex2D.width != texture.width || tex2D.height != texture.height)
            tex2D = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        var prevRT = RenderTexture.active;
        RenderTexture.active = texture;

        tex2D.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        tex2D.Apply();

        RenderTexture.active = prevRT;

        return tex2D;
    }

    public RenderTexture GetResultTexture()
    {
        labelBuffer.SetData(outputs0);
        compute.SetBuffer(labelToTexKernel, "LabelBuffer", labelBuffer);
        compute.SetBuffer(labelToTexKernel, "ColorTable", colorTableBuffer);
        compute.SetTexture(labelToTexKernel, "Result", labelTex);
        compute.Dispatch(labelToTexKernel, resizeOptions.width / 8, resizeOptions.height / 8, 1);

        return labelTex;
    }
}
}
