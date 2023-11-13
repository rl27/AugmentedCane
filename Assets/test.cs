using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class test : MonoBehaviour
{
    ARMeshManager mm;
    // Start is called before the first frame update
    void Start()
    {
        mm = GetComponent<ARMeshManager>();
    }

    // Update is called once per frame
    void Update()
    {
        Debug.unityLogger.Log("mytag", mm.meshes.Count);
        int tricount = 0;
        foreach (var m in mm.meshes) {
            tricount += m.mesh.triangles.Length / 3;
        }
        Debug.unityLogger.Log("mytag", tricount);
    }
}
