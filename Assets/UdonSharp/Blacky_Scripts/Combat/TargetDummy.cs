using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// TargetDummy:
/// Synced target dummy for shooting ranges and combat testing.
/// Features Head and Chest hitboxes with multipliers, reactive wobble physics,
/// mesh hit flash, world-space health display, knockdown on death, and automatic network-synced respawn.
/// Fully compatible with SetProgramVariable damage inputs.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class TargetDummy : UdonSharpBehaviour
{
    [Header("--- Target Information ---")]
    [Tooltip("Display name of this dummy.")]
    public string dummyName = "Combat Target Dummy";

    [Tooltip("Team ID for this dummy (e.g. 2 = Hostile / Red Team).")]
    public int teamId = 2;

    [Tooltip("If true, can be damaged by entities on the same teamId.")]
    public bool allowFriendlyFire = true;

    [Header("--- Health & Shields ---")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;
    public float maxShield = 50f;
    public float currentShield = 50f;
    public bool isInvulnerable = false;

    [Header("--- Hitboxes & Multipliers ---")]
    public Collider headCollider;
    public Collider chestCollider;
    public float headshotMultiplier = 2.0f;
    public float chestMultiplier = 1.0f;

    [Header("--- Death & Respawn ---")]
    [Tooltip("Delay in seconds before automatically respawning after death.")]
    public float respawnDelay = 4.0f;

    [Tooltip("Particle system played upon dummy death / destruction.")]
    public GameObject deathEffect;

    [Header("--- Visual & Wobble Feedback (Boneworks-Style) ---")]
    [Tooltip("Transform that tilts/wobbles when shot and falls backward upon death.")]
    public Transform dummyPivot;

    [Tooltip("Mesh renderer to flash upon taking damage.")]
    public MeshRenderer targetRenderer;

    [Tooltip("Flash color when struck by damage.")]
    public Color hitFlashColor = new Color(0.8f, 0.1f, 0.1f, 1f);

    [Header("--- Audio Feedback ---")]
    public AudioSource dummyAudio;
    public AudioClip bodyHitSound;
    public AudioClip headshotDingSound;
    public AudioClip deathSound;
    public AudioClip respawnSound;

    [Header("--- Optional Counter-Attack / AoE on Death ---")]
    [Tooltip("If true, deals explosion damage in a radius when killed.")]
    public bool explodeOnDeath = false;
    public float explosionRadius = 4f;
    public float explosionDamage = 50f;

    [Header("--- Debug & Logging ---")]
    public bool enableLogging = true;
    public string logPrefix = "[TargetDummy]";

    // Variables for external SetProgramVariable access
    [HideInInspector, System.NonSerialized] public float incomingDamage;
    [HideInInspector, System.NonSerialized] public float damage;
    [HideInInspector, System.NonSerialized] public float damageAmount;
    [HideInInspector, System.NonSerialized] public float damageMultiplier = 1.0f;
    [HideInInspector, System.NonSerialized] public Collider hitCollider;
    [HideInInspector, System.NonSerialized] public bool isHeadshot = false;
    [HideInInspector, System.NonSerialized] public bool isChest = false;
    [HideInInspector, System.NonSerialized] public string hitPart = "";
    [HideInInspector, System.NonSerialized] public VRCPlayerApi attackerPlayer;
    [HideInInspector, System.NonSerialized] public string attackerName = "";
    [HideInInspector, System.NonSerialized] public int attackerTeamId = 0;

    // Synced State
    [UdonSynced] private float _syncedHealth;
    [UdonSynced] private float _syncedShield;
    [UdonSynced] private bool _syncedIsDead;

    // Internal State
    [HideInInspector] public bool isDead = false;
    private Quaternion _restRotation;
    private Quaternion _deadRotation;
    private float _currentTilt = 0f;
    private Color _originalColor = Color.white;
    private Material _matInstance;

    void Start()
    {
        currentHealth = maxHealth;
        currentShield = maxShield;
        isDead = false;

        if (dummyPivot != null)
        {
            _restRotation = dummyPivot.localRotation;
            _deadRotation = _restRotation * Quaternion.Euler(85f, 0f, 0f); // Knocks backward onto floor
        }

        if (targetRenderer != null && targetRenderer.material != null)
        {
            _matInstance = targetRenderer.material;
            if (_matInstance.HasProperty("_Color"))
            {
                _originalColor = _matInstance.color;
            }
        }

        if (Networking.IsOwner(gameObject))
        {
            _syncedHealth = currentHealth;
            _syncedShield = currentShield;
            _syncedIsDead = isDead;
            RequestSerialization();
        }
    }

    void Update()
    {
        // Polling safeguard
        if (incomingDamage > 0f || damage > 0f || damageAmount > 0f)
        {
            _TakeDamage();
        }

        // Wobble spring physics recovery
        if (!isDead && dummyPivot != null)
        {
            if (_currentTilt > 0.01f)
            {
                _currentTilt = Mathf.Lerp(_currentTilt, 0f, Time.deltaTime * 6f);
                dummyPivot.localRotation = _restRotation * Quaternion.Euler(_currentTilt, 0f, 0f);
            }
        }
    }

    /// <summary>
    /// Adjusts hitbox positions and sizes to match any custom model.
    /// </summary>
    public void _SetHeadHitboxDimensions(Vector3 offset, float radius)
    {
        if (headCollider != null && headCollider.GetType() == typeof(SphereCollider))
        {
            SphereCollider sphere = (SphereCollider)headCollider;
            sphere.center = offset;
            sphere.radius = radius;
        }
    }

    public void _SetChestHitboxDimensions(Vector3 offset, Vector3 size)
    {
        if (chestCollider != null && chestCollider.GetType() == typeof(BoxCollider))
        {
            BoxCollider box = (BoxCollider)chestCollider;
            box.center = offset;
            box.size = size;
        }
    }

    // -------------------------------------------------------------------------
    // Damage Processing
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

        if (rawDmg <= 0f || isDead || isInvulnerable) return;

        // Friendly fire check
        if (!allowFriendlyFire && teamId != 0 && attackerTeamId != 0 && attackerTeamId == teamId)
        {
            if (enableLogging) Debug.Log($"{logPrefix} Friendly fire prevented on '{dummyName}'.");
            attackerTeamId = 0;
            return;
        }
        attackerTeamId = 0;

        // Multiplier computation
        float mult = damageMultiplier > 0f ? damageMultiplier : 1.0f;
        bool crit = false;

        if (hitCollider != null)
        {
            if (headCollider != null && hitCollider == headCollider)
            {
                mult *= headshotMultiplier;
                crit = true;
            }
            else if (chestCollider != null && hitCollider == chestCollider)
            {
                mult *= chestMultiplier;
            }
        }
        else if (isHeadshot || (!string.IsNullOrEmpty(hitPart) && hitPart.ToLower().Contains("head")))
        {
            mult *= headshotMultiplier;
            crit = true;
        }

        // Reset hit flags
        isHeadshot = false;
        isChest = false;
        hitPart = "";
        hitCollider = null;
        damageMultiplier = 1.0f;

        float finalDmg = rawDmg * mult;

        // Forward to owner or apply locally
        if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        _ApplyDamageLocally(finalDmg, crit);
    }

    public void TakeDamage()
    {
        _TakeDamage();
    }

    private void _ApplyDamageLocally(float finalDmg, bool isCrit)
    {
        if (isDead) return;

        // Shield absorption
        float damageToHealth = finalDmg;
        if (currentShield > 0f)
        {
            if (currentShield >= finalDmg)
            {
                currentShield -= finalDmg;
                damageToHealth = 0f;
            }
            else
            {
                damageToHealth = finalDmg - currentShield;
                currentShield = 0f;
            }
        }

        currentHealth = Mathf.Max(0f, currentHealth - damageToHealth);

        // Feedback
        _PlayHitFeedback(isCrit);

        if (enableLogging)
        {
            string critText = isCrit ? " [CRITICAL HEADSHOT!]" : "";
            Debug.Log($"{logPrefix} '{dummyName}' took {finalDmg:F1} dmg{critText} | HP: {currentHealth:F1}/{maxHealth:F1} | Shield: {currentShield:F1}");
        }

        if (currentHealth <= 0f)
        {
            _HandleDeath();
        }

        _syncedHealth = currentHealth;
        _syncedShield = currentShield;
        _syncedIsDead = isDead;
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }

    private void _PlayHitFeedback(bool isCrit)
    {
        // Wobble reaction: tilt back 15 degrees
        _currentTilt = isCrit ? 22f : 14f;

        // Audio
        if (dummyAudio != null)
        {
            if (isCrit && headshotDingSound != null)
            {
                dummyAudio.PlayOneShot(headshotDingSound);
            }
            else if (bodyHitSound != null)
            {
                dummyAudio.PlayOneShot(bodyHitSound);
            }
        }

        // Color flash
        if (_matInstance != null && _matInstance.HasProperty("_Color"))
        {
            _matInstance.color = hitFlashColor;
            SendCustomEventDelayedSeconds(nameof(_ResetColorFlash), 0.1f);
        }
    }

    public void _ResetColorFlash()
    {
        if (_matInstance != null && _matInstance.HasProperty("_Color"))
        {
            _matInstance.color = _originalColor;
        }
    }

    // -------------------------------------------------------------------------
    // Death & Respawning
    // -------------------------------------------------------------------------
    private void _HandleDeath()
    {
        isDead = true;

        if (deathSound != null && dummyAudio != null)
        {
            dummyAudio.PlayOneShot(deathSound);
        }

        if (deathEffect != null)
        {
            deathEffect.SetActive(true);
        }

        // Knock dummy flat onto the ground
        if (dummyPivot != null)
        {
            dummyPivot.localRotation = _deadRotation;
        }

        // Disable hitboxes while down
        if (headCollider != null) headCollider.enabled = false;
        if (chestCollider != null) chestCollider.enabled = false;

        // Optional AoE explosion
        if (explodeOnDeath)
        {
            _Explode();
        }

        if (enableLogging) Debug.Log($"{logPrefix} '{dummyName}' knocked down! Auto-respawning in {respawnDelay:F1}s...");

        if (respawnDelay > 0f)
        {
            SendCustomEventDelayedSeconds(nameof(_Respawn), respawnDelay);
        }
    }

    /// <summary>
    /// Respawns the dummy with full health and stands it upright.
    /// </summary>
    public void _Respawn()
    {
        isDead = false;
        currentHealth = maxHealth;
        currentShield = maxShield;
        _currentTilt = 0f;

        // Restore upright position
        if (dummyPivot != null)
        {
            dummyPivot.localRotation = _restRotation;
        }

        // Re-enable hitboxes
        if (headCollider != null) headCollider.enabled = true;
        if (chestCollider != null) chestCollider.enabled = true;

        if (deathEffect != null)
        {
            deathEffect.SetActive(false);
        }

        if (dummyAudio != null && respawnSound != null)
        {
            dummyAudio.PlayOneShot(respawnSound);
        }

        if (_matInstance != null && _matInstance.HasProperty("_Color"))
        {
            _matInstance.color = _originalColor;
        }

        _syncedHealth = currentHealth;
        _syncedShield = currentShield;
        _syncedIsDead = isDead;
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }

        if (enableLogging) Debug.Log($"{logPrefix} '{dummyName}' stood back up and respawned at full health!");
    }

    public void Respawn()
    {
        _Respawn();
    }

    public void _ResetHealth()
    {
        _Respawn();
    }

    // -------------------------------------------------------------------------
    // Applying Damage to Nearby Players / Objects (Counter-Attack / Explosive)
    // -------------------------------------------------------------------------
    private void _Explode()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius);
        if (hits == null || hits.Length == 0) return;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];
            if (col == null || col == headCollider || col == chestCollider) continue;

            UdonBehaviour ub = (UdonBehaviour)col.GetComponent(typeof(UdonBehaviour));
            if (ub == null) ub = (UdonBehaviour)col.GetComponentInParent(typeof(UdonBehaviour));

            if (ub != null)
            {
                // Set variables using <UdonBehaviour>().SetProgramVariable
                ub.SetProgramVariable("incomingDamage", explosionDamage);
                ub.SetProgramVariable("damage", explosionDamage);
                ub.SetProgramVariable("damageAmount", explosionDamage);
                ub.SetProgramVariable("hitPart", "Explosion");
                ub.SetProgramVariable("attackerName", dummyName);
                ub.SetProgramVariable("attackerTeamId", teamId);
                ub.SendCustomEvent("_TakeDamage");
                ub.SendCustomEvent("TakeDamage");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Network Deserialization
    // -------------------------------------------------------------------------
    public override void OnDeserialization()
    {
        currentHealth = _syncedHealth;
        currentShield = _syncedShield;
        bool wasDead = isDead;
        isDead = _syncedIsDead;

        if (!wasDead && isDead)
        {
            _HandleDeath();
        }
        else if (wasDead && !isDead)
        {
            _Respawn();
        }
    }
}
