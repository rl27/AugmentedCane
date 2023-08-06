using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Navigation : MonoBehaviour
{
    public List<Point> allPoints;
    public JArray steps;
    [NonSerialized]
    public int curWaypoint = -1;
    [NonSerialized]
    public Double curOrientation = 0;
    [NonSerialized]
    public List<int> stepStartIndices; // The starting location for each step corresponds to a point/index in allPoints.

    [SerializeField]
    GameObject TTSHandler;
    TTS tts;

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

    void Start()
    {
        tts = TTSHandler.GetComponent<TTS>();

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

    // Call this first to request a route and populate variables
    public void RequestWaypoints(double startLat, double startLng, double endLat, double endLng)
    {
        StartCoroutine(WebClient.SendRouteRequest(startLat, startLng, endLat, endLng,
            response => {
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
            })
        );
    }

    // Call this every time the user location is updated
    public void OnLocationUpdate(Point loc)
    {
        if (!initialized)
            return;

        int bestWaypoint = FindBestWaypoint(loc);
        if (bestWaypoint == allPoints.Count - 1) {
            tts.RequestTTS("Reached destination");
            initialized = false;
            return;
        }
        if (curWaypoint != bestWaypoint) {
            curWaypoint = bestWaypoint;
            // Check if new waypoint corresponds with the starting position of a step
            int stepIndex = stepStartIndices.IndexOf(curWaypoint);
            if (stepIndex != -1) {
                string instr = steps[stepIndex]["navigationInstruction"]["instructions"].ToString();
                tts.RequestTTS(instr);
            }
        }

        double ori = Orientation(loc, allPoints[curWaypoint + 1]);
        if (curOrientation != ori) {
            curOrientation = ori;
            tts.RequestTTS(String.Format("Target: {0} degrees", (int) Math.Round(ori / 10) * 10));
        }
    }

    // Find index of most suitable waypoint for a given user location
    private int FindBestWaypoint(Point loc)
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

            // Check for waypoint based on distances from current location to consecutive waypoints
            double curToNext = Dist(allPoints[i], allPoints[i+1]);
            if (distFromCur < curToNext) {
                index = i;
                break;
            }
        }
        return index;
    }

    // https://gist.github.com/shinyzhu/4617989
    private static List<Point> DecodePolyline(string encodedPoints)
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

    // Compass orientation between two points in degrees
    private double Orientation(Point start, Point end) {
        double deltaY = end.lat - start.lat;
        double deltaX = end.lng - start.lng;
        double rad = (- Math.Atan2(deltaY, deltaX) + 5 * Math.PI / 2) % (Math.PI * 2);
        return rad * 180 / Math.PI;
    }

    private static bool ApproxEq(Point p1, Point p2) {
        return (Math.Abs(p1.lat - p2.lat) <= 1e-5 && Math.Abs(p1.lng - p2.lng) <= 1e-5);
    }

    private double Dist(Point p1, Point p2) {
        double latDiff = p1.lat - p2.lat;
        double lngDiff = p2.lng - p2.lng;
        return Math.Sqrt(latDiff*latDiff + lngDiff*lngDiff);
    }
}
