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
    public GameObject outputViewParent;
    private AspectRatioFitter aspectRatioFitter;

    [SerializeField]
    private DDRNet.Options options = default;

    private DDRNet model;

    private UniTask<bool> task;
    private CancellationToken cancellationToken;
    private bool working = false;

    bool testing = true;
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

        aspectRatioFitter = outputViewParent.GetComponent<AspectRatioFitter>();

        if (testing)
            testPNG = LoadPNG("Assets/demo2.png");
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

    private static Texture LoadPNG(string filePath) {

        Texture2D tex = null;
        byte[] fileData;

        if (File.Exists(filePath)) {
            Debug.Log("loaded");
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
        var tex = await model.InvokeAsync(texture, cancellationToken);
        outputView.texture = tex;
        aspectRatioFitter.aspectRatio = (float) tex.width / tex.height;
        Debug.Log("success");
        return true;
    }
}
