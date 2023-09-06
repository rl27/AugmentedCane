using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Unity.Barracuda;
using TensorFlowLite;

// https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/GettingStarted.html
public class Vision : MonoBehaviour
{
    [SerializeField]
    public RawImage outputView = null;

    [SerializeField]
    public RawImage inputView = null;

    [SerializeField]
    public GameObject outputViewParent;
    private AspectRatioFitter outputAspectRatioFitter;

    [SerializeField]
    public GameObject inputViewParent;
    private AspectRatioFitter inputAspectRatioFitter;

    public NNModel modelAsset;
    private Model model;
    private IWorker worker;

    TextureResizer resizer;
    TextureResizer.ResizeOptions resizeOptions;

    private bool working = false;

    bool testing = false;
    Texture2D testPNG;

    public ComputeShader compute;
    private ComputeBuffer labelBuffer;
    private ComputeBuffer colorTableBuffer;
    private RenderTexture labelTex;
    private int labelToTexKernel;

    public static readonly Color32[] COLOR_TABLE = new Color32[]
    {
        new Color32(0, 0, 0, 255),
        new Color32(0, 0, 255, 255),
        new Color32(217, 217, 217, 255),
        new Color32(198, 89, 17, 255),
        new Color32(128, 128, 128, 255),
        new Color32(255, 230, 153, 255),
        new Color32(55, 86, 35, 255),
        new Color32(110, 168, 70, 255),
        new Color32(255, 255, 0, 255),
        new Color32(128, 96, 0, 255),
        new Color32(255, 128, 255, 255),
        new Color32(255, 0, 255, 255),
        new Color32(230, 170, 255, 255),
        new Color32(208, 88, 255, 255),
        new Color32(138, 60, 200, 255),
        new Color32(88, 38, 128, 255),
        new Color32(255, 155, 155, 255),
        new Color32(255, 192, 0, 255),
        new Color32(255, 0, 0, 255),
        new Color32(0, 255, 0, 255),
        new Color32(255, 128, 0, 255),
        new Color32(105, 105, 255, 255)
    };

    void Start()
    {
        model = ModelLoader.Load(modelAsset);
        // See worker types here: https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/Worker.html
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);

        outputAspectRatioFitter = outputViewParent.GetComponent<AspectRatioFitter>();
        inputAspectRatioFitter = inputViewParent.GetComponent<AspectRatioFitter>();

        resizer = new TextureResizer();
        resizeOptions = new TextureResizer.ResizeOptions()
        {
            aspectMode = AspectMode.Fill,
            rotationDegree = 90,
            mirrorHorizontal = false,
            mirrorVertical = false,
            width = 480,
            height = 480,
        };

        #if UNITY_EDITOR
            testing = true;
            testPNG = (Texture2D) DDRNetSample.LoadPNG("Assets/TestImages/MP_SEL_SUR_000004.png");
        #endif

        // Init compute shader resources
        labelTex = new RenderTexture(resizeOptions.width, resizeOptions.height, 0, RenderTextureFormat.ARGB32);
        labelTex.enableRandomWrite = true;
        labelTex.Create();
        labelBuffer = new ComputeBuffer(resizeOptions.width * resizeOptions.height, sizeof(float));
        colorTableBuffer = new ComputeBuffer(COLOR_TABLE.Length, sizeof(float) * 4);

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

    void Update()
    {
        if (testing)
            StartCoroutine(Detect(testPNG));
    }

    private Texture2D ResizeTexture(Texture inputTex)
    {
        resizeOptions.rotationDegree = DepthImage.GetRotation();
        RenderTexture resizedTex = resizer.Resize(inputTex, resizeOptions);
        return RenderTo2D(resizedTex);
    }

    Texture2D tex2D;
    private Texture2D RenderTo2D(RenderTexture texture)
    {
        if (tex2D == null || tex2D.width != texture.width || tex2D.height != texture.height)
            tex2D = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        var prevRT = RenderTexture.active;
        RenderTexture.active = texture;

        tex2D.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        tex2D.Apply();

        RenderTexture.active = prevRT;

        return tex2D;
    }

    // https://forum.unity.com/threads/asynchronous-inference-in-barracuda.1370181/
    public IEnumerator Detect(Texture tex)
    {
        if (working)
            yield break;
        working = true;

        // https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/TensorHandling.html
        Texture2D resizedTex = ResizeTexture(tex);
        Tensor input = new Tensor(resizedTex); // BHWC: 1, H, W, 3

        // worker.Execute(input);
        var enumerator = worker.StartManualSchedule(input);
        int step = 0;
        int stepsPerFrame = 60;
        while (enumerator.MoveNext()) {
            if (++step % stepsPerFrame == 0) yield return null;
        }
        Tensor output = worker.PeekOutput(); // BHWC: 1, 1, W, H
        // Debug.Log(output.shape);

        inputView.texture = resizedTex;
        outputView.texture = GetResultTexture(output.ToReadOnlyArray());

        outputView.rectTransform.sizeDelta = new Vector2(resizeOptions.width, resizeOptions.height);
        inputView.rectTransform.sizeDelta = new Vector2(resizeOptions.width, resizeOptions.height);
        outputAspectRatioFitter.aspectMode = DDRNetSample.GetMode();
        inputAspectRatioFitter.aspectMode = DDRNetSample.GetMode();
        outputAspectRatioFitter.aspectRatio = (float) resizeOptions.width / resizeOptions.height;
        inputAspectRatioFitter.aspectRatio = (float) resizeOptions.width / resizeOptions.height;

        input.Dispose();

        working = false;
    }

    private RenderTexture GetResultTexture(float[] data)
    {
        labelBuffer.SetData(data);
        compute.SetBuffer(labelToTexKernel, "LabelBuffer", labelBuffer);
        compute.SetBuffer(labelToTexKernel, "ColorTable", colorTableBuffer);
        compute.SetTexture(labelToTexKernel, "Result", labelTex);
        compute.Dispatch(labelToTexKernel, resizeOptions.width / 8, resizeOptions.height / 8, 1);

        return labelTex;
    }
}