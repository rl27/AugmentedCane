using System;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

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
    GameObject XR;
    PointCloud pc;

    bool depthActive = true;
    bool IMUActive = true;
    bool GPSActive = true;
    bool pcActive = true;

    List<double> gpsCoords = new List<double>();
    List<byte[]> depthArrays = new List<byte[]>();
    Dictionary<string, dynamic> log = new Dictionary<string, dynamic>();

    private DateTime gpsLastLog;
    private float gpsLogInterval = 0.2f;

    void Awake()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep; // Prevent phone from dimming/sleeping

        depth = DepthHandler.GetComponent<DepthImage>();
        sensors = SensorHandler.GetComponent<SensorData>();
        gps = GPSHandler.GetComponent<GPSData>();
        pc = XR.GetComponent<PointCloud>();

        DepthHandler.SetActive(depthActive);
        SensorHandler.SetActive(IMUActive);
        GPSHandler.SetActive(GPSActive);
        pc.enabled = pcActive;
        XR.GetComponent<ARPointCloudManager>().enabled = pcActive;

        TestCMAES();
    }

    private double[] bestVector = null;
    private double bestValue = double.MaxValue;
    private void TestCMAES()
    {
        double[] x = new double[] { 0, 0 };
        CMAES cmaoptimizer = new CMAES(x, 1.5);

        bool converged = false;

        string means = "";
        string sigmas = "";
        for (int i = 0; i < 1000; i++) {
            double output = TestFunction(x);
            if (output < bestValue) {
                bestValue = output;
                bestVector = x;
            }
            (x, converged) = cmaoptimizer.Optimize(x, output);
            means += cmaoptimizer.cma._mean[0] + ", " + cmaoptimizer.cma._mean[1] + ", ";
            sigmas += cmaoptimizer.cma._sigma + ", ";
            if (converged) break;
        }

        // Debug.Log(bestVector[0]);
        // Debug.Log(bestVector[1]);
        // Debug.Log(bestValue);
        Debug.Log(means);
        Debug.Log(sigmas);
    }

    private static double TestFunction(double[] x)
    {
        return Math.Pow(x[0] - 3, 2) + Math.Pow(10 * (x[1] + 2), 2);
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
        Application.targetFrameRate = 30; // Must be done in Update(). Doing this in Start() makes it not work for mobile devices.
        // QualitySettings.vSyncCount = 0;

        if ((DateTime.Now - gpsLastLog).TotalSeconds > gpsLogInterval) {
            Navigation.Point loc = GPSData.EstimatedUserLocation();
            gpsCoords.Add(loc.lat);
            gpsCoords.Add(loc.lng);
            gpsLastLog = DateTime.Now;
        }

        m_StringBuilder.Clear();
        m_StringBuilder.AppendLine($"FPS: {(int)(1.0f / Time.smoothDeltaTime)}\n");

        m_StringBuilder.AppendLine($"{Math.Round(Vision.direction, 1)}°, {Math.Round(Vision.relativeDir, 1)}°, {Vision.logging}\n");

        if (GPSActive) {
            // m_StringBuilder.AppendLine($"{gps.GPSstring()}");
            m_StringBuilder.AppendLine($"{Navigation.info}\n");
        }
        if (IMUActive)
            m_StringBuilder.AppendLine($"{sensors.IMUstring()}");
        if (depthActive)
            m_StringBuilder.AppendLine($"{depth.m_StringBuilder.ToString()}");
        if (pcActive)
            m_StringBuilder.AppendLine($"{pc.info}");

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
