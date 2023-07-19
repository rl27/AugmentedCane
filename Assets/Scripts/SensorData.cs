using System;
using System.Collections;
using UnityEngine;

// REFERENCES
// https://docs.unity3d.com/ScriptReference/LocationService.Start.html
// https://nosuchstudio.medium.com/how-to-access-gps-location-in-unity-521f1371a7e3

// https://docs.unity3d.com/Manual/MobileInput.html
// https://docs.unity3d.com/ScriptReference/Gyroscope.html
// https://docs.unity3d.com/ScriptReference/Compass.html
public class SensorData : MonoBehaviour
{
    private bool isRemote = true; // For using Unity Remote

    // GPS data
    public LocationInfo gps;

    // IMU data
    public Vector3 accel;
    public Vector3 gyro;
    public Vector3 attitude;
    public Vector3 mag;
    public float heading;

    private float desiredAccuracyInMeters = 2f;
    private float updateDistanceInMeters = 2f;

    void Update() {
        UpdateData();
    }

    // Update GPS and IMU data, or start location services if it hasn't been started
    public void UpdateData() {
        if (Input.location.status == LocationServiceStatus.Running) {
            accel.x = -Input.acceleration.y;
            accel.y = Input.acceleration.z;
            accel.z = Input.acceleration.x;
            gyro = Input.gyro.rotationRateUnbiased;
            attitude = Input.gyro.attitude.eulerAngles;

            mag = Input.compass.rawVector;
            heading = Input.compass.trueHeading;

            gps = Input.location.lastData;
        }
        else
            StartCoroutine(LocationStart());
    }

    // Start location services
    public IEnumerator LocationStart() {
#if UNITY_EDITOR
        if (isRemote) {
            yield return new WaitForSecondsRealtime(5f); // Need to add delay for Unity Remote to work
            yield return new WaitWhile(() => !UnityEditor.EditorApplication.isRemoteConnected);
        }
#elif UNITY_ANDROID
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.CoarseLocation))
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.CoarseLocation);

        if (!Input.location.isEnabledByUser) {
            Debug.LogFormat("Android location not enabled");
            yield break;
        }
#elif UNITY_IOS
        if (!Input.location.isEnabledByUser) {
            Debug.LogFormat("iOS location not enabled");
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
            yield break;
        }

        if (Input.location.status != LocationServiceStatus.Running) {
            Debug.LogFormat("Unable to determine device location. Failed with status {0}", Input.location.status);
            yield break;
        }
        else {
            Input.gyro.enabled = true;
            Input.gyro.updateInterval = 0.1f;
            Input.compass.enabled = true;
        }
    }

    // Stop location services
    public IEnumerator LocationStop() {
        Input.location.Stop();
        Input.gyro.enabled = false;
        Input.compass.enabled = false;
        yield return null;
    }

    // Format GPS data into string
    public string GPSstring() {
        // return string.Format("Latitude: {0} \nLongitude: {1} \nAltitude: {2} \nHorizontal accuracy: {3} \nTimestamp: {4}", gps.latitude, gps.longitude, gps.altitude, gps.horizontalAccuracy, DateTimeOffset.FromUnixTimeSeconds((long) gps.timestamp));
        return string.Format("GPS last update: {0} \nLatitude: {1} \nLongitude: {2} \nHorizontal accuracy: {3}",
            DateTimeOffset.FromUnixTimeSeconds((long) gps.timestamp).LocalDateTime.TimeOfDay, gps.latitude, gps.longitude, gps.horizontalAccuracy);
    }

    // Format IMU data into string
    public string IMUstring() {
        // return string.Format("Accel: {0} \nGyro: {1} \nMag: {2} \nAttitude: {3} \nHeading: {4}", accel, gyro, mag, attitude, heading);
        return string.Format("Attitude: {0} \nHeading: {1}", attitude, heading);
    }
}
