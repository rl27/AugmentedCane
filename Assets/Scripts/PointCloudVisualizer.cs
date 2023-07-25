using System;
using System.Collections.Generic;
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

    // Group points based on location.
    public Dictionary<Vector3, Dictionary<ulong, Vector3>> grid = new Dictionary<Vector3, Dictionary<ulong, Vector3>>();
    float gridScale = 10f;

    public HashSet<Vector3> points2 = new HashSet<Vector3>();
    public Dictionary<ulong, float> confidences = new Dictionary<ulong, float>();

    // ParticleSystem for rendering particles.
    new ParticleSystem particleSystem;
    ParticleSystem.Particle[] particles;
    int prevNumParticles = 0;

    float heightToDiscard = 0.15f;

    // Access depth data
    DepthImage depthSource;

    Plane planeSource;

    ARPointCloud pointCloud;

    // Whether to use built-in point cloud mechanisms or manually create points from depth.
    private bool useDepth = false;

    private bool useGrid = false;

    void Awake()
    {
        particleSystem = GetComponent<ParticleSystem>();
        pointCloud = GetComponent<ARPointCloud>();
    }

    void OnEnable()
    {
        depthSource = GameObject.Find("DepthHandler").GetComponent<DepthImage>();

        planeSource = GameObject.Find("PlaneHandler").GetComponent<Plane>();

        pointCloud.updated += OnPointCloudUpdated;
    }

    void OnDisable()
    {
        pointCloud.updated -= OnPointCloudUpdated;
    }

    void UpdateParticles()
    {
        // Create or resize particle array if necessary
        int numParticles = points.Count; // Set this to positions.Length if only rendering points in the current frame.
        if (particles == null || particles.Length < numParticles)
            particles = new ParticleSystem.Particle[(int) (1.5 * numParticles)]; // Create an array with extra space to reduce re-creations.

        Color32 startColor = particleSystem.main.startColor.color;
        float startSize = particleSystem.main.startSize.constant;
        int index = 0;
        foreach (var kvp in points) { // Iterate over positions[] if only rendering points in the current frame.
            Vector3 pos = kvp.Value;
            if (pos.y < planeSource.min + heightToDiscard)
                particles[index].startColor = Color.red;
            else
                particles[index].startColor = startColor;
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

    void UpdateParticles2()
    {
        // Create or resize particle array if necessary
        int numParticles = points2.Count;
        if (particles == null || particles.Length < numParticles)
            particles = new ParticleSystem.Particle[(int) (1.5 * numParticles)]; // Create an array with extra space to reduce re-creations.

        Color32 startColor = particleSystem.main.startColor.color;
        float startSize = particleSystem.main.startSize.constant;
        int index = 0;
        foreach (Vector3 pos in points2) {
            if (pos.y < planeSource.min + heightToDiscard)
                particles[index].startColor = Color.red;
            else
                particles[index].startColor = startColor;
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

    void UpdateParticles3()
    {
        // Create or resize particle array if necessary
        int numParticles = points.Count; // Set this to positions.Length if only rendering points in the current frame.
        if (particles == null || particles.Length < numParticles)
            particles = new ParticleSystem.Particle[(int) (1.5 * numParticles)]; // Create an array with extra space to reduce re-creations.

        Color32 startColor = particleSystem.main.startColor.color;
        float startSize = particleSystem.main.startSize.constant;
        int index = 0;
        foreach (var gtp in grid) {
            var d = gtp.Value;
            foreach (var kvp in d) {
                Vector3 pos = kvp.Value;
                if (pos.y < planeSource.min + heightToDiscard)
                    particles[index].startColor = Color.red;
                else
                    particles[index].startColor = startColor;
                particles[index].startSize = startSize;
                particles[index].position = pos;
                particles[index].remainingLifetime = 1f;
                index++;
            }
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

                    if (depthSource.GetConfidence(x, y) != 255)
                        continue;

                    Vector3 v = depthSource.TransformLocalToWorld(depthSource.ComputeVertex(x, y, depthSource.GetDepth(x, y)));
                    points2.Add(SnapToGrid(v));
                }
            }

            UpdateParticles2();
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
                    if (confidences.ContainsKey(id) && confidences[id] > 0.35)
                        points[id] = positions[i];
                }

                UpdateParticles();
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

                UpdateParticles3();
            }
        }
    }

    Vector3 SnapToGrid(Vector3 v)
    {
        return new Vector3(Mathf.Round(v.x * gridScale) / gridScale, Mathf.Round(v.y * gridScale) / gridScale, Mathf.Round(v.z * gridScale) / gridScale);
    }
}
