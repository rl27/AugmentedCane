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

    void Awake()
    {
        apm = GetComponent<ARPlaneManager>();
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
        float min = Single.PositiveInfinity;
        foreach (var plane in apm.trackables) {
            if (plane.center.y < min) {
                min = plane.center.y;
                lowestPlane = plane.trackableId;
            }
        }

        // Set color of lowest plane to cyan
        var visualizer = apm.trackables[lowestPlane].GetComponent<PlaneVisualizer>();
        if (visualizer) {
            Color planeMatColor = Color.cyan;
            planeMatColor.a = 0.33f;
            visualizer.meshRenderer.material.color = planeMatColor;
        }
    }
}
