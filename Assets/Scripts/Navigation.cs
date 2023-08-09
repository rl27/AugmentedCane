using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[RequireComponent(typeof(AudioSource))]
public class Navigation : MonoBehaviour
{
    [SerializeField]
    GameObject AudioSourceObject;
    AudioSource audioSource;
    public AudioClip onAxis;
    public AudioClip offAxis;
    public AudioClip behind;
    private double onAxisAngle = 15;
    private double offAxisAngle = 90;

    public List<Point> allPoints;
    public JArray steps;
    [NonSerialized]
    public int curWaypoint = -5;
    // [NonSerialized]
    // public int curOrientation = 0;
    [NonSerialized]
    public List<int> stepStartIndices; // The starting location for each step corresponds to a point/index in allPoints.

    [SerializeField]
    GameObject TTSHandler;
    TTS tts;

    private double closeRadius = 0.00006; // Roughly 6 meters
    private double farRadius = 0.0004; // Roughly 40 meters

    private bool initialized = false; // Tracks whether RequestWaypoints has been called & completed

    private DateTime lastOriented; // Time at which orientation was last given
    private float orientationUpdateInterval = 10.0f; // Minimum interval at which to give orientation

    private DateTime lastInstructed; // Time at which instructions were last given
    private float instructionUpdateInterval = 1.0f; // Minimum interval at which to give instructions

    private bool testing = false;

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
        audioSource = GetComponent<AudioSource>();
        tts = TTSHandler.GetComponent<TTS>();

        if (testing) {
            // Testing - plan a route
            double startLat = 42.36346643895319;
            double startLng = -71.12569797709479;
            double endLat = 42.36302180414251;
            double endLng = -71.12749282880507;
            RequestWaypoints(startLat, startLng, endLat, endLng);
        }
    }

    void Update()
    {
        if (testing) {
            // Testing - get navigation information based on user location
            if (initialized) {
                Point userLoc = new Point(42.36346856360623, -71.12569653098912);
                OnLocationUpdate(userLoc);
            }
        }
        else {
            // Do navigation tasks
            if (initialized) {
                OnLocationUpdate(GPSData.EstimatedUserLocation());
            }
        }
    }

    // Parse input location from text box, then request waypoints
    public void OnCoordsEntered(string input)
    {
        string[] splits = input.Split(',');
        if (splits.Length != 2)
            return;
        float targetLat;
        float targetLng;
        if (float.TryParse(splits[0], out targetLat) && float.TryParse(splits[1], out targetLng)) {
            Point userLoc = GPSData.EstimatedUserLocation();
            RequestWaypoints(userLoc.lat, userLoc.lng, targetLat, targetLng);
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

    // Call this to get instructions from the latest user location
    public void OnLocationUpdate(Point loc)
    {
        if (!initialized)
            return;

        (int bestWaypoint, bool closeToWaypoint) = FindBestWaypoint(loc);
        // Recalculate route if too far from any point
        if (bestWaypoint == -2) {
            initialized = false;
            tts.RequestTTS("Recalculating");
            Point userLoc = GPSData.EstimatedUserLocation();
            Point end = allPoints[allPoints.Count - 1];
            RequestWaypoints(userLoc.lat, userLoc.lng, end.lat, end.lng);
            return;
        }
        // Completed route.
        if (bestWaypoint == allPoints.Count - 1) {
            tts.RequestTTS("Arriving at destination");
            initialized = false;
            return;
        }

        if (curWaypoint != bestWaypoint && (DateTime.Now - lastInstructed).TotalSeconds > instructionUpdateInterval) {
            // Check if new waypoint corresponds with the starting position of a step, i.e. that it has an associated instruction
            if (closeToWaypoint || bestWaypoint == curWaypoint + 1) {
                int stepIndex = stepStartIndices.IndexOf(bestWaypoint);
                if (stepIndex != -1) {
                    string instr = steps[stepIndex]["navigationInstruction"]["instructions"].ToString();
                    tts.RequestTTS(String.Format("Step {0}: {1}", stepIndex + 1, instr));
                }
            }
            curWaypoint = bestWaypoint;
            lastInstructed = DateTime.Now;
        }

        // Speak orientation & distance to nearest waypoint
        double ori = Orientation(loc, allPoints[curWaypoint + 1]);
        if ((DateTime.Now - lastOriented).TotalSeconds > orientationUpdateInterval) {
            double dist = Math.Round(GPSData.degreeToMeter * Dist(loc, allPoints[curWaypoint + 1]));
            tts.RequestTTS(String.Format("{0}, {1} degrees, {2} meters", curWaypoint + 1, (int) ori, dist));
            lastOriented = DateTime.Now;
        }
        // Play orientation audio
        if (DepthImage.direction == DepthImage.Direction.None && !audioSource.isPlaying) {
            double headingDiff = (ori - SensorData.heading + 360) % 360;
            if (headingDiff > 180) // Move range to [-pi, pi]
                headingDiff -= 360;
            AudioSourceObject.transform.position = DepthImage.position + new Vector3(2 * (float) Math.Sin(headingDiff), 2 * (float) Math.Cos(headingDiff), 0);
            if (Math.Abs(headingDiff) < onAxisAngle)
                audioSource.PlayOneShot(onAxis, 2);
            else if (Math.Abs(headingDiff) < offAxisAngle)
                audioSource.PlayOneShot(offAxis, 2);
            else
                audioSource.PlayOneShot(behind, 2);
        }
    }

    // Find index of most suitable waypoint for a given user location
    private (int, bool) FindBestWaypoint(Point loc)
    {
        // Check if reached final waypoint
        if (Dist(loc, allPoints[allPoints.Count - 1]) < closeRadius) {
            return (allPoints.Count - 1, true);
        }

        double minPointDist = Double.PositiveInfinity;
        double minOrthoDist = Double.PositiveInfinity;
        int pointIndex = -1;
        int orthoIndex = -1;
        for (int i = 0; i < allPoints.Count - 1; i++) {
            // If close enough to current waypoint, immediately return
            double distFromCur = Dist(loc, allPoints[i]);
            if (distFromCur < closeRadius) {
                return (i, true);
            }
            // Track closest point in case orthogonal projection doesn't find anything suitable
            if (distFromCur < minPointDist) {
                minPointDist = distFromCur;
                pointIndex = i - 1;
            }
            double orthoDist = OrthogonalDist(allPoints[i], allPoints[i+1], loc);
            if (orthoDist < minOrthoDist) {
                minOrthoDist = orthoDist;
                orthoIndex = i;
            }
        }

        if (minOrthoDist < farRadius)
            return (orthoIndex, false);
        if (minPointDist < farRadius)
            return (pointIndex, false);
        return (-2, false);
    }

    // Projects location onto segment (p1,p2).
    // If projection is inside segment, returns distance to segment. Otherwise, returns infinity.
    private double OrthogonalDist(Point p1, Point p2, Point loc)
    {
        double u1 = loc.lng - p1.lng;
        double u2 = loc.lat - p1.lat;
        double v1 = p2.lng - p1.lng;
        double v2 = p2.lat - p1.lat;

        double scalar = (u1*v1 + u2*v2) / (v1*v1 + v2*v2);
        double proj1 = scalar * v1;
        double proj2 = scalar * v2;
        double d = Dist(0, 0, v1, v2);
        if (Dist(0, 0, proj1, proj2) < d && Dist(proj1, proj2, v1, v2) < d)
            return Dist(u1, u2, proj1, proj2);
        return Double.PositiveInfinity;
    }

    private double Dist(double x1, double y1, double x2, double y2)
    {
        return Math.Sqrt((x2 - x1)*(x2 - x1) + (y2 - y1)*(y2 - y1));
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

        List<Point> allPts = new List<Point>();

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

            allPts.Add(new Point(currentLat / 1E5, currentLng / 1E5));
        }
        return allPts;
    }

    // Compass orientation between two points in degrees
    // Range: [0,360]
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
        double lngDiff = p1.lng - p2.lng;
        return Math.Sqrt(latDiff*latDiff + lngDiff*lngDiff);
    }
}
