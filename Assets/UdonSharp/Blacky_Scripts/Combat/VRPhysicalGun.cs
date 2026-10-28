using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// VRPhysicalGun:
/// Comprehensive Boneworks & Destiny 2 inspired VR physical firearm for VRChat Udon.
/// Features manual magazine insertion and physical drop ejection, slide racking with chambering,
/// hitscan and ballistic projectile fire modes, networked hitboxes, teams, target filtering (Player vs AI),
/// weapon durability/damage-taking, and SetProgramVariable damage dispatch.
/// </summary>
public enum GunFireMode
{
    SemiAuto = 0,
    FullAuto = 1,
    Burst = 2,
    Safe = 3
}

public enum GunFireType
{
    Hitscan = 0,
    Projectile = 1,
    Hybrid = 2
}

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
[RequireComponent(typeof(VRCPickup))]
public class VRPhysicalGun : UdonSharpBehaviour
{

    [Header("--- Weapon Information ---")]
    [Tooltip("Display name of this weapon.")]
    public string weaponName = "Physical Pistol";

    [Tooltip("Team ID for this weapon and its holder (0 = Neutral/FFA, 1 = Team A, 2 = Team B, etc.).")]
    public int teamId = 0;

    [Tooltip("If true, can deal damage to players/entities on the same teamId.")]
    public bool allowFriendlyFire = false;

    [Header("--- Target Filtering (Player vs AI) ---")]
    [Tooltip("Can this weapon deal damage to Players.")]
    public bool targetPlayers = true;

    [Tooltip("Can this weapon deal damage to AI / NPCs / Target Dummies.")]
    public bool targetAI = true;

    [Header("--- Firing Ballistics & Damage ---")]
    [Tooltip("Core firing mechanism: Hitscan (instant raycast), Projectile (ballistic object), or Hybrid (hitscan + visual tracer).")]
    public GunFireType fireType = GunFireType.Hitscan;

    [Tooltip("Base damage dealt per bullet hit.")]
    public float baseDamage = 35f;

    [Tooltip("Damage multiplier when a Head hitbox is hit (e.g. 2.0 = 200%).")]
    public float headshotMultiplier = 2.0f;

    [Tooltip("Damage multiplier when a Chest hitbox is hit.")]
    public float chestMultiplier = 1.0f;

    [Tooltip("Firing mode: Semi-Auto, Full-Auto, Burst, or Safe.")]
    public GunFireMode fireMode = GunFireMode.SemiAuto;

    [Tooltip("Rounds per minute (e.g. 600 RPM = 10 rounds/sec).")]
    public float roundsPerMinute = 450f;

    [Tooltip("Number of shots fired per burst when in Burst fire mode.")]
    public int burstCount = 3;

    [Tooltip("Delay in seconds between shots within a burst.")]
    public float burstShotDelay = 0.08f;

    [Tooltip("Number of pellets fired per trigger pull (set > 1 for Shotguns).")]
    public int pelletsPerShot = 1;

    [Tooltip("Base bullet spread angle in degrees.")]
    public float spreadAngle = 0.6f;

    [Tooltip("Maximum effective range in meters for hitscan raycasts.")]
    public float maxRange = 150f;

    [Tooltip("Layers checked for bullet impacts (hitboxes, environment, players).")]
    public LayerMask hitLayers = -1;

    [Tooltip("Projectile pool for Projectile or Hybrid fire mode.")]
    public CombatProjectilePool projectilePool;

    [Tooltip("Speed of physical projectile (m/s).")]
    public float projectileSpeed = 160f;

    [Header("--- Physical Reloading (Boneworks-Style) ---")]
    [Tooltip("Caliber / Magazine type this weapon accepts (e.g. '9mm', '5.56mm').")]
    public string acceptedMagazineType = "9mm";

    [Tooltip("Socket transform where the magazine docks into the magwell.")]
    public Transform magazineSocket;

    [Tooltip("Trigger collider that detects incoming magazines.")]
    public Collider magazineSnapZone;

    [Tooltip("Currently loaded magazine (can be null if unmagged).")]
    public GunMagazine currentMagazine;

    [Tooltip("Is a live round currently chambered (can fire even without a magazine!).")]
    public bool isRoundChambered = false;

    [Tooltip("Is the slide locked back (e.g. after firing the last round).")]
    public bool isSlideLocked = false;

    [Tooltip("Infinite ammo toggle for testing or arcade gameplay.")]
    public bool infiniteAmmo = false;

    [Tooltip("Force applied to magazine when ejected.")]
    public Vector3 magEjectionForce = new Vector3(0f, -1.2f, 0f);

    [Header("--- Slide Mechanics ---")]
    [Tooltip("Slide or bolt transform.")]
    public Transform slideTransform;

    [Tooltip("Rest local position of slide.")]
    public Vector3 slideRestLocalPos = Vector3.zero;

    [Tooltip("Locked back local position of slide.")]
    public Vector3 slideLockedLocalPos = new Vector3(0f, 0f, -0.04f);

    [Header("--- Transforms & Emitters ---")]
    [Tooltip("Muzzle transform where bullets, rays, and muzzle flashes originate.")]
    public Transform muzzlePoint;

    [Tooltip("Ejection port transform where spent shell casings and live rounds eject.")]
    public Transform ejectionPort;

    [Header("--- Audio & Visual Feedback ---")]
    public AudioSource weaponAudio;
    public AudioClip gunshotSound;
    public AudioClip dryFireSound;
    public AudioClip slideRackSound;
    public AudioClip slideReleaseSound;
    public AudioClip magInsertSound;
    public AudioClip magEjectSound;
    public AudioClip fireModeSound;
    public AudioClip destroyedSound;

    [Tooltip("Particle system for muzzle flash.")]
    public ParticleSystem muzzleFlashParticle;

    [Tooltip("Particle system for ejected brass casing.")]
    public ParticleSystem casingParticle;

    [Tooltip("Unspent live round prefab/object spawned if racked with a round in the chamber.")]
    public GameObject liveRoundDropPrefab;

    [Tooltip("Impact spark effect for hitscan impacts.")]
    public GameObject impactSparkEffect;

    [Header("--- Secondary Grip (Destiny 2 Two-Handed Stabilization) ---")]
    [Tooltip("Optional secondary VR pickup on foregrip to stabilize recoil and improve aim.")]
    public VRCPickup secondaryGrip;
    [HideInInspector] public bool isTwoHanded = false;

    [Header("--- Weapon Durability & Taking Damage ---")]
    [Tooltip("If true, the weapon itself can take damage from other weapons/sources.")]
    public bool canTakeDamage = true;
    public float maxWeaponHealth = 100f;
    public float currentWeaponHealth = 100f;
    public bool isWeaponDestroyed = false;
    public GameObject weaponDamageSmoke;

    [Header("--- Debug & Logging ---")]
    public bool enableLogging = true;
    public string logPrefix = "[VRPhysicalGun]";

    // Variables for external SetProgramVariable access
    [HideInInspector, System.NonSerialized] public float incomingDamage;
    [HideInInspector, System.NonSerialized] public float damage;
    [HideInInspector, System.NonSerialized] public float damageAmount;
    [HideInInspector, System.NonSerialized] public VRCPlayerApi attackerPlayer;
    [HideInInspector, System.NonSerialized] public string attackerName = "";
    [HideInInspector, System.NonSerialized] public int attackerTeamId = 0;

    // Synced State
    [UdonSynced] private int _syncedFireCount = 0;
    [UdonSynced] private bool _syncedIsChambered = false;
    [UdonSynced] private bool _syncedIsSlideLocked = false;
    [UdonSynced] private float _syncedHealth = 100f;
    [UdonSynced] private bool _syncedIsDestroyed = false;

    // Internal Runtime State
    private VRCPickup _pickup;
    private bool _isHeld = false;
    private bool _isTriggerDown = false;
    private float _nextFireTime = 0f;
    private int _burstsRemaining = 0;
    private int _localFireCount = 0;

    void Start()
    {
        _pickup = (VRCPickup)GetComponent(typeof(VRCPickup));
        currentWeaponHealth = maxWeaponHealth;

        if (muzzlePoint == null) muzzlePoint = transform;
        if (ejectionPort == null) ejectionPort = transform;

        // Auto-detect magazine if child of socket on start
        if (currentMagazine == null && magazineSocket != null)
        {
            currentMagazine = magazineSocket.GetComponentInChildren<GunMagazine>();
            if (currentMagazine != null)
            {
                currentMagazine._InsertIntoGun(this, magazineSocket);
            }
        }

        if (Networking.IsOwner(gameObject))
        {
            _syncedIsChambered = isRoundChambered;
            _syncedIsSlideLocked = isSlideLocked;
            _syncedHealth = currentWeaponHealth;
            _syncedIsDestroyed = isWeaponDestroyed;
            RequestSerialization();
        }

        _UpdateSlideVisual();
    }

    void Update()
    {
        // Polling damage safeguard
        if (incomingDamage > 0f || damage > 0f || damageAmount > 0f)
        {
            _TakeDamage();
        }

        // Full-auto fire handling while trigger is held down
        if (_isHeld && _isTriggerDown && fireMode == GunFireMode.FullAuto)
        {
            if (Time.time >= _nextFireTime)
            {
                _AttemptFire();
            }
        }

        // Burst fire countdown
        if (_burstsRemaining > 0 && Time.time >= _nextFireTime)
        {
            _FireSingleShot();
            _burstsRemaining--;
            if (_burstsRemaining > 0)
            {
                _nextFireTime = Time.time + burstShotDelay;
            }
        }

        // Desktop keyboard controls when held
        if (_isHeld && Networking.LocalPlayer != null && !Networking.LocalPlayer.IsUserInVR())
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                _RackSlide();
            }
            if (Input.GetKeyDown(KeyCode.G) || Input.GetKeyDown(KeyCode.E))
            {
                _EjectMagazine();
            }
            if (Input.GetKeyDown(KeyCode.F))
            {
                _CycleFireMode();
            }
            if (Input.GetKeyDown(KeyCode.T))
            {
                _ToggleTargetMode();
            }
        }
    }

    // -------------------------------------------------------------------------
    // VRChat Pickup & Trigger Callbacks
    // -------------------------------------------------------------------------
    public override void OnPickup()
    {
        _isHeld = true;
        if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
        if (enableLogging) Debug.Log($"{logPrefix} '{weaponName}' picked up by local player.");
    }

    public override void OnDrop()
    {
        _isHeld = false;
        _isTriggerDown = false;
        _burstsRemaining = 0;
        if (enableLogging) Debug.Log($"{logPrefix} '{weaponName}' dropped.");
    }

    public override void OnPickupUseDown()
    {
        _isTriggerDown = true;
        _AttemptFire();
    }

    public override void OnPickupUseUp()
    {
        _isTriggerDown = false;
    }

    // -------------------------------------------------------------------------
    // Fire Cycling & Execution
    // -------------------------------------------------------------------------
    private void _AttemptFire()
    {
        if (isWeaponDestroyed)
        {
            if (weaponAudio != null && dryFireSound != null) weaponAudio.PlayOneShot(dryFireSound);
            return;
        }

        if (fireMode == GunFireMode.Safe)
        {
            if (weaponAudio != null && dryFireSound != null) weaponAudio.PlayOneShot(dryFireSound);
            return;
        }

        if (Time.time < _nextFireTime) return;

        // Check chamber
        if (!isRoundChambered)
        {
            // Dry fire click
            if (weaponAudio != null && dryFireSound != null)
            {
                weaponAudio.PlayOneShot(dryFireSound);
            }
            if (enableLogging) Debug.Log($"{logPrefix} Click: No round chambered!");
            _nextFireTime = Time.time + 0.25f;
            return;
        }

        float fireInterval = 60f / Mathf.Max(60f, roundsPerMinute);
        _nextFireTime = Time.time + fireInterval;

        if (fireMode == GunFireMode.Burst)
        {
            _burstsRemaining = burstCount - 1;
            _FireSingleShot();
            if (_burstsRemaining > 0)
            {
                _nextFireTime = Time.time + burstShotDelay;
            }
        }
        else
        {
            _FireSingleShot();
        }
    }

    private void _FireSingleShot()
    {
        if (!isRoundChambered && !infiniteAmmo) return;

        // 1. Consume the chambered round
        if (!infiniteAmmo)
        {
            isRoundChambered = false;
        }

        // 2. Play local firing effects (sound, particles, haptics)
        _PlayFireEffectsLocally();

        // 3. Dispatch bullet trajectories (Hitscan or Projectile)
        int shotCount = Mathf.Max(1, pelletsPerShot);
        for (int i = 0; i < shotCount; i++)
        {
            Vector3 fireDir = _CalculateFireDirection();

            if (fireType == GunFireType.Hitscan)
            {
                _ExecuteHitscan(fireDir);
            }
            else if (fireType == GunFireType.Projectile)
            {
                _ExecuteProjectile(fireDir);
            }
            else // Hybrid
            {
                _ExecuteHitscan(fireDir);
                _ExecuteProjectile(fireDir);
            }
        }

        // 4. Chamber next round from magazine if available
        if (currentMagazine != null && currentMagazine.currentAmmo > 0)
        {
            if (currentMagazine._ConsumeRound())
            {
                isRoundChambered = true;
                isSlideLocked = false;
            }
        }
        else
        {
            // Empty: slide locks back
            isSlideLocked = true;
            if (enableLogging) Debug.Log($"{logPrefix} Magazine empty! Slide locked back.");
        }

        _UpdateSlideVisual();

        // 5. Sync firing event over network
        _syncedFireCount++;
        _syncedIsChambered = isRoundChambered;
        _syncedIsSlideLocked = isSlideLocked;
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }

    private Vector3 _CalculateFireDirection()
    {
        Vector3 forward = muzzlePoint.forward;
        float currentSpread = spreadAngle;
        if (isTwoHanded) currentSpread *= 0.5f; // Two-handed stability bonus

        if (currentSpread > 0.01f)
        {
            Quaternion spreadRot = Quaternion.Euler(
                Random.Range(-currentSpread, currentSpread),
                Random.Range(-currentSpread, currentSpread),
                0f
            );
            forward = muzzlePoint.rotation * spreadRot * Vector3.forward;
        }

        return forward;
    }

    private void _ExecuteHitscan(Vector3 direction)
    {
        RaycastHit hit;
        if (Physics.Raycast(muzzlePoint.position, direction, out hit, maxRange, hitLayers, QueryTriggerInteraction.Collide))
        {
            _ProcessHit(hit.collider, hit.point, hit.normal);
        }
    }

    private void _ExecuteProjectile(Vector3 direction)
    {
        if (projectilePool == null) return;

        CombatProjectile proj = projectilePool._GetProjectile();
        if (proj != null)
        {
            proj._Launch(
                muzzlePoint.position,
                direction,
                projectileSpeed,
                baseDamage,
                teamId,
                allowFriendlyFire,
                targetPlayers,
                targetAI,
                Networking.LocalPlayer,
                Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Shooter"
            );
        }
    }

    /// <summary>
    /// Processes a bullet impact on any collider and applies damage via SetProgramVariable.
    /// </summary>
    private void _ProcessHit(Collider hitCol, Vector3 point, Vector3 normal)
    {
        if (hitCol == null) return;

        // 1. Check for CombatHitbox
        CombatHitbox hitbox = hitCol.GetComponent<CombatHitbox>();
        if (hitbox != null)
        {
            hitbox._ForwardDamage(
                baseDamage,
                Networking.LocalPlayer,
                Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Shooter",
                teamId,
                hitCol
            );
        }
        else
        {
            // 2. Check for UdonBehaviour on collider or parent
            UdonBehaviour targetUb = (UdonBehaviour)hitCol.GetComponent(typeof(UdonBehaviour));
            if (targetUb == null) targetUb = (UdonBehaviour)hitCol.GetComponentInParent(typeof(UdonBehaviour));

            if (targetUb != null)
            {
                // Inspect headshot vs chest
                string colName = hitCol.gameObject.name.ToLower();
                bool isHead = colName.Contains("head");
                bool isChest = colName.Contains("chest");
                string partName = isHead ? "Head" : (isChest ? "Chest" : "Body");
                float mult = isHead ? headshotMultiplier : (isChest ? chestMultiplier : 1.0f);
                float finalDmg = baseDamage * mult;

                // Set values using <UdonBehaviour>().SetProgramVariable
                targetUb.SetProgramVariable("incomingDamage", finalDmg);
                targetUb.SetProgramVariable("damage", finalDmg);
                targetUb.SetProgramVariable("damageAmount", finalDmg);
                targetUb.SetProgramVariable("damageMultiplier", mult);
                targetUb.SetProgramVariable("hitCollider", hitCol);
                targetUb.SetProgramVariable("isHeadshot", isHead);
                targetUb.SetProgramVariable("isChest", isChest);
                targetUb.SetProgramVariable("hitPart", partName);
                targetUb.SetProgramVariable("attackerPlayer", Networking.LocalPlayer);
                targetUb.SetProgramVariable("attackerName", Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "Shooter");
                targetUb.SetProgramVariable("attackerTeamId", teamId);

                targetUb.SendCustomEvent("_TakeDamage");
                targetUb.SendCustomEvent("TakeDamage");

                if (enableLogging)
                {
                    Debug.Log($"{logPrefix} Hit '{hitCol.gameObject.name}' dealing {finalDmg:F1} dmg [{partName}].");
                }
            }
        }

        // Spawn hit effect
        if (impactSparkEffect != null)
        {
            impactSparkEffect.transform.position = point;
            impactSparkEffect.transform.rotation = Quaternion.LookRotation(normal);
            impactSparkEffect.SetActive(false);
            impactSparkEffect.SetActive(true);
        }
    }

    private void _PlayFireEffectsLocally()
    {
        if (weaponAudio != null && gunshotSound != null)
        {
            weaponAudio.pitch = Random.Range(0.96f, 1.04f);
            weaponAudio.PlayOneShot(gunshotSound);
        }

        if (muzzleFlashParticle != null)
        {
            muzzleFlashParticle.Play();
        }

        if (casingParticle != null)
        {
            casingParticle.Play();
        }

        // VR controller haptic kick
        if (Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR() && _pickup != null)
        {
            VRC_Pickup.PickupHand hand = _pickup.currentHand;
            if (hand == VRC_Pickup.PickupHand.Left)
            {
                Networking.LocalPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, 0.08f, 0.75f, 0.4f);
            }
            else if (hand == VRC_Pickup.PickupHand.Right)
            {
                Networking.LocalPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, 0.08f, 0.75f, 0.4f);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Magazine Interaction & Socket Trigger Callbacks
    // -------------------------------------------------------------------------
    public void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        _CheckIncomingMagazine(other);
    }

    private void _CheckIncomingMagazine(Collider other)
    {
        if (currentMagazine != null) return; // Already has magazine

        GunMagazine mag = other.GetComponent<GunMagazine>();
        if (mag == null) mag = other.GetComponentInParent<GunMagazine>();

        if (mag != null && !mag.isInserted)
        {
            // Verify caliber / magazine type compatibility
            if (string.IsNullOrEmpty(acceptedMagazineType) || mag.magazineType == acceptedMagazineType)
            {
                _InsertMagazine(mag);
            }
            else
            {
                if (enableLogging) Debug.LogWarning($"{logPrefix} Magazine caliber '{mag.magazineType}' incompatible with weapon '{acceptedMagazineType}'.");
            }
        }
    }

    public void _InsertMagazine(GunMagazine mag)
    {
        if (mag == null || currentMagazine != null) return;

        currentMagazine = mag;
        mag._InsertIntoGun(this, magazineSocket);

        if (weaponAudio != null && magInsertSound != null)
        {
            weaponAudio.PlayOneShot(magInsertSound);
        }

        if (enableLogging) Debug.Log($"{logPrefix} Loaded magazine into '{weaponName}' ({mag.currentAmmo}/{mag.maxAmmo}).");
    }

    public void _EjectMagazine()
    {
        if (currentMagazine == null) return;

        GunMagazine magToEject = currentMagazine;
        currentMagazine = null;

        Vector3 force = transform.TransformDirection(magEjectionForce);
        magToEject._EjectFromGun(force);

        if (weaponAudio != null && magEjectSound != null)
        {
            weaponAudio.PlayOneShot(magEjectSound);
        }

        if (enableLogging) Debug.Log($"{logPrefix} Ejected magazine from '{weaponName}'.");
    }

    // -------------------------------------------------------------------------
    // Slide Racking Mechanics (Boneworks-Style)
    // -------------------------------------------------------------------------
    public void _RackSlide()
    {
        _OnSlidePulledBack();
        _OnSlideReturnedForward();
    }

    /// <summary>
    /// Called when the slide is pulled back past the racking threshold.
    /// Ejects an unspent live round if chamber was loaded.
    /// </summary>
    public void _OnSlidePulledBack()
    {
        if (weaponAudio != null && slideRackSound != null)
        {
            weaponAudio.PlayOneShot(slideRackSound);
        }

        // If a round was chambered, eject it unspent
        if (isRoundChambered)
        {
            isRoundChambered = false;
            _EjectLiveRound();
            if (enableLogging) Debug.Log($"{logPrefix} Live round extracted from chamber!");
        }
    }

    /// <summary>
    /// Called when the slide releases forward to rest position.
    /// Feeds a new round from the magazine into the chamber.
    /// </summary>
    public void _OnSlideReturnedForward()
    {
        if (currentMagazine != null && currentMagazine.currentAmmo > 0)
        {
            if (currentMagazine._ConsumeRound())
            {
                isRoundChambered = true;
                isSlideLocked = false;
                if (enableLogging) Debug.Log($"{logPrefix} Fresh round chambered! (Mag ammo: {currentMagazine.currentAmmo})");
            }
        }
        else
        {
            if (enableLogging) Debug.Log($"{logPrefix} Slide closed on empty magazine/well.");
        }

        if (weaponAudio != null && slideReleaseSound != null)
        {
            weaponAudio.PlayOneShot(slideReleaseSound);
        }

        _UpdateSlideVisual();

        _syncedIsChambered = isRoundChambered;
        _syncedIsSlideLocked = isSlideLocked;
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }

    private void _EjectLiveRound()
    {
        if (liveRoundDropPrefab != null && ejectionPort != null)
        {
            liveRoundDropPrefab.transform.position = ejectionPort.position;
            liveRoundDropPrefab.transform.rotation = ejectionPort.rotation;
            liveRoundDropPrefab.SetActive(false);
            liveRoundDropPrefab.SetActive(true);
        }
        else if (casingParticle != null)
        {
            casingParticle.Play();
        }
    }

    private void _UpdateSlideVisual()
    {
        if (slideTransform == null) return;

        if (isSlideLocked)
        {
            slideTransform.localPosition = slideLockedLocalPos;
        }
        else
        {
            slideTransform.localPosition = slideRestLocalPos;
        }
    }

    // -------------------------------------------------------------------------
    // Mode Toggling (Fire Mode, Target Mode, Safe)
    // -------------------------------------------------------------------------
    public void _CycleFireMode()
    {
        int nextMode = ((int)fireMode + 1) % 4;
        fireMode = (GunFireMode)nextMode;

        if (weaponAudio != null && fireModeSound != null)
        {
            weaponAudio.PlayOneShot(fireModeSound);
        }

        if (enableLogging) Debug.Log($"{logPrefix} Swapped fire mode to: {fireMode}");
    }

    public void _ToggleTargetMode()
    {
        if (targetPlayers && targetAI)
        {
            targetPlayers = true;
            targetAI = false;
        }
        else if (targetPlayers && !targetAI)
        {
            targetPlayers = false;
            targetAI = true;
        }
        else
        {
            targetPlayers = true;
            targetAI = true;
        }

        if (enableLogging) Debug.Log($"{logPrefix} Target mode updated: Players={targetPlayers}, AI={targetAI}");
    }

    // -------------------------------------------------------------------------
    // Weapon Receiving Damage (Durability & Weapon Destruction)
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

        if (!canTakeDamage || rawDmg <= 0f || isWeaponDestroyed) return;

        currentWeaponHealth = Mathf.Max(0f, currentWeaponHealth - rawDmg);
        if (enableLogging) Debug.Log($"{logPrefix} Weapon took {rawDmg:F1} damage. Health: {currentWeaponHealth:F1}/{maxWeaponHealth:F1}");

        if (currentWeaponHealth <= 0f)
        {
            _HandleWeaponDestroyed();
        }

        _syncedHealth = currentWeaponHealth;
        _syncedIsDestroyed = isWeaponDestroyed;
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }

    public void TakeDamage()
    {
        _TakeDamage();
    }

    private void _HandleWeaponDestroyed()
    {
        isWeaponDestroyed = true;
        if (weaponAudio != null && destroyedSound != null)
        {
            weaponAudio.PlayOneShot(destroyedSound);
        }

        if (weaponDamageSmoke != null)
        {
            weaponDamageSmoke.SetActive(true);
        }

        // Drop from hand if currently held
        if (_isHeld && _pickup != null)
        {
            _pickup.Drop();
        }

        if (enableLogging) Debug.Log($"{logPrefix} *** Weapon destroyed from damage! ***");
    }

    public void _RepairWeapon()
    {
        currentWeaponHealth = maxWeaponHealth;
        isWeaponDestroyed = false;

        if (weaponDamageSmoke != null)
        {
            weaponDamageSmoke.SetActive(false);
        }

        _syncedHealth = currentWeaponHealth;
        _syncedIsDestroyed = isWeaponDestroyed;
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }

        if (enableLogging) Debug.Log($"{logPrefix} Weapon repaired and operational.");
    }

    // -------------------------------------------------------------------------
    // Network Deserialization
    // -------------------------------------------------------------------------
    public override void OnDeserialization()
    {
        // Replicate firing event on remote clients
        if (_syncedFireCount != _localFireCount)
        {
            _localFireCount = _syncedFireCount;
            _PlayFireEffectsLocally();
        }

        isRoundChambered = _syncedIsChambered;
        isSlideLocked = _syncedIsSlideLocked;
        currentWeaponHealth = _syncedHealth;

        if (!isWeaponDestroyed && _syncedIsDestroyed)
        {
            _HandleWeaponDestroyed();
        }
        else if (isWeaponDestroyed && !_syncedIsDestroyed)
        {
            _RepairWeapon();
        }

        _UpdateSlideVisual();
    }
}
