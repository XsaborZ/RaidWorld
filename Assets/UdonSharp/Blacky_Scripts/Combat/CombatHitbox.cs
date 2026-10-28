using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// CombatHitbox:
/// Attached to specific body colliders (Head, Chest, Limbs, Weakpoints).
/// Supports damage multipliers, dynamic size/offset adjustment to fit any avatar or NPC model,
/// and delegates damage to parent DamageSystem / TargetDummy / VRPhysicalGun using SetProgramVariable.
/// </summary>
public enum HitboxType
{
    Head = 0,
    Chest = 1,
    Body = 2,
    Limb = 3,
    Weakpoint = 4,
    Custom = 5
}

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
[RequireComponent(typeof(Collider))]
public class CombatHitbox : UdonSharpBehaviour
{

    [Header("--- Hitbox Configuration ---")]
    [Tooltip("Type of hitbox this collider represents.")]
    public HitboxType hitboxType = HitboxType.Body;

    [Tooltip("Custom label for this hitbox (e.g. 'Head', 'Chest', 'Core'). Defaults to HitboxType name if empty.")]
    public string hitboxName = "";

    [Tooltip("Damage multiplier applied when this hitbox is hit (e.g. 2.0 = 200% damage for headshot).")]
    public float damageMultiplier = 1.0f;

    [Tooltip("Primary damage receiver UdonBehaviour (DamageSystem, TargetDummy, etc.). Auto-detected in parent if unassigned.")]
    public UdonBehaviour targetDamageReceiver;

    [Header("--- Dimensions & Bone Fitting ---")]
    [Tooltip("Positional offset relative to parent bone / transform.")]
    public Vector3 localOffset = Vector3.zero;

    [Tooltip("Box size or Sphere radius multiplier.")]
    public Vector3 hitboxDimensions = Vector3.one;

    [Header("--- Debug & Logging ---")]
    public bool enableLogging = false;
    public string logPrefix = "[CombatHitbox]";

    // Variables for external SetProgramVariable access
    [HideInInspector, System.NonSerialized] public float incomingDamage;
    [HideInInspector, System.NonSerialized] public float damage;
    [HideInInspector, System.NonSerialized] public float damageAmount;
    [HideInInspector, System.NonSerialized] public Collider hitCollider;
    [HideInInspector, System.NonSerialized] public VRCPlayerApi attackerPlayer;
    [HideInInspector, System.NonSerialized] public string attackerName = "";
    [HideInInspector, System.NonSerialized] public int attackerTeamId = 0;

    private Collider _attachedCollider;

    void Start()
    {
        _Initialize();
    }

    private void _Initialize()
    {
        _attachedCollider = GetComponent<Collider>();

        // Default multipliers if unset
        if (damageMultiplier <= 0f)
        {
            switch (hitboxType)
            {
                case HitboxType.Head:
                    damageMultiplier = 2.0f;
                    break;
                case HitboxType.Weakpoint:
                    damageMultiplier = 3.0f;
                    break;
                case HitboxType.Chest:
                    damageMultiplier = 1.0f;
                    break;
                case HitboxType.Limb:
                    damageMultiplier = 0.75f;
                    break;
                default:
                    damageMultiplier = 1.0f;
                    break;
            }
        }

        if (string.IsNullOrEmpty(hitboxName))
        {
            hitboxName = hitboxType.ToString();
        }

        // Auto-discover target damage receiver in parent hierarchy if not explicitly set
        if (targetDamageReceiver == null)
        {
            targetDamageReceiver = (UdonBehaviour)GetComponentInParent(typeof(UdonBehaviour));
        }

        _ApplyDimensions();
    }

    /// <summary>
    /// Adjusts hitbox positions and sizes to match avatar or NPC models.
    /// </summary>
    public void _SetHitboxDimensions(Vector3 newOffset, Vector3 newDimensions)
    {
        localOffset = newOffset;
        hitboxDimensions = newDimensions;
        _ApplyDimensions();
    }

    private void _ApplyDimensions()
    {
        transform.localPosition = localOffset;

        if (_attachedCollider == null)
        {
            _attachedCollider = GetComponent<Collider>();
            if (_attachedCollider == null) return;
        }

        if (_attachedCollider.GetType() == typeof(BoxCollider))
        {
            BoxCollider box = (BoxCollider)_attachedCollider;
            box.size = hitboxDimensions;
        }
        else if (_attachedCollider.GetType() == typeof(SphereCollider))
        {
            SphereCollider sphere = (SphereCollider)_attachedCollider;
            sphere.radius = Mathf.Max(0.01f, hitboxDimensions.x);
        }
        else if (_attachedCollider.GetType() == typeof(CapsuleCollider))
        {
            CapsuleCollider capsule = (CapsuleCollider)_attachedCollider;
            capsule.radius = Mathf.Max(0.01f, hitboxDimensions.x);
            capsule.height = Mathf.Max(0.02f, hitboxDimensions.y);
        }
    }

    /// <summary>
    /// Core method to receive damage and forward to parent receiver via SetProgramVariable.
    /// </summary>
    public void _TakeDamage()
    {
        float rawDmg = 0f;
        if (incomingDamage > 0f) rawDmg = incomingDamage;
        else if (damage > 0f) rawDmg = damage;
        else if (damageAmount > 0f) rawDmg = damageAmount;

        // Reset variables
        incomingDamage = 0f;
        damage = 0f;
        damageAmount = 0f;

        if (rawDmg <= 0f) return;

        _ForwardDamage(rawDmg, attackerPlayer, attackerName, attackerTeamId, hitCollider != null ? hitCollider : _attachedCollider);
    }

    public void TakeDamage()
    {
        _TakeDamage();
    }

    /// <summary>
    /// Forwards damage to the assigned target receiver using SetProgramVariable.
    /// </summary>
    public void _ForwardDamage(float rawDmg, VRCPlayerApi attacker, string aName, int aTeam, Collider col)
    {
        if (targetDamageReceiver == null)
        {
            targetDamageReceiver = (UdonBehaviour)GetComponentInParent(typeof(UdonBehaviour));
            if (targetDamageReceiver == null)
            {
                if (enableLogging) Debug.LogWarning($"{logPrefix} No targetDamageReceiver found to forward damage!");
                return;
            }
        }

        bool isHead = (hitboxType == HitboxType.Head);
        bool isChest = (hitboxType == HitboxType.Chest);

        // Forward to target using <UdonBehaviour>().SetProgramVariable
        targetDamageReceiver.SetProgramVariable("incomingDamage", rawDmg);
        targetDamageReceiver.SetProgramVariable("damage", rawDmg);
        targetDamageReceiver.SetProgramVariable("damageAmount", rawDmg);
        targetDamageReceiver.SetProgramVariable("damageMultiplier", damageMultiplier);
        targetDamageReceiver.SetProgramVariable("hitCollider", col != null ? col : _attachedCollider);
        targetDamageReceiver.SetProgramVariable("isHeadshot", isHead);
        targetDamageReceiver.SetProgramVariable("isChest", isChest);
        targetDamageReceiver.SetProgramVariable("hitPart", hitboxName);
        targetDamageReceiver.SetProgramVariable("attackerPlayer", attacker);
        targetDamageReceiver.SetProgramVariable("attackerName", aName);
        targetDamageReceiver.SetProgramVariable("attackerTeamId", aTeam);

        targetDamageReceiver.SendCustomEvent("_TakeDamage");
        targetDamageReceiver.SendCustomEvent("TakeDamage");

        if (enableLogging)
        {
            Debug.Log($"{logPrefix} Hit on '{hitboxName}' ({damageMultiplier:F1}x) forwarding {rawDmg:F1} dmg to '{targetDamageReceiver.gameObject.name}'.");
        }
    }
}
