using System;
using System.Collections;
using Unity.XRTemplate;
using UnityEngine;

public class MachineScanner : MonoBehaviour
{
    [SerializeField]
    private Camera xrCamera;

    [SerializeField]
    private string mqttImageTopic = "machine/scan/image";

    [SerializeField]
    private string mqttResultTopic = "machine/scan/result";

    [SerializeField]
    private MQTTCommunicationManager mqttManager;

    [SerializeField]
    private Unity.XRTemplate.DigitalTwinManager digitalTwinManager;

    [SerializeField]
    private ScanUIController scanUIController;

    private bool isScanning = false;
    private int scanTimeoutSeconds = 10; // Timeout for waiting response from Node-RED

    private void Start()
    {
        // If camera is not set, try to find the main camera
        if (xrCamera == null)
            xrCamera = Camera.main;

        // If MQTT manager is not set, try to find it
        if (mqttManager == null)
            mqttManager = FindObjectOfType<MQTTCommunicationManager>();

        // Subscribe to the result topic to receive machine IDs from Node-RED
        if (mqttManager != null)
        {
            mqttManager.Subscribe(mqttResultTopic, OnMachineIdReceived);
        }

        Debug.Log("MachineScanner initialized successfully");
    }

    // Method that will be called by the ScanUIController or ScanButtonHandler
    public void TriggerScan()
    {
        if (isScanning)
            return;

        StartCoroutine(ScanProcess());
    }

    private IEnumerator ScanProcess()
    {
        isScanning = true;
        Debug.Log("Starting scan process...");

        // Show scanning in progress UI
        if (scanUIController != null)
        {
            scanUIController.ShowScanningIndicator(true);
        }

        // Capture the image
        Texture2D capturedImage = CaptureImageFromCamera();

        if (capturedImage == null)
        {
            Debug.LogError("Failed to capture camera image");
            ScanFailed("Failed to capture image from camera");
            yield break;
        }

        Debug.Log($"Captured image: {capturedImage.width}x{capturedImage.height}");

        // Convert the image to base64 for MQTT transmission
        string base64Image = ConvertTextureToBase64(capturedImage);

        // Send the image to Node-RED via MQTT
        if (mqttManager != null)
        {
            // Create a simple JSON message with the image data
            string jsonMessage =
                $"{{\"timestamp\":\"{DateTime.Now.ToString("o")}\",\"image\":\"{base64Image}\"}}";
            mqttManager.Publish(mqttImageTopic, jsonMessage);
            Debug.Log("Image sent to Node-RED via MQTT");

            // Wait for response (this will be handled by OnMachineIdReceived callback)
            float timeElapsed = 0;
            while (isScanning && timeElapsed < scanTimeoutSeconds)
            {
                timeElapsed += Time.deltaTime;
                yield return null;
            }

            // If we're still scanning after timeout, it failed
            if (isScanning)
            {
                ScanFailed("Recognition timeout - no response received from server");
            }
        }
        else
        {
            ScanFailed("MQTT manager not found");
        }
    }

    private void OnMachineIdReceived(string message)
    {
        // Parse the JSON message from Node-RED
        try
        {
            // Simplified JSON parsing - in production use a proper JSON library
            string machineId = ExtractMachineIdFromJson(message);

            if (!string.IsNullOrEmpty(machineId))
            {
                Debug.Log($"Received machine ID: {machineId}");
                SpawnDigitalTwin(machineId);
            }
            else
            {
                ScanFailed("No machine recognized");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error parsing machine ID message: {ex.Message}");
            ScanFailed("Error processing recognition result");
        }
    }

    private void SpawnDigitalTwin(string machineId)
    {
        if (digitalTwinManager != null)
        {
            GameObject spawnedTwin = digitalTwinManager.SpawnDigitalTwin(machineId);

            if (spawnedTwin != null)
            {
                // Show success in UI
                if (scanUIController != null)
                {
                    scanUIController.ShowResult(
                        true,
                        $"Machine {machineId} identified successfully!"
                    );
                }
                Debug.Log($"Digital twin spawned for machine ID: {machineId}");
            }
            else
            {
                ScanFailed($"Failed to spawn digital twin for machine ID: {machineId}");
            }
        }
        else
        {
            ScanFailed("Digital Twin Manager not found");
        }

        isScanning = false;
    }

    private void ScanFailed(string errorMessage)
    {
        Debug.LogError($"Scan failed: {errorMessage}");

        if (scanUIController != null)
        {
            scanUIController.ShowScanningIndicator(false);
            scanUIController.ShowResult(false, $"Scan failed: {errorMessage}");
        }

        isScanning = false;
    }

    private Texture2D CaptureImageFromCamera()
    {
        if (xrCamera == null)
        {
            Debug.LogError("No XR Camera assigned for capture");
            return null;
        }

        // Create a texture the size of the screen
        Texture2D screenCapture = new Texture2D(
            Screen.width,
            Screen.height,
            TextureFormat.RGB24,
            false
        );

        // Read the pixels from the camera's render target
        RenderTexture.active = xrCamera.targetTexture != null ? xrCamera.targetTexture : null;

        // Read pixels from the screen
        screenCapture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        screenCapture.Apply();

        return screenCapture;
    }

    private string ConvertTextureToBase64(Texture2D texture)
    {
        // Convert to JPG to reduce size
        byte[] jpgBytes = texture.EncodeToJPG(75); // 75% quality
        return Convert.ToBase64String(jpgBytes);
    }

    private string ExtractMachineIdFromJson(string json)
    {
        // Simple JSON parsing - in production, use JsonUtility or Newtonsoft.Json
        // Expected format: {"machineId": "motor_001", "confidence": 0.95}

        if (string.IsNullOrEmpty(json))
            return null;

        // Look for "machineId":" pattern and extract the value
        const string pattern = "\"machineId\":\"";
        int startIndex = json.IndexOf(pattern);

        if (startIndex >= 0)
        {
            startIndex += pattern.Length;
            int endIndex = json.IndexOf("\"", startIndex);

            if (endIndex >= 0)
            {
                return json.Substring(startIndex, endIndex - startIndex);
            }
        }

        return null;
    }
}
