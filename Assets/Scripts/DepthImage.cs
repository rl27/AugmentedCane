// using System.Collections.Generic;
using System;
using System.Linq;
using System.Collections;
using System.Text;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

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
    GameObject AudioHandler;
    AudioPlayer audioPlayer;

    [SerializeField]
    GameObject VisionHandler;
    [SerializeField]
    GameObject TFLiteHandler;

    Vision vision;
    SsdSample ssd;
    YOLOSample yolo;
    DDRNetSample ddrnet;

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

    // True if everything is fine and Update() should be called. False if something went wrong.
    private bool shouldProceed = false;

    // Converts local coordinates to world coordinates.
    private Matrix4x4 localToWorldTransform = Matrix4x4.identity;
    private Matrix4x4 screenRotation = Matrix4x4.Rotate(Quaternion.identity);
    private new Camera camera;

    public static Vector3 position;
    public static Vector3 rotation;

    // These variables are for obstacle avoidance.
    private bool doObstacleAvoidance = true;
    float distanceToObstacle = 2.5f; // Distance in meters at which to alert for obstacles
    int collisionWindowWidth = 15; // Num. pixels left/right of the middle to check for obstacles
    float collisionSumThreshold = 2f;
    int confidenceMax = 255;

    public enum Direction { Left, Right, None }
    public static Direction direction = Direction.None;

    // Perform vision tasks on camera image
    bool visionActive = true;
    public static bool tflite = false;

    public Toggle toggle;
    void Awake()
    {
        if (m_OcclusionManager == null) {
            LogDepth("No occlusion manager");
            return;
        }
        if (m_CameraManager == null) {
            LogDepth("No camera manager");
            return;
        }

        camera = m_CameraManager.GetComponent<Camera>();
        if (!camera) {
            LogDepth("No camera");
            return;
        }

        #if UNITY_EDITOR
            doObstacleAvoidance = false;
        #endif

        #if UNITY_ANDROID
            confidenceMax = 255;
        #elif UNITY_IOS
            confidenceMax = 2;
        #endif

        audioPlayer = AudioHandler.GetComponent<AudioPlayer>();

        if (!visionActive) {
            yolo.frameContainer.enabled = false;
            vision.outputView.enabled = false;
            vision.inputView.enabled = false;
            VisionHandler.SetActive(false);
        }
        else {
            if (tflite) {
            ssd = TFLiteHandler.GetComponent<SsdSample>();
            yolo = TFLiteHandler.GetComponent<YOLOSample>();
            ddrnet = TFLiteHandler.GetComponent<DDRNetSample>();
            VisionHandler.SetActive(false);
            }
            else {
                vision = VisionHandler.GetComponent<Vision>();
                TFLiteHandler.SetActive(false);
            }   
        }

        // Set depth image material
        m_RawImage.material = m_DepthMaterial;

        // Disable the displayed images if necessary
        if (!showCameraImage) {
            // m_RawImage.enabled = false;
            m_RawCameraImage.enabled = false;
        }

        shouldProceed = true;
    }

    // This is called every frame
    void Update()
    {
        if (!shouldProceed)
            return;

        // Check if device supports environment depth.
        var descriptor = m_OcclusionManager.descriptor;
        if (descriptor != null && descriptor.environmentDepthImageSupported == Supported.Supported) {
            LogDepth("Environment depth is supported!");
        }
        else {
            if (descriptor == null || descriptor.environmentDepthImageSupported == Supported.Unsupported)
                LogDepth("Environment depth is not supported on this device.");
            else if (descriptor.environmentDepthImageSupported == Supported.Unknown)
                LogDepth("Determining environment depth support...");
            m_RawImage.texture = null;
            // m_RawImage.enabled = false;
            // return;
        }

        position = camera.transform.position;
        rotation = camera.transform.rotation.eulerAngles;

        screenRotation = Matrix4x4.Rotate(Quaternion.Euler(0, 0, GetRotationForScreen()));
        localToWorldTransform = camera.transform.localToWorldMatrix * screenRotation;

        m_StringBuilder.Clear();

        m_StringBuilder.AppendLine($"Camera position: {position}");
        m_StringBuilder.AppendLine($"Camera rotation: {rotation.y}");

        if (Vision.doSidewalkDirection)
            UpdateCameraImage();

        direction = Direction.None;
        if (!doObstacleAvoidance) return;

        UpdateDepthImages();

        // m_StringBuilder.AppendLine($"Width: {depthWidth}");
        // m_StringBuilder.AppendLine($"Height: {depthHeight}");

        // In portrait mode, (0.1, 0.1) is top right, (0.5, 0.5) is middle, (0.9, 0.9) is bottom left.
        // Screen orientation does not change coordinate locations on the screen.
        m_StringBuilder.AppendLine("DEPTH:");
        m_StringBuilder.AppendLine($"(0.01,0.01): {GetDepth(new Vector2(0.01f, 0.01f))}");
        m_StringBuilder.AppendLine($"(0.50,0.50): {GetDepth(new Vector2(0.5f, 0.5f))}");
        m_StringBuilder.AppendLine($"(0.99,0.99): {GetDepth(new Vector2(0.99f, 0.99f))}");

        m_StringBuilder.AppendLine("CONFIDENCE:");
        m_StringBuilder.AppendLine($"(0.01,0.01): {GetConfidence(new Vector2(0.01f, 0.01f))}");
        m_StringBuilder.AppendLine($"(0.50,0.50): {GetConfidence(new Vector2(0.5f, 0.5f))}");
        m_StringBuilder.AppendLine($"(0.99,0.99): {GetConfidence(new Vector2(0.99f, 0.99f))}");

        // int numLow = 0;
        // int numMed = 0;
        // int numHigh = 0;
        // #if UNITY_ANDROID
        //     for (int y = 0; y < depthHeight; y++) {
        //         for (int x = 0; x < depthWidth; x++) {
        //             int val = confidenceArray[(y * depthWidth) + x];
        //             if (val < 40)
        //                 numLow += 1;
        //             else if (val < 255)
        //                 numMed += 1;
        //             else if (val == 255)
        //                 numHigh += 1;
        //         }
        //     }
        // #elif UNITY_IOS
        //     for (int y = 0; y < depthHeight; y++) {
        //         for (int x = 0; x < depthWidth; x++) {
        //             int val = confidenceArray[(y * depthWidth) + x];
        //             if (val == 0)
        //                 numLow += 1;
        //             else if (val == 1)
        //                 numMed += 1;
        //             else if (val == 2)
        //                 numHigh += 1;
        //         }
        //     }
        // #endif
        // int numPixels = depthWidth * depthHeight;
        // m_StringBuilder.AppendLine("CONFIDENCE PROPORTIONS:");
        // m_StringBuilder.AppendLine($"Low: {(float) numLow / numPixels}");
        // m_StringBuilder.AppendLine($"Med: {(float) numMed / numPixels}");
        // m_StringBuilder.AppendLine($"High: {(float) numHigh / numPixels}");

        // Check for obstacles
        float[] closeTotals = AccumulateClosePoints(); // In portrait mode, index 0 = right side, max index = left side
        bool hasObstacle = false;
        int len = closeTotals.Length;
        for (int i = len/2 - collisionWindowWidth; i < len/2 + collisionWindowWidth + 1; i++) {
            if (closeTotals[i] > collisionSumThreshold) {
                hasObstacle = true;
                break;
            }
        }

        if (hasObstacle) { // Search for longest gap
            m_StringBuilder.AppendLine("Obstacle: Yes");
            int start = 0, end = 0, temp = 0;
            bool open = false;
            for (int i = 0; i < len; i++) {
                if (closeTotals[i] > collisionSumThreshold) { // No gap
                    if (open) {
                        if (end - start < i - temp) {
                            start = temp;
                            end = i;
                        }
                        open = false;
                    }
                }
                else { // Gap
                    if (!open)  {
                        temp = i;
                        open = true;
                    }
                }
            }

            bool goLeft;
            // If longest gap is long enough, go towards that gap
            // Sides/indices will be flipped depending on screen orientation
            if (end - start > collisionWindowWidth) {
                goLeft = (end-start)/2 > len/2;
            }
            else { // Otherwise, take side with higher avg distance
                float leftAvg, rightAvg;
                if (IsPortrait()) {
                    leftAvg = GetDepthAvg(0, depthWidth, depthHeight/2, depthHeight);
                    rightAvg = GetDepthAvg(0, depthWidth, 0, depthHeight/2);
                }
                else {
                    leftAvg = GetDepthAvg(depthWidth/2, depthWidth, 0, depthHeight);
                    rightAvg = GetDepthAvg(0, depthWidth/2, 0, depthHeight);
                }
                goLeft = leftAvg > rightAvg;
            }
            if (Screen.orientation == ScreenOrientation.PortraitUpsideDown || Screen.orientation == ScreenOrientation.LandscapeLeft)
                goLeft = !goLeft;
            direction = goLeft ? Direction.Left : Direction.Right;
            m_StringBuilder.AppendLine(goLeft ? "Dir: Left" : "Dir: Right");
            PlayCollision(goLeft ? -1 : 1);
        }
        else {
            m_StringBuilder.AppendLine("Obstacle: No");
        }
    }

    private void UpdateDepthImages()
    {
        // Acquire a depth image and update the corresponding raw image.
        if (occlusionManager.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage image)) {
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

    // mag = -1 for left, mag = 1 for right
    private void PlayCollision(int mag)
    {
        float localRot = -rotation.y * Mathf.Deg2Rad;
        AudioHandler.transform.position = position + new Vector3(mag * Mathf.Cos(localRot), mag * Mathf.Sin(localRot), 0);
        audioPlayer.PlayCollision();
    }

    private void UpdateCameraImage()
    {
        // Acquire a camera image, update the corresponding raw image, and do CV
        if (visionActive) {
            if (m_CameraManager.TryAcquireLatestCpuImage(out XRCpuImage cameraImage)) {
                using (cameraImage) {
                    UpdateRawImage(m_RawCameraImage, cameraImage, TextureFormat.RGB24, false);
                    if (tflite) {
                        // ssd.DoInvoke(m_RawCameraImage.texture);
                        // yolo.DoInvoke(m_RawCameraImage.texture);
                        ddrnet.DoInvoke(m_RawCameraImage.texture);
                    }
                    else {
                        if (!Vision.working) {
                            Vision.working = true;
                            Texture2D tex = m_RawCameraImage.texture as Texture2D;
                            StartCoroutine(vision.Detect(tex));
                        }
                    }
                }
            }
        }
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
        doObstacleAvoidance = !doObstacleAvoidance;
    }

    public void ToggleSmoothing()
    {
        m_OcclusionManager.environmentDepthTemporalSmoothingRequested = toggle.isOn;
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
            depthInMeters = BitConverter.ToUInt16(depthArray, depthStride * index) / 1000f;

        if (depthInMeters > 0) { 
            float vertex_x = (x - principalPoint.x) * depthInMeters / focalLength.x;
            float vertex_y = (y - principalPoint.y) * depthInMeters / focalLength.y;
            return Mathf.Sqrt(vertex_x*vertex_x + vertex_y*vertex_y + depthInMeters*depthInMeters);
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

    // Transforms a vertex in local space to world space.
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

            focalLength = MultiplyVector2(cameraIntrinsics.focalLength, intrinsicsScale);
            principalPoint = MultiplyVector2(cameraIntrinsics.principalPoint, intrinsicsScale);
        }
    }

    private static Vector2 MultiplyVector2(Vector2 v1, Vector2 v2)
    {
        return new Vector2(v1.x * v2.x, v1.y * v2.y);
    }

    // This function returns a 1D array representing a horizontal row of pixels.
    // Each point in the 2D depth array that is (1) high enough confidence and (2) within a certain distance
    // will give 1 vote towards the corresponding value in the 1D array.
    private float[] AccumulateClosePoints()
    {
        bool portrait = IsPortrait();
        float[] output = new float[portrait ? depthHeight : depthWidth];
        for (int y = 0; y < depthHeight; y++) {
            for (int x = 0; x < depthWidth; x++) {
                float dist = GetDepth(x, y);
                if (dist > distanceToObstacle)
                    continue;
                output[portrait ? y : x] += GetConfidence(x, y) / confidenceMax;
            }
        }
        return output;
    }

    // Get average of confidence-weighted depth values over the given coordinates
    private float GetDepthAvg(int xmin, int xmax, int ymin, int ymax)
    {
        float sum = 0;
        float count = 0;
        for (int y = ymin; y < ymax; y++) {
            for (int x = xmin; x < xmax; x++) {
                float confidence = GetConfidence(x, y);
                if (confidence > 0) {
                    sum += confidence * GetDepth(x, y);
                    count += confidence;
                }
            }
        }
        return (count != 0) ? (sum / count) : 0;
    }
}
