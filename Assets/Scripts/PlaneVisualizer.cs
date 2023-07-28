using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPlane))]
[RequireComponent(typeof(MeshRenderer))]

public class PlaneVisualizer : MonoBehaviour
{
    ARPlane m_ARPlane;

    [System.NonSerialized]
    public MeshRenderer meshRenderer;

    void Awake()
    {
        m_ARPlane = GetComponent<ARPlane>();
        meshRenderer = GetComponent<MeshRenderer>();

        // Color planeMatColor = Color.yellow;
        // planeMatColor.a = 0.2f;
        // meshRenderer.material.color = planeMatColor;
    }
}