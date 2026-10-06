using UnityEngine;

public class RotateObject : MonoBehaviour
{

    public float degreesPerSecond = 150;

    void Update()
    {
            transform.Rotate(new Vector3(0, -degreesPerSecond, 0) * Time.deltaTime);
    }
}