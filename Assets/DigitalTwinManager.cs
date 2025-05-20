using System.Collections.Generic;
using UnityEngine;

namespace Unity.XRTemplate
{
    /// <summary>
    /// Manages the different digital twin prefabs that can be spawned based on machine recognition.
    /// </summary>
    public class DigitalTwinManager : MonoBehaviour
    {
        [System.Serializable]
        public class MachineMapping
        {
            public string machineId;
            public GameObject digitalTwinPrefab;

            [TextArea(3, 5)]
            public string description;
        }

        [SerializeField]
        [Tooltip("List of machine IDs and their corresponding digital twin prefabs")]
        private List<MachineMapping> machineMappings = new List<MachineMapping>();

        [SerializeField]
        [Tooltip("Default digital twin prefab if specific machine ID is not found")]
        private GameObject defaultDigitalTwin;

        [SerializeField]
        [Tooltip("Distance from camera to spawn the digital twin")]
        private float spawnDistance = 1.0f;

        [SerializeField]
        [Tooltip("Offset from detected position (useful for aligning with real-world object)")]
        private Vector3 spawnOffset = Vector3.zero;

        private Camera mainCamera;
        private Dictionary<string, GameObject> machineMap = new Dictionary<string, GameObject>();

        private void Awake()
        {
            // Initialize the dictionary for faster lookups
            foreach (var mapping in machineMappings)
            {
                if (!string.IsNullOrEmpty(mapping.machineId) && mapping.digitalTwinPrefab != null)
                {
                    machineMap[mapping.machineId] = mapping.digitalTwinPrefab;
                }
            }

            mainCamera = Camera.main;
        }

        /// <summary>
        /// Spawns a digital twin for the specified machine ID.
        /// </summary>
        /// <param name="machineId">The ID of the recognized machine</param>
        /// <param name="position">Optional position to spawn at. If null, will spawn in front of camera</param>
        /// <param name="rotation">Optional rotation to use. If null, will orient toward camera</param>
        /// <returns>The spawned digital twin GameObject, or null if spawn failed</returns>
        public GameObject SpawnDigitalTwin(
            string machineId,
            Vector3? position = null,
            Quaternion? rotation = null
        )
        {
            // Find the right prefab
            GameObject prefabToSpawn;
            if (!machineMap.TryGetValue(machineId, out prefabToSpawn))
            {
                if (defaultDigitalTwin != null)
                {
                    prefabToSpawn = defaultDigitalTwin;
                    Debug.LogWarning(
                        $"Machine ID '{machineId}' not found in mappings. Using default digital twin."
                    );
                }
                else
                {
                    Debug.LogError(
                        $"Machine ID '{machineId}' not found and no default digital twin specified."
                    );
                    return null;
                }
            }

            // Determine spawn position
            Vector3 spawnPosition;
            if (position.HasValue)
            {
                spawnPosition = position.Value;
            }
            else if (mainCamera != null)
            {
                spawnPosition =
                    mainCamera.transform.position + mainCamera.transform.forward * spawnDistance;
            }
            else
            {
                Debug.LogError("No spawn position provided and no main camera found.");
                return null;
            }

            // Apply offset
            spawnPosition += spawnOffset;

            // Determine rotation
            Quaternion spawnRotation;
            if (rotation.HasValue)
            {
                spawnRotation = rotation.Value;
            }
            else if (mainCamera != null)
            {
                // Make the digital twin face the camera
                Vector3 lookDirection = mainCamera.transform.position - spawnPosition;
                lookDirection.y = 0; // Keep the twin upright (if needed)

                if (lookDirection != Vector3.zero)
                {
                    spawnRotation = Quaternion.LookRotation(lookDirection);
                }
                else
                {
                    spawnRotation = Quaternion.identity;
                }
            }
            else
            {
                spawnRotation = Quaternion.identity;
            }

            // Instantiate the digital twin
            GameObject twinInstance = Instantiate(prefabToSpawn, spawnPosition, spawnRotation);
            twinInstance.name = $"DigitalTwin_{machineId}";

            // Add any extra initialization here
            DigitalTwin twinComponent = twinInstance.GetComponent<DigitalTwin>();
            if (twinComponent != null)
            {
                twinComponent.Initialize(machineId);
            }

            return twinInstance;
        }

        /// <summary>
        /// Gets the digital twin prefab for a specific machine ID without spawning it.
        /// </summary>
        /// <param name="machineId">The ID of the machine to look up</param>
        /// <returns>The prefab for the specified machineId, or the default prefab if not found</returns>
        public GameObject GetDigitalTwinPrefab(string machineId)
        {
            if (machineMap.TryGetValue(machineId, out GameObject prefab))
            {
                return prefab;
            }
            return defaultDigitalTwin;
        }

        /// <summary>
        /// Spawns a digital twin for the specified machine ID that starts invisible.
        /// </summary>
        /// <param name="machineId">The ID of the recognized machine</param>
        /// <param name="position">Optional position to spawn at. If null, will spawn in front of camera</param>
        /// <param name="rotation">Optional rotation to use. If null, will orient toward camera</param>
        /// <param name="startVisible">Whether the twin should start visible or invisible</param>
        /// <returns>The spawned digital twin GameObject, or null if spawn failed</returns>
        public GameObject SpawnDigitalTwin(
            string machineId,
            Vector3? position = null,
            Quaternion? rotation = null,
            bool startVisible = false
        )
        {
            // Find the right prefab
            GameObject prefabToSpawn;
            if (!machineMap.TryGetValue(machineId, out prefabToSpawn))
            {
                if (defaultDigitalTwin != null)
                {
                    prefabToSpawn = defaultDigitalTwin;
                    Debug.LogWarning(
                        $"Machine ID '{machineId}' not found in mappings. Using default digital twin."
                    );
                }
                else
                {
                    Debug.LogError(
                        $"Machine ID '{machineId}' not found and no default digital twin specified."
                    );
                    return null;
                }
            }

            // Determine spawn position
            Vector3 spawnPosition;
            if (position.HasValue)
            {
                spawnPosition = position.Value;
            }
            else if (mainCamera != null)
            {
                spawnPosition =
                    mainCamera.transform.position + mainCamera.transform.forward * spawnDistance;
            }
            else
            {
                Debug.LogError("No spawn position provided and no main camera found.");
                return null;
            }

            // Apply offset
            spawnPosition += spawnOffset;

            // Determine rotation
            Quaternion spawnRotation;
            if (rotation.HasValue)
            {
                spawnRotation = rotation.Value;
            }
            else if (mainCamera != null)
            {
                // Make the digital twin face the camera
                Vector3 lookDirection = mainCamera.transform.position - spawnPosition;
                lookDirection.y = 0; // Keep the twin upright

                if (lookDirection != Vector3.zero)
                {
                    spawnRotation = Quaternion.LookRotation(lookDirection);
                }
                else
                {
                    spawnRotation = Quaternion.identity;
                }
            }
            else
            {
                spawnRotation = Quaternion.identity;
            }

            // Instantiate the digital twin
            GameObject twinInstance = Instantiate(prefabToSpawn, spawnPosition, spawnRotation);
            twinInstance.name = $"DigitalTwin_{machineId}";

            // Set initial visibility
            SetTwinVisibility(twinInstance, startVisible);

            // Initialize the twin component
            DigitalTwin twinComponent = twinInstance.GetComponent<DigitalTwin>();
            if (twinComponent != null)
            {
                twinComponent.Initialize(machineId);
            }

            return twinInstance;
        }

        /// <summary>
        /// Makes a previously spawned digital twin visible or invisible
        /// </summary>
        /// <param name="twin">The digital twin GameObject</param>
        /// <param name="visible">Whether to make it visible or invisible</param>
        public void SetTwinVisibility(GameObject twin, bool visible)
        {
            if (twin == null)
                return;

            // Get all renderers
            Renderer[] renderers = twin.GetComponentsInChildren<Renderer>();

            // Set visibility for all renderers
            foreach (var renderer in renderers)
            {
                renderer.enabled = visible;
            }

            // Get all colliders if you want to disable interaction while invisible
            Collider[] colliders = twin.GetComponentsInChildren<Collider>();

            // Set colliders enabled/disabled based on visibility
            foreach (var collider in colliders)
            {
                collider.enabled = visible;
            }
        }

        /// <summary>
        /// Makes a previously spawned digital twin visible
        /// </summary>
        /// <param name="machineId">The ID of the digital twin to make visible</param>
        public void RevealDigitalTwin(string machineId)
        {
            GameObject twin = GameObject.Find($"DigitalTwin_{machineId}");
            if (twin != null)
            {
                SetTwinVisibility(twin, true);

                // Play reveal animation if the twin has the component
                DigitalTwin twinComponent = twin.GetComponent<DigitalTwin>();
                if (twinComponent != null)
                {
                    twinComponent.PlayRevealAnimation();
                }
            }
        }
    }
}
