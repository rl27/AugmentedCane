using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPlaneManager))]

public class Plane : MonoBehaviour
{
    ARPlaneManager apm;

    void Awake()
    {
        apm = GetComponent<ARPlaneManager>();
    }

    void Update()
    {
        Debug.unityLogger.Log("mytag", apm.trackables.count);

        foreach (var plane in apm.trackables) {
            Debug.unityLogger.Log("mytag", plane.normal);
        }
    }
}
