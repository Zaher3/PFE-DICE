using System;
using System.Collections;
using Unity.XRTemplate; // Make sure to add the namespace
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
    private Unity.XRTemplate.ScanUIController scanUIController;

    private bool isScanning = false;

private void Start()
{
    // If camera is not set, try to find the main camera
    if (xrCamera == null)
        xrCamera = Camera.main;

    // If MQTT manager is not set, try to find it
    if (mqttManager == null)
        mqttManager = FindObjectOfType<MQTTCommunicationManager>();

    // Subscribe to the MQTT connected event
    if (mqttManager != null)
    {
        // Subscribe to connection events for future connections
        mqttManager.onConnected += OnMQTTConnected;
        mqttManager.onConnectionFailed += OnMQTTConnectionFailed;
        mqttManager.onConnectionLost += OnMQTTConnectionLost;
        
        // If already connected, subscribe immediately
        if (mqttManager.IsConnected)
        {
            SubscribeToTopics();
        }
        else if (!mqttManager.IsConnecting)
        {
            // Start connection process if not already connecting
            mqttManager.StartConnectionProcess();
        }
    }
    else
    {
        Debug.LogError("MQTTCommunicationManager not found");
    }

    Debug.Log("MachineScanner initialized successfully");
}

private void OnDestroy()
{
    // Clean up events
    if (mqttManager != null)
    {
        mqttManager.onConnected -= OnMQTTConnected;
        mqttManager.onConnectionFailed -= OnMQTTConnectionFailed;
        mqttManager.onConnectionLost -= OnMQTTConnectionLost;
    }
}

private void OnMQTTConnected()
{
    Debug.Log("MQTT Connected! Subscribing to machine scan topics...");
    SubscribeToTopics();
}

private void OnMQTTConnectionFailed(string reason)
{
    Debug.LogWarning($"MQTT Connection failed: {reason}");
    
    // You could update UI here to show connection status
    if (scanUIController != null)
    {
        scanUIController.ShowConnectionStatus(false, $"Connection failed: {reason}");
    }
}

private void OnMQTTConnectionLost()
{
    Debug.LogWarning("MQTT Connection lost. Reconnecting...");
    
    // You could update UI here to show connection status
    if (scanUIController != null)
    {
        scanUIController.ShowConnectionStatus(false, "Connection lost. Reconnecting...");
    }
}

private void SubscribeToTopics()
{
    // Subscribe to the result topic to receive machine IDs from Node-RED
    mqttManager.Subscribe(mqttResultTopic, OnMachineIdReceived);
    Debug.Log("Subscribed to MQTT topics for machine scanning");
    
    // You could update UI here to show connection status
    if (scanUIController != null)
    {
        scanUIController.ShowConnectionStatus(true, "Connected to MQTT broker");
    }
}

    // Method called by the ScanButtonHandler
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

        // Update UI to show scanning
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
        if (mqttManager != null && mqttManager.IsConnected)
        {
            // Create a simple JSON message with the image data
            string jsonMessage =
                $"{{\"timestamp\":\"{DateTime.Now.ToString("o")}\",\"image\":\"{base64Image}\"}}";
            mqttManager.Publish(mqttImageTopic, jsonMessage);
            Debug.Log("Image sent to Node-RED via MQTT");
        }
        else
        {
            ScanFailed("MQTT manager not connected");
        }

        // Wait for the response in OnMachineIdReceived callback
    }

    private void OnMachineIdReceived(string message)
    {
        // Parse the JSON message from Node-RED
        try
        {
            string machineId = ExtractMachineIdFromJson(message);

            if (!string.IsNullOrEmpty(machineId))
            {
                Debug.Log($"Received machine ID: {machineId}");

                // Check if there's already a model with this ID in the scene
                GameObject existingModel = GameObject.Find($"moteur_electrique_{machineId}");
                if (existingModel != null)
                {
                    // Show the pre-placed model
                    HideUntilRecognized hideScript =
                        existingModel.GetComponent<HideUntilRecognized>();
                    if (hideScript != null)
                    {
                        hideScript.ShowModel();

                        // Show success UI
                        if (scanUIController != null)
                        {
                            scanUIController.ShowScanningIndicator(false);
                            scanUIController.ShowResult(
                                true,
                                $"Machine {machineId} identified successfully!"
                            );
                        }
                    }
                    else
                    {
                        // If the script isn't attached, spawn a new digital twin
                        SpawnDigitalTwin(machineId);
                    }
                }
                else
                {
                    // No pre-placed model found, spawn a new one
                    SpawnDigitalTwin(machineId);
                }
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
            // Spawn the digital twin invisible initially
            GameObject spawnedTwin = digitalTwinManager.SpawnDigitalTwin(
                machineId,
                null,
                null,
                false
            );

            if (spawnedTwin != null)
            {
                // Show success in UI
                if (scanUIController != null)
                {
                    scanUIController.ShowScanningIndicator(false);
                    scanUIController.ShowResult(
                        true,
                        $"Machine {machineId} identified successfully!"
                    );
                }

                Debug.Log($"Digital twin spawned for machine ID: {machineId}");

                // Wait a brief moment, then reveal the digital twin
                StartCoroutine(RevealTwinAfterDelay(machineId, 0.5f));
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

    private IEnumerator RevealTwinAfterDelay(string machineId, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (digitalTwinManager != null)
        {
            digitalTwinManager.RevealDigitalTwin(machineId);
        }
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
        byte[] jpgBytes = texture.EncodeToJPG(75); // 75% quality, reduce if needed
        return Convert.ToBase64String(jpgBytes);
    }

    private string ExtractMachineIdFromJson(string json)
    {
        // Simple JSON parsing - in production use JsonUtility
        if (string.IsNullOrEmpty(json))
            return null;

        // Check for error messages
        if (json.Contains("\"error\":"))
        {
            Debug.LogWarning($"Received error response: {json}");
            return null;
        }

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
