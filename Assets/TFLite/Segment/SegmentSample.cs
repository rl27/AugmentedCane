using System.Threading;
using Cysharp.Threading.Tasks;
using TensorFlowLite;
using UnityEngine;
using UnityEngine.UI;

public class SegmentSample : MonoBehaviour
{
    [SerializeField]
    public RawImage outputView = null;

    [SerializeField]
    private Segment.Options options = default;

    private Segment segment;

    private UniTask<bool> task;
    private CancellationToken cancellationToken;
    private bool working = false;

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Application.isEditor)
        {
            options.accelerator = Segment.Accelerator.NNAPI;
            string cacheDir = Application.persistentDataPath;
            string modelToken = "segment-token";
            var interpreterOptions = new InterpreterOptions();
            var nnapiOptions = NNAPIDelegate.DefaultOptions;
            nnapiOptions.AllowFp16 = true;
            nnapiOptions.CacheDir = cacheDir;
            nnapiOptions.ModelToken = modelToken;
            interpreterOptions.AddDelegate(new NNAPIDelegate(nnapiOptions));
            segment = new Segment(options, interpreterOptions);
        }
        else
#endif
        {
            options.accelerator = Segment.Accelerator.GPU;
            segment = new Segment(options);
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
