using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPlane))]
[RequireComponent(typeof(MeshRenderer))]

public class PlaneVisualizer : MonoBehaviour
{
    /* // Uncomment this if you want to do something with each individual plane.
    ARPlane m_ARPlane;
    MeshRenderer m_PlaneMeshRenderer;

    void Awake()
    {
        m_ARPlane = GetComponent<ARPlane>();
        m_PlaneMeshRenderer = GetComponent<MeshRenderer>();

        Color planeMatColor = Color.yellow;
        planeMatColor.a = 0.33f;
        m_PlaneMeshRenderer.material.color = planeMatColor;
    }
    */
}