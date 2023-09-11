using System;
using System.Text;
using System.Collections.Generic;
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

    List<double> gpsCoords = new List<double>();
    List<byte[]> depthArrays = new List<byte[]>();
    Dictionary<string, dynamic> log = new Dictionary<string, dynamic>();

    private DateTime gpsLastLog;
    private float gpsLogInterval = 0.2f;

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

    public void LogButtonPress()
    {   
        // depthArrays.Add(DepthImage.depthArray);
        // log["depth"] = depthArrays;
        log["model"] = SystemInfo.deviceModel;
        log["gps"] = gpsCoords;
        StartCoroutine(WebClient.SendLogData(log));
    }

    // Update is called once per frame
    void Update()
    {
        if ((DateTime.Now - gpsLastLog).TotalSeconds > gpsLogInterval) {
            Navigation.Point loc = GPSData.EstimatedUserLocation();
            gpsCoords.Add(loc.lat);
            gpsCoords.Add(loc.lng);
            gpsLastLog = DateTime.Now;
        }

        m_StringBuilder.Clear();
        Debug.Log((int)(1.0f / Time.smoothDeltaTime));
        m_StringBuilder.AppendLine($"FPS: {(int)(1.0f / Time.smoothDeltaTime)}");

        if (depthActive)
            m_StringBuilder.AppendLine($"{depth.m_StringBuilder.ToString()}");
        if (IMUActive)
            m_StringBuilder.AppendLine($"{sensors.IMUstring()}");
        if (GPSActive) {
            m_StringBuilder.AppendLine($"{gps.GPSstring()}");
            m_StringBuilder.AppendLine($"{Navigation.info}");
        }
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
