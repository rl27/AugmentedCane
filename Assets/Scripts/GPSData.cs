using System;
using System.Collections;
using UnityEngine;

// REFERENCES
// https://docs.unity3d.com/ScriptReference/LocationService.Start.html
// https://nosuchstudio.medium.com/how-to-access-gps-location-in-unity-521f1371a7e3
public class GPSData : MonoBehaviour
{
    private bool isRemote = true; // For using Unity Remote

    // GPS data
    [NonSerialized]
    public static LocationInfo gps;

    private float desiredAccuracyInMeters = 4f;
    private float updateDistanceInMeters = 3f;

    private float delay = 0.5f;

    private bool isStarting = false;
    private bool dataUpdating = false;

    private double lastUpdated = 0;
    private static Vector3 posAtLastUpdated;

    public static double degreeToMeter = 111139;

    void Update()
    {
        StartCoroutine(UpdateData());
    }

    // Update GPS data, or start location services if it hasn't been started
    public IEnumerator UpdateData() {
        // Exit if already updating the data
        if (dataUpdating)
            yield break;

        dataUpdating = true;
        if (Input.location.status == LocationServiceStatus.Running) {
            gps = Input.location.lastData;
            if (lastUpdated != gps.timestamp) {
                lastUpdated = gps.timestamp;
                posAtLastUpdated = DepthImage.position;
            }
        }
        else
            StartCoroutine(LocationStart());

        // Wait for a bit before trying to update again
        yield return new WaitForSeconds(delay);
        dataUpdating = false;
    }

    // Estimate location using AR camera position/rotation, true heading, and GPS.
    // Replace with geospatial?
    public static Navigation.Point EstimatedUserLocation()
    {
        Vector3 posDiff = DepthImage.position - posAtLastUpdated;
        float rot = DepthImage.rotation.y;
        float heading = SensorData.heading;
        float angleDiff = (heading - rot) * Mathf.Deg2Rad;
        float sin = Mathf.Sin(angleDiff);
        float cos = Mathf.Cos(angleDiff);
        return new Navigation.Point((double)(decimal) gps.latitude + (posDiff.z * cos - posDiff.x * sin) / degreeToMeter,
                                    (double)(decimal) gps.longitude + (posDiff.z * sin + posDiff.x * cos) / degreeToMeter);
    }

    // Start location services
    public IEnumerator LocationStart() {
        if (isStarting)
            yield break;

        isStarting = true;

#if UNITY_EDITOR
        if (isRemote) {
            yield return new WaitForSecondsRealtime(5f); // Need to add delay for Unity Remote to work
            yield return new WaitWhile(() => !UnityEditor.EditorApplication.isRemoteConnected);
        }
#elif UNITY_ANDROID
        // https://forum.unity.com/threads/runtime-permissions-do-not-work-for-gps-location-first-two-runs.1005001
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation);

        if (!Input.location.isEnabledByUser) {
            Debug.LogFormat("Android location not enabled");
            isStarting = false;
            yield break;
        }
#elif UNITY_IOS
        if (!Input.location.isEnabledByUser) {
            Debug.LogFormat("iOS location not enabled");
            isStarting = false;
            yield break;
        }
#endif

        // Start service before querying location
        // Start(desiredAccuracyInMeters, updateDistanceInMeters)
        // Default values: 10, 10
        Input.location.Start(desiredAccuracyInMeters, updateDistanceInMeters);
                
        // Wait until service initializes
        int maxWait = 15;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0) {
            yield return new WaitForSecondsRealtime(1);
            maxWait--;
        }

        // Editor has a bug which doesn't set the service status to Initializing. So extra wait in Editor.
#if UNITY_EDITOR
        int editorMaxWait = 15;
        while (Input.location.status == LocationServiceStatus.Stopped && editorMaxWait > 0) {
            yield return new WaitForSecondsRealtime(1);
            editorMaxWait--;
        }
#endif

        // Service didn't initialize in 15 seconds
        if (maxWait < 1) {
            Debug.Log("Timed out");
            isStarting = false;
            yield break;
        }

        if (Input.location.status != LocationServiceStatus.Running) {
            Debug.LogFormat("Unable to determine device location. Failed with status {0}", Input.location.status);
            isStarting = false;
            yield break;
        }

        isStarting = false;
    }

    // Stop location services
    public IEnumerator LocationStop() {
        Input.location.Stop();
        yield return null;
    }

    // Format GPS data into string
    public string GPSstring() {
        // return string.Format("Latitude: {0} \nLongitude: {1} \nAltitude: {2} \nHorizontal accuracy: {3} \nTimestamp: {4}", gps.latitude, gps.longitude, gps.altitude, gps.horizontalAccuracy, DateTimeOffset.FromUnixTimeSeconds((long) gps.timestamp));
        // return string.Format("GPS last update: {0} \nLatitude: {1} \nLongitude: {2} \nHorizontal accuracy: {3}",
        //     DateTimeOffset.FromUnixTimeSeconds((long) gps.timestamp).LocalDateTime.TimeOfDay, gps.latitude.ToString("R"), gps.longitude.ToString("R"), gps.horizontalAccuracy);
        return string.Format("GPS last updated: {0} \nAccuracy: {1}m \nLat/Lng: {2}, {3} \nEst. loc: {4}\n",
            DateTimeOffset.FromUnixTimeSeconds((long) gps.timestamp).LocalDateTime.TimeOfDay, gps.horizontalAccuracy.ToString("F2"), gps.latitude.ToString("F7"), gps.longitude.ToString("F7"), EstimatedUserLocation());
    }
}
