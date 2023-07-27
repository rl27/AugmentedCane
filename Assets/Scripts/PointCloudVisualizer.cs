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

    // Group points based on location.
    public Dictionary<Vector3, Dictionary<ulong, Vector3>> grid = new Dictionary<Vector3, Dictionary<ulong, Vector3>>();
    Dictionary<ulong, int> blockingPoints; // point id to index in particles[]

    // Distance to check for nearby points
    float nearbyDistance = 0.08f;
    float gridScale;

    public List<Vector3> points2 = new List<Vector3>();

    // ParticleSystem for rendering particles.
    new ParticleSystem particleSystem;
    ParticleSystem.Particle[] particles;
    int prevNumParticles = 0;

    // Height (in meters) above the floor height at which to discard points.
    float heightToDiscard = 0.3f;

    // Access data from other scripts
    DepthImage depthSource;
    Plane planeSource;

    ARPointCloud pointCloud;

    // Whether to use built-in point cloud mechanisms or manually create points from depth.
    private bool useDepth = false;

    private bool useGrid = false;

    Color32 startColor;
    Color32 failColor = Color.red;
    float startSize;

    void Awake()
    {
        particleSystem = GetComponent<ParticleSystem>();
        pointCloud = GetComponent<ARPointCloud>();
    }

    void OnEnable()
    {
        depthSource = GameObject.Find("DepthHandler").GetComponent<DepthImage>();
        planeSource = GameObject.Find("PlaneHandler").GetComponent<Plane>();

        startColor = particleSystem.main.startColor.color;
        startSize = particleSystem.main.startSize.constant;

        gridScale = 1 / (2 * nearbyDistance);

        pointCloud.updated += OnPointCloudUpdated;
    }

    void OnDisable()
    {
        pointCloud.updated -= OnPointCloudUpdated;
    }

    // Iterate over positions[] if only rendering points in the current frame.
    // Set numParticles to positions.Length if only rendering points in the current frame.
    void UpdateParticles(IEnumerable<KeyValuePair<ulong, Vector3>> pts, int numParticles)
    {
        blockingPoints = new Dictionary<ulong, int>();

        // Create or resize particle array if necessary
        if (particles == null || particles.Length < numParticles)
            particles = new ParticleSystem.Particle[(int) (1.5 * numParticles)]; // Create an array with extra space to reduce re-creations.

        int index = 0;
        foreach (var kvp in pts) {
            Vector3 pos = kvp.Value;

            if (pos.y < planeSource.min + heightToDiscard)
                particles[index].startColor = failColor;
            else {
                particles[index].startColor = startColor;
                blockingPoints[kvp.Key] = index;
            }
            particles[index].startSize = startSize;
            particles[index].position = pos;
            particles[index].remainingLifetime = 1f;
            index++;
        }

        // Remove any extra pre-existing particles
        for (int i = numParticles; i < prevNumParticles; i++)
            particles[i].remainingLifetime = -1f;

        particleSystem.SetParticles(particles, Math.Max(numParticles, prevNumParticles));
        prevNumParticles = numParticles;
    }

    void OnPointCloudUpdated(ARPointCloudUpdatedEventArgs eventArgs)
    {
        /** UPDATE POINTS **/
        
        if (useDepth) { // Construct points from depth
            int width = depthSource.depthWidth;
            int height = depthSource.depthHeight;

            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {

                    int maxConfidence = 255;
                #if UNITY_ANDROID
                    maxConfidence = 255;
                #elif UNITY_IOS
                    maxConfidence = 2;
                #endif
                    if (depthSource.GetConfidence(x, y) != maxConfidence)
                        continue;

                    Vector3 v = depthSource.TransformLocalToWorld(depthSource.ComputeVertex(x, y, depthSource.GetDepth(x, y)));
                    points[(ulong) points.Count] = SnapToGrid(v);
                }
            }

            UpdateParticles(points, points.Count);
        }
        else { // Use built-in point cloud system
            if (!pointCloud.positions.HasValue || !pointCloud.identifiers.HasValue)
                return;

            // Positions in current frame. Positions & identifiers should be parallel.
            var positions = pointCloud.positions.Value;
            var identifiers = pointCloud.identifiers.Value;

            // ARKit does not provide point cloud confidence values. https://forum.unity.com/threads/getconfidence-method-for-arkit-pointcloud.614920
            if (pointCloud.confidenceValues.HasValue) {
                var conf = pointCloud.confidenceValues.Value;
                for (int i = 0; i < positions.Length; i++)
                    confidences[identifiers[i]] = conf[i];
            }

            if (!useGrid) {
                // Create/update positions in dictionary
                for (int i = 0; i < positions.Length; i++) {
                    ulong id = identifiers[i];
                    if (!confidences.ContainsKey(id) || confidences[id] > 0.35)
                        points[id] = positions[i];
                }

                UpdateParticles(points, points.Count);
            }
            else {
                // Create/update positions in dictionary
                for (int i = 0; i < positions.Length; i++) {
                    ulong id = identifiers[i];
                    Vector3 pos = positions[i];
                    Vector3 gridPoint = SnapToGrid(pos);

                    // If this specific point already exists, remove it from the grid dict and update update the listing.
                    if (points.ContainsKey(id))
                        grid[points[id]].Remove(id);
                    points[id] = gridPoint;

                    // Add to (or update) grid dict
                    if (!grid.ContainsKey(gridPoint))
                        grid[gridPoint] = new Dictionary<ulong, Vector3>();
                    else
                        grid[gridPoint][id] = pos;
                }

                IEnumerable<KeyValuePair<ulong, Vector3>> allPts = grid.SelectMany(x => x.Value);
                UpdateParticles(allPts, points.Count);

                // Identify lone blocking particles and color them blue
                foreach (var kvp in blockingPoints) {
                    ulong id = kvp.Key;
                    
                    Vector3 gridPt = points[id];
                    Vector3 pos = grid[gridPt][id];

                    List<Vector3> gridNeighbors = new List<Vector3>();

                    // TODO: Add the rest of the neighboring grid points
                    gridNeighbors.Add(SnapToGrid(gridPt));
                    gridNeighbors.Add(SnapToGrid(gridPt + Vector3.up/gridScale));
                    gridNeighbors.Add(SnapToGrid(gridPt + Vector3.down/gridScale));
                    gridNeighbors.Add(SnapToGrid(gridPt + Vector3.back/gridScale));
                    gridNeighbors.Add(SnapToGrid(gridPt + Vector3.forward/gridScale));
                    gridNeighbors.Add(SnapToGrid(gridPt + Vector3.left/gridScale));
                    gridNeighbors.Add(SnapToGrid(gridPt + Vector3.right/gridScale));

                    int totalNearby = -1;
                    foreach (Vector3 gridN in gridNeighbors) {
                        if (grid.ContainsKey(gridN)) {
                            foreach (var kvp2 in grid[gridN]) {
                                if (blockingPoints.ContainsKey(kvp2.Key) && (pos - kvp2.Value).magnitude < nearbyDistance)
                                    totalNearby += 1;
                            }
                        }
                    }
                    if (totalNearby < 3)
                        particles[kvp.Value].startColor = Color.blue;
                }

                particleSystem.SetParticles(particles, prevNumParticles);
            }
        }
    }

    Vector3 SnapToGrid(Vector3 v)
    {
        return new Vector3(Mathf.Round(v.x * gridScale)/gridScale, Mathf.Round(v.y * gridScale)/gridScale, Mathf.Round(v.z * gridScale)/gridScale);
    }
}
