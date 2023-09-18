using System;
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
    GameObject TTSHandler;
    TTS tts;

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

    public static bool working = false;

    bool testing = false;
    Texture2D testPNG;

    public ComputeShader compute;
    private ComputeBuffer labelBuffer;
    private ComputeBuffer colorTableBuffer;
    private RenderTexture labelTex;
    private int labelToTexKernel;

    private int W = 480;
    private int H = 480;

    public AudioClip sidewalk;
    public AudioClip crosswalk;
    public AudioClip road;

    public static readonly Color32[] COLOR_TABLE = new Color32[]
    {
        new Color32(0, 0, 0, 255), // background
        new Color32(0, 0, 255, 255), // road
        new Color32(255, 255, 0, 255), // curb
        new Color32(200, 200, 0, 255), // curb cut
        new Color32(0, 255, 0, 255), // sidewalk
        new Color32(255, 0, 255, 255), // plain crosswalk
        new Color32(255, 0, 255, 255), // zebra crosswalk
        new Color32(255, 0, 0, 255), // grating
        new Color32(255, 0, 0, 255), // manhole
        new Color32(128, 96, 0, 255) // rail track
    };

    void Start()
    {
        #if UNITY_EDITOR
            testing = !DepthImage.tflite;
            testPNG = (Texture2D) DDRNetSample.LoadPNG("Assets/TestImages/test4.png");
        #endif

        tts = TTSHandler.GetComponent<TTS>();

        model = ModelLoader.Load(modelAsset);
        // See worker types here: https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/Worker.html
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);

        resizer = new TextureResizer();
        resizeOptions = new TextureResizer.ResizeOptions()
        {
            aspectMode = AspectMode.Fill,
            rotationDegree = 90,
            mirrorHorizontal = false,
            mirrorVertical = false,
            width = W,
            height = H,
        };

        toggle.onValueChanged.AddListener(delegate {ToggleSidewalkDirection();});

        outputAspectRatioFitter = outputViewParent.GetComponent<AspectRatioFitter>();
        inputAspectRatioFitter = inputViewParent.GetComponent<AspectRatioFitter>();
        outputAspectRatioFitter.aspectRatio = (float) resizeOptions.width / resizeOptions.height;
        inputAspectRatioFitter.aspectRatio = (float) resizeOptions.width / resizeOptions.height;
        outputView.enabled = false;
        inputView.enabled = false;

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

    Tensor input;
    // https://forum.unity.com/threads/asynchronous-inference-in-barracuda.1370181/
    public IEnumerator Detect(Texture tex)
    {
        // if (working)
        //     yield break;
        working = true;

        // https://docs.unity3d.com/Packages/com.unity.barracuda@3.0/manual/TensorHandling.html
        Texture2D resizedTex = ResizeTexture(tex);
        input = new Tensor(resizedTex); // BHWC: 1, H, W, 3

        // worker.Execute(input);
        var enumerator = worker.StartManualSchedule(input);
        int step = 0;
        int stepsPerFrame = 53; // FPS should be capped at 30; total num of steps for MNV3 is 221
        while (enumerator.MoveNext()) {
            if (++step % stepsPerFrame == 0) yield return null;
        }

        // (0, 0, 0  , 0  ) = top left
        // (0, 0, 0  , H-1) = bottom left
        // (0, 0, W-1, 0  ) = top right
        // (0, 0, W-1, H-1) = bottom right
        Tensor output = worker.PeekOutput(); // Debug.Log(output.shape); // BHWC: 1, 1, W, H

        ProcessOutput(output);

        SetTextures(resizedTex, GetResultTexture(output.ToReadOnlyArray()));
        
        input.Dispose();
        // output.Dispose();

        working = false;
    }

    public static DateTime lastValidDirection; // To be used by other scripts to determine whether to use vision direction
    public static float validDuration = 1.5f;
    public static float maxDisparity = 75; // Max degree difference from GPS waypoint direction

    public static float relativeDir; // Relative direction
    public static float direction; // Absolute direction based on current heading & segmentation outputs
    private const float scale = 0.65f; // Scale the direction down since the camera can't actually see from -90 to +90
    
    // Moving avg of relative direction
    private float[] weights = new float[]{0.5f, 0.25f, 0.15f, 0.1f};
    private const int numValues = 4;
    private float[] values = new float[numValues];
    private int avgIndex = 0;

    private DateTime lastWalkableTime; // Last time at which user was on a walkable surface
    private float nonWalkableTime = 0.8f; // Time to wait before deciding that user is not on walkable surface

    private static int numRaycasts = 31;
    private float radWidth = Mathf.PI / (numRaycasts - 1);

    private void ProcessOutput(Tensor output)
    {
        if (!doSidewalkDirection) return;

        int curCls = (int) output[0, 0, W/2, H-1];

        if (curCls >= 4 && curCls <= 6) { // Sidewalk, crosswalk
            lastWalkableTime = DateTime.Now;

            // Raycast to find highest walkable point
            float x = -1, y = -1;
            float bestDirection = 0;
            for (int i = 0; i < numRaycasts; i++) {
                (float a, float b) = PerformRaycast(W/2, H-1, ref output, i * radWidth, true);
                if (b > y) {
                    (x, y) = (a, b);
                    bestDirection = i * radWidth * Mathf.Rad2Deg - 90;
                }
            }
            // Set orientation
            if (x != -1) {
                // float _velocity = 0;
                // relativeDir = Mathf.SmoothDampAngle(relativeDir, bestDirection * scale, ref _velocity, 0.06f);

                values[avgIndex] = bestDirection * scale;
                relativeDir = 0;
                for (int i = 0; i < numValues; i++)
                    relativeDir += values[(avgIndex - i + numValues) % numValues] * weights[i];
                avgIndex = (avgIndex + 1) % numValues;

                direction = (relativeDir + SensorData.heading + 360) % 360;
                lastValidDirection = DateTime.Now;
            }

            PlayAudio(curCls);
        }
        else if (curCls != 7 && curCls != 8) { // Ignore grating & manhole as these can be on either sidewalk, crosswalk, or road
            if ((DateTime.Now - lastWalkableTime).TotalSeconds > nonWalkableTime) {
                // No longer performing raycasts to find closest sidewalk as the results don't seem useful

                PlayAudio(curCls);
            }
        }
    }

    public Toggle toggle;
    public static bool doSidewalkDirection = false;
    public void ToggleSidewalkDirection()
    {
        doSidewalkDirection = toggle.isOn;
        lastValidDirection = DateTime.MinValue;
        relativeDir = 0;
        direction = 0;
        lastClass = -1;
        logging = "None";
        outputView.enabled = doSidewalkDirection;
        inputView.enabled = doSidewalkDirection;
    }

    // Returns coordinates of raycast relative to middle of bottom of image
    // Grating & manhole are "wildcards" and can count as either road or sidewalk/crosswalk
    // Returns (0, -1) if no suitable point is found
    private (float, float) PerformRaycast(float x, float y, ref Tensor output, float radFromLeft, bool onWalkable)
    {
        bool valid = onWalkable;
        float dx = -Mathf.Cos(radFromLeft);
        float dy = Mathf.Sin(radFromLeft);
        while (x >= 0 && y >= 0 && x < W && y < H) {
            int cls = (int) output[0, 0, (int) x, (int) y];
            if (onWalkable && (cls < 4 || cls > 8)) // Cast from walkable, reached non-walkable
                break;
            else if (!onWalkable && cls >= 4 && cls <= 6) { // Cast from non-walkable, reached walkable
                valid = true;
                break;
            }
            x += dx;
            y -= dy;
        }
        if (valid)
            return (x-W/2, H-1-y);
        return (0, -1); // This only occurs if we start from non-walkable and no raycasts hit a walkable
    }

    private int lastClass = 0;
    private DateTime lastClassChange; // Time at which user was last informed of a class change
    private float classChangeInterval = 3.0f; // Time to wait after playing audio before doing it again; should be longer than any of the audio files

    // Conditionally plays audio to inform user of class change
    // Only changes lastClass if it successfully plays audio
    public static string logging = "None";
    private void PlayAudio(int cls)
    {
        if (lastClass != cls && (DateTime.Now - lastClassChange).TotalSeconds > classChangeInterval) {
            switch (cls)
            {
                case 1: // Road
                    lastClass = cls;
                    lastClassChange = DateTime.Now;
                    tts.EnqueueTTS(road);
                    logging = "Road";
                    break;
                case 4: // Sidewalk
                    lastClass = cls;
                    lastClassChange = DateTime.Now;
                    tts.EnqueueTTS(sidewalk);
                    logging = "Sidewalk";
                    break;
                case 5: // Crosswalk
                case 6:
                    lastClass = cls;
                    lastClassChange = DateTime.Now;
                    tts.EnqueueTTS(crosswalk);
                    logging = "Crosswalk";
                    break;
                case 0: // Background
                    lastClass = cls;
                    logging = "Unknown";
                    break;
                default:
                    break;
            }
        }
    }

    private void SetTextures(Texture2D resizedTex, RenderTexture outputTex)
    {
        inputView.texture = resizedTex;
        outputView.texture = outputTex;

        outputAspectRatioFitter.aspectMode = DDRNetSample.GetMode();
        inputAspectRatioFitter.aspectMode = DDRNetSample.GetMode();
        outputView.rectTransform.sizeDelta = new Vector2(resizeOptions.width, resizeOptions.height);
        inputView.rectTransform.sizeDelta = new Vector2(resizeOptions.width, resizeOptions.height);
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

    void OnDisable()
    {
        input.Dispose();
        worker?.Dispose();
        resizer?.Dispose();

        if (labelTex != null) {
            labelTex.Release();
            UnityEngine.Object.Destroy(labelTex);
        }
        labelBuffer?.Release();
        colorTableBuffer?.Release();
    }
}