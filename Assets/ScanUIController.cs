using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

namespace Unity.XRTemplate
{
    /// <summary>
    /// Controls the UI elements related to machine scanning functionality.
    /// </summary>
    public class ScanUIController : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField]
        private GameObject scanInstructionsPanel;

        [SerializeField]
        private GameObject scanFrameVisual;

        [SerializeField]
        private GameObject scanningIndicator;

        [SerializeField]
        private GameObject resultPanel;

        [SerializeField]
        private TextMeshProUGUI resultText;

        [SerializeField]
        private Image scanPreviewImage;

        [SerializeField]
        private XRBaseInteractable scanButton;

        [Header("Scan Settings")]
        [SerializeField]
        private MachineScanner machineScanner;

        [SerializeField]
        private float instructionsDuration = 5.0f;

        [SerializeField]
        private float resultDisplayDuration = 4.0f;

        [SerializeField]
        private string[] instructionMessages = new string[]
        {
            "Position yourself in front of the machine",
            "Frame the machine in the viewfinder",
            "Press the scan button to identify the machine",
        };

        [SerializeField]
        private TextMeshProUGUI instructionsText;

        private int currentInstructionIndex = 0;
        private Coroutine instructionsCoroutine;
        private bool isFirstScan = true;

        void Start()
        {
            // Initialize UI state
            if (scanInstructionsPanel != null)
                scanInstructionsPanel.SetActive(false);

            if (scanFrameVisual != null)
                scanFrameVisual.SetActive(false);

            if (scanningIndicator != null)
                scanningIndicator.SetActive(false);

            if (resultPanel != null)
                resultPanel.SetActive(false);

            if (scanPreviewImage != null)
                scanPreviewImage.gameObject.SetActive(false);

            // Register to scan button events if available
            if (scanButton != null)
            {
                scanButton.selectEntered.AddListener(OnScanButtonPressed);
            }
        }

        void OnDestroy()
        {
            if (scanButton != null)
            {
                scanButton.selectEntered.RemoveListener(OnScanButtonPressed);
            }
        }

        /// <summary>
        /// Shows the scanning UI and begins the user guidance process.
        /// </summary>
        public void StartScanningProcess()
        {
            if (isFirstScan)
            {
                // On first scan, show the full instructions sequence
                StartInstructions();
                isFirstScan = false;
            }
            else
            {
                // For subsequent scans, just show the scan frame
                ShowScanFrame();
            }
        }

        private void StartInstructions()
        {
            // Stop any running instructions
            if (instructionsCoroutine != null)
            {
                StopCoroutine(instructionsCoroutine);
            }

            instructionsCoroutine = StartCoroutine(ShowInstructionsSequence());
        }

        private IEnumerator ShowInstructionsSequence()
        {
            if (scanInstructionsPanel != null)
            {
                scanInstructionsPanel.SetActive(true);
                currentInstructionIndex = 0;

                // Show each instruction
                foreach (var message in instructionMessages)
                {
                    if (instructionsText != null)
                        instructionsText.text = message;

                    yield return new WaitForSeconds(
                        instructionsDuration / instructionMessages.Length
                    );
                    currentInstructionIndex++;
                }

                scanInstructionsPanel.SetActive(false);

                // After instructions, show the scan frame
                ShowScanFrame();
            }
        }

        private void ShowScanFrame()
        {
            if (scanFrameVisual != null)
            {
                scanFrameVisual.SetActive(true);
            }
        }

        public void OnScanButtonPressed(SelectEnterEventArgs args)
        {
            if (machineScanner != null)
            {
                // Hide instruction panel if visible
                if (scanInstructionsPanel != null && scanInstructionsPanel.activeSelf)
                {
                    scanInstructionsPanel.SetActive(false);
                    if (instructionsCoroutine != null)
                    {
                        StopCoroutine(instructionsCoroutine);
                    }
                }

                // Show scanning indicator
                if (scanningIndicator != null)
                {
                    scanningIndicator.SetActive(true);
                }

                // Trigger the scan
                machineScanner.TriggerScan();

                // Start the scan feedback process
                StartCoroutine(HandleScanFeedback());
            }
        }

        private IEnumerator HandleScanFeedback()
        {
            // Wait a moment to show scan in progress
            yield return new WaitForSeconds(1f);

            // Hide scanning indicator
            if (scanningIndicator != null)
            {
                scanningIndicator.SetActive(false);
            }

            // Update result text based on whether a digital twin was spawned
            // In a real implementation, you would connect this to actual results from the scanner
            if (resultPanel != null && resultText != null)
            {
                resultPanel.SetActive(true);

                // This is placeholder logic - will need to be connected to actual scan results
                bool scanSuccess = Random.value > 0.3f; // 70% success rate for demonstration

                if (scanSuccess)
                {
                    resultText.text = "Machine identified successfully!";
                    resultText.color = Color.green;
                }
                else
                {
                    resultText.text = "Unable to identify machine. Please try again.";
                    resultText.color = Color.red;
                }

                // Display the result briefly
                yield return new WaitForSeconds(resultDisplayDuration);
                resultPanel.SetActive(false);
            }

            // Hide scan frame after process is complete
            if (scanFrameVisual != null)
            {
                scanFrameVisual.SetActive(false);
            }

            // Reset UI state for next scan
            if (scanPreviewImage != null)
            {
                scanPreviewImage.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Updates the scan preview image with the provided texture
        /// </summary>
        public void UpdateScanPreview(Texture2D previewTexture)
        {
            if (scanPreviewImage != null && previewTexture != null)
            {
                scanPreviewImage.sprite = Sprite.Create(
                    previewTexture,
                    new Rect(0, 0, previewTexture.width, previewTexture.height),
                    Vector2.one * 0.5f
                );

                scanPreviewImage.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Displays a result message to the user
        /// </summary>
        public void ShowResult(bool success, string message)
        {
            if (resultPanel != null && resultText != null)
            {
                resultPanel.SetActive(true);
                resultText.text = message;
                resultText.color = success ? Color.green : Color.red;

                // Auto-hide after delay
                StartCoroutine(AutoHideResult());
            }
        }

        private IEnumerator AutoHideResult()
        {
            yield return new WaitForSeconds(resultDisplayDuration);

            if (resultPanel != null)
            {
                resultPanel.SetActive(false);
            }
        }

        public void ShowScanningIndicator(bool visible)
        {
            if (scanningIndicator != null)
            {
                scanningIndicator.SetActive(visible);
            }
        }
    }
}
