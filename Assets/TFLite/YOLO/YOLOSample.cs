using System;
using System.Collections.Generic;
using System.Threading;
using TensorFlowLite;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class YOLOSample : MonoBehaviour
{
    [SerializeField]
    private YOLO.Options options = default;

    [SerializeField]
    public AspectRatioFitter frameContainer = null;

    [SerializeField]
    private Text framePrefab = null;

    // [SerializeField, Range(0f, 1f)]
    // private float scoreThreshold = 0.5f;

    [SerializeField]
    private TextAsset labelMap = null;

    private YOLO yolo;
    private Text[] frames;
    private string[] labels;

    private bool working = false;

    private UniTask<bool> task;
    private CancellationToken cancellationToken;
    private bool runBackground = true;

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Using NNAPI for Android.
        if (!Application.isEditor)
        {
            options.accelerator = YOLO.Accelerator.NNAPI;
            string cacheDir = Application.persistentDataPath;
            string modelToken = "yolo-token";
            var interpreterOptions = new InterpreterOptions();
            var nnapiOptions = NNAPIDelegate.DefaultOptions;
            nnapiOptions.AllowFp16 = true;
            nnapiOptions.CacheDir = cacheDir;
            nnapiOptions.ModelToken = modelToken;
            interpreterOptions.AddDelegate(new NNAPIDelegate(nnapiOptions));
            yolo = new YOLO(options, interpreterOptions);
        }
        else
#endif
        {
            options.accelerator = YOLO.Accelerator.GPU;
            yolo = new YOLO(options);
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
        yolo?.Dispose();
    }

    public void DoInvoke(Texture texture)
    {
        if (working)
            return;
        working = true;

        if (runBackground) {
            if (task.Status.IsCompleted())
                task = InvokeAsync(texture);
        }
        else
            Invoke(texture);

        working = false;
    }

    private void Invoke(Texture texture)
    {
        yolo.Invoke(texture);
        List<YOLO.Result> results = yolo.GetResults();
        Vector2 size = (frameContainer.transform as RectTransform).rect.size;
        for (int i = 0; i < Math.Min(results.Count, 10); i++)
            SetFrame(frames[i], results[i], size);
    }

    private async UniTask<bool> InvokeAsync(Texture texture)
    {
        List<YOLO.Result> results = await yolo.InvokeAsync(texture, cancellationToken);
        Vector2 size = (frameContainer.transform as RectTransform).rect.size;
        for (int i = 0; i < Math.Min(results.Count, 10); i++)
            SetFrame(frames[i], results[i], size);
        for (int i = results.Count; i < 10; i++)
            frames[i].gameObject.SetActive(false);
        return true;
    }

    private void SetFrame(Text frame, YOLO.Result result, Vector2 size)
    {
        frame.gameObject.SetActive(true);

        size = size * 6.4f;
        frame.text = $"{GetLabelName(result.classID)} : {(int)(result.score * 100)}%";
        var rt = frame.transform as RectTransform;
        rt.anchoredPosition = result.rect.position * size - size * 0.5f;
        rt.sizeDelta = result.rect.size * size;
    }

    private string GetLabelName(int id)
    {
        if (id < 0 || id >= labels.Length - 1)
            return "?";
        return labels[id];
    }

}
