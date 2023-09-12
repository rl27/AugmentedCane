using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[RequireComponent(typeof(AudioSource))]
public class Navigation : MonoBehaviour
{
    [NonSerialized]
    public static string info;

    [SerializeField]
    GameObject AudioSourceObject;
    AudioSource audioSource;
    public AudioClip onAxis;
    public AudioClip offAxis;
    public AudioClip behind;
    private double onAxisAngle = 22.5;
    private double offAxisAngle = 90;
    public AudioClip reachWaypoint;

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

    // Rough conversion: 0.00001 = 1 meter
    private double closeRadius = 0.000045;
    private double farRadius = 0.00020;
    private double farLineDist = 0.00020;

    public static bool initialized = false; // Tracks whether RequestWaypoints has been called & completed

    private DateTime lastOriented; // Time at which orientation was last given
    private float orientationUpdateInterval = 12.0f; // Minimum interval at which to give orientation

    private DateTime lastInstructed; // Time at which instructions were last given
    private float instructionUpdateInterval = 2.0f; // Minimum interval at which to give instructions

    private float minPitch = 0.5f; // Minimum pitch to apply to audio

    private bool reachedFirstWaypoint = false;

    private bool testing = false;

    public class Point {
        public bool isAddress; // True for address, false for lat/long coords
        public string address;
        public double lat;
        public double lng;
        public Point(double x, double y) {
            this.lat = x;
            this.lng = y;
            this.address = "";
            this.isAddress = false;
        }
        public Point(string addr) {
            this.lat = 0;
            this.lng = 0;
            this.address = addr;
            this.isAddress = true;
        }
        public override string ToString() {
            string ll = Math.Round(lat, 7) + "," + Math.Round(lng, 7);
            if (!isAddress)
                return ll;
            return address + ": " + ll;
        }
    }

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        tts = TTSHandler.GetComponent<TTS>();

        if (testing) {
            // Testing - plan a route
            Point start = new Point(42.36346643895319,-71.12569797709479);
            // Point end = new Point(42.36302180414251,-71.12749282880507);
            Point end = new Point("trader joes allston");
            RequestWaypoints(start, end);
        }
    }

    void Update()
    {
        if (!initialized)
            return;
        if (testing) {
            // Testing - get navigation information based on user location
            Point userLoc = new Point(42.36346856360623, -71.12569653098912);
            OnLocationUpdate(userLoc);
        }
        else {
            // Do navigation tasks
            OnLocationUpdate(GPSData.EstimatedUserLocation());
        }
    }

    // Parse input location from lat/long text box, then request waypoints
    public void OnCoordsEntered(string input)
    {
        string[] splits = input.Split(',');
        if (splits.Length != 2)
            return;
        float targetLat;
        float targetLng;
        if (float.TryParse(splits[0], out targetLat) && float.TryParse(splits[1], out targetLng)) {
            RequestWaypoints(GPSData.EstimatedUserLocation(), new Point(targetLat, targetLng));
        }
    }

    // Parse input location from address text box, then request waypoints
    public void OnAddressEntered(string input)
    {
        if (input != "")
            RequestWaypoints(GPSData.EstimatedUserLocation(), new Point(input));
    }

    // Call this first to request a route and populate variables
    public void RequestWaypoints(Point start, Point end)
    {
        StartCoroutine(WebClient.SendRouteRequest(start, end,
            response => {
                if (!response.HasValues) {
                    tts.RequestTTS("Route request failed");
                    return;
                }
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
                reachedFirstWaypoint = false;
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
            RequestWaypoints(loc, allPoints[allPoints.Count - 1]);
            return;
        }
        // Completed route.
        if (bestWaypoint == allPoints.Count - 1) {
            tts.RequestTTS("Arriving at destination");
            initialized = false;
            return;
        }

        if (curWaypoint != bestWaypoint && (DateTime.Now - lastInstructed).TotalSeconds > instructionUpdateInterval) {
            if (closeToWaypoint || bestWaypoint > curWaypoint) {
                audioSource.Stop();
                AudioSourceObject.transform.position = DepthImage.position;
                audioSource.pitch = 1;
                audioSource.PlayOneShot(reachWaypoint, 1f);

                // Check if new waypoint corresponds with the starting position of a step, i.e. that it has an associated instruction
                int stepIndex = stepStartIndices.IndexOf(bestWaypoint);
                if (stepIndex != -1) {
                    string instr = steps[stepIndex]["navigationInstruction"]["instructions"].ToString();
                    tts.RequestTTS(String.Format("Step {0}: {1}", stepIndex + 1, instr));
                }
                else { // Otherwise, give basic instruction? e.g. "Turn right"
                }
            }
            curWaypoint = bestWaypoint;
            lastInstructed = DateTime.Now;
        }

        // Calculate orientation & distance to next waypoint
        int targetWaypoint = curWaypoint + 1;
        double ori = Orientation(loc, allPoints[targetWaypoint]);
        double dist = GPSData.degreeToMeter * Dist(loc, allPoints[targetWaypoint]);
        info = String.Format("WP {0}, {1}°, {2} m", targetWaypoint, Math.Round(ori), Math.Round(dist, 2));

        if ((DateTime.Now - Vision.lastValidDirection).TotalSeconds < Vision.validDuration) {
            double visionDiff = (Vision.direction - ori + 360) % 360;
            if (visionDiff > 180)
                visionDiff -= 360;
            if (Math.Abs(visionDiff) < Vision.maxDisparity)
                ori = Vision.direction;
            info += String.Format(", {0}°, {1}", Math.Round(Vision.direction), Math.Abs(visionDiff) < Vision.maxDisparity);
        }

        // Play orientation audio
        if (DepthImage.direction == DepthImage.Direction.None && !audioSource.isPlaying) {
            double headingDiff = (ori - SensorData.heading + 360) % 360;
            if (headingDiff > 180) // Move range to [-pi, pi]
                headingDiff -= 360;
            float rad = (float) headingDiff * Mathf.Deg2Rad;
            float sin = Mathf.Sin(rad);
            double absDiff = Math.Abs(headingDiff);
            AudioSourceObject.transform.position = DepthImage.position + new Vector3(sin, 0, 0);
            audioSource.pitch = (float) (1 - (1 - minPitch) * (absDiff / offAxisAngle)); // Range: [minPitch, 1]
            if (absDiff < onAxisAngle)
                audioSource.PlayOneShot(onAxis, 2.5f);
            else if (absDiff < offAxisAngle)
                audioSource.PlayOneShot(offAxis, 2.5f);
            else {
                audioSource.pitch = 1;
                audioSource.PlayOneShot(behind, 2.5f);
            }
        }

        // TTS orientation info
        if ((DateTime.Now - lastOriented).TotalSeconds > orientationUpdateInterval) {
            string facingCardinal = CardinalOrientation(SensorData.heading);
            string targetCardinal = CardinalOrientation(ori);
            tts.RequestTTS(String.Format("Facing {0}, head {1} for {2} meters", facingCardinal, targetCardinal, Math.Round(dist)));
            lastOriented = DateTime.Now;
        }
    }

    // Find index of most suitable waypoint for a given user location
    private (int, bool) FindBestWaypoint(Point loc)
    {
        // Guide user to first waypoint before doing all the other navigation stuff
        if (!reachedFirstWaypoint) {
            if (Dist(loc, allPoints[0]) < closeRadius) {
                reachedFirstWaypoint = true;
                return (0, true);
            }
            return (-1, false);
        }

        // Check if reached final waypoint
        if (Dist(loc, allPoints[allPoints.Count - 1]) < closeRadius) {
            return (allPoints.Count - 1, true);
        }

        double minPointDist = Double.PositiveInfinity;
        double minOrthoDist = Double.PositiveInfinity;
        int pointIndex = -1;
        int orthoIndex = -1;
        int latestClose = -1; // Highest index waypoint that is very close to user location
        for (int i = 0; i < allPoints.Count - 1; i++) {
            double distFromCur = Dist(loc, allPoints[i]);
            if (distFromCur < closeRadius) {
                latestClose = i;
            }
            // Track closest point
            if (distFromCur < minPointDist) {
                minPointDist = distFromCur;
                pointIndex = i;
            }
            // Track closest line segment between points
            double orthoDist = OrthogonalDist(allPoints[i], allPoints[i+1], loc);
            if (orthoDist < minOrthoDist) {
                minOrthoDist = orthoDist;
                orthoIndex = i;
            }
        }

        // If very close to a waypoint, use that point
        if (latestClose != -1)
            return (latestClose, true);
        // Otherwise, if close enough to closest line segment, use that
        if (minOrthoDist < farLineDist)
            return (orthoIndex, false);
        // Otherwise, if close enough to waypoint, use that point
        if (minPointDist < farRadius)
            return (pointIndex - 1, false);
        // Otherwise, should recalculate
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

    private string CardinalOrientation(double angle) {
        if (angle > 180)
            angle -= 360;
        string northsouth = "";
        string eastwest = "";
        if (angle >= -67.5 && angle <= 67.5)
            northsouth = "north";
        else if (angle >= 112.5 || angle <= -112.5)
            northsouth = "south";
        if (angle >= 22.5 && angle <= 157.5)
            eastwest = "east";
        else if (angle <= -22.5 && angle >= -157.5)
            eastwest = "west";
        return northsouth + eastwest;
    }
}
