using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class ScanButtonHandler : MonoBehaviour
{
    [SerializeField]
    private MachineScanner machineScanner;

    private XRBaseInteractable interactable;

    private void Awake()
    {
        // Find the interactable component on this GameObject or parent
        interactable = GetComponentInParent<XRBaseInteractable>();

        if (interactable == null)
        {
            Debug.LogError(
                "Could not find an XRBaseInteractable component on this button or its parents. Button interactions won't work.",
                this
            );
            return;
        }

        // Subscribe to the select event
        interactable.selectEntered.AddListener(OnButtonSelected);
    }

    private void OnDestroy()
    {
        // Clean up the event subscription
        if (interactable != null)
        {
            interactable.selectEntered.RemoveListener(OnButtonSelected);
        }
    }

    private void OnButtonSelected(SelectEnterEventArgs args)
    {
        if (machineScanner != null)
        {
           machineScanner.TriggerScan();
    }
    else
    {
        Debug.LogWarning("MachineScanner reference not set on ScanButtonHandler", this);
    }
}
}
