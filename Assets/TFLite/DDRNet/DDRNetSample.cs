using System.Threading;
using Cysharp.Threading.Tasks;
using TensorFlowLite;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

public class DDRNetSample : MonoBehaviour
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

    [SerializeField]
    private DDRNet.Options options = default;

    private DDRNet model;

    private UniTask<bool> task;
    private CancellationToken cancellationToken;
    private bool working = false;

    bool testing = false;
    Texture testPNG;

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Application.isEditor)
        {
            options.accelerator = DDRNet.Accelerator.NNAPI;
            string cacheDir = Application.persistentDataPath;
            string modelToken = "model-token";
            var interpreterOptions = new InterpreterOptions();
            var nnapiOptions = NNAPIDelegate.DefaultOptions;
            nnapiOptions.AllowFp16 = true;
            nnapiOptions.CacheDir = cacheDir;
            nnapiOptions.ModelToken = modelToken;
            interpreterOptions.AddDelegate(new NNAPIDelegate(nnapiOptions));
            model = new DDRNet(options, interpreterOptions);
        }
        else
#endif
        {
            options.accelerator = DDRNet.Accelerator.GPU;
            model = new DDRNet(options);
        }

        cancellationToken = this.GetCancellationTokenOnDestroy();

        outputAspectRatioFitter = outputViewParent.GetComponent<AspectRatioFitter>();
        inputAspectRatioFitter = inputViewParent.GetComponent<AspectRatioFitter>();

        #if UNITY_EDITOR
            testing = true;
            testPNG = LoadPNG("Assets/TestImages/test2.png");
        #endif
    }

    private void OnDestroy()
    {
        if (task.Status.IsCompleted())
            model?.Dispose();
    }

    public void DoInvoke(Texture texture)
    {
        if (working)
            return;
        working = true;

        if (task.Status.IsCompleted())
            task = InvokeAsync(texture);

        working = false;
    }

    public static Texture LoadPNG(string filePath) {

        Texture2D tex = null;
        byte[] fileData;

        if (File.Exists(filePath)) {
            Debug.Log("file loaded");
            fileData = File.ReadAllBytes(filePath);
            tex = new Texture2D(2, 2);
            tex.LoadImage(fileData);
        }
        return (Texture) tex;
    }

    void Update()
    {
        if (testing)
            DoInvoke(testPNG);
    }

    private async UniTask<bool> InvokeAsync(Texture texture)
    {
        RenderTexture input;
        RenderTexture output;
        (input, output) = await model.InvokeAsync(texture, cancellationToken);
        inputView.texture = input;
        outputView.texture = output;

        outputView.rectTransform.sizeDelta = new Vector2(480, 480);
        inputView.rectTransform.sizeDelta = new Vector2(480, 480);
        outputAspectRatioFitter.aspectMode = GetMode();
        inputAspectRatioFitter.aspectMode = GetMode();
        outputAspectRatioFitter.aspectRatio = (float) output.width / output.height;
        inputAspectRatioFitter.aspectRatio = (float) input.width / input.height;

        // Debug.Log("success");
        return true;
    }

    public static AspectRatioFitter.AspectMode GetMode() => Screen.orientation switch
    {
        ScreenOrientation.Portrait => AspectRatioFitter.AspectMode.WidthControlsHeight,
        ScreenOrientation.LandscapeLeft => AspectRatioFitter.AspectMode.HeightControlsWidth,
        ScreenOrientation.PortraitUpsideDown => AspectRatioFitter.AspectMode.WidthControlsHeight,
        ScreenOrientation.LandscapeRight => AspectRatioFitter.AspectMode.HeightControlsWidth,
        _ => AspectRatioFitter.AspectMode.WidthControlsHeight
    };
}
