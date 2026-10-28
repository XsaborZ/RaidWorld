using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// GunMagazine:
/// VR-Physical magazine for manual weapon reloading (Boneworks-style).
/// Can be picked up, inserted into weapon magwell sockets, ejected with physical momentum,
/// tracks synced ammo counts, and updates visual bullet meshes inside the magazine feed lips.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
[RequireComponent(typeof(Rigidbody))]
public class GunMagazine : UdonSharpBehaviour
{
    [Header("--- Magazine Specifications ---")]
    [Tooltip("Caliber / Magazine type identifier. Must match the gun's acceptedMagazineType (e.g., '9mm', '5.56mm').")]
    public string magazineType = "9mm";

    [Tooltip("Maximum ammunition capacity.")]
    public int maxAmmo = 15;

    [Tooltip("Current ammunition loaded.")]
    public int currentAmmo = 15;

    [Header("--- Visuals & Feedback ---")]
    [Tooltip("Optional bullet meshes shown at the top of the magazine, hidden as ammo empties.")]
    public GameObject[] bulletVisuals;

    [Tooltip("Audio source for magazine interaction clicks.")]
    public AudioSource magazineAudio;

    [Tooltip("Sound played when magazine snaps into a gun.")]
    public AudioClip snapInClip;

    [Tooltip("Sound played when magazine is ejected.")]
    public AudioClip ejectClip;

    [Header("--- Lifecycle & Persistence ---")]
    [Tooltip("If dropped and left untouched, seconds before auto-respawning / returning to start position (0 = never).")]
    public float despawnDelay = 60f;

    [Tooltip("Respawn transform position if dropped.")]
    public Transform spawnPoint;

    [Header("--- Durability / Taking Damage ---")]
    [Tooltip("Can this magazine take damage from other weapons (can spark / explode if damaged).")]
    public bool takeDamage = true;
    public float durability = 30f;
    public GameObject damageSparkEffect;

    [Header("--- Debug & Logging ---")]
    public bool enableLogging = true;
    public string logPrefix = "[GunMagazine]";

    // Variables for external SetProgramVariable access
    [HideInInspector, System.NonSerialized] public float incomingDamage;
    [HideInInspector, System.NonSerialized] public float damage;
    [HideInInspector, System.NonSerialized] public float damageAmount;
    [HideInInspector, System.NonSerialized] public VRCPlayerApi attackerPlayer;
    [HideInInspector, System.NonSerialized] public string attackerName = "";
    [HideInInspector, System.NonSerialized] public int attackerTeamId = 0;

    // Synced State
    [UdonSynced] private int _syncedAmmo;
    [UdonSynced] private bool _syncedIsInserted;

    // Internal State
    [HideInInspector] public bool isInserted = false;
    [HideInInspector] public VRPhysicalGun attachedGun;

    private Rigidbody _rigidbody;
    private VRCPickup _pickup;
    private Collider _collider;
    private Vector3 _initialPosition;
    private Quaternion _initialRotation;
    private float _dropTime = -1f;

    void Start()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _pickup = (VRCPickup)GetComponent(typeof(VRCPickup));
        _collider = GetComponent<Collider>();

        _initialPosition = spawnPoint != null ? spawnPoint.position : transform.position;
        _initialRotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;

        if (Networking.IsOwner(gameObject))
        {
            _syncedAmmo = currentAmmo;
            _syncedIsInserted = isInserted;
            RequestSerialization();
        }

        _UpdateBulletVisuals();
    }

    void Update()
    {
        // Polling damage safeguard
        if (incomingDamage > 0f || damage > 0f || damageAmount > 0f)
        {
            _TakeDamage();
        }

        // Auto despawn / reset check when dropped
        if (despawnDelay > 0f && _dropTime > 0f && !isInserted)
        {
            if (Time.time - _dropTime >= despawnDelay)
            {
                _RespawnMagazine();
            }
        }
    }

    /// <summary>
    /// Snaps magazine into a gun socket.
    /// </summary>
    public void _InsertIntoGun(VRPhysicalGun gun, Transform socket)
    {
        if (isInserted) return;

        if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        isInserted = true;
        attachedGun = gun;
        _dropTime = -1f;

        // Parent to socket
        if (socket != null)
        {
            transform.SetParent(socket, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        // Disable physics and pickup while seated in weapon
        if (_rigidbody != null)
        {
            _rigidbody.isKinematic = true;
            _rigidbody.velocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        if (_pickup != null)
        {
            _pickup.Drop();
            _pickup.pickupable = false;
        }

        if (_collider != null)
        {
            _collider.isTrigger = true;
        }

        if (magazineAudio != null && snapInClip != null)
        {
            magazineAudio.PlayOneShot(snapInClip);
        }

        _syncedAmmo = currentAmmo;
        _syncedIsInserted = isInserted;
        RequestSerialization();

        if (enableLogging) Debug.Log($"{logPrefix} Magazine '{magazineType}' inserted into gun. (Ammo: {currentAmmo}/{maxAmmo})");
    }

    /// <summary>
    /// Ejects magazine from gun with physical impulse.
    /// </summary>
    public void _EjectFromGun(Vector3 ejectionForce)
    {
        if (!isInserted) return;

        if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        isInserted = false;
        attachedGun = null;
        transform.SetParent(null, true);

        if (_collider != null)
        {
            _collider.isTrigger = false;
        }

        if (_rigidbody != null)
        {
            _rigidbody.isKinematic = false;
            _rigidbody.velocity = ejectionForce;
            _rigidbody.angularVelocity = Random.insideUnitSphere * 2f;
        }

        if (_pickup != null)
        {
            _pickup.pickupable = true;
        }

        if (magazineAudio != null && ejectClip != null)
        {
            magazineAudio.PlayOneShot(ejectClip);
        }

        _dropTime = Time.time;

        _syncedAmmo = currentAmmo;
        _syncedIsInserted = isInserted;
        RequestSerialization();

        if (enableLogging) Debug.Log($"{logPrefix} Magazine ejected. (Remaining ammo: {currentAmmo})");
    }

    /// <summary>
    /// Consumes 1 round from the magazine. Returns true if round was available.
    /// </summary>
    public bool _ConsumeRound()
    {
        if (currentAmmo <= 0) return false;

        if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        currentAmmo--;
        _UpdateBulletVisuals();

        _syncedAmmo = currentAmmo;
        RequestSerialization();
        return true;
    }

    /// <summary>
    /// Refills magazine to full capacity.
    /// </summary>
    public void _Refill()
    {
        if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        currentAmmo = maxAmmo;
        _UpdateBulletVisuals();

        _syncedAmmo = currentAmmo;
        RequestSerialization();

        if (enableLogging) Debug.Log($"{logPrefix} Magazine refilled ({currentAmmo}/{maxAmmo}).");
    }

    private void _UpdateBulletVisuals()
    {
        if (bulletVisuals == null || bulletVisuals.Length == 0) return;

        for (int i = 0; i < bulletVisuals.Length; i++)
        {
            if (bulletVisuals[i] != null)
            {
                bulletVisuals[i].SetActive(i < currentAmmo);
            }
        }
    }

    private void _RespawnMagazine()
    {
        if (isInserted) return;

        if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        if (_rigidbody != null)
        {
            _rigidbody.velocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        transform.position = _initialPosition;
        transform.rotation = _initialRotation;
        _dropTime = -1f;

        _Refill();
        if (enableLogging) Debug.Log($"{logPrefix} Magazine respawned at origin.");
    }

    // -------------------------------------------------------------------------
    // VRChat Pickup Callbacks
    // -------------------------------------------------------------------------
    public override void OnPickup()
    {
        _dropTime = -1f;
        if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }

    public override void OnDrop()
    {
        if (!isInserted)
        {
            _dropTime = Time.time;
        }
    }

    // -------------------------------------------------------------------------
    // Durability & Receiving Damage
    // -------------------------------------------------------------------------
    public void _TakeDamage()
    {
        float rawDmg = 0f;
        if (incomingDamage > 0f) rawDmg = incomingDamage;
        else if (damage > 0f) rawDmg = damage;
        else if (damageAmount > 0f) rawDmg = damageAmount;

        incomingDamage = 0f;
        damage = 0f;
        damageAmount = 0f;

        if (!takeDamage || rawDmg <= 0f) return;

        durability -= rawDmg;
        if (damageSparkEffect != null)
        {
            damageSparkEffect.SetActive(true);
        }

        if (enableLogging) Debug.Log($"{logPrefix} Magazine took {rawDmg:F1} damage. Durability: {durability:F1}");

        if (durability <= 0f)
        {
            // If shot enough, empty the mag
            currentAmmo = 0;
            _UpdateBulletVisuals();
            _syncedAmmo = 0;
            if (Networking.IsOwner(gameObject))
            {
                RequestSerialization();
            }
        }
    }

    public void TakeDamage()
    {
        _TakeDamage();
    }

    // -------------------------------------------------------------------------
    // Network Deserialization
    // -------------------------------------------------------------------------
    public override void OnDeserialization()
    {
        currentAmmo = _syncedAmmo;
        isInserted = _syncedIsInserted;
        _UpdateBulletVisuals();
    }
}
