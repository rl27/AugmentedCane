// using System.Collections;
// using System.Collections.Generic;
using System;
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
    // Get or set the AROcclusionManager.
    public AROcclusionManager occlusionManager
    {
        get => m_OcclusionManager;
        set => m_OcclusionManager = value;
    }
    [SerializeField]
    [Tooltip("The AROcclusionManager which will produce depth textures.")]
    AROcclusionManager m_OcclusionManager;

    // Get or set the ARCameraManager.
    public ARCameraManager cameraManager
    {
        get => m_CameraManager;
        set => m_CameraManager = value;
    }
    [SerializeField]
    [Tooltip("The ARCameraManager which will produce camera frame events.")]
    ARCameraManager m_CameraManager;

    // The UI RawImage used to display the image on screen.
    public RawImage rawImage
    {
        get => m_RawImage;
        set => m_RawImage = value;
    }
    [SerializeField]
    RawImage m_RawImage;

    public RawImage rawCameraImage
    {
        get => m_RawCameraImage;
        set => m_RawCameraImage = value;
    }
    [SerializeField]
    RawImage m_RawCameraImage;

    // The UI Text used to display information about the image on screen.
    public Text imageInfo
    {
        get => m_ImageInfo;
        set => m_ImageInfo = value;
    }
    [SerializeField]
    Text m_ImageInfo;

    // This is for using a custom shader that lets us see the full range of depth.
    // See the Details section here: https://github.com/andijakl/arfoundation-depth
    public Material depthMaterial
    {
        get => m_DepthMaterial;
        set => m_DepthMaterial = value;
    }
    [SerializeField]
    Material m_DepthMaterial;

    // Array for holding distance
    byte[] depthArray = new byte[0];
    int depthArrayLength = 0;
    int stride = 4;

    // Depth image resolution
    int depthWidth = 0;
    int depthHeight = 0;

    // Camera intrinsics
    Vector2 focalLength = Vector2.zero;
    Vector2 principalPoint = Vector2.zero;

    // StringBuilder for building strings to be logged.
    readonly StringBuilder m_StringBuilder = new StringBuilder();

    SensorData sensors;

    bool testingBool = true;

    void OnEnable()
    {
        Debug.Assert(m_CameraManager != null, "No camera manager");
        m_RawImage.material = m_DepthMaterial;

        sensors = GameObject.Find("SensorHandler").GetComponent<SensorData>();
    }

    // Update is called once per frame
    void Update()
    {
        // Debug.Assert(m_OcclusionManager != null, "no occlusion manager");
        if (m_OcclusionManager == null) {
            LogText("No occlusion manager");
            return;
        }
        if (m_CameraManager == null) {
            LogText("No camera manager");
            return;
        }

        // Check if device supports environment depth.
        var descriptor = m_OcclusionManager.descriptor;
        if (descriptor != null && descriptor.environmentDepthImageSupported == Supported.Supported) {
            // LogText("Environment depth is supported!");
        }
        else {
            if (descriptor == null || descriptor.environmentDepthImageSupported == Supported.Unsupported)
                LogText("Environment depth is not supported on this device.");
            else if (descriptor.environmentDepthImageSupported == Supported.Unknown)
                LogText("Determining environment depth support...");
            m_RawImage.texture = null;
            // return;
        }

        // Acquire a depth image and update the displayed image.
        if (occlusionManager.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage image)) {
            using (image) {
                UpdateRawImage(m_RawImage, image, true);

                // Get distance data into depthArray
                // https://github.com/googlesamples/arcore-depth-lab/blob/8f76532d4a67311463ecad6b88b3f815c6cf1eea/Assets/ARRealismDemos/Common/Scripts/MotionStereoDepthDataSource.cs#L250
                depthWidth = image.width;
                depthHeight = image.height;
                UpdateCameraParams();

                int numPixels = depthWidth * depthHeight;
                Debug.Assert(image.planeCount == 1, "Plane count is not 1");
                stride = image.GetPlane(0).pixelStride;
                int numBytes = numPixels * stride;
                if (depthArrayLength != numBytes) {
                    depthArray = new byte[numBytes];
                    depthArrayLength = numBytes;
                }
                image.GetPlane(0).data.CopyTo(depthArray);
            }
        }
        else {
            m_RawImage.enabled = false;
            // return;
        }

        // Camera image
        int cameraPlanes = 0;
        if (m_CameraManager.TryAcquireLatestCpuImage(out XRCpuImage cameraImage))
        {
            using (cameraImage) {
                UpdateRawImage(m_RawCameraImage, cameraImage, false);
                cameraPlanes = cameraImage.planeCount;

                if (testingBool) {
                    testingBool = false;
                    Texture2D testTex = m_RawCameraImage.texture as Texture2D;
                    byte[] bytes = ImageConversion.EncodeToJPG(testTex);
                    // persistentDataPath directory: https://docs.unity3d.com/ScriptReference/Application-persistentDataPath.html
                    File.WriteAllBytes(Application.persistentDataPath + "/SavedScreen.jpg", bytes);

                    // NativeArray<byte> data = testTex.getRawTextureData<byte>();
                }
            }
        }
        
        // Display some distance info.
        m_StringBuilder.Clear();
        m_StringBuilder.AppendLine($"camera planes: {cameraPlanes}");
        m_StringBuilder.AppendLine($"width: {depthWidth}");
        m_StringBuilder.AppendLine($"width: {depthHeight}");
        m_StringBuilder.AppendLine($"focalLength: {focalLength}");
        m_StringBuilder.AppendLine($"principalPoint: {principalPoint}");
        // In portrait mode, (0.1, 0.1) is top right, (0.5, 0.5) is middle, (0.9, 0.9) is bottom left.
        // Phone orientation does not change coordinate locations on the screen.
        m_StringBuilder.AppendLine($"(0.1,0.1): {GetDepth(new Vector2(0.1f, 0.1f), depthArray, stride)}");
        m_StringBuilder.AppendLine($"(0.5,0.5): {GetDepth(new Vector2(0.5f, 0.5f), depthArray, stride)}");
        m_StringBuilder.AppendLine($"(0.9,0.9): {GetDepth(new Vector2(0.9f, 0.9f), depthArray, stride)}");

        m_StringBuilder.AppendLine($"{sensors.GPSstring()}");
        m_StringBuilder.AppendLine($"{sensors.IMUstring()}");
        LogText(m_StringBuilder.ToString());
    }

    // Log the given text to the screen if the image info UI is set. Otherwise, log the text to debug.
    void LogText(string text)
    {
        if (m_ImageInfo != null)
            m_ImageInfo.text = text;
        else
            Debug.Log(text);
    }

    private void UpdateRawImage(RawImage rawImage, XRCpuImage cpuImage, bool isDepth)
    {
        Debug.Assert(rawImage != null, "no raw image");

        // Get the texture associated with the UI.RawImage that we wish to display on screen.
        var texture = rawImage.texture as Texture2D;

        // If the texture hasn't yet been created, or if its dimensions have changed, (re)create the texture.
        // Note: Although texture dimensions do not normally change frame-to-frame, they can change in response to
        //    a change in the camera resolution (for camera images) or changes to the quality of the human depth
        //    and human stencil buffers.
        TextureFormat format = isDepth ? cpuImage.format.AsTextureFormat() : TextureFormat.RGBA32; // RGB24?
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

        // Make sure it's enabled.
        rawImage.enabled = true;


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

    // Obtain the depth value in meters. [u,v] should each range from 0 to 1. stride is the pixel stride of the acquired environment depth image.
    // This function is based on: https://github.com/googlesamples/arcore-depth-lab/blob/8f76532d4a67311463ecad6b88b3f815c6cf1eea/Assets/ARRealismDemos/OrientedReticle/Scripts/OrientedReticle.cs#L116
    // Further references:
    // https://developers.google.com/ar/develop/unity-arf/depth/developer-guide#extract_distance_from_a_depth_image
    // https://github.com/googlesamples/arcore-depth-lab/blob/8f76532d4a67311463ecad6b88b3f815c6cf1eea/Assets/ARRealismDemos/Common/Scripts/DepthSource.cs#L436
    // Another distance example: https://github.com/googlesamples/arcore-depth-lab/blob/8f76532d4a67311463ecad6b88b3f815c6cf1eea/Assets/ARRealismDemos/PointCloud/Scripts/RawPointCloudBlender.cs#L208
    public float GetDepth(Vector2 uv, byte[] arr, int stride)
    {
        if (arr.Length == 0)
            return float.NegativeInfinity;

        int x = (int)(uv.x * (depthWidth - 1));
        int y = (int)(uv.y * (depthHeight - 1));

        Debug.Assert(x >= 0 && x < depthWidth && y >= 0 && y < depthHeight, "Invalid depth index");

        // Depth in meters
        int index = (y * depthWidth) + x;

        // On an iPhone 12 Pro, the image data is in DepthFloat32 format, so we use ToSingle().
        // https://docs.unity3d.com/Packages/com.unity.xr.arsubsystems@4.1/api/UnityEngine.XR.ARSubsystems.XRCpuImage.Format.html
        // See the code in the following link if the XRCpuImage format is something different, e.g. DepthUint16.
        // https://forum.unity.com/threads/how-to-measure-distance-from-depth-map.1440799
        float depthM = BitConverter.ToSingle(arr, stride * index);

        if (depthM > 0) {
            // Here we are calculating the magnitude of a 3D point (vertex_x, -vertex_y, depthM). 
            float vertex_x = (x - principalPoint.x) * depthM / focalLength.x;
            float vertex_y = (y - principalPoint.y) * depthM / focalLength.y;
            return Mathf.Sqrt(vertex_x*vertex_x + vertex_y*vertex_y + depthM*depthM);
        }

        return float.NegativeInfinity;
    }

    // https://github.com/Unity-Technologies/arfoundation-samples/issues/266#issuecomment-523316133
    private static int GetRotation() => Screen.orientation switch
    {
        ScreenOrientation.Portrait => 90,
        ScreenOrientation.LandscapeLeft => 180,
        ScreenOrientation.PortraitUpsideDown => -90,
        ScreenOrientation.LandscapeRight => 0,
        _ => 90
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
}
