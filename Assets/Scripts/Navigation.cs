using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// https://developers.google.com/maps/documentation/routes/compute_route_directions
// https://developers.google.com/maps/documentation/routes/reference/rest/v2/TopLevel/computeRoutes

public class Navigation : MonoBehaviour
{
    private string SERVER_URL = "https://routes.googleapis.com/directions/v2:computeRoutes";
    private string apiKey;

    public List<Point> allPoints;
    public JArray steps;
    [NonSerialized]
    public int curWaypoint = -1;
    [NonSerialized]
    public Double curOrientation = 0;
    [NonSerialized]
    public List<int> stepStartIndices; // The starting location for each step corresponds to a point/index in allPoints.

    private double closeRadius = 0.000045;

    [NonSerialized]
    public bool initialized = false;

    public class Point {
        public double lat;
        public double lng;
        public Point(double x, double y) {
            this.lat = x;
            this.lng = y;
        }
        public override string ToString() {
            return lat + "," + lng;
        }
    }

    // Compass orientation between two points in degrees
    private double Orientation(Point start, Point end) {
        double deltaY = end.lat - start.lat;
        double deltaX = end.lng - start.lng;
        double rad = (- Math.Atan2(deltaY, deltaX) + 5 * Math.PI / 2) % (Math.PI * 2);
        return rad * 180 / Math.PI;
    }

    public static bool ApproxEq(Point p1, Point p2) {
        return (Math.Abs(p1.lat - p2.lat) <= 1e-5 && Math.Abs(p1.lng - p2.lng) <= 1e-5);
    }

    public static bool Between(Point start, Point end, Point pt) {
        return false;
    }

    double Dist(Point p1, Point p2) {
        double latDiff = p1.lat - p2.lat;
        double lngDiff = p2.lng - p2.lng;
        return Math.Sqrt(latDiff*latDiff + lngDiff*lngDiff);
    }

    void Start()
    {
        var sr = new StreamReader("Assets/Scripts/apikey.txt");
        apiKey = sr.ReadLine();
        sr.Close();
        Debug.Log(apiKey);
        double startLat = 42.36382619802787;
        double startLng = -71.12962948677604;
        double endLat = 42.360894446542666;
        double endLng = -71.13030875355446;
        RequestWaypoints(startLat, startLng, endLat, endLng);
    }

    void Update()
    {
        if (initialized) {
            Point userLoc = new Point(42.36110, -71.12996);
            OnLocationUpdate(userLoc);
        }
    }

    public void OnLocationUpdate(Point loc)
    {
        if (!initialized)
            return;

        int bestWaypoint = FindBestWaypoint(loc);
        if (bestWaypoint == allPoints.Count - 1) {
            Debug.Log("Reached destination");
            initialized = false;
            return;
        }
        if (curWaypoint != bestWaypoint) {
            curWaypoint = bestWaypoint;
            int stepIndex = stepStartIndices.IndexOf(curWaypoint);
            if (stepIndex != -1) {
                string instr = steps[stepIndex]["navigationInstruction"]["instructions"].ToString();
                Debug.Log(instr);
            }
        }

        double ori = Orientation(loc, allPoints[curWaypoint + 1]);
        if (curOrientation != ori) {
            curOrientation = ori;
            Debug.Log("New orientation: " + curOrientation);
        }
    }

    // Find index of most suitable waypoint for a given user location.
    public int FindBestWaypoint(Point loc)
    {
        // Check if reached final waypiont
        if (Dist(loc, allPoints[allPoints.Count - 1]) < closeRadius) {
            return allPoints.Count - 1;
        }

        double minDist = Double.PositiveInfinity;
        int index = -1;
        for (int i = 0; i < allPoints.Count - 1; i++) {
            // If close enough to current waypoint, immediately return
            double distFromCur = Dist(loc, allPoints[i]);
            if (distFromCur < closeRadius) {
                index = i;
                break;
            }
            // Track closest point as backup in case all else fails
            else if (distFromCur < minDist) {
                minDist = distFromCur;
                index = i;
            }

            // Smarter check for waypoint based on distances
            double curToNext = Dist(allPoints[i], allPoints[i+1]);
            if (distFromCur < curToNext) {
                index = i;
                break;
            }
        }
        return index;
    }

    public void RequestWaypoints(double startLat, double startLng, double endLat, double endLng)
    {
        StartCoroutine(SendRequest(ConstructRequest(startLat, startLng, endLat, endLng)));
    }

    private IEnumerator SendRequest(JObject request)
    {
        string url = SERVER_URL;

        using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
        {   
            // Request and wait for the desired page.
            byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(request.ToString());
            webRequest.uploadHandler = (UploadHandler) new UploadHandlerRaw(jsonToSend);
            webRequest.downloadHandler = (DownloadHandler) new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("X-Goog-Api-Key", apiKey);
            webRequest.SetRequestHeader("X-Goog-FieldMask", "routes.legs.distanceMeters,routes.legs.duration,routes.legs.polyline,routes.legs.steps");
            yield return webRequest.SendWebRequest();

            if (checkStatus(webRequest, url.Split('/'))) {
                var response = JObject.Parse(webRequest.downloadHandler.text);
                var values = response["routes"][0]["legs"][0];
                float distInMeters = (float) values["distanceMeters"];
                string duration = values["duration"].ToString();
                allPoints = DecodePolyline(values["polyline"]["encodedPolyline"].ToString());
                // foreach (var asdf in allPoints)
                //     Debug.Log(asdf);

                // Populate indices of starting locations for each step
                steps = (JArray) values["steps"];
                stepStartIndices = new List<int>();
                for (int i = 0; i < steps.Count; i++) {
                    double lat = (double) steps[i]["startLocation"]["latLng"]["latitude"];
                    double lng = (double) steps[i]["startLocation"]["latLng"]["longitude"];
                    Point p = new Point(lat, lng);
                    int index = -1;
                    for (int j = 0; j < allPoints.Count; j++) {
                        if (ApproxEq(p, allPoints[j])) {
                            index = j;
                            break;
                        }
                    }
                    stepStartIndices.Add(index);
                }
                initialized = true;
            }
        }
    }

    // Returns true on success, false on fail.
    bool checkStatus(UnityWebRequest webRequest, string[] pages)
    {
        int page = pages.Length - 1;
        switch (webRequest.result)
        {
            case UnityWebRequest.Result.ConnectionError:
                Debug.LogError(pages[page] + ": Error: " + webRequest.error);
                return false;
            case UnityWebRequest.Result.DataProcessingError:
                Debug.LogError(pages[page] + ": Error: " + webRequest.error);
                return false;
            case UnityWebRequest.Result.ProtocolError:
                Debug.LogError(pages[page] + ": HTTP Error: " + webRequest.error);
                return false;
            case UnityWebRequest.Result.Success:
                Debug.Log(pages[page] + ":\nReceived: " + webRequest.downloadHandler.text);
                return true;
        }
        return false;
    }

    // https://gist.github.com/shinyzhu/4617989
    public static List<Point> DecodePolyline(string encodedPoints)
    {
        char[] polylineChars = encodedPoints.ToCharArray();
        int index = 0;

        int currentLat = 0;
        int currentLng = 0;
        int next5bits;
        int sum;
        int shifter;

        List<Point> allPoints = new List<Point>();

        while (index < polylineChars.Length)
        {
            // Uncomment this to get lat/lng relative changes rather than absolute values
            // currentLat = 0;
            // currentLng = 0;

            // calculate next latitude
            sum = 0;
            shifter = 0;
            do {
                next5bits = (int) polylineChars[index++] - 63;
                sum |= (next5bits & 31) << shifter;
                shifter += 5;
            } while (next5bits >= 32 && index < polylineChars.Length);

            if (index >= polylineChars.Length)
                break;

            currentLat += (sum & 1) == 1 ? ~(sum >> 1) : (sum >> 1);

            //calculate next longitude
            sum = 0;
            shifter = 0;
            do {
                next5bits = (int) polylineChars[index++] - 63;
                sum |= (next5bits & 31) << shifter;
                shifter += 5;
            } while (next5bits >= 32 && index < polylineChars.Length);

            if (index >= polylineChars.Length && next5bits >= 32)
                break;

            currentLng += (sum & 1) == 1 ? ~(sum >> 1) : (sum >> 1);

            allPoints.Add(new Point(currentLat / 1E5, currentLng / 1E5));
        }
        return allPoints;
    }

    JObject ConstructRequest(double startLat, double startLng, double endLat, double endLng)
    {
        return
            new JObject(
                new JProperty("origin",
                    new JObject(
                        new JProperty("location",
                            new JObject(
                                new JProperty("latLng",
                                    new JObject(
                                        new JProperty("latitude", startLat),
                                        new JProperty("longitude", startLng)
                                    )
                                )
                            )
                        )
                    )
                ),
                new JProperty("destination",
                    new JObject(
                        new JProperty("location",
                            new JObject(
                                new JProperty("latLng",
                                    new JObject(
                                        new JProperty("latitude", endLat),
                                        new JProperty("longitude", endLng)
                                    )
                                )
                            )
                        )
                    )
                ),
                new JProperty("travelMode", "WALK"),
                new JProperty("polylineQuality", "OVERVIEW"), // "HIGH_QUALITY"
                new JProperty("computeAlternativeRoutes", false),
                new JProperty("routeModifiers",
                    new JObject(
                        new JProperty("avoidTolls", false),
                        new JProperty("avoidHighways", false),
                        new JProperty("avoidFerries", false)
                    )
                ),
                new JProperty("languageCode", "en-US"),
                new JProperty("units", "METRIC")
            );
    }
}
