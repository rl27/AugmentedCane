using System;
using System.IO;
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

    CMAES cmaoptimizer;

    // distanceToObstacle, halfPersonWidth, collisionSumThreshold, collisionAudioDelay
    // 2.5, 0.3, 1.1, 0.2
    private double[] original = new double[] { DepthImage.distanceToObstacle, DepthImage.halfPersonWidth, DepthImage.collisionSumThreshold, DepthImage.collisionAudioDelay };
    private double[] x;
    private double[] lowerBounds = new double[] {0.5, 0.01, 0.01, 0};
    private double[] upperBounds = new double[] {4, 0.7, 80, 1};

    private double[] bestVector = null;
    private double bestValue = double.MaxValue;

    private string cmaesPath;

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

        cmaesPath = Path.Combine(Application.persistentDataPath, "cmaes.bin");
        if (!File.Exists(cmaesPath)) {
            x = Normalize(original);
            cmaoptimizer = new CMAES(x, 0.3, Normalize(lowerBounds), Normalize(upperBounds));
        }
        else {
            cmaoptimizer = BinarySerialization.ReadFromBinaryFile<CMAES>(cmaesPath);
            x = cmaoptimizer.ask;
            SetParams();
        }
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

    private void SetParams()
    {
        double[] x2 = Denormalize(x);
        DepthImage.distanceToObstacle = (float) x2[0];
        DepthImage.halfPersonWidth = (float) x2[1];
        DepthImage.collisionSumThreshold = (float) x2[2];
        DepthImage.collisionAudioDelay = (float) x2[3];
    }

    private double[] Normalize(double[] v)
    {
        double[] v2 = new double[v.Length];
        for (int i = 0; i < v.Length; i++)
            v2[i] = v[i] / upperBounds[i];
        return v2;
    }
    private double[] Denormalize(double[] v)
    {
        double[] v2 = new double[v.Length];
        for (int i = 0; i < v.Length; i++)
            v2[i] = v[i] * upperBounds[i];
        return v2;
    }

    private string GetString(double[] v) {
        return Math.Round(v[0],2) + " " + Math.Round(v[1],2) + " " + Math.Round(v[2],2) + " " + Math.Round(v[3],2);
    }

    // Enter a sample for CMA-ES
    public void OnSampleEntered(string input)
    {
        float output;
        if (float.TryParse(input, out output)) {
            CMAGenerate(output);
        }
    }

    // Comma-separated params entered
    public void OnParamsEntered(string input)
    {
        string[] splits = input.Split(' ');
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
        log["inputs"] = cmaoptimizer.inputs;
        log["outputs"] = cmaoptimizer.outputs;
        log["means"] = cmaoptimizer.means;
        log["sigmas"] = cmaoptimizer.sigmas;
        StartCoroutine(WebClient.SendLogData(log));
    }

    public void ResetButtonPress()
    {
        File.Delete(cmaesPath);
        x = Normalize(original);
        cmaoptimizer = new CMAES(x, 0.3, Normalize(lowerBounds), Normalize(upperBounds));
        SetParams();
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

        m_StringBuilder.AppendLine($"{Math.Round(Vision.direction, 1)}°, {Math.Round(Vision.relativeDir, 1)}°, {Vision.logging}");

        if (GPSActive) {
            // m_StringBuilder.AppendLine($"{gps.GPSstring()}");
            m_StringBuilder.AppendLine($"{Navigation.info}\n");
        }
        if (IMUActive)
            m_StringBuilder.AppendLine($"{sensors.IMUstring()}");
        if (depthActive)
            m_StringBuilder.AppendLine($"{depth.m_StringBuilder.ToString()}");
        if (pcActive)
            m_StringBuilder.AppendLine($"{pc.info}\n");
        
        m_StringBuilder.AppendLine($"Gen {cmaoptimizer.cma.Generation}, sample {cmaoptimizer.solutions.Count}");
        m_StringBuilder.AppendLine($"Params: {GetString(Denormalize(x))}");
        m_StringBuilder.AppendLine($"Mean: {GetString(Denormalize(cmaoptimizer.cma._mean.ToArray()))}");
        m_StringBuilder.AppendLine($"Sigma: {Math.Round(cmaoptimizer.cma._sigma,3)}");

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
