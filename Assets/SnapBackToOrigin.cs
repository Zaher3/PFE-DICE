using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Component that allows an object to snap back to its original position
/// when released within a specified distance of its origin,
/// with audio feedback for entering the snap zone.
/// </summary>
public class SnapBackToOrigin : MonoBehaviour
{
    [Header("Snap Settings")]
    [Tooltip("Distance threshold for snapping back to original position")]
    [SerializeField] private float snapDistance = 0.3f;
    
    [Tooltip("How fast the object snaps back to its original position")]
    [SerializeField] private float snapSpeed = 10f;
    
    [Header("Visual Indicator")]
    [Tooltip("Optional visual indicator for snap zone")]
    [SerializeField] private GameObject snapZoneIndicator;
    
    [Tooltip("Whether to show a visual indicator of the snap zone")]
    [SerializeField] private bool showSnapZone = false;
    
    [Header("Audio Feedback")]
    [Tooltip("Audio source for snap zone sounds")]
    [SerializeField] private AudioSource audioSource;
    
    [Tooltip("Sound played when entering the snap zone")]
    [SerializeField] private AudioClip enterSnapZoneSound;
    
    [Tooltip("Sound played when snapping back to origin")]
    [SerializeField] private AudioClip snapBackSound;
    
    [Tooltip("Volume for snap sounds")]
    [Range(0f, 1f)]
    [SerializeField] private float soundVolume = 0.5f;
    
    [Header("Haptic Feedback")]
    [Tooltip("Intensity of haptic feedback when entering snap zone (0-1)")]
    [Range(0f, 1f)]
    [SerializeField] private float hapticIntensity = 0.5f;
    
    [Tooltip("Duration of haptic feedback in seconds")]
    [SerializeField] private float hapticDuration = 0.1f;
    
    // Original position and rotation
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    
    // XR Grab Interactable component
    private XRGrabInteractable grabInteractable;
    
    // Whether object is currently snapping back
    private bool isSnapping = false;
    
    // Cache for object's rigidbody
    private Rigidbody rb;
    
    // Tracking whether we're currently in the snap zone
    private bool isInSnapZone = false;

    private void Awake()
    {
        // Cache the components
        grabInteractable = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();
        
        // Create AudioSource if not assigned
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1.0f; // Full 3D sound
            audioSource.volume = soundVolume;
            audioSource.playOnAwake = false;
        }
        
        // Store the original position and rotation
        originalPosition = transform.position;
        originalRotation = transform.rotation;
        
        // Set up snap zone indicator if needed
        if (snapZoneIndicator != null)
        {
            snapZoneIndicator.transform.position = originalPosition;
            snapZoneIndicator.SetActive(showSnapZone);
            
            // Scale indicator based on snap distance
            snapZoneIndicator.transform.localScale = new Vector3(
                snapDistance * 2, 
                snapDistance * 2, 
                snapDistance * 2
            );
        }
    }

    private void OnEnable()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(OnGrabbed);
            grabInteractable.selectExited.AddListener(OnGrabReleased);
        }
    }

    private void OnDisable()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            grabInteractable.selectExited.RemoveListener(OnGrabReleased);
        }
    }
    
    private void OnGrabbed(SelectEnterEventArgs args)
    {
        // Reset the snap zone state when grabbing
        isInSnapZone = false;
    }

    private void Update()
    {
        // Only check for snap zone entry while being held
        if (grabInteractable != null && grabInteractable.isSelected)
        {
            CheckForSnapZoneEntry();
        }
    }
    
    private void CheckForSnapZoneEntry()
    {
        float distanceToOrigin = Vector3.Distance(transform.position, originalPosition);
        
        // Check if we've entered the snap zone
        if (distanceToOrigin <= snapDistance && !isInSnapZone)
        {
            EnterSnapZone();
        }
        // Check if we've exited the snap zone
        else if (distanceToOrigin > snapDistance && isInSnapZone)
        {
            ExitSnapZone();
        }
    }
    
    private void EnterSnapZone()
    {
        isInSnapZone = true;
        
        // Provide haptic feedback if possible
        if (grabInteractable.isSelected && grabInteractable.interactorsSelecting.Count > 0)
        {
            var interactor = grabInteractable.interactorsSelecting[0];
            
            // Try to get a controller that supports haptics
            if (interactor is XRBaseControllerInteractor controllerInteractor)
            {
                // Send haptic impulse through the controller
                controllerInteractor.xrController?.SendHapticImpulse(hapticIntensity, hapticDuration);
            }
        }
        
        // Play enter snap zone sound
        if (enterSnapZoneSound != null && audioSource != null)
        {
            audioSource.clip = enterSnapZoneSound;
            audioSource.volume = soundVolume;
            audioSource.Play();
        }
    }
    
    private void ExitSnapZone()
    {
        isInSnapZone = false;
        
        // You could add visual feedback for leaving the snap zone
    }

    private void OnGrabReleased(SelectExitEventArgs args)
    {
        // Check if we're within the snap-back distance
        float distanceToOrigin = Vector3.Distance(transform.position, originalPosition);
        
        if (distanceToOrigin <= snapDistance && !isSnapping)
        {
            // Start the snap-back routine
            StartCoroutine(SnapBack());
        }
    }

    private IEnumerator SnapBack()
    {
        isSnapping = true;
        
        // Temporarily disable physics and user interaction
        if (rb != null)
        {
            rb.isKinematic = true;
        }
        
        if (grabInteractable != null)
        {
            grabInteractable.enabled = false;
        }
        
        // Play snap back sound
        if (snapBackSound != null && audioSource != null)
        {
            audioSource.clip = snapBackSound;
            audioSource.volume = soundVolume;
            audioSource.Play();
        }
        
        // Animate the object moving back to origin
        float startTime = Time.time;
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;
        
        while (Time.time - startTime < 1f)
        {
            float t = (Time.time - startTime) * snapSpeed;
            if (t > 1f) t = 1f;
            
            // Use smooth step for more natural movement
            float smoothT = t * t * (3f - 2f * t); 
            
            transform.position = Vector3.Lerp(startPos, originalPosition, smoothT);
            transform.rotation = Quaternion.Slerp(startRot, originalRotation, smoothT);
            
            yield return null;
        }
        
        // Ensure we're exactly at the original position
        transform.position = originalPosition;
        transform.rotation = originalRotation;
        
        // Re-enable physics and interaction
        if (rb != null)
        {
            rb.isKinematic = false;
        }
        
        if (grabInteractable != null)
        {
            grabInteractable.enabled = true;
        }
        
        isSnapping = false;
        isInSnapZone = false;
    }

    // Optional: Method to reset object if it goes out of bounds
    public void ResetToOrigin()
    {
        if (!isSnapping)
        {
            StartCoroutine(SnapBack());
        }
    }
    
    // Optional: Display snap zone in editor (for debugging)
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(originalPosition, snapDistance);
    }
}