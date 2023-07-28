using System;
using System.Collections;
using UnityEngine;

// REFERENCES
// https://docs.unity3d.com/Manual/MobileInput.html
// https://docs.unity3d.com/ScriptReference/Gyroscope.html
// https://docs.unity3d.com/ScriptReference/Compass.html
public class SensorData : MonoBehaviour
{
    // IMU data
    public Vector3 accel;
    public Vector3 gyro;
    public Vector3 attitude;
    public Vector3 mag;
    public float heading;

    private float delay = 0.2f;

    private bool dataUpdating = false;

    void Awake()
    {
        Input.gyro.enabled = true;
        Input.gyro.updateInterval = 0.1f;
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
        heading = Input.compass.trueHeading;

        // Wait for a bit before trying to update again
        yield return new WaitForSeconds(delay);
        dataUpdating = false;
    }

    void OnDisable() {
        Input.gyro.enabled = false;
        Input.compass.enabled = false;
    }

    // Format IMU data into string
    public string IMUstring() {
        // return string.Format("Accel: {0} \nGyro: {1} \nMag: {2} \nAttitude: {3} \nHeading: {4}", accel, gyro, mag, attitude, heading);
        return string.Format("Attitude: {0} \nHeading: {1}", attitude, heading);
    }
}
