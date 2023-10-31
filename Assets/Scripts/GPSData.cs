using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;

// REFERENCES
// https://docs.unity3d.com/ScriptReference/LocationService.Start.html
// https://nosuchstudio.medium.com/how-to-access-gps-location-in-unity-521f1371a7e3
[RequireComponent(typeof(AREarthManager))]
public class GPSData : MonoBehaviour
{
    public ARCoreExtensions arCoreExtensions;
    private static LocationInfo gps;
    public static GeospatialPose pose;
    public static float eunHeading;

    private float desiredAccuracyInMeters = 3f;
    private float updateDistanceInMeters = 3f;

    private float delay = 0.5f;

    private bool isStarting = false;
    private bool dataUpdating = false;

    private double lastUpdated = 0;
    private static Vector3 posAtLastUpdated;
    public static float headingAtLastUpdated;

    public static double degreeToMeter = 111139;

    private AREarthManager earthManager;
    FeatureSupported geospatialSupported = FeatureSupported.Unknown;
    bool checkingVPS = false;
    bool vpsAvailable = false;
    bool recheckVps = false;
    public static bool geospatial = false;

    void Start()
    {
        earthManager = GetComponent<AREarthManager>();
    }

    private IEnumerator VPSAvailabilityCheck()
    {
        if (checkingVPS) yield break;
        checkingVPS = true;

        while (gps.latitude == 0 && gps.longitude == 0) yield return null;

        var promise = AREarthManager.CheckVpsAvailabilityAsync(gps.latitude, gps.longitude);
        yield return promise;

        // https://developers.google.com/ar/reference/unity-arf/namespace/Google/XR/ARCoreExtensions#vpsavailability
        vpsAvailable = false;
        recheckVps = false;
        if (promise.Result == VpsAvailability.Available)
            vpsAvailable = true;
        else if (promise.Result == VpsAvailability.Unknown ||
                 promise.Result == VpsAvailability.ErrorNetworkConnection ||
                 promise.Result == VpsAvailability.ErrorInternal)
            recheckVps = true;

        checkingVPS = false;
    }

    void Update()
    {
        if (geospatialSupported == FeatureSupported.Unknown) {
            geospatialSupported = earthManager.IsGeospatialModeSupported(GeospatialMode.Enabled);
            if (geospatialSupported == FeatureSupported.Supported) {
                arCoreExtensions.ARCoreExtensionsConfig.GeospatialMode = GeospatialMode.Enabled;
                StartCoroutine(VPSAvailabilityCheck());
            }
        }
        if (recheckVps) {
            StartCoroutine(VPSAvailabilityCheck());
        }
        StartCoroutine(UpdateData());
    }

    // Update GPS data, or start location services if it hasn't been started
    public IEnumerator UpdateData() {
        // Exit if already updating the data
        if (dataUpdating) yield break;
        dataUpdating = true;

        // Only do VPS if navigating
        if (Navigation.initialized && vpsAvailable) {
            if (earthManager.EarthTrackingState == TrackingState.Tracking) {
                pose = earthManager.CameraGeospatialPose;
                Quaternion q = pose.EunRotation;
                eunHeading = Mathf.Atan2(2*(q.y*q.w-q.x*q.z), 1-2*(q.y*q.y+q.z*q.z)) * Mathf.Rad2Deg;

                posAtLastUpdated = DepthImage.position;
                headingAtLastUpdated = DepthImage.rotation.y;
                geospatial = true;
                delay = 5f;
            }
        }
        else if (Input.location.status == LocationServiceStatus.Running) {
            gps = Input.location.lastData;
            if (lastUpdated != gps.timestamp) {
                lastUpdated = gps.timestamp;
                posAtLastUpdated = DepthImage.position;
            }
            geospatial = false;
            delay = 1f;
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
        double lat = geospatial ? pose.Latitude : Convert.ToDouble(gps.latitude.ToString("R"));
        double lng = geospatial ? pose.Longitude : Convert.ToDouble(gps.longitude.ToString("R"));
        Vector3 posDiff = DepthImage.position - posAtLastUpdated;
        float rot = DepthImage.rotation.y;
        float heading = SensorData.heading;
        float angleDiff = (heading - rot) * Mathf.Deg2Rad;
        float sin = Mathf.Sin(angleDiff);
        float cos = Mathf.Cos(angleDiff);
        // This ToString("R") thing is magic. Summons extra precision out of nowhere.
        // https://forum.unity.com/threads/precision-of-location-longitude-is-worse-when-longitude-is-beyond-100-degrees.133192/
        return new Navigation.Point(lat + (posDiff.z * cos - posDiff.x * sin) / degreeToMeter,
                                    lng + (posDiff.z * sin + posDiff.x * cos) / degreeToMeter);
    }

    // Start location services
    public IEnumerator LocationStart() {
        if (isStarting) yield break;
        isStarting = true;

#if UNITY_EDITOR
        // Uncomment if using Unity Remote
        // yield return new WaitForSecondsRealtime(5f); // Need to add delay for Unity Remote to work
        // yield return new WaitWhile(() => !UnityEditor.EditorApplication.isRemoteConnected);
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
        return string.Format("Geospatial: {0} \nVPS available: {1} \nUsing geo: {2} \nAcc: {3} \n", geospatialSupported.ToString(), vpsAvailable, geospatial, geospatial ? pose.HorizontalAccuracy.ToString("F2") : gps.horizontalAccuracy.ToString("F2"));
        // return string.Format("GPS last updated: {0} \nAccuracy: {1}m \nLat/Lng: {2}, {3} \nEst. loc: {4}\n Accuracy: {5}\n Lat/Lng: {6}, {7}\n Heading: {8}\n Heading acc: {9}\n",
        //     DateTimeOffset.FromUnixTimeSeconds((long) gps.timestamp).LocalDateTime.TimeOfDay, gps.horizontalAccuracy.ToString("F2"), gps.latitude.ToString("R"), gps.longitude.ToString("R"), EstimatedUserLocation(), pose.HorizontalAccuracy.ToString("F2"), pose.Latitude.ToString("F7"), pose.Longitude.ToString("F7"), pose.Heading.ToString("F2"), pose.OrientationYawAccuracy.ToString("F2"));
    }
}
