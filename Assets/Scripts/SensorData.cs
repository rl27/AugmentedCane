using System;
using System.Linq;
using System.Collections;
using UnityEngine;

// REFERENCES
// https://docs.unity3d.com/Manual/MobileInput.html
// https://docs.unity3d.com/ScriptReference/Gyroscope.html
// https://docs.unity3d.com/ScriptReference/Compass.html
public class SensorData : MonoBehaviour
{
    // IMU data
    public static Vector3 accel;
    public static Vector3 gyro;
    public static Vector3 attitude;
    public static Vector3 mag;
    public static float heading;
    private float headingAccuracy;

    private bool dataUpdating = false;

    // Moving average for heading
    private static int numHeadings = 7;
    private float[] pastHeadings = new float[numHeadings];
    private int headingIndex = 0;

    void Awake()
    {
        Input.gyro.enabled = true;
        Input.gyro.updateInterval = 0.033333f;
        Input.compass.enabled = true;
    }

    void Update()
    {
        StartCoroutine(UpdateData());
    }

    // Update IMU data
    public IEnumerator UpdateData() {
        // Exit if already updating the data
        if (dataUpdating)
            yield break;

        dataUpdating = true;

        accel.x = -Input.acceleration.y;
        accel.y = Input.acceleration.z;
        accel.z = Input.acceleration.x;
        gyro = Input.gyro.rotationRateUnbiased;
        attitude = Input.gyro.attitude.eulerAngles;

        mag = Input.compass.rawVector;

        // heading = Mathf.SmoothDampAngle(heading, Input.compass.trueHeading, ref _headingVelocity, 0.1f);

        // https://docs.unity3d.com/ScriptReference/Compass-headingAccuracy.html
        headingAccuracy = Input.compass.headingAccuracy;
        
        headingIndex += 1;
        pastHeadings[headingIndex % numHeadings] = Input.compass.trueHeading;
        heading = HeadingAverage();

        // Wait for a bit before trying to update again
        // yield return new WaitForSeconds(delay);
        dataUpdating = false;
    }

    void OnDisable() {
        Input.gyro.enabled = false;
        Input.compass.enabled = false;
    }

    private float HeadingAverage()
    {
        float closerToPi = 0;
        for (int i = 0; i < numHeadings; i++) {
            if (pastHeadings[i] > 90 && pastHeadings[i] < 270) closerToPi++;
        }

        float sum = 0;
        if (closerToPi > numHeadings/2) { // [0,360]
            sum = pastHeadings.Sum();
        }
        else { // [-180,180]
            for (int i = 0; i < numHeadings; i++) {
                float temp = pastHeadings[i];
                if (temp > 180) temp -= 360;
                sum += temp;
            }
            if (sum < 0) sum = (sum % 360) + 360;
        }
        return sum / numHeadings;
    }

    // Format IMU data into string
    public string IMUstring() {
        // return string.Format("Accel: {0} \nGyro: {1} \nMag: {2} \nAttitude: {3} \nHeading: {4}", accel, gyro, mag, attitude, heading);
        return string.Format("Attitude: {0} \nHeading: {1}°, Acc: {2}", attitude, heading.ToString("F1"), headingAccuracy);
    }
}
