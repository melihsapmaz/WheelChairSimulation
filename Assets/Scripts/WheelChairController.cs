using System;
using System.IO.Ports;
using System.Threading;
using TMPro;
using UnityEngine;

public class WheelChairController : MonoBehaviour
{
    [Header("Connection")]
    [SerializeField] private string portName = "COM7"; // Editable in Inspector
    [SerializeField] private int baudRate = 9600;
    private SerialPort _serialPort;

    [Header("Wheelchair Physics Properties")]
    public float wheelRadius = 0.3f; // Radius of the drive wheels in meters
    public float axleLength = 0.5f; // IMPORTANT: Distance between the two drive wheels in meters
    
    // Wheel meshes for visual rotation
    [Header("Wheel Meshes")]
    public Transform leftWheel; // Assign in Inspector
    public Transform rightWheel; // Assign in Inspector

    [Header("Encoder/Motor Configuration")]
    public float ticksPerRevolution = 30f;
    public bool invertLeftEncoder;
    public bool invertRightEncoder;
    
    // UI tmp motor force
    [Header("UI Motor Force")]
    private int _motorForce; // Motor force percentage for UI, not used in physics
    [SerializeField] private TextMeshProUGUI motorForceText;
    
    

    private Rigidbody _rb;

    // These variables will be updated by Update() and used by FixedUpdate()
    private volatile int _deltaLeft;
    private volatile int _deltaRight;
    
    private int _prevLeftTicks;
    private int _prevRightTicks;
    
    // Unused motor feedback variables from original code, kept for completeness
    private int _lastSentPwm;
    private float _lastSendTime;
    //private const float FeedbackSendInterval = 0.5f;

    private void Start()
    {
        // Initialize the serial port
        _serialPort = new SerialPort(portName, baudRate);
        try
        {
            _serialPort.Open();
            _serialPort.ReadTimeout = 50;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Serial port error: " + e.Message);
        }

        _rb = GetComponent<Rigidbody>();
        _rb.sleepThreshold = 0.0f;
    }

    // Update is called once per frame. Use it for reading input.
    private void Update()
    {
        if (!_serialPort.IsOpen) return;

        try
        {
            var data = _serialPort.ReadLine().Trim();
            var parts = data.Split(',');

            var leftTicks = 0;
            var rightTicks = 0;

            foreach (var part in parts)
            {
                if (part.Contains("L:"))
                    leftTicks = int.Parse(part.Split(':')[1].Trim());
                else if (part.Contains("R:"))
                    rightTicks = int.Parse(part.Split(':')[1].Trim());
            }

            if (leftTicks == _prevLeftTicks && rightTicks == _prevRightTicks)
            {
                return; // No new encoder data
            }
            
            Debug.Log("Received encoder data: L=" + leftTicks + ", R=" + rightTicks);

            // Atomically add the difference to our deltas.
            // This is safer when one thread (Update) writes and another (FixedUpdate) reads.
            Interlocked.Add(ref _deltaLeft, leftTicks - _prevLeftTicks);
            Interlocked.Add(ref _deltaRight, rightTicks - _prevRightTicks);
            
            _prevLeftTicks = leftTicks;
            _prevRightTicks = rightTicks;
        }
        catch (TimeoutException) { /* Ignore timeout errors, they are expected */ }
        catch (Exception e)
        {
            Debug.LogWarning("Data parse error: " + e.Message);
        }
    }

    // FixedUpdate is called on a fixed timer, synchronized with the physics engine.
    // Use it for all Rigidbody and physics operations.
    private void FixedUpdate()
    {
        if (_deltaLeft == 0 && _deltaRight == 0)
        {
            return; // No movement to apply
        }

        // 1. Calculate distance traveled by each wheel from accumulated deltas
        var leftDistance = (2 * Mathf.PI * wheelRadius) * (_deltaLeft / ticksPerRevolution);
        var rightDistance = (2 * Mathf.PI * wheelRadius) * (_deltaRight / ticksPerRevolution);
        
        // --- FIX: Apply inversion from the Inspector if needed ---
        if (invertLeftEncoder) leftDistance *= -1;
        if (invertRightEncoder) rightDistance *= -1;

        // 2. Calculate forward movement and rotation of the chassis
        var forwardDistance = (leftDistance + rightDistance) / 2.0f;
        var rotationAngleRad = (rightDistance - leftDistance) / axleLength;

        // 3. Apply movement and rotation using the Rigidbody for stable physics
        var movement = transform.forward * forwardDistance;
        _rb.MovePosition(_rb.position + movement);
        
        var rotation = Quaternion.Euler(Vector3.up * (rotationAngleRad * Mathf.Rad2Deg));
        _rb.MoveRotation(_rb.rotation * rotation);
        
        // 5. Rotate the wheels visually
        float leftRotationDegrees = (_deltaLeft / ticksPerRevolution) * 360f;
        float rightRotationDegrees = (_deltaRight / ticksPerRevolution) * 360f;

        leftWheel.Rotate(Vector3.right, leftRotationDegrees);
        rightWheel.Rotate(Vector3.right, rightRotationDegrees);

        
        // 4. Reset deltas now that the movement has been applied
        _deltaLeft = 0;
        _deltaRight = 0;
    }

    private void OnCollisionStay(Collision collision)
    {
        Vector3 avgNormal = Vector3.zero;
        int contactCount = 0;

        foreach (var contact in collision.contacts)
        {
            avgNormal += contact.normal;
            contactCount++;
        }

        if (contactCount == 0) return;

        avgNormal.Normalize();

        // Use a consistent global axis (Z = forward) for ramp calculation
        float rampAngle = Vector3.SignedAngle(avgNormal, Vector3.up, Vector3.Cross(Vector3.up, transform.forward));

        // Optional: stabilize angle by rounding
        rampAngle = Mathf.Round(rampAngle * 10f) / 10f;

        // Dead zone: treat near-flat surfaces as flat
        if (Mathf.Abs(rampAngle) < 2f) rampAngle = 0f;
        int targetPwm = 0;

        if (rampAngle != 0f && Mathf.Abs(rampAngle) <= 30f)
        {
            float absAngle = Mathf.Clamp(Mathf.Abs(rampAngle), 0f, 30f);
            float pwm = Mathf.Lerp(16f, 255f, absAngle / 30f);
            targetPwm = rampAngle > 0 ? Mathf.RoundToInt(pwm) : -Mathf.RoundToInt(pwm);
        }

        // Calculate motor force percentage for UI, 100 % for 255 PWM, 0% for 15 PWM
        _motorForce = Mathf.Clamp(Mathf.RoundToInt((Mathf.Abs(targetPwm) - 15) / 240f * 100), 0, 100);
        
        if (motorForceText != null)
        {
            motorForceText.text = "Motor Force: " + _motorForce + "%";
        }

        //ApplyMotorFeedback(targetPwm);
    }

    /*
    private void ApplyMotorFeedback(int pwm)
    {
        if (!_serialPort.IsOpen) return;
        if (pwm == _lastSentPwm && Time.time - _lastSendTime < FeedbackSendInterval) return;

        _lastSentPwm = pwm;
        _lastSendTime = Time.time;

        pwm = Mathf.Clamp(pwm, -255, 255);

        try
        {
            _serialPort.WriteLine("L" + pwm);
            _serialPort.WriteLine("R" + pwm);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Motor feedback send failed: " + e.Message);
        }
    }
    */
    
    private void OnApplicationQuit()
    {
        if (_serialPort is not { IsOpen: true }) return;

        try
        {
            _serialPort.WriteLine("L0");
            _serialPort.WriteLine("R0");
            _serialPort.Close();
        }
        catch (Exception e)
        {
            Debug.LogWarning("Error on quit: " + e.Message);
        }
    }
}
