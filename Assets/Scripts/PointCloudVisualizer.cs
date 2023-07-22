using System;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPointCloud))]
[RequireComponent(typeof(ParticleSystem))]

public class PointCloudVisualizer : MonoBehaviour
{
    // For logging purposes
    public string info = "";

    // Keep track of all points.
    public Dictionary<ulong, Vector3> points = new Dictionary<ulong, Vector3>();
    public Dictionary<ulong, float> confidences = new Dictionary<ulong, float>();

    // ParticleSystem for rendering particles.
    new ParticleSystem particleSystem;
    ParticleSystem.Particle[] particles;
    int prevNumParticles = 0;

    ARPointCloud pointCloud;

    void Awake()
    {
        particleSystem = GetComponent<ParticleSystem>();
        pointCloud = GetComponent<ARPointCloud>();   
    }

    void OnEnable()
    {
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

        // Create/update positions in dictionary
        for (int i = 0; i < positions.Length; i++)
            points[identifiers[i]] = positions[i];

        // ARKit does not provide point cloud confidence values. https://forum.unity.com/threads/getconfidence-method-for-arkit-pointcloud.614920
        if (pointCloud.confidenceValues.HasValue) {
            var conf = pointCloud.confidenceValues.Value;
            for (int i = 0; i < positions.Length; i++)
                confidences[identifiers[i]] = conf[i];
        }

        /** RENDER POINTS **/

        // Create or resize particle array if necessary
        int numParticles = points.Count; // Set this to positions.Length if only rendering points in the current frame.
        if (particles == null || particles.Length < numParticles)
            particles = new ParticleSystem.Particle[(int) (1.2 * numParticles)]; // Create an array with extra space to reduce re-creations.

        Color32 startColor = particleSystem.main.startColor.color;
        float startSize = particleSystem.main.startSize.constant;
        int index = 0;
        foreach (var kvp in points) { // Iterate over positions[] if only rendering points in the current frame.
            Vector3 pos = kvp.Value;
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
}