using UdonSharp;
using UnityEngine;

/// <summary>
/// CombatProjectilePool:
/// Manages pre-allocated CombatProjectile instances to eliminate runtime garbage collection
/// and instantiation in VRChat worlds.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CombatProjectilePool : UdonSharpBehaviour
{
    [Header("--- Pool Configuration ---")]
    [Tooltip("Pre-spawned projectile objects assigned in scene/prefab.")]
    public CombatProjectile[] projectiles;

    private int _currentIndex = 0;

    void Start()
    {
        if (projectiles == null) projectiles = new CombatProjectile[0];

        // Ensure all projectiles start inactive
        for (int i = 0; i < projectiles.Length; i++)
        {
            if (projectiles[i] != null)
            {
                projectiles[i].gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Returns the next available CombatProjectile instance from the pool.
    /// </summary>
    public CombatProjectile _GetProjectile()
    {
        if (projectiles == null || projectiles.Length == 0) return null;

        int searchCount = 0;
        while (searchCount < projectiles.Length)
        {
            CombatProjectile p = projectiles[_currentIndex];
            _currentIndex = (_currentIndex + 1) % projectiles.Length;

            if (p != null)
            {
                return p;
            }
            searchCount++;
        }

        return null;
    }
}
