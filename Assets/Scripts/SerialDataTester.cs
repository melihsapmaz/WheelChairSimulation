using UnityEngine;
using System;
using System.IO.Ports;

public class SerialDataTester : MonoBehaviour
{
    // --- Settings ---
    // Make sure these match your Arduino and system configuration
    public string portName = "COM6";
    public int baudRate = 9600;

    private SerialPort serialPort;
    private bool isPortOpen = false;

    void Start()
    {
        Debug.Log("Attempting to open serial port...");
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            
            // --- Connection Stability Settings ---
            // These settings can prevent issues where the Arduino resets on connect
            // or where communication stalls.
            serialPort.DtrEnable = false;
            serialPort.RtsEnable = false;
            
            // Set a short timeout to prevent the app from freezing if data stops
            serialPort.ReadTimeout = 50; 
            
            serialPort.Open();
            isPortOpen = true;
            Debug.Log($"Serial port '{portName}' opened successfully!");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error opening serial port: {e.Message}");
        }
    }

    void Update()
    {
        if (!isPortOpen) return;

        try
        {
            // Keep reading lines as long as there is data in the input buffer.
            // This is a robust way to handle messages arriving from the Arduino.
            while (serialPort.BytesToRead > 0)
            {
                // Read one complete line from the serial port
                string rawLine = serialPort.ReadLine();
                if (string.IsNullOrEmpty(rawLine)) continue;

                string line = rawLine.Trim();

                // Log the raw line to see exactly what Unity is receiving
                Debug.Log($"[RAW LINE RECEIVED]: {line}");

                // --- Safe Parsing Logic ---
                var parts = line.Split(',');
                int leftTicks = -999;  // Use a default value to easily see if it's updated
                int rightTicks = -999;

                foreach (var p in parts)
                {
                    var part = p.Trim();
                    if (part.StartsWith("L:"))
                    {
                        // Safely parse the number after "L:"
                        int.TryParse(part.Substring(2).Trim(), out leftTicks);
                    }
                    else if (part.StartsWith("R:"))
                    {
                        // Safely parse the number after "R:"
                        int.TryParse(part.Substring(2).Trim(), out rightTicks);
                    }
                }
                
                // Log the final parsed values in a distinct color
                Debug.Log($"<color=cyan>[PARSED VALUES]</color> Left: {leftTicks}, Right: {rightTicks}");
            }
        }
        catch (TimeoutException)
        {
            // A timeout is normal and expected if the buffer has partial data.
            // We can safely ignore it and wait for the next frame.
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Error during serial communication: {e.Message}");
        }
    }

    void OnApplicationQuit()
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            // It's crucial to close the port when the application quits.
            serialPort.Close();
            Debug.Log("Serial port closed.");
        }
    }
}