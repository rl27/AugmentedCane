using System.Threading;
using Cysharp.Threading.Tasks;
using TensorFlowLite;
using UnityEngine;
using UnityEngine.UI;

public class YOLOSegSample : MonoBehaviour
{
    [SerializeField]
    public RawImage outputView = null;

    [SerializeField]
    private YOLOSeg.Options options = default;

    private YOLOSeg segment;

    private UniTask<bool> task;
    private CancellationToken cancellationToken;
    private bool working = false;

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Application.isEditor)
        {
            options.accelerator = YOLOSeg.Accelerator.NNAPI;
            string cacheDir = Application.persistentDataPath;
            string modelToken = "YOLOSeg-token";
            var interpreterOptions = new InterpreterOptions();
            var nnapiOptions = NNAPIDelegate.DefaultOptions;
            nnapiOptions.AllowFp16 = true;
            nnapiOptions.CacheDir = cacheDir;
            nnapiOptions.ModelToken = modelToken;
            interpreterOptions.AddDelegate(new NNAPIDelegate(nnapiOptions));
            segment = new YOLOSeg(options, interpreterOptions);
        }
        else
#endif
        {
            options.accelerator = YOLOSeg.Accelerator.GPU;
            segment = new YOLOSeg(options);
        }

        cancellationToken = this.GetCancellationTokenOnDestroy();
    }

    private void OnDestroy()
    {
        segment?.Dispose();
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

    private async UniTask<bool> InvokeAsync(Texture texture)
    {
        RenderTexture tex = await segment.InvokeAsync(texture, cancellationToken);
        outputView.texture = tex;
        return true;
    }
}
