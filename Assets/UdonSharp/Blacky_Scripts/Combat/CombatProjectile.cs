using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// CombatProjectile:
/// Physical ballistic bullet projectile with high-speed swept raycast detection,
/// gravity drop, impact spark spawning, and SetProgramVariable damage dispatch.
/// Designed for zero-allocation pooling.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CombatProjectile : UdonSharpBehaviour
{
    [Header("--- Ballistics ---")]
    [Tooltip("Initial bullet speed in meters per second.")]
    public float speed = 150f;

    [Tooltip("Gravity acceleration multiplier applied to projectile trajectory.")]
    public float gravityMultiplier = 1.0f;

    [Tooltip("Maximum lifetime in seconds before auto-despawning.")]
    public float maxLifetime = 4f;

    [Tooltip("Collision layer mask for environment, hitboxes, players, and AI.")]
    public LayerMask collisionLayers = -1;

    [Header("--- Impact Visuals ---")]
    [Tooltip("Optional particle system or spark effect GameObject spawned at impact point.")]
    public GameObject impactSparkPrefab;

    [Tooltip("Optional bullet trail renderer.")]
    public TrailRenderer trailRenderer;

    [Header("--- Debug & Logging ---")]
    public bool enableLogging = false;
    public string logPrefix = "[CombatProjectile]";

    // Runtime state
    private Vector3 _velocity;
    private float _damage = 25f;
    private int _teamId = 0;
    private bool _allowFriendlyFire = false;
    private bool _targetPlayers = true;
    private bool _targetAI = true;
    private VRCPlayerApi _shooterPlayer;
    private string _shooterName = "";
    private float _spawnTime;
    private bool _isActive = false;

    // SetProgramVariable interface for shooting projectiles out of mid-air
    [HideInInspector, System.NonSerialized] public float incomingDamage;
    [HideInInspector, System.NonSerialized] public float damage;

    void Start()
    {
        if (trailRenderer == null)
        {
            trailRenderer = GetComponent<TrailRenderer>();
        }
    }

    /// <summary>
    /// Launches projectile from a muzzle point with full ballistic configuration.
    /// </summary>
    public void _Launch(
        Vector3 position,
        Vector3 direction,
        float projectileSpeed,
        float projectileDamage,
        int team,
        bool friendlyFire,
        bool hitPlayers,
        bool hitAI,
        VRCPlayerApi shooter,
        string sName)
    {
        transform.position = position;
        transform.rotation = Quaternion.LookRotation(direction);

        _velocity = direction.normalized * (projectileSpeed > 0f ? projectileSpeed : speed);
        _damage = projectileDamage;
        _teamId = team;
        _allowFriendlyFire = friendlyFire;
        _targetPlayers = hitPlayers;
        _targetAI = hitAI;
        _shooterPlayer = shooter;
        _shooterName = sName;
        _spawnTime = Time.time;
        _isActive = true;

        if (trailRenderer != null)
        {
            trailRenderer.Clear();
            trailRenderer.enabled = true;
        }

        gameObject.SetActive(true);
    }

    void Update()
    {
        if (!_isActive) return;

        // Check if shot down mid-air
        if (incomingDamage > 0f || damage > 0f)
        {
            _TakeDamage();
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Auto-despawn on timeout
        if (Time.time - _spawnTime >= maxLifetime)
        {
            _Deactivate();
            return;
        }

        // Apply gravity to velocity
        _velocity += Physics.gravity * (gravityMultiplier * dt);

        Vector3 currentPos = transform.position;
        Vector3 step = _velocity * dt;
        float distance = step.magnitude;
        Vector3 dir = step / distance;

        // Swept raycast to prevent tunneling through thin colliders at high speed
        RaycastHit hit;
        if (Physics.Raycast(currentPos, dir, out hit, distance, collisionLayers, QueryTriggerInteraction.Collide))
        {
            transform.position = hit.point;
            _OnHit(hit);
            return;
        }

        transform.position = currentPos + step;
        transform.rotation = Quaternion.LookRotation(_velocity);
    }

    private void _OnHit(RaycastHit hit)
    {
        Collider col = hit.collider;
        if (col != null)
        {
            // 1. Check if hit a CombatHitbox
            CombatHitbox combatHitbox = col.GetComponent<CombatHitbox>();
            if (combatHitbox != null)
            {
                combatHitbox._ForwardDamage(_damage, _shooterPlayer, _shooterName, _teamId, col);
            }
            else
            {
                // 2. Check if hit has an UdonBehaviour (DamageSystem, TargetDummy, VRPhysicalGun, etc.)
                UdonBehaviour ub = (UdonBehaviour)col.GetComponent(typeof(UdonBehaviour));
                if (ub == null) ub = (UdonBehaviour)col.GetComponentInParent(typeof(UdonBehaviour));

                if (ub != null)
                {
                    // Inspect head vs chest hitboxes based on naming or collider tags
                    string colName = col.gameObject.name.ToLower();
                    bool isHead = colName.Contains("head");
                    bool isChest = colName.Contains("chest");
                    string partName = isHead ? "Head" : (isChest ? "Chest" : "Body");

                    // Set variables using <UdonBehaviour>().SetProgramVariable
                    ub.SetProgramVariable("incomingDamage", _damage);
                    ub.SetProgramVariable("damage", _damage);
                    ub.SetProgramVariable("damageAmount", _damage);
                    ub.SetProgramVariable("hitCollider", col);
                    ub.SetProgramVariable("isHeadshot", isHead);
                    ub.SetProgramVariable("isChest", isChest);
                    ub.SetProgramVariable("hitPart", partName);
                    ub.SetProgramVariable("attackerPlayer", _shooterPlayer);
                    ub.SetProgramVariable("attackerName", _shooterName);
                    ub.SetProgramVariable("attackerTeamId", _teamId);

                    ub.SendCustomEvent("_TakeDamage");
                    ub.SendCustomEvent("TakeDamage");
                }
            }
        }

        _SpawnImpact(hit.point, hit.normal);
        _Deactivate();
    }

    private void _SpawnImpact(Vector3 point, Vector3 normal)
    {
        if (impactSparkPrefab != null)
        {
            impactSparkPrefab.transform.position = point;
            impactSparkPrefab.transform.rotation = Quaternion.LookRotation(normal);
            impactSparkPrefab.SetActive(false);
            impactSparkPrefab.SetActive(true);
        }
    }

    public void _TakeDamage()
    {
        incomingDamage = 0f;
        damage = 0f;
        _SpawnImpact(transform.position, Vector3.up);
        _Deactivate();
    }

    public void TakeDamage()
    {
        _TakeDamage();
    }

    private void _Deactivate()
    {
        _isActive = false;
        if (trailRenderer != null)
        {
            trailRenderer.enabled = false;
        }
        gameObject.SetActive(false);
    }
}
