using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class test : MonoBehaviour
{
    private List<Vector3> pts = new List<Vector3>();
    ARMeshManager mm;
    DepthImage depthSource;

    public static int pointAhead = 0; // 0 = no obstacle; 1 = go left; 2 = go right
    public static float closest = 999;

    // Start is called before the first frame update
    void Start()
    {
        mm = GetComponent<ARMeshManager>();
        depthSource = GameObject.Find("DepthHandler").GetComponent<DepthImage>();
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
        ProcessData();
    }

    void ProcessData()
    {
        // For calculations
        Vector3 userLoc = DepthImage.position;
        float sin = Mathf.Sin(DepthImage.rotation.y * Mathf.Deg2Rad);
        float cos = Mathf.Cos(DepthImage.rotation.y * Mathf.Deg2Rad);

        pointAhead = 0;
        float leftCount = 0;
        float leftSum = 0;
        float rightCount = 0;
        float rightSum = 0;
        int numPoints = 0;
        closest = 999f;
        foreach (Vector3 pos in pts) {
            Vector3 translated = pos - userLoc;

            if (translated.y > DepthImage.ground && translated.y < DepthImage.ground + DepthImage.personHeight) { // Height check
                float rX = cos*translated.x - sin*translated.z;
                float rZ = sin*translated.x + cos*translated.z;
                // Distance & width check
                if (rZ > 0 && rZ < DepthImage.distanceToObstacle && rX > -DepthImage.halfPersonWidth && rX < DepthImage.halfPersonWidth) {
                    numPoints += 1;
                    float t = rX*rX+rZ*rZ;
                    if (t < closest) closest = t;
                }

                if (rX > 0) {
                    rightSum += rZ;
                    rightCount += 1;
                }
                else {
                    leftSum += rZ;
                    leftCount += 1;
                }
            }

            if (translated.y < 0)
                DepthImage.AddToGrid(pos);
        }

        closest = Mathf.Sqrt(closest);

        if (numPoints >= 2) {
            float leftAvg = (leftCount == 0) ? Single.PositiveInfinity : leftSum/leftCount;
            float rightAvg = (rightCount == 0) ? Single.PositiveInfinity : rightSum/rightCount;
            pointAhead = (leftAvg > rightAvg) ? 1 : 2;
        }
    }
}
