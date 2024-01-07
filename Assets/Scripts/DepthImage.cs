using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Priority_Queue;

// ARFoundation references:
// https://github.com/Unity-Technologies/arfoundation-samples/blob/main/Assets/Scripts/Runtime/DisplayDepthImage.cs
// https://github.com/Unity-Technologies/arfoundation-samples/blob/main/Assets/Scripts/Runtime/CpuImageSample.cs
public class DepthImage : MonoBehaviour
{
    // For logging
    [NonSerialized]
    public StringBuilder m_StringBuilder = new StringBuilder();

    // Get or set the AROcclusionManager.
    public AROcclusionManager occlusionManager {
        get => m_OcclusionManager;
        set => m_OcclusionManager = value;
    }
    [SerializeField]
    [Tooltip("The AROcclusionManager which will produce depth textures.")]
    AROcclusionManager m_OcclusionManager;

    // Get or set the ARCameraManager.
    public ARCameraManager cameraManager {
        get => m_CameraManager;
        set => m_CameraManager = value;
    }
    [SerializeField]
    [Tooltip("The ARCameraManager which will produce camera frame events.")]
    ARCameraManager m_CameraManager;

    // Using multiple audio sources to queue collision audio with no delay
    // https://johnleonardfrench.com/ultimate-guide-to-playscheduled-in-unity/#queue_clips
    // https://docs.unity3d.com/ScriptReference/AudioSource.SetScheduledEndTime.html
    public AudioSource[] audioSources;
    public static float collisionAudioMinRate = 2f; // Rate at which audio plays for obstacles at max distance
    public static float collisionAudioCapDistance = 0.5f; // Distance where audio speed caps out
    private float collisionAudioMaxRate;
    private double audioDuration; // Collision audio duration = 0.0853333333333333 (if using audioclip.length, it's 0.08533333)

    // The UI RawImage used to display the image on screen.
    public RawImage rawImage {
        get => m_RawImage;
        set => m_RawImage = value;
    }
    [SerializeField]
    RawImage m_RawImage;

    public RawImage rawCameraImage {
        get => m_RawCameraImage;
        set => m_RawCameraImage = value;
    }
    [SerializeField]
    RawImage m_RawCameraImage;

    // UI Text used to display whether depth is supported.
    public Text depthInfo {
        get => m_DepthInfo;
        set => m_DepthInfo = value;
    }
    [SerializeField]
    Text m_DepthInfo;

    // This is for using a custom shader that lets us see the full range of depth.
    // See the Details section here: https://github.com/andijakl/arfoundation-depth
    public Material depthMaterial {
        get => m_DepthMaterial;
        set => m_DepthMaterial = value;
    }
    [SerializeField]
    Material m_DepthMaterial;

    [SerializeField]
    GameObject VisionHandler;
    Vision vision;

    // Depth array
    [NonSerialized]
    public static byte[] depthArray = new byte[0];
    [NonSerialized]
    public int depthWidth = 0; // (width, height) = (160, 90) on OnePlus 11; (256, 192) on iPhone 12 Pro
    [NonSerialized]
    public int depthHeight = 0;
    int depthStride = 4; // Should be either 2 or 4

    // Depth confidence array
    // For iOS, confidence values are 0, 1, or 2. https://forum.unity.com/threads/depth-confidence-error-iphone-12-pro.1201831
    byte[] confidenceArray = new byte[0];
    int confidenceStride = 1; // Should be 1
    
    // Camera intrinsics
    Vector2 focalLength = Vector2.zero;
    Vector2 principalPoint = Vector2.zero;

    private bool showCameraImage = false;

    // Converts local coordinates to world coordinates.
    private Matrix4x4 localToWorldTransform = Matrix4x4.identity;
    private Matrix4x4 screenRotation = Matrix4x4.Rotate(Quaternion.identity);
    private new Camera camera;

    public static Vector3 position;
    public static Vector3 rotation;

    // These variables are for obstacle avoidance.
    private bool doObstacleAvoidance = true;
    public static float distanceToObstacle = 2.5f; // Distance in meters at which to alert for obstacles
    private int confidenceMax = 255;
    private float depthConfidenceThreshold = 0.1f;

    public static float personRadius = 0.3f; // Estimated half-width of a person
    public static float personHeight = 1.8f - groundPadding; // Estimated height of a person

    public enum Direction { Left, Right, None }
    public static Direction direction = Direction.None;

    public Toggle depthToggle;
    public Toggle smoothingToggle;

    double curTime = 0;
    double lastDSP = 0;

    void Awake()
    {
        camera = m_CameraManager.GetComponent<Camera>();

        // if (m_OcclusionManager == null) {
        //     LogDepth("No occlusion manager");
        //     return;
        // }
        // if (m_CameraManager == null) {
        //     LogDepth("No camera manager");
        //     return;
        // }
        // if (!camera) {
        //     LogDepth("No camera");
        //     return;
        // }

        m_CameraManager.frameReceived += OnCameraFrameReceived;
        // smoothingToggle.enabled = (m_OcclusionManager.descriptor.environmentDepthTemporalSmoothingSupported == Supported.Supported);

        #if UNITY_ANDROID
            confidenceMax = 255;
        #elif UNITY_IOS
            confidenceMax = 2;
        #endif

        vision = VisionHandler.GetComponent<Vision>();

        // Set depth image material
        m_RawImage.material = m_DepthMaterial;

        // Disable the displayed images if necessary
        if (!showCameraImage) {
            // m_RawImage.enabled = false;
            m_RawCameraImage.enabled = false;
        }

        audioDuration = (double) audioSources[0].clip.samples / audioSources[0].clip.frequency;
        collisionAudioMaxRate = 1 / (float)audioDuration;
    }

    bool initConfig = false;
    void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        if (!initConfig) {
            SetXRCameraConfiguration();
            initConfig = true;
        }

        if (Vision.doSidewalkDirection && !Vision.working)
            UpdateCameraImage();

        direction = Direction.None;
        if (doObstacleAvoidance) {
            UpdateDepthImages();
            ProcessDepthImages();
        }
    }

    // This is called every frame
    void Update()
    {
        // Update timer for collision audio
        if (lastDSP != AudioSettings.dspTime) {
            lastDSP = AudioSettings.dspTime;
            curTime = lastDSP;
        }
        else curTime += Time.unscaledDeltaTime;

        // Check if device supports environment depth.
        var descriptor = m_OcclusionManager.descriptor;
        if (descriptor != null && descriptor.environmentDepthImageSupported == Supported.Supported) {
            LogDepth("Environment depth is supported!");
        }
        else {
            if (descriptor == null || descriptor.environmentDepthImageSupported == Supported.Unsupported) {
                LogDepth("Environment depth is not supported on this device.");
                doObstacleAvoidance = false;
            }
            else if (descriptor.environmentDepthImageSupported == Supported.Unknown)
                LogDepth("Determining environment depth support...");
            m_RawImage.texture = null;
            // m_RawImage.enabled = false;
            // return;
        }

        // position and rotation briefly become 0 on focus loss/regain, which can mess things up
        // Same for localToWorldTransform
        if (camera.transform.position != Vector3.zero && camera.transform.rotation.eulerAngles != Vector3.zero) {
            position = camera.transform.position;
            rotation = camera.transform.rotation.eulerAngles;
        }
        screenRotation = Matrix4x4.Rotate(Quaternion.Euler(0, 0, GetRotationForScreen()));
        if (camera.transform.localToWorldMatrix != Matrix4x4.identity)
            localToWorldTransform = camera.transform.localToWorldMatrix * screenRotation;

        m_StringBuilder.Clear();
        m_StringBuilder.AppendLine($"Local position: {position}");
        m_StringBuilder.AppendLine($"Local rotation: {rotation.y.ToString("F1")}°");
        // m_StringBuilder.AppendLine($"FOV: {2*Mathf.Atan(depthWidth/(2*focalLength.x))*Mathf.Rad2Deg}, {2*Mathf.Atan(depthHeight/(2*focalLength.y))*Mathf.Rad2Deg}");
    }

    private void UpdateDepthImages()
    {
        // Acquire a depth image and update the corresponding raw image.
        if (m_OcclusionManager.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage image)) {
            using (image) {
                UpdateRawImage(m_RawImage, image, image.format.AsTextureFormat(), true);

                // Get distance data into depthArray
                // https://github.com/googlesamples/arcore-depth-lab/blob/8f76532d4a67311463ecad6b88b3f815c6cf1eea/Assets/ARRealismDemos/Common/Scripts/MotionStereoDepthDataSource.cs#L250
                depthWidth = image.width;
                depthHeight = image.height;
                UpdateCameraParams();

                int numPixels = depthWidth * depthHeight;
                Debug.Assert(image.planeCount == 1, "Plane count is not 1");
                depthStride = image.GetPlane(0).pixelStride;
                int numBytes = numPixels * depthStride;
                if (depthArray.Length != numBytes)
                    depthArray = new byte[numBytes];
                image.GetPlane(0).data.CopyTo(depthArray);
            }
        }

        // Acquire a depth confidence image.
        if (occlusionManager.TryAcquireEnvironmentDepthConfidenceCpuImage(out XRCpuImage confidenceImage)) {
            using (confidenceImage) {
                if (confidenceImage.width != depthWidth || confidenceImage.height != depthHeight) {
                    LogDepth("Confidence dimensions don't match");
                }
                else {
                    int numPixels = depthWidth * depthHeight;
                    Debug.Assert(confidenceImage.planeCount == 1, "Plane count is not 1");
                    confidenceStride = confidenceImage.GetPlane(0).pixelStride;
                    int numBytes = numPixels * confidenceStride;
                    if (confidenceArray.Length != numBytes)
                        confidenceArray = new byte[numBytes];
                    confidenceImage.GetPlane(0).data.CopyTo(confidenceArray);
                }
            }
        }
    }

    private void ProcessDepthImages()
    {
        // m_StringBuilder.AppendLine($"Depth dims: {depthWidth} {depthHeight}");
        // if (m_CameraManager.subsystem.currentConfiguration != null) {
        //     var cfg = (XRCameraConfiguration) m_CameraManager.subsystem.currentConfiguration;
        //     m_StringBuilder.AppendLine($"Img dims: {cfg.width} {cfg.height} {cfg.framerate}FPS");
        // }

        // In portrait mode, (0.1, 0.1) is top right, (0.5, 0.5) is middle, (0.9, 0.9) is bottom left.
        // Screen orientation does not change coordinate locations on the screen.
        // m_StringBuilder.AppendLine("DEPTH:");
        // m_StringBuilder.AppendLine($"(0.1,0.1): {GetDepth(new Vector2(0.1f, 0.1f))}");
        // m_StringBuilder.AppendLine($"(0.5,0.5): {GetDepth(new Vector2(0.5f, 0.5f))}");
        // m_StringBuilder.AppendLine($"(0.9,0.9): {GetDepth(new Vector2(0.9f, 0.9f))}");

        m_StringBuilder.AppendLine($"Ground: {ground.ToString("F2")}m");

        CullGrid();
        ProcessDepthImage();
        PointCloudVisualizer.ProcessPoints();

        (float relHeading, float closest) = CheckForObstacle();
        if (closest < 30) {
            m_StringBuilder.AppendLine("Obstacle ahead");
            direction = Direction.Left;
            float rate = (closest - collisionAudioCapDistance) / (distanceToObstacle - collisionAudioCapDistance);
            rate = Mathf.Lerp(collisionAudioMaxRate, collisionAudioMinRate, rate);
            PlayCollision(relHeading * Mathf.Deg2Rad, 1/rate - audioDuration);

            m_StringBuilder.AppendLine($" Closest {closest.ToString("F2")}m; Beep rate {rate.ToString("F2")}; Dir {relHeading.ToString("F2")}");
        }
    }

    // mag = -1 for left, mag = 1 for right
    private double lastScheduled = -10;
    private int audioSelect = 0;
    private void PlayCollision(float dir, double delay)
    {
        float mag = Mathf.Sin(dir);
        float localRot = -rotation.y * Mathf.Deg2Rad;
        this.transform.position = position + new Vector3(mag * Mathf.Cos(localRot), 0, mag * Mathf.Sin(localRot));

        double nextSchedule = Math.Max(curTime, lastScheduled + audioDuration + delay);
        while (nextSchedule - curTime < 0.3 && !audioSources[audioSelect].isPlaying) { // Schedule next audio if it will be needed soon
            audioSources[audioSelect].PlayScheduled(nextSchedule);
            audioSelect = (audioSelect + 1) % audioSources.Length;
            lastScheduled = nextSchedule;
            nextSchedule = Math.Max(curTime, lastScheduled + audioDuration + delay);
        }
    }

    private void UpdateCameraImage()
    {
        // Acquire a camera image, update the corresponding raw image, and do CV
        if (m_CameraManager.TryAcquireLatestCpuImage(out XRCpuImage cameraImage)) {
            using (cameraImage) {
                UpdateRawImage(m_RawCameraImage, cameraImage, TextureFormat.RGB24, false);
                StartCoroutine(vision.Detect(m_RawCameraImage.texture));
            }
        }
    }

    void SetXRCameraConfiguration()
    {
        NativeArray<XRCameraConfiguration> configurations = m_CameraManager.GetConfigurations(Allocator.Temp);
        var bestConfig = configurations[0];
        foreach (var config in configurations)
        {
            if (config.width < Vision.H || config.height < Vision.W) // Assume Vision.H == Vision.W
                continue;
            if ((float) bestConfig.width / bestConfig.height < (float) config.width / config.height)
                continue;
            if (config.height < bestConfig.height || config.width < bestConfig.width || config.framerate < bestConfig.framerate) {
                bestConfig = config;
            }
        }
        m_CameraManager.subsystem.currentConfiguration = bestConfig;
    }

    // Log the given text to the depth info text box.
    void LogDepth(string text)
    {
        if (m_DepthInfo != null)
            m_DepthInfo.text = text;
        else
            Debug.Log(text);
    }

    public void ToggleObstacleAvoidance()
    {
        doObstacleAvoidance = depthToggle.isOn;
        m_OcclusionManager.enabled = depthToggle.isOn;
    }

    public void ToggleSmoothing()
    {
        m_OcclusionManager.environmentDepthTemporalSmoothingRequested = smoothingToggle.isOn;
    }

    private void UpdateRawImage(RawImage rawImage, XRCpuImage cpuImage, TextureFormat format, bool isDepth)
    {
        Debug.Assert(rawImage != null, "no raw image");

        // Get the texture associated with the UI.RawImage that we wish to display on screen.
        var texture = rawImage.texture as Texture2D;

        // If the texture hasn't yet been created, or if its dimensions have changed, (re)create the texture.
        // Note: Although texture dimensions do not normally change frame-to-frame, they can change in response to
        //    a change in the camera resolution (for camera images) or changes to the quality of the human depth
        //    and human stencil buffers.
        if (texture == null || texture.width != cpuImage.width || texture.height != cpuImage.height)
        {
            texture = new Texture2D(cpuImage.width, cpuImage.height, format, false);
            rawImage.texture = texture;
        }

        // For display, we need to mirror about the vertical axis.
        var conversionParams = new XRCpuImage.ConversionParams(cpuImage, format, XRCpuImage.Transformation.MirrorY);

        // Get the Texture2D's underlying pixel buffer.
        var rawTextureData = texture.GetRawTextureData<byte>();

        // Make sure the destination buffer is large enough to hold the converted data (they should be the same size)
        Debug.Assert(rawTextureData.Length == cpuImage.GetConvertedDataSize(conversionParams.outputDimensions, conversionParams.outputFormat),
            "The Texture2D is not the same size as the converted data.");

        // Perform the conversion.
        cpuImage.Convert(conversionParams, rawTextureData);

        // "Apply" the new pixel data to the Texture2D.
        texture.Apply();

        // Get the aspect ratio for the current texture.
        var textureAspectRatio = (float)texture.width / texture.height;

        // Determine the raw image rectSize preserving the texture aspect ratio, matching the screen orientation,
        // and keeping a minimum dimension size.
        float minDimension = 480.0f;
        float maxDimension = Mathf.Round(minDimension * textureAspectRatio);
        Vector2 rectSize;
        if (isDepth) {
            switch (Screen.orientation)
            {
                case ScreenOrientation.LandscapeRight:
                case ScreenOrientation.LandscapeLeft:
                    rectSize = new Vector2(maxDimension, minDimension);
                    break;
                case ScreenOrientation.PortraitUpsideDown:
                case ScreenOrientation.Portrait:
                default:
                    rectSize = new Vector2(minDimension, maxDimension);
                    break;
            }
            rawImage.rectTransform.sizeDelta = rectSize;

            // Rotate the depth material to match screen orientation.
            Quaternion rotation = Quaternion.Euler(0, 0, GetRotation());
            Matrix4x4 rotMatrix = Matrix4x4.Rotate(rotation);
            m_RawImage.material.SetMatrix(Shader.PropertyToID("_DisplayRotationPerFrame"), rotMatrix);
        }
        else {
            rectSize = new Vector2(maxDimension, minDimension);
            rawImage.rectTransform.sizeDelta = rectSize;
        }
    }

    /*
    Obtain the depth value in meters. (u,v) are normalized screen coordinates; stride is the pixel stride of the acquired environment depth image.
    This function is based on: https://github.com/googlesamples/arcore-depth-lab/blob/8f76532d4a67311463ecad6b88b3f815c6cf1eea/Assets/ARRealismDemos/OrientedReticle/Scripts/OrientedReticle.cs#L116
    Further references:
    https://developers.google.com/ar/develop/unity-arf/depth/developer-guide#extract_distance_from_a_depth_image
    https://github.com/googlesamples/arcore-depth-lab/blob/8f76532d4a67311463ecad6b88b3f815c6cf1eea/Assets/ARRealismDemos/Common/Scripts/DepthSource.cs#L436
    */
    public float GetDepth(Vector2 uv)
    {
        if (depthArray.Length == 0)
            return 99999f;
        
        int x = (int)(uv.x * (depthWidth - 1));
        int y = (int)(uv.y * (depthHeight - 1));

        return GetDepth(x, y);
    }

    public float GetDepth(int x, int y)
    {
        if (depthArray.Length == 0)
            return 99999f;

        // if (x < 0 || x >= depthWidth || y < 0 || y >= depthHeight) {
        //     Debug.Log("Invalid depth index");
        //     return -99999f;
        // }

        /*
        On an iPhone 12 Pro, the image data is in DepthFloat32 format, so we use ToSingle().
        On a OnePlus 11, it is in DepthUInt16 format.
        CPU image formats are described here:
        https://docs.unity3d.com/Packages/com.unity.xr.arsubsystems@4.1/api/UnityEngine.XR.ARSubsystems.XRCpuImage.Format.html
        Also see the below example code:
        https://forum.unity.com/threads/how-to-measure-distance-from-depth-map.1440799
        */
        int index = (y * depthWidth) + x;
        float depthInMeters = 0;
        if (depthStride == 4) // DepthFloat32
            depthInMeters = BitConverter.ToSingle(depthArray, depthStride * index);
        else if (depthStride == 2) // DepthUInt16
            depthInMeters = BitConverter.ToUInt16(depthArray, depthStride * index) * 0.001f;

        if (depthInMeters > 0) {
            // Do not factor in focalLength and principalPoint if only measuring forward distance from camera
            /*float vertex_x = (x - principalPoint.x) * depthInMeters / focalLength.x;
            float vertex_y = (y - principalPoint.y) * depthInMeters / focalLength.y;
            return Mathf.Sqrt(vertex_x*vertex_x + vertex_y*vertex_y + depthInMeters*depthInMeters);*/
            return depthInMeters;
        }

        return 99999f;
    }

    public float GetConfidence(Vector2 uv)
    {
        if (confidenceArray.Length == 0)
            return 0;
        int x = (int)(uv.x * (depthWidth - 1));
        int y = (int)(uv.y * (depthHeight - 1));
        int index = (y * depthWidth) + x;
        return confidenceArray[confidenceStride * index];
    }

    public float GetConfidence(int x, int y)
    {
        if (confidenceArray.Length == 0)
            return 0;
        int index = (y * depthWidth) + x;
        return confidenceArray[confidenceStride * index];
    }

    // Given image pixel coordinates (x,y) and distance z, returns a vertex in local camera space.
    public Vector3 ComputeVertex(int x, int y, float z)
    {
        Vector3 vertex = Vector3.negativeInfinity;
        if (z > 0) {
            float vertex_x = (x - principalPoint.x) * z / focalLength.x;
            float vertex_y = (y - principalPoint.y) * z / focalLength.y;
            vertex.x = vertex_x;
            vertex.y = -vertex_y;
            vertex.z = z;
        }
        return vertex;
    }

    // Transforms a vertex in local space to world space
    // NOTE: Is not the same as using camera.ScreenToWorldPoint.
    // https://forum.unity.com/threads/how-to-get-point-cloud-in-arkit.967681/#post-6340404
    public Vector3 TransformLocalToWorld(Vector3 vertex)
    {
        return localToWorldTransform.MultiplyPoint(vertex);
    }

    // https://github.com/Unity-Technologies/arfoundation-samples/issues/266#issuecomment-523316133
    public static int GetRotation() => Screen.orientation switch
    {
        ScreenOrientation.Portrait => 90,
        ScreenOrientation.LandscapeLeft => 180,
        ScreenOrientation.PortraitUpsideDown => -90,
        ScreenOrientation.LandscapeRight => 0,
        _ => 90
    };

    private static int GetRotationForScreen() => Screen.orientation switch
    {
        ScreenOrientation.Portrait => -90,
        ScreenOrientation.LandscapeLeft => 0,
        ScreenOrientation.PortraitUpsideDown => 90,
        ScreenOrientation.LandscapeRight => 180,
        _ => -90
    };

    private static bool IsPortrait() => Screen.orientation switch
    {
        ScreenOrientation.Portrait => true,
        ScreenOrientation.LandscapeLeft => false,
        ScreenOrientation.PortraitUpsideDown => true,
        ScreenOrientation.LandscapeRight => false,
        _ => true
    };

    // https://github.com/googlesamples/arcore-depth-lab/blob/8f76532d4a67311463ecad6b88b3f815c6cf1eea/Assets/ARRealismDemos/Common/Scripts/MotionStereoDepthDataSource.cs#L219
    private void UpdateCameraParams()
    {
        // Gets the camera parameters to create the required number of vertices.
        if (m_CameraManager.TryGetIntrinsics(out XRCameraIntrinsics cameraIntrinsics))
        {
            // Scales camera intrinsics to the depth map size.
            Vector2 intrinsicsScale;
            intrinsicsScale.x = depthWidth / (float)cameraIntrinsics.resolution.x;
            intrinsicsScale.y = depthHeight / (float)cameraIntrinsics.resolution.y;

            // intrinsicsScale: 0.25, 0.19
            // cameraIntrinsics.resolution: 640, 480
            // cameraIntrinsics.focalLength): 442.88, 443.52
            // cameraIntrinsics.principalPoint: 321.24, 239.47

            focalLength = MultiplyVector2(cameraIntrinsics.focalLength, intrinsicsScale); // On OnePlus 11: close to (110.32, 82.62), but should probably be (110,110)
            principalPoint = MultiplyVector2(cameraIntrinsics.principalPoint, intrinsicsScale); // This is always close to (depthWidth/2, depthHeight/2)

            // focalLength.y is not accurate
            // Inspired by: https://github.com/googlesamples/arcore-depth-lab/blob/8f76532d4a67311463ecad6b88b3f815c6cf1eea/Assets/ARRealismDemos/OrientedReticle/Scripts/OrientedReticle.cs#L240
            focalLength.y = focalLength.x;
        }
    }

    private static Vector2 MultiplyVector2(Vector2 v1, Vector2 v2)
    {
        return new Vector2(v1.x * v2.x, v1.y * v2.y);
    }

    // 3D grid for tracking points / potential obstacles
    private static Dictionary<Vector3, uint> grid3d = new Dictionary<Vector3, uint>();
    public static void AddToGrid(Vector3 v)
    {
        Vector3 gridPt = new Vector3(nodeSize * Mathf.Round(v.x / nodeSize),
                                     nodeSize * Mathf.Round(v.y / nodeSize),
                                     nodeSize * Mathf.Round(v.z / nodeSize));
        if (!grid3d.ContainsKey(gridPt))
            grid3d[gridPt] = 1;
        else grid3d[gridPt] += 1;
    }

    // Delete any cells that are too far from user location
    private float cellDeletionRange = searchRadius + 1;

    // Reset counts for cells that are either too far away or within view of the camera
    private void CullGrid()
    {
        Vector3 TR = TransformLocalToWorld(ComputeVertex(0, 0, 1));
        Vector3 BR = TransformLocalToWorld(ComputeVertex(depthWidth-1, 0, 1));
        Vector3 BL = TransformLocalToWorld(ComputeVertex(depthWidth-1, depthHeight-1, 1));
        Vector3 TL = TransformLocalToWorld(ComputeVertex(0, depthHeight-1, 1));
        Plane right = new Plane(position, TR, BR);
        Plane bot = new Plane(position, BR, BL);
        Plane left = new Plane(position, BL, TL);
        Plane top = new Plane(position, TL, TR);
        
        List<Vector3> toRemove = new List<Vector3>();
        List<Vector3> toZero = new List<Vector3>();
        foreach (Vector3 gridPt in grid3d.Keys) {
            if (new Vector2(gridPt.x - position.x, gridPt.z - position.z).magnitude > cellDeletionRange)
                toRemove.Add(gridPt);
            else if (!right.GetSide(gridPt) && !left.GetSide(gridPt) && !bot.GetSide(gridPt) && !top.GetSide(gridPt)) {
                if (grid3d[gridPt] == 0)
                    toRemove.Add(gridPt);
                else toZero.Add(gridPt);
            }
        }
        foreach (Vector3 gridPt in toRemove)
            grid3d.Remove(gridPt);
        foreach (Vector3 gridPt in toZero)
            grid3d[gridPt] = 0;
    }

    public static float ground = -0.5f; // Ground elevation (in meters) relative to camera; default floor is 0.5m below camera
    private const float groundPadding = 0.35f; // Height to add to calculated ground level to count as ground
    private const float groundRadius = 0.25f;

    private const float nodeSize = 0.1f;
    private byte[,] searchGrid;
    private const float searchRadius = 6f;
    private const int searchWidthHalf = (int) (searchRadius / nodeSize);
    private const int searchWidth = 1 + 2 * ((int) (searchRadius / nodeSize));

    private float prevPersonRadius = 0; // This is for tracking when personRadius changes
    private List<Vector2Int> circleCells = new List<Vector2Int>(); // Cells to block off based on personRadius

    private const int numPoints = 1; // Number of points required for a grid3d cell to be considered blocked
    (float, float) CheckForObstacle()
    {
        // For calculations
        float sin = Mathf.Sin(rotation.y * Mathf.Deg2Rad);
        float cos = Mathf.Cos(rotation.y * Mathf.Deg2Rad);

        float direction = 0;
        float closest = 999f;
        float groundSum = 0;
        float groundCount = 0;
        int blockingCount = 0;
        foreach (Vector3 gridPt in grid3d.Keys) {
            if (grid3d[gridPt] >= numPoints) {
                Vector3 translated = gridPt - position;
                if (translated.y > ground && translated.y < (ground + personHeight)) { // Height check
                    // Distance & width check
                    float rX = cos*translated.x - sin*translated.z;
                    float rZ = sin*translated.x + cos*translated.z;
                    if (rZ > 0 && rZ < distanceToObstacle && rX > -personRadius && rX < personRadius) {
                        blockingCount++;
                        float t = rX*rX+rZ*rZ;
                        if (t < closest) {
                            closest = t;
                        }
                    }
                }
                if (translated.y < 0 && (new Vector2(translated.x, translated.z)).magnitude < groundRadius) {
                    groundSum += translated.y * grid3d[gridPt];
                    groundCount += grid3d[gridPt];
                }
            }
        }
        closest = Mathf.Sqrt(closest);
        if (groundCount >= 5)
            ground = Mathf.Min(-0.5f, groundSum/groundCount + groundPadding);

        // If there is an obstacle ahead, search for direction
        if (blockingCount >= 2) {
            direction = RunSearch();
        }

        return (direction, closest);
    }

    // Populates the grid dictionary using the depth image.
    private void ProcessDepthImage()
    {
        int count = 0;
        for (int y = 0; y < depthHeight; y++) {
            for (int x = 0; x < depthWidth; x++) {
                float conf = GetConfidence(x, y);
                if (conf / confidenceMax < depthConfidenceThreshold) continue;
                count++;
                Vector3 pos = TransformLocalToWorld(ComputeVertex(x, y, GetDepth(x, y)));
                AddToGrid(pos);
            }
        }
        m_StringBuilder.AppendLine($"Points used: {(((float) count) / (depthWidth * depthHeight)).ToString("F3")}");
    }

    private float RunSearch()
    {
        // Create search grid if it doesn't exist
        if (searchGrid == null) {
            searchGrid = new byte[searchWidth, searchWidth];
        }
        else { // Clear obstacles on the grid
            Array.Clear(searchGrid, 0, searchGrid.Length);
        }

        // Recalculate circle cells if personRadius has changed
        if (prevPersonRadius != personRadius) {
            prevPersonRadius = personRadius;
            circleCells.Clear();
            int bound = (int) Mathf.Ceil(personRadius / nodeSize);
            for (int i = -bound; i <= bound; i++) {
                for (int j = -bound; j <= bound; j++) {
                    if (i == 0 && j == 0)
                        continue;
                    if (Mathf.Sqrt(i*i+j*j) <= personRadius / nodeSize)
                        circleCells.Add(new Vector2Int(i, j));
                }
            }
        }

        // Populate search grid using grid3d; Track blocking points in the search grid
        float sin = Mathf.Sin(rotation.y * Mathf.Deg2Rad);
        float cos = Mathf.Cos(rotation.y * Mathf.Deg2Rad);
        HashSet<Vector2Int> blocking = new HashSet<Vector2Int>();
        foreach (Vector3 gridPt in grid3d.Keys) {
            if (grid3d[gridPt] >= numPoints) {
                float yDiff = gridPt.y - position.y;
                if (yDiff > ground && yDiff < (ground + personHeight)) { // Height check
                    float x = gridPt.x - position.x;
                    float y = gridPt.z - position.z;
                    int i = (int) ((x * cos - y * sin) / nodeSize) + searchWidthHalf;
                    if (i <= 0 || i >= searchWidth - 1) continue;
                    int j = (int) ((x * sin + y * cos) / nodeSize) + searchWidthHalf;
                    if (j <= 0 || j >= searchWidth - 1) continue;
                    searchGrid[i, j] = 1;
                    blocking.Add(new Vector2Int(i, j));
                }
            }
        }

        // For each blocking point in the grid, also block out nearby points within personRadius
        foreach (Vector2Int b in blocking)
            SetInCircle(b, 1);

        // Unblock the person
        SetInCircle(new Vector2Int(searchWidthHalf, searchWidthHalf), 0);

        // Re-block original obstacles
        foreach (Vector2Int b in blocking) {
            searchGrid[b.x, b.y] = 1;
        }

        return BestDirection();
    }

    private class Node {
        public Node parent;
        public int x;
        public int y;
        public Node(int x2, int y2, Node par) {
            x = x2;
            y = y2;
            parent = par;
        }
    }

    SimplePriorityQueue<Node> priorityQueue = new SimplePriorityQueue<Node>();
    bool retry;
    private float BestDirection()
    {
        priorityQueue.Clear();

        CheckAndEnqueue(searchWidthHalf, searchWidthHalf, null);

        int curX, curY;
        Node best = null;
        while (priorityQueue.Count != 0) {
            Node curNode = priorityQueue.Dequeue();
            curX = curNode.x;
            curY = curNode.y;
            if (best == null || curY > best.y)
                best = curNode;

            retry = true;

            // Attempt to move up
            if (CheckAndEnqueue(curX, curY + 1, curNode)) {
                // Node above is blocked; check left and right
                if (CheckAndEnqueue(curX + 1, curY + 1, curNode)) { // top right
                    if (CheckAndEnqueue(curX + 1, curY, curNode)) { // right
                        if (CheckAndEnqueue(curX + 1, curY - 1, curNode)) { // bot right
                            CheckAndEnqueue(curX, curY - 1, curNode); // bot
                        }
                    }
                }
                if (CheckAndEnqueue(curX - 1, curY + 1, curNode)) { // top left
                    if (CheckAndEnqueue(curX - 1, curY, curNode)) { // left
                        if (CheckAndEnqueue(curX - 1, curY - 1, curNode)) { // bot left
                            CheckAndEnqueue(curX, curY - 1, curNode); // bot
                        }
                    }
                }
            }

            // No possible move found; backtrack and retry parent node
            if (retry) {
                retry = false;
                searchGrid[curX, curY] = 1; // Set this to 1 so it doesn't stop scan from parent
                priorityQueue.Enqueue(curNode.parent, -9999f);
            }
        }

        while (best != null) {
            if (AttemptLine(best.x, best.y)) {
                break;
            }
            best = best.parent;
        }
        if (best != null) {
            return 90 - Mathf.Atan2(best.y - searchWidthHalf, best.x - searchWidthHalf) * Mathf.Rad2Deg;
        }

        return 0;
    }

    // Return value indicates whether to continue checking. True means continue checking, false means stop
    private bool CheckAndEnqueue(int x, int y, Node parent)
    {
        if (x >= 0 && x < searchWidth && y >= 0 && y < searchWidth && searchGrid[x, y] != 1) {
            if (searchGrid[x, y] == 0) {
                searchGrid[x, y] = 2;
                priorityQueue.Enqueue(new Node(x, y, parent), -y);
                retry = false;
            }
            return false;
        }
        return true;
    }

    // Attempt to draw straight lines from user to target location
    // Returns true if target was successfully reached; returns false if encountered an obstacle
    private bool AttemptLine(int targetX, int targetY)
    {
        // http://www.cse.yorku.ca/~amana/research/grid.pdf
        int x = searchWidthHalf, y = searchWidthHalf;
        float dX = targetX - x + 1E-6f, dY = targetY - y + 1E-6f;
        int stepX = (dX > 0) ? 1 : -1, stepY = (dY > 0) ? 1 : -1;
        float dist = Mathf.Sqrt(dX*dX+dY*dY);
        float tDeltaX = Mathf.Abs(dist / dX), tDeltaY = Mathf.Abs(dist / dY);
        float tMaxX = tDeltaX, tMaxY = tDeltaY;
        bool hitObstacle = false;
        while (x != targetX && y != targetY) {
            if (tMaxX < tMaxY) {
                tMaxX += tDeltaX;
                x += stepX;
            }
            else {
                tMaxY += tDeltaY;
                y += stepY;
            }
            if (searchGrid[x, y] == 1) {
                hitObstacle = true;
                break;
            }
        }
        return hitObstacle;
    }

    private void SetInCircle(Vector2Int center, byte val)
    {
        foreach (Vector2Int circleCell in circleCells) {
            int x = center.x + circleCell.x;
            if (x < 0 || x >= searchWidth) continue;
            int y = center.y + circleCell.y;
            if (y < 0 || y >= searchWidth) continue;
            searchGrid[x, y] = val;
        }
    }
}