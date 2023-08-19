using System.Threading;
using Cysharp.Threading.Tasks;
using TensorFlowLite;
using UnityEngine;
using UnityEngine.UI;

public class DeepLabPlusSample : MonoBehaviour
{
    [SerializeField]
    public RawImage outputView = null;

    [SerializeField]
    private DeepLabPlus.Options options = default;

    private DeepLabPlus deepLab;

    private UniTask<bool> task;
    private CancellationToken cancellationToken;
    private bool working = false;

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Application.isEditor)
        {
            options.accelerator = DeepLabPlus.Accelerator.NNAPI;
            string cacheDir = Application.persistentDataPath;
            string modelToken = "deeplabplus-token";
            var interpreterOptions = new InterpreterOptions();
            var nnapiOptions = NNAPIDelegate.DefaultOptions;
            nnapiOptions.AllowFp16 = true;
            nnapiOptions.CacheDir = cacheDir;
            nnapiOptions.ModelToken = modelToken;
            interpreterOptions.AddDelegate(new NNAPIDelegate(nnapiOptions));
            deepLab = new DeepLabPlus(options, interpreterOptions);
        }
        else
#endif
        {
            options.accelerator = DeepLabPlus.Accelerator.GPU;
            deepLab = new DeepLabPlus(options);
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
