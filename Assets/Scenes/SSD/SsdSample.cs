using System.Collections;
using System.Threading;
using TensorFlowLite;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class SsdSample : MonoBehaviour
{
    [SerializeField]
    private SSD.Options options = default;

    [SerializeField]
    private AspectRatioFitter frameContainer = null;

    [SerializeField]
    private Text framePrefab = null;

    [SerializeField, Range(0f, 1f)]
    private float scoreThreshold = 0.5f;

    [SerializeField]
    private TextAsset labelMap = null;

    private SSD ssd;
    private Text[] frames;
    private string[] labels;

    private bool working = false;
    private float delay = 0.033f;

    private UniTask<bool> task;
    private CancellationToken cancellationToken;
    private bool runBackground = true;

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Using NNAPI for Android.
        // if (options.accelerator == SSD.Accelerator.NNAPI && !Application.isEditor)
        if (!Application.isEditor)
        {
            options.accelerator = SSD.Accelerator.NNAPI;
            string cacheDir = Application.persistentDataPath;
            string modelToken = "ssd-token";
            var interpreterOptions = new InterpreterOptions();
            var nnapiOptions = NNAPIDelegate.DefaultOptions;
            nnapiOptions.AllowFp16 = true;
            nnapiOptions.CacheDir = cacheDir;
            nnapiOptions.ModelToken = modelToken;
            interpreterOptions.AddDelegate(new NNAPIDelegate(nnapiOptions));
            ssd = new SSD(options, interpreterOptions);
        }
        else
#endif
        {
            options.accelerator = SSD.Accelerator.GPU;
            ssd = new SSD(options);
        }

        frames = new Text[10];
        Transform parent = frameContainer.transform;
        for (int i = 0; i < frames.Length; i++) {
            frames[i] = Instantiate(framePrefab, Vector3.zero, Quaternion.identity, parent);
            frames[i].transform.localPosition = Vector3.zero;
        }

        labels = labelMap.text.Split('\n');

        cancellationToken = this.GetCancellationTokenOnDestroy();
    }

    private void OnDestroy()
    {
        ssd?.Dispose();
    }

    private void Invoke(Texture texture)
    {
        ssd.Invoke(texture);
        SSD.Result[] results = ssd.GetResults();
        Vector2 size = (frameContainer.transform as RectTransform).rect.size;
        for (int i = 0; i < 10; i++)
            SetFrame(frames[i], results[i], size);
    }

    public IEnumerator DoInvoke(Texture texture)
    {
        if (working)
            yield break;
        working = true;

        if (runBackground) {
            if (task.Status.IsCompleted())
                task = InvokeAsync(texture);
        }
        else
            Invoke(texture);

        yield return new WaitForSeconds(delay);
        working = false;
    }

    private async UniTask<bool> InvokeAsync(Texture texture)
    {
        SSD.Result[] results = await ssd.InvokeAsync(texture, cancellationToken);
        Vector2 size = (frameContainer.transform as RectTransform).rect.size;
        for (int i = 0; i < 10; i++)
            SetFrame(frames[i], results[i], size);
        return true;
    }

    private void SetFrame(Text frame, SSD.Result result, Vector2 size)
    {
        if (result.score < scoreThreshold)
        {
            frame.gameObject.SetActive(false);
            return;
        }
        else
        {
            frame.gameObject.SetActive(true);
        }

        Debug.unityLogger.Log("mytag", result.rect.position);

        size = size * 6f;
        frame.text = $"{GetLabelName(result.classID)} : {(int)(result.score * 100)}%";
        var rt = frame.transform as RectTransform;
        rt.anchoredPosition = result.rect.position * size - size * 0.5f;
        rt.sizeDelta = result.rect.size * size;
    }

    private string GetLabelName(int id)
    {
        if (id < 0 || id >= labels.Length - 1)
        {
            return "?";
        }
        return labels[id + 1];
    }

}
