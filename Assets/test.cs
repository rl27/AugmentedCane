using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class test : MonoBehaviour
{
    public static List<Vector3> pts = new List<Vector3>();
    ARMeshManager mm;
    // Start is called before the first frame update
    void Start()
    {
        mm = GetComponent<ARMeshManager>();
    }

    // Update is called once per frame
    void Update()
    {
        pts.Clear();
        foreach (var m in mm.meshes) {
            var t = m.mesh.triangles;
            var v = m.mesh.vertices;
            int numTriangles = t.Length / 3;
            for (int i = 0; i < numTriangles; i++) {
                pts.Add((v[t[i*3]] + v[t[i*3+1]] + v[t[i*3+2]]) / 3f);
            }
        }
    }
}
