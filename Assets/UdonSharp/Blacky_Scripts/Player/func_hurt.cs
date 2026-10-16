using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// func_hurt / trigger_hurt:
/// A universal hazard and out-of-bounds trigger volume for VRChat worlds.
/// Applies damage or instant kill to Players and AI entering or standing in the trigger.
/// Seamlessly integrates with DamageSystem.cs.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
[RequireComponent(typeof(Collider))]
public class func_hurt : UdonSharpBehaviour
{
    [Header("--- Damage & Hazard Settings ---")]
    [Tooltip("Amount of damage dealt to players or AI upon entering/staying in the volume.")]
    public float damageAmount = 25f;

    [Tooltip("If true, this volume acts as Out of Bounds (instant kill / massive damage).")]
    public bool isOutOfBounds = false;

    [Tooltip("If true, deals repeated tick damage while an entity remains inside.")]
    public bool continuousDamage = true;

    [Tooltip("Delay in seconds between repeated damage ticks when continuousDamage is enabled.")]
    public float damageInterval = 0.5f;

    [Tooltip("Name or description of this hazard (e.g., 'Out of Bounds', 'Lava', 'Spikes', 'Acid').")]
    public string hazardName = "Hazard";

    [Header("--- Target Filtering ---")]
    [Tooltip("If true, hurts players entering the trigger.")]
    public bool hurtPlayers = true;

    [Tooltip("If true, hurts AI / enemies entering the trigger.")]
    public bool hurtAI = true;

    [Tooltip("Optional direct reference to the local player's DamageSystem. If left empty, will automatically find it.")]
    public DamageSystem localPlayerDamageSystem;

    [Header("--- Out Of Bounds & Respawn Options ---")]
    [Tooltip("If true and isOutOfBounds is active, forces the player to respawn immediately.")]
    public bool forcePlayerRespawn = true;

    [Tooltip("Optional custom teleport/respawn point. If assigned, teleports out-of-bounds players/AI here instead of world spawn.")]
    public Transform customRespawnPoint;

    [Header("--- Effects & Audio Feedback ---")]
    [Tooltip("Optional sound effect played when an entity takes damage from this volume.")]
    public AudioSource hurtAudioSource;

    [Tooltip("Optional particle system or visual effect GameObject triggered on damage.")]
    public GameObject hurtEffect;

    [Header("--- Debug & Logging ---")]
    [Tooltip("Log damage and out-of-bounds events to the console.")]
    public bool enableLogging = true;

    [Tooltip("Prefix for console logs.")]
    public string logPrefix = "[func_hurt]";

    // Variable exposed for external scripts querying GetProgramVariable("damage")
    [HideInInspector, System.NonSerialized] public float damage;

    // Runtime state
    private Collider _triggerCollider;
    private float _nextPlayerDamageTime = 0f;
    private float _nextAIDamageTime = 0f;
    private bool _isActive = true;

    void Start()
    {
        _triggerCollider = GetComponent<Collider>();
        if (_triggerCollider != null)
        {
            // Ensure collider acts as a trigger volume
            if (!_triggerCollider.isTrigger)
            {
                _triggerCollider.isTrigger = true;
                if (enableLogging) Debug.Log($"{logPrefix} Set collider on '{gameObject.name}' to isTrigger = true.");
            }
        }
        else
        {
            Debug.LogError($"{logPrefix} No Collider component found on '{gameObject.name}'! func_hurt requires a Collider.");
        }

        // Set public variable representation
        damage = isOutOfBounds ? 99999f : damageAmount;

        // Player DamageSystem is resolved dynamically via Networking.GetPlayerObjects when entering the trigger

        if (enableLogging)
        {
            string mode = isOutOfBounds ? "Out Of Bounds (Lethal)" : $"{damageAmount:F1} DMG (Interval: {damageInterval:F2}s)";
            Debug.Log($"{logPrefix} Initialized on '{gameObject.name}' | Mode: {mode} | Hazard: '{hazardName}'");
        }
    }

    // -------------------------------------------------------------------------
    // VRChat Player Trigger Callbacks
    // -------------------------------------------------------------------------
    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!_isActive || !hurtPlayers) return;
        if (player == null || !player.IsValid() || !player.isLocal) return;

        _HurtLocalPlayer(player);
    }

    public override void OnPlayerTriggerStay(VRCPlayerApi player)
    {
        if (!_isActive || !hurtPlayers || !continuousDamage) return;
        if (player == null || !player.IsValid() || !player.isLocal) return;

        if (Time.time >= _nextPlayerDamageTime)
        {
            _HurtLocalPlayer(player);
        }
    }

    private DamageSystem _GetPlayerDamageSystem(VRCPlayerApi player)
    {
        if (localPlayerDamageSystem != null) return localPlayerDamageSystem;
        if (player == null || !player.IsValid()) return null;

        // VRC Player Object: get instantiated objects for this player
        GameObject[] playerObjects = Networking.GetPlayerObjects(player);
        if (playerObjects != null)
        {
            for (int i = 0; i < playerObjects.Length; i++)
            {
                GameObject obj = playerObjects[i];
                if (obj != null)
                {
                    DamageSystem ds = obj.GetComponent<DamageSystem>();
                    if (ds != null)
                    {
                        if (player.isLocal) localPlayerDamageSystem = ds;
                        return ds;
                    }
                }
            }
        }

        return null;
    }

    private void _HurtLocalPlayer(VRCPlayerApi player)
    {
        _nextPlayerDamageTime = Time.time + damageInterval;

        float appliedDamage = isOutOfBounds ? 99999f : damageAmount;

        // Play audio & visual feedback
        _PlayFeedback();

        DamageSystem targetSystem = _GetPlayerDamageSystem(player);

        if (isOutOfBounds)
        {
            if (enableLogging)
            {
                Debug.Log($"{logPrefix} [OUT OF BOUNDS] Local Player '{player.displayName}' entered out-of-bounds area '{gameObject.name}' ({hazardName})!");
            }

            // Apply lethal damage to DamageSystem if available
            if (targetSystem != null)
            {
                targetSystem.incomingDamage = appliedDamage;
                targetSystem.hitPart = hazardName;
                targetSystem.attackerName = gameObject.name;
                targetSystem._TakeDamage();
            }

            // Teleport or Respawn
            if (customRespawnPoint != null)
            {
                player.TeleportTo(customRespawnPoint.position, customRespawnPoint.rotation);
                if (enableLogging) Debug.Log($"{logPrefix} Teleported '{player.displayName}' to custom respawn point '{customRespawnPoint.name}'.");
            }
            else if (forcePlayerRespawn)
            {
                player.Respawn();
                if (enableLogging) Debug.Log($"{logPrefix} Respawned '{player.displayName}' at world spawn.");
            }
        }
        else
        {
            // Standard hazard damage
            if (enableLogging)
            {
                Debug.Log($"{logPrefix} [HAZARD] Local Player '{player.displayName}' took {appliedDamage:F1} damage from '{gameObject.name}' ({hazardName}).");
            }

            if (targetSystem != null)
            {
                targetSystem.incomingDamage = appliedDamage;
                targetSystem.hitPart = hazardName;
                targetSystem.attackerName = gameObject.name;
                targetSystem._TakeDamage();
            }
            else
            {
                if (enableLogging)
                {
                    Debug.LogWarning($"{logPrefix} Local Player took damage, but no DamageSystem was found on the player object!");
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Unity Physics Trigger Callbacks (AI & Entity Hitboxes)
    // -------------------------------------------------------------------------
    public void OnTriggerEnter(Collider other)
    {
        if (!_isActive || other == null) return;
        _HandleEntityCollider(other, false);
    }

    public void OnTriggerStay(Collider other)
    {
        if (!_isActive || !continuousDamage || other == null) return;
        if (Time.time < _nextAIDamageTime) return;

        _HandleEntityCollider(other, true);
    }

    private void _HandleEntityCollider(Collider other, bool isStay)
    {
        // Ignore environment, pickups, or default non-combat layers
        int otherLayer = other.gameObject.layer;
        if (otherLayer == 11 || otherLayer == 0) return;

        // Try to find DamageSystem on the entity or its parents
        UdonBehaviour targetUdon = (UdonBehaviour)other.GetComponent(typeof(UdonBehaviour));
        if (targetUdon == null)
        {
            targetUdon = (UdonBehaviour)other.GetComponentInParent(typeof(UdonBehaviour));
        }

        if (targetUdon != null)
        {
            // Check if this target is an AI or Player
            object isPlayerObj = targetUdon.GetProgramVariable("isPlayer");
            bool isTargetPlayer = false;
            if (isPlayerObj != null && isPlayerObj.GetType() == typeof(bool))
            {
                isTargetPlayer = (bool)isPlayerObj;
            }

            // Filter out targets based on settings
            if (isTargetPlayer && !hurtPlayers) return;
            if (!isTargetPlayer && !hurtAI) return;

            // If it is a local player, OnPlayerTriggerEnter already handled them
            if (isTargetPlayer) return;

            _nextAIDamageTime = Time.time + damageInterval;

            float appliedDamage = isOutOfBounds ? 99999f : damageAmount;

            // Apply damage via SetProgramVariable
            targetUdon.SetProgramVariable("incomingDamage", appliedDamage);
            targetUdon.SetProgramVariable("hitPart", hazardName);
            targetUdon.SetProgramVariable("attackerName", gameObject.name);
            targetUdon.SendCustomEvent("_TakeDamage");

            _PlayFeedback();

            if (enableLogging)
            {
                string targetType = isTargetPlayer ? "Player" : "AI";
                Debug.Log($"{logPrefix} [HIT] {targetType} '{other.gameObject.name}' took {appliedDamage:F1} damage from '{gameObject.name}' ({hazardName}).");
            }

            // Handle AI Out Of Bounds repositioning
            if (isOutOfBounds)
            {
                if (customRespawnPoint != null)
                {
                    Transform rootTransform = other.transform.root;
                    rootTransform.position = customRespawnPoint.position;
                    rootTransform.rotation = customRespawnPoint.rotation;

                    Rigidbody rb = other.attachedRigidbody;
                    if (rb != null)
                    {
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }

                    if (enableLogging) Debug.Log($"{logPrefix} Relocated '{other.gameObject.name}' to custom respawn point.");
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Feedback & Controls
    // -------------------------------------------------------------------------
    private void _PlayFeedback()
    {
        if (hurtAudioSource != null)
        {
            hurtAudioSource.Play();
        }

        if (hurtEffect != null)
        {
            hurtEffect.SetActive(true);
        }
    }

    public void _EnableHurt()
    {
        _isActive = true;
        if (_triggerCollider != null) _triggerCollider.enabled = true;
        if (enableLogging) Debug.Log($"{logPrefix} Activated '{gameObject.name}'.");
    }

    public void _DisableHurt()
    {
        _isActive = false;
        if (_triggerCollider != null) _triggerCollider.enabled = false;
        if (enableLogging) Debug.Log($"{logPrefix} Deactivated '{gameObject.name}'.");
    }

    public void _ToggleHurt()
    {
        if (_isActive) _DisableHurt();
        else _EnableHurt();
    }
}
