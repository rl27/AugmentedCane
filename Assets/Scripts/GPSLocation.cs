using System;
using System.Collections;
using UnityEngine;

// REFERENCES
// https://docs.unity3d.com/ScriptReference/LocationService.Start.html
// https://nosuchstudio.medium.com/how-to-access-gps-location-in-unity-521f1371a7e3
public class GPSLocation : MonoBehaviour
{
    private bool test = true;
    private bool isRemote = true; // For Unity Remote
    private bool continuousUse = true;
    private LocationInfo data;

    void Update() {
        if (test && !continuousUse) {
            test = false;
            StartCoroutine(LocationCoroutine());
        }
        else
            StartCoroutine(LocationCoroutine());
    }

    public IEnumerator LocationCoroutine() {

#if UNITY_EDITOR
        if (isRemote) {
            yield return new WaitForSecondsRealtime(5f); // Need to add delay for Unity Remote to work
            yield return new WaitWhile(() => !UnityEditor.EditorApplication.isRemoteConnected);
        }
#elif UNITY_ANDROID
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.CoarseLocation))
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.CoarseLocation);

        if (!UnityEngine.Input.location.isEnabledByUser) {
            Debug.LogFormat("Android location not enabled");
            yield break;
        }
#elif UNITY_IOS
        if (!UnityEngine.Input.location.isEnabledByUser) {
            Debug.LogFormat("iOS location not enabled");
            yield break;
        }
#endif

        // Start service before querying location
        // Start(desiredAccuracyInMeters, updateDistanceInMeters)
        // Default values: 10, 10
        UnityEngine.Input.location.Start(2f, 2f);
                
        // Wait until service initializes
        int maxWait = 15;
        while (UnityEngine.Input.location.status == LocationServiceStatus.Initializing && maxWait > 0) {
            yield return new WaitForSecondsRealtime(1);
            maxWait--;
        }

        // Editor has a bug which doesn't set the service status to Initializing. So extra wait in Editor.
#if UNITY_EDITOR
        int editorMaxWait = 15;
        while (UnityEngine.Input.location.status == LocationServiceStatus.Stopped && editorMaxWait > 0) {
            yield return new WaitForSecondsRealtime(1);
            editorMaxWait--;
        }
#endif

        // Service didn't initialize in 15 seconds
        if (maxWait < 1) {
            Debug.Log("Timed out");
            yield break;
        }

        // Connection has failed
        if (UnityEngine.Input.location.status != LocationServiceStatus.Running) {
            Debug.LogFormat("Unable to determine device location. Failed with status {0}", UnityEngine.Input.location.status);
            yield break;
        }
        else {
            data = UnityEngine.Input.location.lastData;
        }

        // Stop service if there is no need to query location updates continuously
        if (!continuousUse)
            UnityEngine.Input.location.Stop();
    }

    public string dataString() {
        return string.Format("Latitude: {0} \nLongitude: {1} \nAltitude: {2} \nHorizontal accuracy: {3} \nTimestamp: {4}", data.latitude, data.longitude, data.altitude, data.horizontalAccuracy, DateTimeOffset.FromUnixTimeSeconds((long)data.timestamp));
    }
}
