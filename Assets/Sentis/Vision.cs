using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Unity.Sentis;

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
    public GameObject arrow;

    [SerializeField]
    public GameObject outputViewParent;
    private AspectRatioFitter outputAspectRatioFitter;

    [SerializeField]
    public GameObject inputViewParent;
    private AspectRatioFitter inputAspectRatioFitter;

    public ModelAsset modelAsset;
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

    public static int W = 480;
    public static int H = 480;

    public AudioClip sidewalk;
    public AudioClip crosswalk;
    public AudioClip road;

    private Unity.Sentis.BackendType backendType;

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
            testing = true;
            testPNG = Utils.LoadPNG("Assets/TestImages/test3.png");
        #endif

        tts = TTSHandler.GetComponent<TTS>();

        model = ModelLoader.Load(modelAsset);
        backendType = SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.GPUPixel;
        worker = WorkerFactory.CreateWorker(backendType, model);

        // Do this to deal with initial lag
        // The initial lag only happens once; if the app is closed and re-opened, it doesn't happen
        // Texture2D temp = Texture2D.blackTexture;
        // input = TextureConverter.ToTensor(temp);

        resizer = new TextureResizer();
        resizeOptions = new TextureResizer.ResizeOptions()
        {
            aspectMode = TextureResizer.AspectMode.Fill,
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
        labelBuffer = new ComputeBuffer(resizeOptions.width * resizeOptions.height, sizeof(int));
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

    TensorFloat input;
    const int maxStepsPerFrame = 100;
    const float maxTimePerFrame = 0.095f; // Aim for 10 FPS
    public IEnumerator Detect(Texture tex)
    {
        if (working)
            yield break;
        working = true;

        Texture2D resizedTex = ResizeTexture(tex);
        input = TextureConverter.ToTensor(resizedTex); // input.shape: 1, 3, 480, 480

        // worker.Execute(input);
        var enumerator = worker.StartManualSchedule(input); // Total num of steps for PP-MobileSeg-tiny is 378
        int step = 0;
        int lastFrame = -1;
        float start = 0;
        float maxTime = 0;
        float lastStep = 0;
        float lastTime = 0;
        while (enumerator.MoveNext()) {
            if (lastFrame != Time.frameCount) {
                lastFrame = Time.frameCount;
                start = Time.realtimeSinceStartup;
                lastStep = step;
                maxTime = maxTimePerFrame - Time.smoothDeltaTime + lastTime;
            }
            if ((++step - lastStep) % maxStepsPerFrame == 0 || (Time.realtimeSinceStartup - start) > maxTime) {
                if (step != 377 && step != 378) { // Bandaid fix for iPhone bug
                    lastTime = Time.realtimeSinceStartup - start;
                    yield return null;
                }
            }
        }

        yield return null; // Reading the output is expensive, wait for next frame

        // [0, 0  , 0  ] = top left
        // [0, H-1, 0  ] = bottom left
        // [0, 0,   W-1] = top right
        // [0, H-1, W-1] = bottom right
        TensorInt output = worker.PeekOutput() as TensorInt; // output.shape: 1, 480, 480
        output.MakeReadable();
        ProcessOutput(output);
        SetTextures(resizedTex, GetResultTexture(output.ToReadOnlyArray()));

        // Use this code if GetResultTexture is not working
        /*Texture2D output2D = new Texture2D(W, H, TextureFormat.RGB24, false);
        for (int i = 0; i < W; i++) {
            for (int j = 0; j < H; j++) {
                output2D.SetPixel(i, H-j, COLOR_TABLE[(int)output[0, 0, i, j]]);
            }
        }
        output2D.Apply();
        SetTextures(resizedTex, output2D);*/
        
        input.Dispose();

        working = false;
    }

    public static DateTime lastValidDirection; // To be used by other scripts to determine whether to use vision direction
    public static float validDuration = 1.5f;
    public static float maxDisparity = 60; // Max degree difference from GPS waypoint direction

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

    private void ProcessOutput(TensorInt output)
    {
        if (!doSidewalkDirection) return;

        int curCls = CheckForWalkable(ref output);
        if (StrictWalkable(curCls)) { // Sidewalk or crosswalk close to middle of bottom of output image
            lastWalkableTime = DateTime.Now;

            // Raycast to find highest walkable point
            float x = 0, y = 0;
            float bestDirection = 0;
            for (int i = 0; i < numRaycasts; i++) {
                (float a, float b) = PerformRaycast(W/2, H-1, ref output, i * radWidth);
                if (b > y) {
                    (x, y) = (a, b);
                    bestDirection = i * radWidth * Mathf.Rad2Deg - 90;
                }
            }
            // Set orientation
            values[avgIndex] = bestDirection * scale;
            relativeDir = 0;
            for (int i = 0; i < numValues; i++)
                relativeDir += values[(avgIndex - i + numValues) % numValues] * weights[i];
            avgIndex = (avgIndex + 1) % numValues;

            direction = (relativeDir + SensorData.heading + 360) % 360;
            lastValidDirection = DateTime.Now;

            PlayAudio(curCls);
            arrow.transform.eulerAngles = new Vector3(0,0,-relativeDir/scale);
            arrow.SetActive(true);
        }
        else if (curCls != 7 && curCls != 8) { // Ignore grating & manhole as these can be on either sidewalk, crosswalk, or road
            if ((DateTime.Now - lastWalkableTime).TotalSeconds > nonWalkableTime) {
                // No longer performing raycasts to find closest sidewalk as the results don't seem useful

                PlayAudio(curCls);
                arrow.SetActive(false);
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
        if (!doSidewalkDirection)
            arrow.SetActive(doSidewalkDirection);
    }

    // Returns end coordinates of raycast w.r.t. middle of bottom of image
    // Curb, curb cut, grating, manhole are counted as walkable when raycasting
    private const int maxSkips = 48;
    private (float, float) PerformRaycast(float x, float y, ref TensorInt output, float radFromLeft)
    {
        float dx = -Mathf.Cos(radFromLeft);
        float dy = Mathf.Sin(radFromLeft);
        float numSkips = 0;
        float validX = x;
        float validY = y;
        while (x >= 0 && y >= 0 && x < W && y < H) {
            int cls = (int) output[0, (int) y, (int) x];
            if (!LaxWalkable(cls)) { // On non-walkable; break if too many skips used
                if (++numSkips > maxSkips) break;
            }
            else { // On walkable; reset skips and update validX, validY
                (validX, validY) = (x, y);
                numSkips = 0;
            }
            x += dx;
            y -= dy;
        }
        return (validX-W/2, H-1-validY);
    }

    // If sidewalk or crosswalk found, returns the corresponding class. Otherwise, returns the bottom/middle of the image.
    private int CheckForWalkable(ref TensorInt output)
    {
        int bestCls = (int) output[0, H-1, W/2], bestCount = Int32.MaxValue;
        if (StrictWalkable(bestCls)) return bestCls;
        for (int i = 0; i < numRaycasts; i++) {
            (int cls, int count) = RaycastToWalkable(W/2, H-1, ref output, i * radWidth);
            if (cls != -1 && count < bestCount) {
                bestCount = count;
                bestCls = cls;
            }
        }
        return bestCls;
    }
    // Search for up to maxSkips iterations for sidewalk or crosswalk
    private (int, int) RaycastToWalkable(float x, float y, ref TensorInt output, float radFromLeft)
    {
        float dx = -Mathf.Cos(radFromLeft);
        float dy = Mathf.Sin(radFromLeft);
        float numSkips = 0;
        int count = 0;
        while (x >= 0 && y >= 0 && x < W && y < H) {
            int cls = (int) output[0, (int) y, (int) x];
            if (StrictWalkable(cls)) return (cls, count);
            else if (++numSkips > maxSkips) break;
            x += dx;
            y -= dy;
            count++;
        }
        return (-1, 999);
    }

    // Sidewalk or crosswalk
    private bool StrictWalkable(int cls) {
        return (cls >= 4 && cls <= 6);
    }
    // Sidewalk, crosswalk, curb, curb cut, grating, manhole
    private bool LaxWalkable(int cls) {
        return (cls >= 2 && cls <= 8);
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

        outputAspectRatioFitter.aspectMode = Utils.GetMode();
        inputAspectRatioFitter.aspectMode = Utils.GetMode();
        outputView.rectTransform.sizeDelta = new Vector2(resizeOptions.width, resizeOptions.height);
        inputView.rectTransform.sizeDelta = new Vector2(resizeOptions.width, resizeOptions.height);
    }

    private RenderTexture GetResultTexture(int[] data)
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