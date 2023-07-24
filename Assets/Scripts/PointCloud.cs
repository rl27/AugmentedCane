using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

// Reference: https://github.com/Unity-Technologies/arfoundation-samples/tree/main/Assets/Scenes/PointClouds

// TODO? Track added/removed point clouds using eventArgs. So far it seems there is always 1 point cloud added and 0 removed.

[RequireComponent(typeof(ARPointCloudManager))]

public class PointCloud : MonoBehaviour
{
    // For logging
    [NonSerialized]
    public string info = "Total points: 0";

    void OnEnable()
    {
        GetComponent<ARPointCloudManager>().pointCloudsChanged += OnPointCloudsChanged;
    }

    void OnDisable()
    {
        GetComponent<ARPointCloudManager>().pointCloudsChanged -= OnPointCloudsChanged;
    }

    void OnPointCloudsChanged(ARPointCloudChangedEventArgs eventArgs)
    {
        info = "Total points:";
        foreach (var pointCloud in eventArgs.updated) {
            var visualizer = pointCloud.GetComponent<PointCloudVisualizer>();
            if (visualizer) {
                info += " " + visualizer.points.Count.ToString();
            }
        }
    }
}
