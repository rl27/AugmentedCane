using System.Threading;
using Cysharp.Threading.Tasks;
using TensorFlowLite;
using UnityEngine;
using UnityEngine.UI;

public class DeepLabSample : MonoBehaviour
{
    [SerializeField]
    private RawImage outputView = null;

    [SerializeField]
    private DeepLab.Options options = default;

    private DeepLab deepLab;

    private UniTask<bool> task;
    private CancellationToken cancellationToken;
    private bool working = false;

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Application.isEditor)
        {
            options.accelerator = DeepLab.Accelerator.NNAPI;
            string cacheDir = Application.persistentDataPath;
            string modelToken = "deeplab-token";
            var interpreterOptions = new InterpreterOptions();
            var nnapiOptions = NNAPIDelegate.DefaultOptions;
            nnapiOptions.AllowFp16 = true;
            nnapiOptions.CacheDir = cacheDir;
            nnapiOptions.ModelToken = modelToken;
            interpreterOptions.AddDelegate(new NNAPIDelegate(nnapiOptions));
            deepLab = new DeepLab(options, interpreterOptions);
        }
        else
#endif
        {
            options.accelerator = DeepLab.Accelerator.GPU;
            deepLab = new DeepLab(options);
        }

        cancellationToken = this.GetCancellationTokenOnDestroy();
    }

    private void OnDestroy()
    {
        deepLab?.Dispose();
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
        RenderTexture tex = await deepLab.InvokeAsync(texture, cancellationToken);
        outputView.texture = tex;
        return true;
    }
}
