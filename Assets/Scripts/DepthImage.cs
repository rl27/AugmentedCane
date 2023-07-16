using System.Collections;
using System.Collections.Generic;
// using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// References:
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

    // The display rotation matrix for the shader.
    Matrix4x4 m_DisplayRotationMatrix = Matrix4x4.identity;

#if UNITY_ANDROID
        // A matrix to flip the Y coordinate for the Android platform.
        Matrix4x4 k_AndroidFlipYMatrix = Matrix4x4.identity;
#endif

        void Awake()
        {
#if UNITY_ANDROID
            k_AndroidFlipYMatrix[1,1] = -1.0f;
            k_AndroidFlipYMatrix[2,1] = 1.0f;
#endif
        }

    void OnEnable()
    {
        Debug.Assert(m_CameraManager != null, "no camera manager");
        m_CameraManager.frameReceived += OnCameraFrameEventReceived;
        m_DisplayRotationMatrix = Matrix4x4.identity;
        m_RawImage.material = m_DepthMaterial;
    }

    // Update is called once per frame
    void Update()
    {
        Debug.Assert(m_OcclusionManager != null, "no occlusion manager");

        // Check if device supports environment depth.
        var descriptor = m_OcclusionManager.descriptor;
        if (descriptor.environmentDepthImageSupported == Supported.Supported) {
            LogText("Environment depth is supported!");
        }
        else {
            if (descriptor == null || descriptor.environmentDepthImageSupported == Supported.Unsupported)
                LogText("Environment depth is not supported on this device.");
            else if (descriptor.environmentDepthImageSupported == Supported.Unknown)
                LogText("Determining environment depth support...");
            m_RawImage.texture = null;
            return;
        }

        // Acquire a depth image and update the displayed image.
        if (occlusionManager.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage image)) {
            using (image) {
                UpdateRawImage(m_RawImage, image);
                // LogText(image.ToString());
            }
        }
        else
            rawImage.enabled = false;
    }

    // Log the given text to the screen if the image info UI is set. Otherwise, log the string to debug.
    void LogText(string text)
    {
        if (m_ImageInfo != null)
            m_ImageInfo.text = text;
        else
            Debug.Log(text);
    }

    private static void UpdateRawImage(RawImage rawImage, XRCpuImage cpuImage)
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
            texture = new Texture2D(cpuImage.width, cpuImage.height, cpuImage.format.AsTextureFormat(), false);
            rawImage.texture = texture;
        }

        // For display, we need to mirror about the vertical access.
        var conversionParams = new XRCpuImage.ConversionParams(cpuImage, cpuImage.format.AsTextureFormat(), XRCpuImage.Transformation.MirrorY);

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
    }

    // https://github.com/Unity-Technologies/arfoundation-samples/issues/266#issuecomment-523316133
    static int GetRotation() => Screen.orientation switch
    {
        ScreenOrientation.Portrait => 90,
        ScreenOrientation.LandscapeLeft => 180,
        ScreenOrientation.PortraitUpsideDown => -90,
        ScreenOrientation.LandscapeRight => 0,
        _ => 90
    };

    void OnCameraFrameEventReceived(ARCameraFrameEventArgs cameraFrameEventArgs)
        {
            Debug.Assert(m_RawImage != null, "no raw image");
            if (m_RawImage.material != null)
            {
                // Copy the display rotation matrix from the camera.
                Matrix4x4 cameraMatrix = cameraFrameEventArgs.displayMatrix ?? Matrix4x4.identity;

                Vector2 affineBasisX = new Vector2(1.0f, 0.0f);
                Vector2 affineBasisY = new Vector2(0.0f, 1.0f);
                Vector2 affineTranslation = new Vector2(0.0f, 0.0f);
#if UNITY_IOS
                affineBasisX = new Vector2(cameraMatrix[0, 0], cameraMatrix[1, 0]);
                affineBasisY = new Vector2(cameraMatrix[0, 1], cameraMatrix[1, 1]);
                affineTranslation = new Vector2(cameraMatrix[2, 0], cameraMatrix[2, 1]);
#endif
#if UNITY_ANDROID
                affineBasisX = new Vector2(cameraMatrix[0, 0], cameraMatrix[0, 1]);
                affineBasisY = new Vector2(cameraMatrix[1, 0], cameraMatrix[1, 1]);
                affineTranslation = new Vector2(cameraMatrix[0, 2], cameraMatrix[1, 2]);
#endif

                // The camera display matrix includes scaling and offsets to fit the aspect ratio of the device. In most
                // cases, the camera display matrix should be used directly without modification when applying depth to
                // the scene because that will line up the depth image with the camera image. However, for this demo,
                // we want to show the full depth image as a picture-in-picture, so we remove these scaling and offset
                // factors while preserving the orientation.
                affineBasisX = affineBasisX.normalized;
                affineBasisY = affineBasisY.normalized;
                m_DisplayRotationMatrix = Matrix4x4.identity;
                m_DisplayRotationMatrix[0,0] = affineBasisX.x;
                m_DisplayRotationMatrix[0,1] = affineBasisY.x;
                m_DisplayRotationMatrix[1,0] = affineBasisX.y;
                m_DisplayRotationMatrix[1,1] = affineBasisY.y;
                m_DisplayRotationMatrix[2,0] = Mathf.Round(affineTranslation.x);
                m_DisplayRotationMatrix[2,1] = Mathf.Round(affineTranslation.y);

#if UNITY_ANDROID
                m_DisplayRotationMatrix = k_AndroidFlipYMatrix * m_DisplayRotationMatrix;
#endif

                Quaternion rotation = Quaternion.Euler(0, 0, GetRotation());
                Matrix4x4 rotMatrix = Matrix4x4.Rotate(rotation);
                m_RawImage.material.SetMatrix(Shader.PropertyToID("_DisplayRotationPerFrame"), rotMatrix);
                // Set the matrix to the raw image material.
                // m_RawImage.material.SetMatrix(Shader.PropertyToID("_DisplayRotationPerFrame"), m_DisplayRotationMatrix);
            }
        }
}
