using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// Universal Damage System for VRChat worlds (UdonSharp).
/// Supports both Player and AI/NPC damage tracking, head/chest hitboxes with multipliers,
/// network synchronization, and custom damage values passed via SetProgramVariable.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class DamageSystem : UdonSharpBehaviour
{
    [Header("--- Target Configuration ---")]
    [Tooltip("If true, this system manages a Player's health. If false, it manages an AI / NPC / Entity.")]
    public bool isPlayer = true;

    [Tooltip("Custom display name for AI / NPC (used in console logs). Defaults to GameObject name if left empty.")]
    public string aiName = "";

    [Header("--- Health & Shields ---")]
    [Tooltip("Maximum health capacity.")]
    public float maxHealth = 100f;

    [Tooltip("Current health (clamped between 0 and maxHealth).")]
    public float currentHealth = 100f;

    [Tooltip("Optional shield capacity. When > 0, damage is absorbed by shield first.")]
    public float maxShield = 0f;

    [Tooltip("Current shield value.")]
    public float currentShield = 0f;

    [Tooltip("Invulnerability flag. When true, all incoming damage is negated.")]
    public bool isInvulnerable = false;

    [Header("--- Hitbox References (Head & Chest) ---")]
    [Tooltip("Transform representing the Head hitbox.")]
    public Transform headHitbox;

    [Tooltip("Collider representing the Head hitbox (for raycasts / triggers / collision checks).")]
    public Collider headCollider;

    [Tooltip("Transform representing the Chest hitbox.")]
    public Transform chestHitbox;

    [Tooltip("Collider representing the Chest hitbox (for raycasts / triggers / collision checks).")]
    public Collider chestCollider;

    [Header("--- Hitbox Multipliers ---")]
    [Tooltip("Damage multiplier applied when the Head hitbox is hit (e.g., 2.0 = 200% damage).")]
    public float headshotMultiplier = 2.0f;

    [Tooltip("Damage multiplier applied when the Chest hitbox is hit (e.g., 1.0 = 100% damage).")]
    public float chestMultiplier = 1.0f;

    [Header("--- Hitbox Bone Tracking & Attachment ---")]
    [Tooltip("If isPlayer is true, automatically updates head and chest hitboxes to follow player avatar bones.")]
    public bool trackPlayerBones = true;

    [Tooltip("Local positional offset applied to the head hitbox when tracking bones.")]
    public Vector3 headOffset = Vector3.zero;

    [Tooltip("Local positional offset applied to the chest hitbox when tracking bones.")]
    public Vector3 chestOffset = Vector3.zero;

    [Tooltip("Optional AI Head bone transform to track if this is an AI/NPC.")]
    public Transform aiHeadBone;

    [Tooltip("Optional AI Chest bone transform to track if this is an AI/NPC.")]
    public Transform aiChestBone;

    [Tooltip("If true and aiHeadBone/aiChestBone are assigned, automatically parents hitboxes to bones on Start.")]
    public bool autoParentAIBones = false;

    [Header("--- Death & Respawn Behavior ---")]
    [Tooltip("For Players: automatically respawn the player at world spawn when health reaches 0.")]
    public bool respawnOnDeath = true;

    [Tooltip("For AI: deactivate this GameObject when health reaches 0.")]
    public bool deactivateOnDeath = false;

    [Tooltip("Optional visual effect GameObject to activate upon death (e.g., explosion, ragdoll).")]
    public GameObject deathEffect;

    [Header("--- Network Settings ---")]
    [Tooltip("Synchronize health and death state over the network with Manual serialization.")]
    public bool syncHealth = true;

    [Tooltip("If a non-owner applies damage, acquire ownership first. If false, sends a network event to the owner.")]
    public bool takeOwnershipOnDamage = false;

    [Header("--- Debug & Logging ---")]
    [Tooltip("Output detailed logs to the Unity / VRChat console when damage, healing, or death occurs.")]
    public bool enableLogging = true;

    [Tooltip("Prefix attached to all console messages from this script.")]
    public string logPrefix = "[DamageSystem]";

    // =========================================================================
    // Variables accessible via <UdonBehaviour>().SetProgramVariable(...)
    // =========================================================================
    [HideInInspector, System.NonSerialized] public float incomingDamage;
    [HideInInspector, System.NonSerialized] public float damage;
    [HideInInspector, System.NonSerialized] public float damageAmount;
    [HideInInspector, System.NonSerialized] public int incomingDamageInt;
    [HideInInspector, System.NonSerialized] public int damageInt;
    [HideInInspector, System.NonSerialized] public float damageMultiplier = 1.0f;
    [HideInInspector, System.NonSerialized] public string hitPart = "";
    [HideInInspector, System.NonSerialized] public bool isHeadshot = false;
    [HideInInspector, System.NonSerialized] public bool isChest = false;
    [HideInInspector, System.NonSerialized] public Collider hitCollider;
    [HideInInspector, System.NonSerialized] public float healAmount;
    [HideInInspector, System.NonSerialized] public VRCPlayerApi attackerPlayer;
    [HideInInspector, System.NonSerialized] public string attackerName = "";
    [HideInInspector, System.NonSerialized] public VRCPlayerApi targetPlayer;

    // =========================================================================
    // Synced State
    // =========================================================================
    [UdonSynced] private float _syncedHealth;
    [UdonSynced] private bool _syncedIsDead;

    // =========================================================================
    // Internal Runtime State
    // =========================================================================
    [HideInInspector] public bool isDead = false;
    [HideInInspector] public float totalDamageTaken = 0f;
    [HideInInspector] public int totalHitsReceived = 0;

    private bool _hasInitialized = false;

    // -------------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------------
    public void Start()
    {
        _Initialize();
        Debug.Log("Started");
    }

    private void _Initialize()
    {
        if (_hasInitialized) return;
        _hasInitialized = true;

        currentHealth = maxHealth;
        currentShield = maxShield;
        isDead = false;
        totalDamageTaken = 0f;
        totalHitsReceived = 0;

        // Auto-resolve player reference if in player mode (VRC Player Object support)
        if (isPlayer)
        {
            if (targetPlayer == null || !targetPlayer.IsValid())
            {
                targetPlayer = Networking.GetOwner(gameObject);
                if (targetPlayer == null || !targetPlayer.IsValid())
                {
                    targetPlayer = Networking.LocalPlayer;
                }
            }
        }

        // Auto-discover child hitboxes if not explicitly assigned in Inspector
        _AutoDiscoverHitboxes();

        // AI bone auto-parenting if requested
        if (!isPlayer && autoParentAIBones)
        {
            _AttachHitboxesToAIBones();
        }

        // Initialize synced variables if we own this object
        if (syncHealth && Networking.IsOwner(gameObject))
        {
            _syncedHealth = currentHealth;
            _syncedIsDead = isDead;
            RequestSerialization();
        }

        if (enableLogging)
        {
            string entityType = isPlayer ? "Player" : "AI";
            string headStatus = headHitbox != null ? (headCollider != null ? "Ready (Collider attached)" : "Transform only (No Collider)") : "None";
            string chestStatus = chestHitbox != null ? (chestCollider != null ? "Ready (Collider attached)" : "Transform only (No Collider)") : "None";

            Debug.Log($"{logPrefix} Initialized on '{_GetEntityName()}' | Type: {entityType} | Max HP: {maxHealth:F1} | Shield: {maxShield:F1} | Headshot Mult: {headshotMultiplier:F1}x | Head Hitbox: {headStatus} | Chest Hitbox: {chestStatus}");
        }
    }

    // -------------------------------------------------------------------------
    // Hitbox Discovery & Attachment
    // -------------------------------------------------------------------------
    private void _AutoDiscoverHitboxes()
    {
        // Try to find Head Hitbox if null
        if (headHitbox == null)
        {
            Transform found = transform.Find("HeadHitbox");
            if (found == null) found = transform.Find("Head");
            if (found != null) headHitbox = found;
        }

        // Try to find Chest Hitbox if null
        if (chestHitbox == null)
        {
            Transform found = transform.Find("ChestHitbox");
            if (found == null) found = transform.Find("Chest");
            if (found != null) chestHitbox = found;
        }

        // Auto-assign colliders from hitbox transforms if not explicitly set
        if (headHitbox != null && headCollider == null)
        {
            headCollider = headHitbox.GetComponent<Collider>();
        }
        if (chestHitbox != null && chestCollider == null)
        {
            chestCollider = chestHitbox.GetComponent<Collider>();
        }
    }

    /// <summary>
    /// Attach hitboxes to AI bones as children so they move with animations automatically.
    /// </summary>
    public void _AttachHitboxesToAIBones()
    {
        if (aiHeadBone != null && headHitbox != null)
        {
            headHitbox.SetParent(aiHeadBone, false);
            headHitbox.localPosition = headOffset;
            headHitbox.localRotation = Quaternion.identity;
            if (enableLogging) Debug.Log($"{logPrefix} Head hitbox attached to AI bone '{aiHeadBone.name}'.");
        }

        if (aiChestBone != null && chestHitbox != null)
        {
            chestHitbox.SetParent(aiChestBone, false);
            chestHitbox.localPosition = chestOffset;
            chestHitbox.localRotation = Quaternion.identity;
            if (enableLogging) Debug.Log($"{logPrefix} Chest hitbox attached to AI bone '{aiChestBone.name}'.");
        }
    }

    // -------------------------------------------------------------------------
    // Per-Frame Tracking & Fallback Variable Polling
    // -------------------------------------------------------------------------
    public void Update()
    {
        // Polling safeguard: if another script set incoming damage via SetProgramVariable
        // but forgot to call SendCustomEvent("_TakeDamage"), process it automatically.
        if (incomingDamage > 0f || damage > 0f || damageAmount > 0f || incomingDamageInt > 0 || damageInt > 0)
        {
            _TakeDamage();
        }
    }

    public override void PostLateUpdate()
    {
        // PostLateUpdate runs after VRChat avatar IK has resolved, giving jitter-free bone alignment.
        if (isPlayer && trackPlayerBones)
        {
            _UpdatePlayerBoneHitboxes();
        }
        else if (!isPlayer && !autoParentAIBones)
        {
            _UpdateAIBoneHitboxes();
        }
    }

    private void _UpdatePlayerBoneHitboxes()
    {
        VRCPlayerApi player = targetPlayer != null && targetPlayer.IsValid() ? targetPlayer : Networking.LocalPlayer;
        if (player == null || !player.IsValid()) return;

        // Head tracking
        if (headHitbox != null)
        {
            Vector3 headPos = player.GetBonePosition(HumanBodyBones.Head);
            Quaternion headRot = player.GetBoneRotation(HumanBodyBones.Head);

            // Fallback for custom or non-humanoid avatars lacking Head bone
            if (headPos == Vector3.zero)
            {
                VRCPlayerApi.TrackingData headData = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                headPos = headData.position;
                headRot = headData.rotation;
            }

            headHitbox.position = headPos + (headRot * headOffset);
            headHitbox.rotation = headRot;
        }

        // Chest tracking
        if (chestHitbox != null)
        {
            Vector3 chestPos = player.GetBonePosition(HumanBodyBones.Chest);
            Quaternion chestRot = player.GetBoneRotation(HumanBodyBones.Chest);

            // Fallback to Spine if Chest bone is not mapped
            if (chestPos == Vector3.zero)
            {
                chestPos = player.GetBonePosition(HumanBodyBones.Spine);
                chestRot = player.GetBoneRotation(HumanBodyBones.Spine);
            }
            // Fallback to Hips if Spine is also not mapped
            if (chestPos == Vector3.zero)
            {
                chestPos = player.GetBonePosition(HumanBodyBones.Hips);
                chestRot = player.GetBoneRotation(HumanBodyBones.Hips);
            }

            chestHitbox.position = chestPos + (chestRot * chestOffset);
            chestHitbox.rotation = chestRot;
        }
    }

    private void _UpdateAIBoneHitboxes()
    {
        if (aiHeadBone != null && headHitbox != null)
        {
            headHitbox.position = aiHeadBone.position + (aiHeadBone.rotation * headOffset);
            headHitbox.rotation = aiHeadBone.rotation;
        }

        if (aiChestBone != null && chestHitbox != null)
        {
            chestHitbox.position = aiChestBone.position + (aiChestBone.rotation * chestOffset);
            chestHitbox.rotation = aiChestBone.rotation;
        }
    }

    // -------------------------------------------------------------------------
    // Damage Processing
    // -------------------------------------------------------------------------
    /// <summary>
    /// Core local method to calculate and apply incoming damage.
    /// Reads incoming damage variables set via SetProgramVariable.
    /// </summary>
    public void _TakeDamage()
    {
        // 1. Gather raw damage value from any of the supported variables
        float rawDamage = 0f;
        if (incomingDamage > 0f) rawDamage = incomingDamage;
        else if (damage > 0f) rawDamage = damage;
        else if (damageAmount > 0f) rawDamage = damageAmount;
        else if (incomingDamageInt > 0) rawDamage = (float)incomingDamageInt;
        else if (damageInt > 0) rawDamage = (float)damageInt;

        // Reset incoming damage variables immediately to avoid re-triggering
        incomingDamage = 0f;
        damage = 0f;
        damageAmount = 0f;
        incomingDamageInt = 0;
        damageInt = 0;

        if (rawDamage <= 0f) return;

        if (isDead)
        {
            if (enableLogging) Debug.Log($"{logPrefix} Damage ignored on '{_GetEntityName()}': target is already dead.");
            return;
        }

        if (isInvulnerable)
        {
            if (enableLogging) Debug.Log($"{logPrefix} Damage negated on '{_GetEntityName()}': target is invulnerable.");
            return;
        }

        // 2. Determine hitbox hit location & multiplier
        float multiplier = damageMultiplier > 0f ? damageMultiplier : 1.0f;
        string locationName = "Body";

        if (hitCollider != null)
        {
            if (headCollider != null && hitCollider == headCollider)
            {
                multiplier *= headshotMultiplier;
                locationName = "Head (Hitbox Collider)";
            }
            else if (chestCollider != null && hitCollider == chestCollider)
            {
                multiplier *= chestMultiplier;
                locationName = "Chest (Hitbox Collider)";
            }
        }
        else if (isHeadshot || (!string.IsNullOrEmpty(hitPart) && hitPart.ToLower() == "head"))
        {
            multiplier *= headshotMultiplier;
            locationName = "Head";
        }
        else if (isChest || (!string.IsNullOrEmpty(hitPart) && hitPart.ToLower() == "chest"))
        {
            multiplier *= chestMultiplier;
            locationName = "Chest";
        }

        // Reset per-hit variable flags
        isHeadshot = false;
        isChest = false;
        hitPart = "";
        hitCollider = null;
        damageMultiplier = 1.0f;

        // 3. Compute final damage
        float finalDamage = rawDamage * multiplier;

        // 4. Dispatch damage (Owner direct application or Network dispatch)
        if (syncHealth && Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
        {
            if (takeOwnershipOnDamage)
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
                _ApplyDamageLocally(finalDamage, rawDamage, multiplier, locationName);
            }
            else
            {
                if (enableLogging)
                {
                    Debug.Log($"{logPrefix} Non-owner dealing {finalDamage:F1} damage to '{_GetEntityName()}'. Sending NetworkCallable request to Owner...");
                }
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_OwnerRequestDamage), finalDamage, locationName);
            }
        }
        else
        {
            _ApplyDamageLocally(finalDamage, rawDamage, multiplier, locationName);
        }
    }

    /// <summary>
    /// NetworkCallable entry point allowing remote clients to request damage on the owner.
    /// </summary>
    [NetworkCallable]
    public void _OwnerRequestDamage(float requestedDamage, string locationName)
    {
        if (!NetworkCalling.InNetworkCall) return;

        VRCPlayerApi caller = NetworkCalling.CallingPlayer;
        if (caller == null || !caller.IsValid()) return;

        if (!Networking.IsOwner(gameObject)) return;

        attackerPlayer = caller;
        attackerName = caller.displayName;

        _ApplyDamageLocally(requestedDamage, requestedDamage, 1.0f, locationName);
    }

    private void _ApplyDamageLocally(float finalDamage, float rawDamage, float multiplier, string locationName)
    {
        if (isDead) return;

        // Shield absorption logic
        float damageToHealth = finalDamage;
        float shieldAbsorbed = 0f;

        if (currentShield > 0f)
        {
            if (currentShield >= finalDamage)
            {
                shieldAbsorbed = finalDamage;
                currentShield -= finalDamage;
                damageToHealth = 0f;
            }
            else
            {
                shieldAbsorbed = currentShield;
                damageToHealth = finalDamage - currentShield;
                currentShield = 0f;
            }
        }

        // Apply remaining damage to health
        currentHealth = Mathf.Max(0f, currentHealth - damageToHealth);
        totalDamageTaken += finalDamage;
        totalHitsReceived++;

        // Detailed console logging
        if (enableLogging)
        {
            string attackerInfo = !string.IsNullOrEmpty(attackerName) ? $" | Attacker: {attackerName}" : "";
            string shieldInfo = maxShield > 0f ? $" | Shield: {currentShield:F1}/{maxShield:F1} (Absorbed: {shieldAbsorbed:F1})" : "";

            Debug.Log($"{logPrefix} [HIT] '{_GetEntityName()}' took {finalDamage:F1} damage [{locationName}] (Raw: {rawDamage:F1}, Mult: {multiplier:F1}x){attackerInfo}{shieldInfo} | Remaining HP: {currentHealth:F1}/{maxHealth:F1}");
        }

        // Death check
        if (currentHealth <= 0f && !isDead)
        {
            _HandleDeath();
        }

        // Sync with remote clients
        if (syncHealth && Networking.IsOwner(gameObject))
        {
            _syncedHealth = currentHealth;
            _syncedIsDead = isDead;
            RequestSerialization();
        }
    }

    // -------------------------------------------------------------------------
    // Healing & Health Reset
    // -------------------------------------------------------------------------
    public void _Heal()
    {
        if (healAmount <= 0f) return;
        if (isDead)
        {
            if (enableLogging) Debug.Log($"{logPrefix} Cannot heal '{_GetEntityName()}': target is dead.");
            return;
        }

        float previousHP = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + healAmount);
        float actualHealed = currentHealth - previousHP;
        healAmount = 0f;

        if (enableLogging)
        {
            Debug.Log($"{logPrefix} [HEAL] '{_GetEntityName()}' restored {actualHealed:F1} HP. Current HP: {currentHealth:F1}/{maxHealth:F1}");
        }

        if (syncHealth && Networking.IsOwner(gameObject))
        {
            _syncedHealth = currentHealth;
            _syncedIsDead = isDead;
            RequestSerialization();
        }
    }

    public void _ResetHealth()
    {
        currentHealth = maxHealth;
        currentShield = maxShield;
        isDead = false;

        if (enableLogging)
        {
            Debug.Log($"{logPrefix} [RESET] '{_GetEntityName()}' health and shields fully restored ({currentHealth:F1}/{maxHealth:F1}).");
        }

        if (syncHealth && Networking.IsOwner(gameObject))
        {
            _syncedHealth = currentHealth;
            _syncedIsDead = isDead;
            RequestSerialization();
        }
    }

    // -------------------------------------------------------------------------
    // Death Handling
    // -------------------------------------------------------------------------
    private void _HandleDeath()
    {
        isDead = true;

        if (enableLogging)
        {
            Debug.Log($"{logPrefix} [DEATH] *** '{_GetEntityName()}' HAS DIED! *** (Total Damage Taken: {totalDamageTaken:F1}, Hits: {totalHitsReceived})");
        }

        if (deathEffect != null)
        {
            deathEffect.SetActive(true);
        }

        if (isPlayer)
        {
            _OnPlayerDeath();
        }
        else
        {
            _OnAIDeath();
        }
    }

    private void _OnPlayerDeath()
    {
        VRCPlayerApi player = targetPlayer != null && targetPlayer.IsValid() ? targetPlayer : Networking.LocalPlayer;
        if (player != null && player.IsValid() && player.isLocal)
        {
            if (respawnOnDeath)
            {
                if (enableLogging) Debug.Log($"{logPrefix} Respawning local player at world spawn point...");
                player.Respawn();
                _ResetHealth();
            }
        }
    }

    private void _OnAIDeath()
    {
        if (deactivateOnDeath)
        {
            if (enableLogging) Debug.Log($"{logPrefix} Deactivating AI GameObject '{gameObject.name}'.");
            gameObject.SetActive(false);
        }
    }

    // -------------------------------------------------------------------------
    // Network Deserialization (Remote Clients)
    // -------------------------------------------------------------------------
    public override void OnDeserialization()
    {
        float previousHP = currentHealth;
        currentHealth = _syncedHealth;
        bool wasDead = isDead;
        isDead = _syncedIsDead;

        if (enableLogging && Mathf.Abs(previousHP - currentHealth) > 0.01f)
        {
            Debug.Log($"{logPrefix} [NETWORK SYNC] '{_GetEntityName()}' HP updated from owner: {currentHealth:F1}/{maxHealth:F1}");
        }

        if (!wasDead && isDead)
        {
            _HandleDeath();
        }
        else if (wasDead && !isDead)
        {
            if (enableLogging) Debug.Log($"{logPrefix} [NETWORK SYNC] '{_GetEntityName()}' was revived.");
        }
    }

    // -------------------------------------------------------------------------
    // Unity Physics Callbacks (Optional Direct Projectile / Collider Contact)
    // -------------------------------------------------------------------------
    public void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        _InspectIncomingCollider(other.gameObject, null);
    }

    public void OnCollisionEnter(Collision collision)
    {
        if (collision == null || collision.collider == null) return;
        Collider contactCollider = collision.contacts.Length > 0 ? collision.contacts[0].thisCollider : null;
        _InspectIncomingCollider(collision.gameObject, contactCollider);
    }

    private void _InspectIncomingCollider(GameObject sourceObj, Collider ourHitboxCollider)
    {
        if (isDead || sourceObj == null) return;

        UdonBehaviour ub = (UdonBehaviour)sourceObj.GetComponent(typeof(UdonBehaviour));
        if (ub == null) return;

        object dmgVal = ub.GetProgramVariable("damage");
        if (dmgVal == null) dmgVal = ub.GetProgramVariable("projectileDamage");
        if (dmgVal == null) dmgVal = ub.GetProgramVariable("incomingDamage");

        if (dmgVal != null)
        {
            float detectedDmg = 0f;
            System.Type valType = dmgVal.GetType();
            if (valType == typeof(float))
            {
                detectedDmg = (float)dmgVal;
            }
            else if (valType == typeof(int))
            {
                detectedDmg = (float)(int)dmgVal;
            }
            else if (valType == typeof(double))
            {
                detectedDmg = (float)(double)dmgVal;
            }

            if (detectedDmg > 0f)
            {
                hitCollider = ourHitboxCollider;
                incomingDamage = detectedDmg;
                _TakeDamage();
            }
        }
    }

    // -------------------------------------------------------------------------
    // VRChat Events
    // -------------------------------------------------------------------------
    public override void OnAvatarChanged(VRCPlayerApi player)
    {
        if (isPlayer && player != null && player.isLocal)
        {
            if (enableLogging) Debug.Log($"{logPrefix} Avatar changed for local player, updating hitbox tracking offsets.");
            _UpdatePlayerBoneHitboxes();
        }
    }

    public override void OnPlayerRespawn(VRCPlayerApi player)
    {
        if (isPlayer && player != null && player.isLocal)
        {
            _ResetHealth();
        }
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        if (isPlayer && player != null && player.IsValid())
        {
            targetPlayer = player;
            if (enableLogging) Debug.Log($"{logPrefix} Player Object ownership set to '{player.displayName}', updating bone tracking.");
        }
    }

    // -------------------------------------------------------------------------
    // Convenience & Legacy Public Method Aliases
    // -------------------------------------------------------------------------
    public void _ApplyDamage() { _TakeDamage(); }
    public void _TakeHeadDamage() { isHeadshot = true; _TakeDamage(); }
    public void _TakeChestDamage() { isChest = true; _TakeDamage(); }

    // Legacy public methods (without underscore) for scripts calling SendCustomEvent("TakeDamage")
    public void TakeDamage() { _TakeDamage(); }
    public void ApplyDamage() { _TakeDamage(); }
    public void TakeHeadDamage() { _TakeHeadDamage(); }
    public void TakeChestDamage() { _TakeChestDamage(); }
    public void Heal() { _Heal(); }
    public void ResetHealth() { _ResetHealth(); }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private string _GetEntityName()
    {
        if (isPlayer)
        {
            if (targetPlayer != null && targetPlayer.IsValid())
            {
                return $"Player ({targetPlayer.displayName})";
            }
            return "Player (Local)";
        }

        return !string.IsNullOrEmpty(aiName) ? aiName : gameObject.name;
    }
}
