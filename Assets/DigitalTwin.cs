using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace Unity.XRTemplate
{
    /// <summary>
    /// Component representing a digital twin of a physical machine.
    /// This handles interaction, visualization and data exchange with real machine.
    /// </summary>
    public class DigitalTwin : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The ID of the machine this digital twin represents")]
        private string machineId;

        [SerializeField]
        [Tooltip("Status indicator material for normal operation")]
        private Material normalStatusMaterial;

        [SerializeField]
        [Tooltip("Status indicator material for warning state")]
        private Material warningStatusMaterial;

        [SerializeField]
        [Tooltip("Status indicator material for error state")]
        private Material errorStatusMaterial;

        [SerializeField]
        [Tooltip("Renderer components that should change with status")]
        private List<Renderer> statusIndicatorRenderers = new List<Renderer>();

        [SerializeField]
        [Tooltip("Visual feedback for when the digital twin is selected")]
        private GameObject selectionHighlight;

        [SerializeField]
        [Tooltip("How long to play the spawn animation")]
        private float spawnAnimationDuration = 1.5f;

        [SerializeField]
        [Tooltip("Information panel prefab to show when inspecting the digital twin")]
        private GameObject infoPanel;

        [SerializeField]
        [Tooltip("MQTT topic prefix for this machine's telemetry")]
        private string telemetryTopicPrefix = "machine/telemetry/";

        [SerializeField]
        [Tooltip("MQTT topic prefix for sending commands to this machine")]
        private string commandTopicPrefix = "machine/command/";

        private XRGrabInteractable grabInteractable;
        private MQTTCommunicationManager mqttManager;
        private string telemetryTopic;
        private string commandTopic;
        private bool isInitialized = false;
        private GameObject spawnedInfoPanel;

        // Machine state
        private enum MachineStatus
        {
            Normal,
            Warning,
            Error
        }

        private MachineStatus currentStatus = MachineStatus.Normal;
        private Dictionary<string, object> telemetryData = new Dictionary<string, object>();

        private void Awake()
        {
            // Set up the interactable component
            grabInteractable = GetComponent<XRGrabInteractable>();
            if (grabInteractable == null)
            {
                grabInteractable = gameObject.AddComponent<XRGrabInteractable>();
                
                // Configure the interactable for inspection rather than traditional grabbing
                grabInteractable.movementType = XRBaseInteractable.MovementType.VelocityTracking;
                grabInteractable.throwOnDetach = false;
            }

            if (selectionHighlight != null)
            {
                selectionHighlight.SetActive(false);
            }

            // Find the MQTT manager
            mqttManager = FindObjectOfType<MQTTCommunicationManager>();
        }

        private void OnEnable()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.AddListener(OnSelectEntered);
                grabInteractable.selectExited.AddListener(OnSelectExited);
            }
        }

        private void OnDisable()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
                grabInteractable.selectExited.RemoveListener(OnSelectExited);
            }

            // Unsubscribe from MQTT
            if (mqttManager != null && !string.IsNullOrEmpty(telemetryTopic))
            {
                mqttManager.Unsubscribe(telemetryTopic);
            }
        }

        public void Initialize(string id)
        {
            if (isInitialized)
                return;

            machineId = id;
            telemetryTopic = telemetryTopicPrefix + machineId;
            commandTopic = commandTopicPrefix + machineId;

            // Subscribe to machine telemetry if MQTT manager is available
            if (mqttManager != null)
            {
                mqttManager.Subscribe(telemetryTopic, OnTelemetryReceived);
            }

            // Play the spawn animation
            StartCoroutine(PlaySpawnAnimation());

            isInitialized = true;
        }

        private IEnumerator PlaySpawnAnimation()
        {
            // Start small
            transform.localScale = Vector3.zero;
            
            // Scale up with bounce effect
            float timeElapsed = 0;
            while (timeElapsed < spawnAnimationDuration)
            {
                float normalizedTime = timeElapsed / spawnAnimationDuration;
                
                // Elastic ease-out formula for bouncy effect
                const float c = 1.70158f;
                const float c4 = (c + 1) * 1.525f;
                float progress = normalizedTime;
                
                if (normalizedTime < 1)
                {
                    progress = 1 - Mathf.Pow(2, -10 * normalizedTime) * Mathf.Sin((normalizedTime * 10 - 0.75f) * (2 * Mathf.PI) / 3);
                }
                
                transform.localScale = Vector3.one * progress;
                
                timeElapsed += Time.deltaTime;
                yield return null;
            }
            
            // Ensure we end at exactly the target scale
            transform.localScale = Vector3.one;
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (selectionHighlight != null)
            {
                selectionHighlight.SetActive(true);
            }
            
            ShowInfoPanel();
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (selectionHighlight != null)
            {
                selectionHighlight.SetActive(false);
            }
            
            HideInfoPanel();
        }

        private void ShowInfoPanel()
        {
            if (infoPanel != null && spawnedInfoPanel == null)
            {
                // Position the panel near the digital twin
                Vector3 panelPosition = transform.position + Vector3.up * 0.5f;
                
                spawnedInfoPanel = Instantiate(infoPanel, panelPosition, Quaternion.identity);
                
                // Update panel content with machine data
                UpdateInfoPanel();
            }
        }

        private void HideInfoPanel()
        {
            if (spawnedInfoPanel != null)
            {
                Destroy(spawnedInfoPanel);
                spawnedInfoPanel = null;
            }
        }

        private void UpdateInfoPanel()
        {
            if (spawnedInfoPanel == null)
                return;
            
            // Update the info panel with machine data
            // This implementation will depend on your specific UI setup
            
            // Example with a TextMeshProUGUI component:
            /*
            TMPro.TextMeshProUGUI infoText = spawnedInfoPanel.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (infoText != null)
            {
                string infoContent = $"Machine ID: {machineId}\n";
                infoContent += $"Status: {currentStatus}\n";
                
                foreach (var data in telemetryData)
                {
                    infoContent += $"{data.Key}: {data.Value}\n";
                }
                
                infoText.text = infoContent;
            }
            */
        }

        private void OnTelemetryReceived(string message)
        {
            // Parse the telemetry data from JSON
            // This would typically update the telemetryData dictionary
            
            // Example JSON parsing (simplified):
            /*
            try 
            {
                var data = JsonUtility.FromJson<TelemetryData>(message);
                telemetryData.Clear();
                
                // Process telemetry data
                foreach (var item in data.metrics)
                {
                    telemetryData[item.key] = item.value;
                }
                
                // Update machine status based on telemetry
                UpdateMachineStatus(data.status);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error parsing telemetry data: {e.Message}");
            }
            */
            
            // For now, just update the info panel
            UpdateInfoPanel();
        }
        
        private void UpdateMachineStatus(MachineStatus newStatus)
        {
            if (newStatus == currentStatus)
                return;
                
            currentStatus = newStatus;
            
            // Update visual indicators
            Material statusMaterial = null;
            
            switch (currentStatus)
            {
                case MachineStatus.Normal:
                    statusMaterial = normalStatusMaterial;
                    break;
                    
                case MachineStatus.Warning:
                    statusMaterial = warningStatusMaterial;
                    break;
                    
                case MachineStatus.Error:
                    statusMaterial = errorStatusMaterial;
                    break;
            }
            
            if (statusMaterial != null)
            {
                foreach (var renderer in statusIndicatorRenderers)
                {
                    if (renderer != null)
                    {
                        renderer.material = statusMaterial;
                    }
                }
            }
            
            // Update the info panel if it's visible
            UpdateInfoPanel();
        }
        
        /// <summary>
        /// Send a command to the physical machine via MQTT
        /// </summary>
        /// <param name="commandName">Name of the command to execute</param>
        /// <param name="parameters">Optional parameters for the command</param>
        public void SendCommand(string commandName, Dictionary<string, object> parameters = null)
        {
            if (mqttManager == null || string.IsNullOrEmpty(commandTopic))
                return;
                
            // Construct command message
            string commandMessage = $"{{\"command\":\"{commandName}\",\"machineId\":\"{machineId}\"";
            
            if (parameters != null && parameters.Count > 0)
            {
                commandMessage += ",\"parameters\":{";
                bool first = true;
                
                foreach (var param in parameters)
                {
                    if (!first)
                        commandMessage += ",";
                        
                    // This is simplified and won't work for complex objects
                    commandMessage += $"\"{param.Key}\":{JsonValue(param.Value)}";
                    first = false;
                }
                
                commandMessage += "}";
            }
            
            commandMessage += "}";
            
            // Send the command
            mqttManager.Publish(commandTopic, commandMessage);
        }
        
        // Helper method to convert a value to JSON representation
        private string JsonValue(object value)
        {
            if (value == null)
                return "null";
                
            if (value is bool)
                return value.ToString().ToLowerInvariant();
                
            if (value is string)
                return $"\"{value}\"";
                
            if (value is int || value is float || value is double)
                return value.ToString();
                
            // For complex objects, use JsonUtility
            return JsonUtility.ToJson(value);
        }
    }
}