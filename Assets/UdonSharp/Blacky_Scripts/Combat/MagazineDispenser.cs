using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// MagazineDispenser:
/// Infinite ammo station / ammo pouch that dispenses pre-allocated GunMagazine objects.
/// Supports both VR proximity grab and desktop Interact.
/// Complies with VRChat Udon constraints via zero-allocation object pooling.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MagazineDispenser : UdonSharpBehaviour
{
    [Header("--- Dispenser Configuration ---")]
    [Tooltip("Caliber / Magazine type dispensed (e.g. '9mm', '5.56mm').")]
    public string magazineType = "9mm";

    [Tooltip("Pre-spawned magazine instances pooled for dispensing.")]
    public GunMagazine[] magazinePool;

    [Tooltip("Transform position where fresh magazines appear when dispensed.")]
    public Transform dispensePoint;

    [Header("--- Audio Feedback ---")]
    public AudioSource dispenserAudio;
    public AudioClip dispenseSound;

    [Header("--- Debug & Logging ---")]
    public bool enableLogging = true;
    public string logPrefix = "[MagazineDispenser]";

    private int _poolIndex = 0;

    void Start()
    {
        if (dispensePoint == null) dispensePoint = transform;

        // Initialize pool magazines
        if (magazinePool != null)
        {
            for (int i = 0; i < magazinePool.Length; i++)
            {
                if (magazinePool[i] != null)
                {
                    magazinePool[i].magazineType = magazineType;
                }
            }
        }
    }

    /// <summary>
    /// VRChat Interact callback (Desktop click or VR raycast click).
    /// </summary>
    public override void Interact()
    {
        _DispenseMagazine();
    }

    /// <summary>
    /// Dispenses a fresh full magazine to the dispense point.
    /// </summary>
    public void _DispenseMagazine()
    {
        if (magazinePool == null || magazinePool.Length == 0)
        {
            if (enableLogging) Debug.LogWarning($"{logPrefix} Magazine pool is empty!");
            return;
        }

        GunMagazine mag = _GetNextAvailableMagazine();
        if (mag == null) return;

        // Take network ownership
        if (Networking.LocalPlayer != null && !Networking.IsOwner(mag.gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, mag.gameObject);
        }

        // Position at dispense point
        mag.transform.SetParent(null, true);
        mag.transform.position = dispensePoint.position;
        mag.transform.rotation = dispensePoint.rotation;

        // Refill to full
        mag._Refill();

        if (dispenserAudio != null && dispenseSound != null)
        {
            dispenserAudio.PlayOneShot(dispenseSound);
        }

        if (enableLogging) Debug.Log($"{logPrefix} Dispensed fresh '{magazineType}' magazine ({mag.currentAmmo} rds).");
    }

    private GunMagazine _GetNextAvailableMagazine()
    {
        // Search for a magazine not currently inserted in a gun
        int checks = 0;
        while (checks < magazinePool.Length)
        {
            GunMagazine mag = magazinePool[_poolIndex];
            _poolIndex = (_poolIndex + 1) % magazinePool.Length;

            if (mag != null && !mag.isInserted)
            {
                return mag;
            }
            checks++;
        }

        // Fallback: return current indexed mag
        return magazinePool[_poolIndex];
    }
}
