using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARPlaneManager))]

public class Plane : MonoBehaviour
{
    [NonSerialized]
    public string info = "Total planes: 0";

    ARPlaneManager apm;

    Color defaultColor;

    [NonSerialized]
    public float min = Single.PositiveInfinity;

    void Awake()
    {
        apm = GetComponent<ARPlaneManager>();
        defaultColor = Color.yellow;
        defaultColor.a = 0.2f;
    }

    void OnEnable()
    {
        apm.planesChanged += OnPlanesChanged;
    }

    void OnDisable()
    {
        apm.planesChanged -= OnPlanesChanged;
    }

    void OnPlanesChanged(ARPlanesChangedEventArgs eventArgs)
    {
        info = String.Format("Total planes: {0}", apm.trackables.count);

        // Find lowest plane
        TrackableId lowestPlane = TrackableId.invalidId;
        min = Single.PositiveInfinity;
        foreach (var plane in apm.trackables) {
            // Check if plane's center is lower than the previous lowest
            if (plane.center.y < min) {
                min = plane.center.y;
                lowestPlane = plane.trackableId;
            }
            plane.GetComponent<PlaneVisualizer>().meshRenderer.material.color = defaultColor;
        }
        info += "\nLowest plane: " + (min - DepthImage.position.y);

        // Set color of lowest plane to cyan
        var visualizer = apm.trackables[lowestPlane].GetComponent<PlaneVisualizer>();
        if (visualizer) {
            Color planeMatColor = Color.cyan;
            planeMatColor.a = 0.2f;
            visualizer.meshRenderer.material.color = planeMatColor;
        }
    }
}
