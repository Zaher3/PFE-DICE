using UnityEngine;

public class HideUntilRecognized : MonoBehaviour
{
    void Start()
    {
        // Hide the model at startup
        SetVisibility(false);
    }

    public void ShowModel()
    {
        // Call this method when the model is recognized
        SetVisibility(true);
    }

    private void SetVisibility(bool visible)
    {
        // Hide/show all renderers
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            renderer.enabled = visible;
        }

        // Optionally disable colliders too
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (var collider in colliders)
        {
            collider.enabled = visible;
        }
    }
}
