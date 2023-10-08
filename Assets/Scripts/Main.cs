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

    // private DateTime gpsLastLog;
    // private float gpsLogInterval = 0.2f;

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

        cmaoptimizer = new CMAES(x, 0.3, lowerBounds, upperBounds);
    }

    CMAES cmaoptimizer;
    // distanceToObstacle, halfPersonWidth, collisionSumThreshold, collisionAudioDelay
    // 2.5, 0.3, 1.1, 0.2
    private double[] x = new double[] { DepthImage.distanceToObstacle, DepthImage.halfPersonWidth, DepthImage.collisionSumThreshold, DepthImage.collisionAudioDelay };
    private double[] lowerBounds = new double[] {0.5, 0.01, 0.01, 0};
    private double[] upperBounds = new double[] {4, 0.7, 80, 1};

    private double[] bestVector = null;
    private double bestValue = double.MaxValue;

    private void SetParams()
    {
        DepthImage.distanceToObstacle = (float)x[0];
        DepthImage.halfPersonWidth = (float)x[1];
        DepthImage.collisionSumThreshold = (float)x[2];
        DepthImage.collisionAudioDelay = (float)x[3];
    }

    // Use sample for CMA generation
    // Returns true if CMA has converged
    private bool CMAGenerate(double output)
    {
        bool converged = false;
        if (output < bestValue) {
            bestValue = output;
            bestVector = x;
        }
        (x, converged) = cmaoptimizer.Optimize(x, output);
        SetParams();

        return converged;
    }

    // Enter a sample for CMA-ES
    public void OnSampleEntered(string input)
    {
        float output;
        if (float.TryParse(input, out output)) {
            // SAVE X, OUTPUT, MEAN, SIGMA HERE

            CMAGenerate(output);
        }
    }

    // Comma-separated params entered
    public void OnParamsEntered(string input)
    {
        string[] splits = input.Split(',');
        if (splits.Length != 4)
            return;
        float p0, p1, p2, p3;
        if (float.TryParse(splits[0], out p0) &&
            float.TryParse(splits[1], out p1) &&
            float.TryParse(splits[2], out p2) &&
            float.TryParse(splits[3], out p3)) {
            DepthImage.distanceToObstacle = p0;
            DepthImage.halfPersonWidth = p1;
            DepthImage.collisionSumThreshold = p2;
            DepthImage.collisionAudioDelay = p3;
        }
    }

    public void LogButtonPress()
    {   
        // depthArrays.Add(DepthImage.depthArray);
        // log["depth"] = depthArrays;
        // log["model"] = SystemInfo.deviceModel;
        // log["gps"] = gpsCoords;
        log["params"] = x;
        StartCoroutine(WebClient.SendLogData(log));
    }

    // Update is called once per frame
    void Update()
    {
        Application.targetFrameRate = 30; // Must be done in Update(). Doing this in Start() makes it not work for mobile devices.
        // QualitySettings.vSyncCount = 0;

        // if ((DateTime.Now - gpsLastLog).TotalSeconds > gpsLogInterval) {
        //     Navigation.Point loc = GPSData.EstimatedUserLocation();
        //     gpsCoords.Add(loc.lat);
        //     gpsCoords.Add(loc.lng);
        //     gpsLastLog = DateTime.Now;
        // }

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
