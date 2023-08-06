using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class Main : MonoBehaviour
{
    // StringBuilder for building strings to be logged.
    readonly StringBuilder m_StringBuilder = new StringBuilder();

    // UI Text used to display information about the image on screen.
    public Text imageInfo {
        get => m_ImageInfo;
        set => m_ImageInfo = value;
    }
    [SerializeField]
    Text m_ImageInfo;

    [SerializeField]
    GameObject DepthHandler;
    DepthImage depth;

    [SerializeField]
    GameObject SensorHandler;
    SensorData sensors;

    [SerializeField]
    GameObject GPSHandler;
    GPSData gps;

    [SerializeField]
    GameObject PointCloudHandler;
    PointCloud pc;

    [SerializeField]
    GameObject PlaneHandler;
    Plane plane;

    bool depthActive = true;
    bool IMUActive = true;
    bool GPSActive = true;
    bool pcActive = false;
    bool planeActive = false;

    void Awake()
    {
        depth = DepthHandler.GetComponent<DepthImage>();
        sensors = SensorHandler.GetComponent<SensorData>();
        gps = GPSHandler.GetComponent<GPSData>();
        pc = PointCloudHandler.GetComponent<PointCloud>();
        plane = PlaneHandler.GetComponent<Plane>();

        DepthHandler.SetActive(depthActive);
        SensorHandler.SetActive(IMUActive);
        GPSHandler.SetActive(GPSActive);
        PointCloudHandler.SetActive(pcActive);
        PlaneHandler.SetActive(planeActive);
    }

    // Update is called once per frame
    void Update()
    {
        // Display some info.
        m_StringBuilder.Clear();
        m_StringBuilder.AppendLine($"FPS: {(int)(1.0f / Time.smoothDeltaTime)}");

        if (depthActive)
            m_StringBuilder.AppendLine($"{depth.m_StringBuilder.ToString()}");
        if (IMUActive)
            m_StringBuilder.AppendLine($"{sensors.IMUstring()}");
        if (GPSActive)
            m_StringBuilder.AppendLine($"{gps.GPSstring()}");
        if (pcActive)
            m_StringBuilder.AppendLine($"{pc.info}");
        if (planeActive)
            m_StringBuilder.AppendLine($"{plane.info}");

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
}
