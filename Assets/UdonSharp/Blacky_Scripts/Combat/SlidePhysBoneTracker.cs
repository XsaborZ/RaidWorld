using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// SlidePhysBoneTracker:
/// Tracks physical slide / charging-handle movement (Boneworks-style) via PhysBones or direct grab.
/// Detects when the slide is pulled back past the racking threshold and releases forward,
/// cycling the chamber, ejecting unspent rounds, and chambering fresh ammo.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SlidePhysBoneTracker : UdonSharpBehaviour
{
    [Header("--- Target Gun & Slide ---")]
    [Tooltip("Parent VRPhysicalGun controller.")]
    public VRPhysicalGun gun;

    [Tooltip("The moving slide or charging handle transform.")]
    public Transform slideTransform;

    [Header("--- Movement Axis & Offsets ---")]
    [Tooltip("Local position of the slide when fully forward (rest position).")]
    public Vector3 restLocalPosition = Vector3.zero;

    [Tooltip("Local position of the slide when fully pulled back.")]
    public Vector3 rackedLocalPosition = new Vector3(0f, 0f, -0.05f);

    [Tooltip("Fraction of total pull distance required to register a rack (0.0 to 1.0, e.g. 0.75 = 75%).")]
    [Range(0.1f, 1.0f)]
    public float rackThresholdFraction = 0.75f;

    [Tooltip("Fraction of return distance required to complete chambering (e.g. 0.25 = within 25% of rest).")]
    [Range(0.0f, 0.5f)]
    public float returnThresholdFraction = 0.25f;

    [Header("--- Audio Feedback ---")]
    public AudioSource slideAudio;
    public AudioClip slideBackSound;
    public AudioClip slideForwardSound;

    [Header("--- Debug & Logging ---")]
    public bool enableLogging = true;
    public string logPrefix = "[SlideTracker]";

    private bool _isPulledPastThreshold = false;
    private float _totalTravelDistance;
    private Vector3 _travelDirection;

    void Start()
    {
        if (gun == null)
        {
            gun = GetComponentInParent<VRPhysicalGun>();
        }

        if (slideTransform == null)
        {
            slideTransform = transform;
        }

        _travelDirection = (rackedLocalPosition - restLocalPosition);
        _totalTravelDistance = _travelDirection.magnitude;
        if (_totalTravelDistance > 0.0001f)
        {
            _travelDirection = _travelDirection.normalized;
        }
    }

    void Update()
    {
        if (gun == null || slideTransform == null || _totalTravelDistance <= 0.0001f) return;

        // Compute current pull progress along the travel direction
        Vector3 currentOffset = slideTransform.localPosition - restLocalPosition;
        float currentDistance = Vector3.Dot(currentOffset, _travelDirection);
        float progress = Mathf.Clamp01(currentDistance / _totalTravelDistance);

        // State Machine: Check if pulled back past rack threshold
        if (!_isPulledPastThreshold)
        {
            if (progress >= rackThresholdFraction)
            {
                _isPulledPastThreshold = true;
                if (slideAudio != null && slideBackSound != null)
                {
                    slideAudio.PlayOneShot(slideBackSound);
                }

                // Notify gun that slide was pulled back (ejects live round if chamber was full)
                if (gun != null)
                {
                    gun._OnSlidePulledBack();
                }

                if (enableLogging) Debug.Log($"{logPrefix} Slide pulled back past threshold ({progress:P0}).");
            }
        }
        else
        {
            // Check if released and returned forward
            if (progress <= returnThresholdFraction)
            {
                _isPulledPastThreshold = false;
                if (slideAudio != null && slideForwardSound != null)
                {
                    slideAudio.PlayOneShot(slideForwardSound);
                }

                // Notify gun that slide returned forward (chambers fresh round)
                if (gun != null)
                {
                    gun._OnSlideReturnedForward();
                }

                if (enableLogging) Debug.Log($"{logPrefix} Slide returned forward. Chamber action cycled.");
            }
        }
    }

    /// <summary>
    /// Programmatic or desktop shortcut rack trigger (e.g. pressing 'R' or clicking rack).
    /// </summary>
    public void _ManualRack()
    {
        if (slideAudio != null && slideBackSound != null)
        {
            slideAudio.PlayOneShot(slideBackSound);
        }

        if (gun != null)
        {
            gun._OnSlidePulledBack();
        }

        SendCustomEventDelayedSeconds(nameof(_FinishManualRack), 0.12f);
    }

    public void _FinishManualRack()
    {
        if (slideAudio != null && slideForwardSound != null)
        {
            slideAudio.PlayOneShot(slideForwardSound);
        }

        if (gun != null)
        {
            gun._OnSlideReturnedForward();
        }
    }
}
