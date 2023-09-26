using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPointCloud))]
[RequireComponent(typeof(ParticleSystem))]

public class PointCloudVisualizer : MonoBehaviour
{
    // For logging purposes
    [NonSerialized]
    public string info = "";

    // Keep track of all points.
    public Dictionary<ulong, Vector3> points = new Dictionary<ulong, Vector3>();
    public Dictionary<ulong, float> confidences = new Dictionary<ulong, float>();

    // ParticleSystem for rendering particles.
    new ParticleSystem particleSystem;
    ParticleSystem.Particle[] particles;
    int prevNumParticles = 0;

    // Access data from other scripts
    DepthImage depthSource;

    ARPointCloud pointCloud;

    Color32 startColor;
    Color32 failColor = Color.red;
    Color32 ignoreColor = Color.yellow;
    float startSize;

    public static int pointAhead = 0; // 0 = no obstacle; 1 = go left; 2 = go right
    public static float ground = -1.0f; // Ground elevation (in meters) relative to camera
    private const float groundPadding = 0.3f; // Height to add to calculated ground level to count as ground

    void Awake()
    {
        particleSystem = GetComponent<ParticleSystem>();
        pointCloud = GetComponent<ARPointCloud>();
    }

    void OnEnable()
    {
        depthSource = GameObject.Find("DepthHandler").GetComponent<DepthImage>();

        startColor = particleSystem.main.startColor.color;
        startSize = particleSystem.main.startSize.constant * 2.5f;

        pointCloud.updated += OnPointCloudUpdated;
    }

    void OnDisable()
    {
        pointCloud.updated -= OnPointCloudUpdated;
    }

    void OnPointCloudUpdated(ARPointCloudUpdatedEventArgs eventArgs)
    {
        /** UPDATE POINTS **/
        if (!pointCloud.positions.HasValue || !pointCloud.identifiers.HasValue)
            return;

        // Positions in current frame. Positions & identifiers should be parallel.
        var positions = pointCloud.positions.Value;
        var identifiers = pointCloud.identifiers.Value;

        // ARKit does not provide point cloud confidence values. https://forum.unity.com/threads/getconfidence-method-for-arkit-pointcloud.614920
        // if (pointCloud.confidenceValues.HasValue) {
        //     var conf = pointCloud.confidenceValues.Value;
        //     for (int i = 0; i < positions.Length; i++)
        //         confidences[identifiers[i]] = conf[i];
        // }

        // Create dictionary with points in current frame
        points = new Dictionary<ulong, Vector3>();
        for (int i = 0; i < positions.Length; i++) {
            ulong id = identifiers[i];
            // if (!confidences.ContainsKey(id) || confidences[id] > 0.2)
            // iPhone has a problem with generating points right on top of the camera, so we don't use points that are too close
            if ((DepthImage.position - positions[i]).sqrMagnitude > 0.15f)
                points[id] = positions[i];
        }

        UpdateParticles(points, points.Count);
    }

    // Iterate over positions[] if only rendering points in the current frame.
    // Set numParticles to positions.Length if only rendering points in the current frame.
    void UpdateParticles(IEnumerable<KeyValuePair<ulong, Vector3>> pts, int numParticles)
    {
        // Create or resize particle array if necessary
        if (particles == null || particles.Length < numParticles)
            particles = new ParticleSystem.Particle[(int) (1.5 * numParticles)]; // Create an array with extra space to reduce the number of re-creations.

        // For calculations
        Vector3 userLoc = DepthImage.position;
        float sin = Mathf.Sin(DepthImage.rotation.y * Mathf.Deg2Rad);
        float cos = Mathf.Cos(DepthImage.rotation.y * Mathf.Deg2Rad);

        pointAhead = 0;
        int index = 0;
        float leftCount = 0;
        float leftSum = 0;
        float rightCount = 0;
        float rightSum = 0;
        int numPoints = 0;
        foreach (var kvp in pts) {
            Vector3 pos = kvp.Value;
            Vector3 translated = pos - userLoc;

            particles[index].startColor = (translated.y > ground) ? startColor : ignoreColor;
            if (translated.y > ground && translated.y < ground + DepthImage.personHeight) { // Height check
                float rX = cos*translated.x - sin*translated.z;
                float rZ = sin*translated.x + cos*translated.z;
                // Distance & width check
                if (rZ > 0 && rZ < DepthImage.distanceToObstacle && rX > -DepthImage.halfPersonWidth && rX < DepthImage.halfPersonWidth) {
                    numPoints += 1;
                    particles[index].startColor = failColor;
                }

                if (rX > 0) {
                    rightSum += rZ;
                    rightCount += 1;
                }
                else {
                    leftSum += rZ;
                    leftCount += 1;
                }
            }

            particles[index].startSize = startSize;
            particles[index].position = pos;
            particles[index].remainingLifetime = 1f;
            index++;
        }

        if (numPoints >= 3) {
            float leftAvg = (leftCount == 0) ? Single.PositiveInfinity : leftSum/leftCount;
            float rightAvg = (rightCount == 0) ? Single.PositiveInfinity : rightSum/rightCount;
            pointAhead = (leftAvg > rightAvg) ? 1 : 2;
        }

        // Remove any extra pre-existing particles
        for (int i = numParticles; i < prevNumParticles; i++)
            particles[i].remainingLifetime = -1f;

        particleSystem.SetParticles(particles, Math.Max(numParticles, prevNumParticles));
        prevNumParticles = numParticles;
    }

    // private bool IsClose(Vector3 a) {
    //     Vector3 b = DepthImage.position;
    //     return (a.x-b.x)*(a.x-b.x) + (a.z-b.z)*(a.z-b.z) <= DepthImage.distanceToObstacle*DepthImage.distanceToObstacle;
    // }
}
