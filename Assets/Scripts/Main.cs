using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Main : MonoBehaviour
{
    public static float timeInFrame = 0;
    private float timeAtStart = 0;

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

    private double[] original = { DepthImage.distanceToObstacle, DepthImage.personRadius, DepthImage.collisionAudioMinRate, DepthImage.collisionAudioCapDistance };
    private double[] lowerBounds = { 0.5, 0.1, 0.5, 0.1 };
    private double[] upperBounds = { 4, 0.7, 11, 3 };
    private int[,] caps = { { 3, 0 } }; // For each pair of values (A,B), the value in index A should be capped by the value in index B

    private double[] x;

    private double[] bestVector = null;
    private double bestValue = double.MaxValue;

    private string cmaesPath;

    [Serializable]
    public class Response
    {
        public double? sample;
        public double[][] inputs;
        public double[] outputs;
        public double[] prms; // params
    }

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
            cmaoptimizer = new CMAES(x, 0.2, Normalize(lowerBounds), Normalize(upperBounds));
        }
        else {
            cmaoptimizer = BinarySerialization.ReadFromBinaryFile<CMAES>(cmaesPath);
            x = cmaoptimizer.ask;
            CapX();
            SetParams();
        }
    }

    // Use sample for CMA generation
    private void CMAGenerate(double output)
    {
        if (output < bestValue) {
            bestValue = output;
            bestVector = x;
        }
        x = cmaoptimizer.Optimize(x, output, true);
        CapX();
        SetParams();
    }

    private void CapX()
    {
        for (int i = 0; i < caps.GetLength(0); i++) {
            x[caps[i,0]] = Math.Min(x[caps[i,0]], x[caps[i,1]]);
        }
    }

    private void SetParams()
    {
        double[] x2 = Denormalize(x);
        DepthImage.distanceToObstacle = (float) x2[0];
        DepthImage.personRadius = (float) x2[1];
        DepthImage.collisionAudioMinRate = (float) x2[2];
        DepthImage.collisionAudioCapDistance = (float) x2[3];
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
        string outStr = "";
        for (int i = 0; i < v.Length; i++) {
            outStr += v[i].ToString("F2") + " ";
        }
        return outStr;
    }

    // Enter a sample for CMA-ES
    public void OnSampleEntered(string input)
    {
        double output;
        if (double.TryParse(input, out output)) {
            CMAGenerate(output);
            StartCoroutine(SendLatestData());
        }
    }

    // Comma-separated params entered
    public void OnParamsEntered(string input)
    {
        string[] splits = input.Split(' ');
        if (splits.Length != original.Length)
            return;
        double[] ps = new double[original.Length];
        for (int i = 0; i < original.Length; i++) {
            if (!double.TryParse(splits[i], out ps[i]))
                return;
        }
        OnParamsEntered(ps);
    }

    void OnParamsEntered(double[] ps)
    {
        x = Normalize(ps);
        SetParams();
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
        timeAtStart = Time.realtimeSinceStartup;
        Application.targetFrameRate = 30; // Must be done in Update(). Doing this in Start() makes it not work for mobile devices.
        // QualitySettings.vSyncCount = 0;

        StartCoroutine(Retrieve());

        // if ((DateTime.Now - gpsLastLog).TotalSeconds > gpsLogInterval) {
        //     Navigation.Point loc = GPSData.EstimatedUserLocation();
        //     gpsCoords.Add(loc.lat);
        //     gpsCoords.Add(loc.lng);
        //     gpsLastLog = DateTime.Now;
        // }

        m_StringBuilder.Clear();
        m_StringBuilder.AppendLine($"FPS: {Convert.ToInt32(1.0 / Time.unscaledDeltaTime)}\n");

        if (Vision.doSidewalkDirection)
            m_StringBuilder.AppendLine($"Sidewalk: {Vision.direction.ToString("F1")}°, {Vision.relativeDir.ToString("F1")}°, {Vision.logging}\n");

        if (GPSActive) {
            m_StringBuilder.AppendLine($"{gps.GPSstring()}");
            if (Navigation.initialized) {
                m_StringBuilder.AppendLine($"{Navigation.info}\n");
                m_StringBuilder.AppendLine($"{Navigation.intersectionStringBuilder.ToString()}\n");
            }
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
        m_StringBuilder.AppendLine($"Sigma: {cmaoptimizer.cma._sigma.ToString("F3")}");

        LogText(m_StringBuilder.ToString());
    }

    void LateUpdate()
    {
        timeInFrame = Time.realtimeSinceStartup - timeAtStart;
    }

    // Log the given text to the screen if the image info UI is set. Otherwise, log the text to debug.
    void LogText(string text)
    {
        if (m_ImageInfo != null)
            m_ImageInfo.text = text;
        else
            Debug.Log(text);
    }

    private bool working = false;
    private IEnumerator Retrieve()
    {
        if (working) yield break;
        working = true;

        string url = "raymondl.pythonanywhere.com/retrieve";

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "GET"))
        {
            webRequest.downloadHandler = (DownloadHandler) new DownloadHandlerBuffer();
            yield return webRequest.SendWebRequest();
            if (WebClient.checkStatus(webRequest, url.Split('/'))) {
                try {
                    Response data = JsonConvert.DeserializeObject<Response>(webRequest.downloadHandler.text);
                    
                    if (data.sample != null) {
                        OnSampleEntered(data.sample.ToString());
                    }
                    else if (data.inputs != null && data.outputs != null) {
                        ResetButtonPress();
                        int len = data.inputs.Length;
                        for (int i = 0; i < len; i++) {
                            x = data.inputs[i];
                            x = cmaoptimizer.Optimize(x, data.outputs[i], i == len - 1);
                        }
                        CapX();
                        SetParams();
                    }
                    else if (data.prms != null) {
                        OnParamsEntered(data.prms);
                    }
                }
                catch (Exception e) {
                    Debug.Log(e);
                }
            }
        }

        yield return new WaitForSeconds(5f);
        working = false;
    }

    private IEnumerator SendLatestData()
    {
        Dictionary<string, dynamic> newData = new Dictionary<string, dynamic>();
        newData["inputs"] = cmaoptimizer.inputs[cmaoptimizer.inputs.Count - 1];
        newData["outputs"] = cmaoptimizer.outputs[cmaoptimizer.outputs.Count - 1];
        newData["means"] = cmaoptimizer.means[cmaoptimizer.means.Count - 1];
        newData["sigmas"] = cmaoptimizer.sigmas[cmaoptimizer.sigmas.Count - 1];

        string url = "raymondl.pythonanywhere.com/append";
        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {
            byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(JsonConvert.SerializeObject(newData));
            webRequest.uploadHandler = (UploadHandler) new UploadHandlerRaw(jsonToSend);
            webRequest.downloadHandler = (DownloadHandler) new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            yield return webRequest.SendWebRequest();
            if (WebClient.checkStatus(webRequest, url.Split('/'))) {
                Debug.Log("log success");
            }
        }
    }
}
