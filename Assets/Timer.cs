using UnityEngine;

public class Timer : MonoBehaviour
{
    public static float timeInFrame = 0;
    private float timeAtStart = 0;

    void Update()
    {
        timeAtStart = Time.realtimeSinceStartup;
    }

    void LateUpdate()
    {
        timeInFrame = Time.realtimeSinceStartup - timeAtStart;
    }
}
