using System.Threading;
using Cysharp.Threading.Tasks;
using TensorFlowLite;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

using Unity.Barracuda;

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
            testPNG = LoadPNG("Assets/test4.png");

        // IOps ops = new BurstCPUOps();
        // float[] data = new float[]{1,2,3,4, 5,6,7,8, 9,10,11,12};
        // Tensor t = new Tensor(1, 2, 2, 3, data);
        // Debug.Log(t[0,0,0,0]);
        // Debug.Log(t[0,0,0,1]);
        // Debug.Log(t[0,0,0,2]);

        // Debug.Log(t[0,0,1,0]);
        // Debug.Log(t[0,0,1,1]);
        // Debug.Log(t[0,0,1,2]);

        // Debug.Log(t[0,1,0,0]);
        // Debug.Log(t[0,1,0,1]);
        // Debug.Log(t[0,1,0,2]);

        // Debug.Log(t[0,1,1,0]);
        // Debug.Log(t[0,1,1,1]);
        // Debug.Log(t[0,1,1,2]);

        // // Tensor t2 = ops.Resample2D(t, new int[]{480, 480}, true);
        // Tensor mean = new Tensor(1, 3, new float[]{0.485f, 0.456f, 0.406f});
        // Tensor std = new Tensor(1, 3, new float[]{0.229f, 0.224f, 0.225f});
        // Tensor processed = ops.Sub(new Tensor[]{t, mean});
        // Tensor t2 = ops.Div(new Tensor[]{processed, std});

        // Debug.Log(t2[0,0,0,0]);
        // Debug.Log(t2[0,0,0,1]);
        // Debug.Log(t2[0,0,0,2]);

        // Debug.Log(t2[0,0,1,0]);
        // Debug.Log(t2[0,0,1,1]);
        // Debug.Log(t2[0,0,1,2]);

        // Debug.Log(t2[0,1,0,0]);
        // Debug.Log(t2[0,1,0,1]);
        // Debug.Log(t2[0,1,0,2]);

        // Debug.Log(t2[0,1,1,0]);
        // Debug.Log(t2[0,1,1,1]);
        // Debug.Log(t2[0,1,1,2]);
        
        // t.Dispose();
        // // t2.Dispose();
        // mean.Dispose();
        // std.Dispose();
        // processed.Dispose();
        // t2.Dispose();

        // Tensor t3 = new Tensor(testPNG);
        // PrintShape(t3);
        // Tensor t4 = ops.Transpose(t3, new int[]{0, 3, 2, 1});
        // PrintShape(t4);
        // t3.Dispose();
        // t4.Dispose();
    }

    private void PrintShape(Tensor t){
        Debug.Log(t.batch);
        Debug.Log(t.height);
        Debug.Log(t.width);
        Debug.Log(t.channels);
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
        // Debug.Log("success");
        return true;
    }
}
