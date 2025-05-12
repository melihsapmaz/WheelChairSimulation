using System;
using UnityEngine;
using System.IO.Ports;

public class WheelChairController : MonoBehaviour
{
    private readonly SerialPort _serialPortNo = new("COM3", 9600);
    public Transform leftWheel;
    public Transform rightWheel;
    public float wheelRadius = 0.3f; // in meters
    public float ticksPerRevolution = 30f;
    public float timeSlice = 0.1f; // 100 ms

    private int _prevLeftTicks = 0;
    private int _prevRightTicks = 0;
    private Rigidbody _rb;

    private float _lastUpdateTime = 0f;
    private int _deltaLeft = 0;
    private int _deltaRight = 0;

    private void Start()
    {
        _serialPortNo.Open();
        _serialPortNo.ReadTimeout = 50;
        _rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (!_serialPortNo.IsOpen) return;

        try
        {
            var data = _serialPortNo.ReadLine().Trim();
            Debug.Log("Raw Data: " + data);

            // Example: "Left : -1 , Right: -1"
            var parts = data.Split(',');

            var leftTicks = 0;
            var rightTicks = 0;

            foreach (var part in parts)
            {
                if (part.Contains("Left"))
                    leftTicks = int.Parse(part.Split(':')[1].Trim());
                else if (part.Contains("Right"))
                    rightTicks = int.Parse(part.Split(':')[1].Trim());
            }

            _deltaLeft += leftTicks - _prevLeftTicks;
            _deltaRight += rightTicks - _prevRightTicks;

            _prevLeftTicks = leftTicks;
            _prevRightTicks = rightTicks;

            if (Time.time - _lastUpdateTime >= timeSlice)
            {
                var leftDistance = (2 * Mathf.PI * wheelRadius) * (_deltaLeft / ticksPerRevolution);
                var rightDistance = (2 * Mathf.PI * wheelRadius) * (_deltaRight / ticksPerRevolution);

                if (Mathf.Abs(_deltaLeft) > 0 && Mathf.Abs(_deltaRight) > 0)
                {
                    // Both wheels are moving
                    var averageDistance = (leftDistance + rightDistance) / 2f;
                    var movement = transform.forward * averageDistance;
                    _rb.MovePosition(_rb.position + movement);

                    var rotationDiff = (rightDistance - leftDistance) / (2f * wheelRadius); // radians
                    transform.Rotate(Vector3.up, rotationDiff * Mathf.Rad2Deg);
                }
                else if (Mathf.Abs(_deltaLeft) > 0)
                {
                    // Only left wheel is moving
                    var rotationDiff = -leftDistance / wheelRadius; // Turn right
                    transform.Rotate(Vector3.up, rotationDiff * Mathf.Rad2Deg);
                }
                else if (Mathf.Abs(_deltaRight) > 0)
                {
                    // Only right wheel is moving
                    var rotationDiff = rightDistance / wheelRadius; // Turn left
                    transform.Rotate(Vector3.up, rotationDiff * Mathf.Rad2Deg);
                }

                // Reset deltas and update time
                _deltaLeft = 0;
                _deltaRight = 0;
                _lastUpdateTime = Time.time;
            }
        }
        catch (TimeoutException)
        {
            // Ignore timeout errors
        }
        catch (Exception e)
        {
            Debug.LogWarning("Data parse error: " + e.Message);
        }
    }
}