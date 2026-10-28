#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System;
using System.IO;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// CombatSceneBuilder:
/// Editor automation script to construct the complete Combat Test Scene for RaidWorld.
/// Automatically creates and configures:
/// - Environment & VRCWorld setup (lighting, floor, spawn, shooting counter)
/// - Boneworks & Destiny 2 inspired VR Physical Guns (9mm Pistol Hitscan, 5.56mm Carbine Projectile)
/// - Ballistic Projectile Pool
/// - Physical Magazines & Infinite Ammo Dispensers
/// - Network-synced Reactive Target Dummies with Head/Chest hitboxes and auto-respawn
/// </summary>
[InitializeOnLoad]
public static class CombatSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/CombatTestScene.unity";

    [MenuItem("Tools/RaidWorld/Setup Player DamageSystem Prefab", false, 20)]
    public static void SetupPlayerDamageSystemPrefab()
    {
        string prefabPath = "Assets/Prefabs/Systems/DamageSystem - Player.prefab";
        if (!File.Exists(prefabPath))
        {
            Debug.LogWarning($"[CombatSceneBuilder] Player prefab not found at '{prefabPath}'");
            return;
        }

        _EnsureAllProgramAssets();

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            Debug.LogError($"[CombatSceneBuilder] Failed to load prefab contents for '{prefabPath}'");
            return;
        }

        try
        {
            int playerHitboxLayer = LayerMask.NameToLayer("HitboxPlayer");
            if (playerHitboxLayer < 0) playerHitboxLayer = 22;

            UdonBehaviour rootUb = (UdonBehaviour)root.GetComponent(typeof(UdonBehaviour));

            // 1. DamageSystem on root
            DamageSystem dmgSys = root.GetComponent<DamageSystem>();
            if (dmgSys == null)
            {
                dmgSys = root.AddUdonSharpComponent<DamageSystem>();
            }

            // 2. Head Hitbox child
            Transform headTrans = root.transform.Find("HeadHitbox");
            if (headTrans == null) headTrans = root.transform.Find("Head");
            if (headTrans == null)
            {
                GameObject newHead = new GameObject("HeadHitbox");
                newHead.transform.SetParent(root.transform, false);
                headTrans = newHead.transform;
            }
            headTrans.gameObject.layer = playerHitboxLayer;

            SphereCollider headCol = headTrans.GetComponent<SphereCollider>();
            if (headCol == null)
            {
                headCol = headTrans.gameObject.AddComponent<SphereCollider>();
                headCol.radius = 0.15f;
            }

            CombatHitbox headCombatHitbox = headTrans.GetComponent<CombatHitbox>();
            if (headCombatHitbox == null)
            {
                headCombatHitbox = headTrans.gameObject.AddUdonSharpComponent<CombatHitbox>();
            }
            headCombatHitbox.hitboxType = HitboxType.Head;
            headCombatHitbox.damageMultiplier = 2.0f;
            headCombatHitbox.hitboxName = "Head";
            headCombatHitbox.targetDamageReceiver = rootUb;

            // 3. Chest Hitbox child
            Transform chestTrans = root.transform.Find("ChestHitbox");
            if (chestTrans == null) chestTrans = root.transform.Find("Chest");
            if (chestTrans == null)
            {
                GameObject newChest = new GameObject("ChestHitbox");
                newChest.transform.SetParent(root.transform, false);
                chestTrans = newChest.transform;
            }
            chestTrans.gameObject.layer = playerHitboxLayer;

            BoxCollider chestCol = chestTrans.GetComponent<BoxCollider>();
            if (chestCol == null)
            {
                chestCol = chestTrans.gameObject.AddComponent<BoxCollider>();
                chestCol.size = new Vector3(0.35f, 0.45f, 0.25f);
            }

            CombatHitbox chestCombatHitbox = chestTrans.GetComponent<CombatHitbox>();
            if (chestCombatHitbox == null)
            {
                chestCombatHitbox = chestTrans.gameObject.AddUdonSharpComponent<CombatHitbox>();
            }
            chestCombatHitbox.hitboxType = HitboxType.Chest;
            chestCombatHitbox.damageMultiplier = 1.0f;
            chestCombatHitbox.hitboxName = "Chest";
            chestCombatHitbox.targetDamageReceiver = rootUb;

            // 4. Wire up references in DamageSystem
            dmgSys.isPlayer = true;
            dmgSys.trackPlayerBones = true;
            dmgSys.headHitbox = headTrans;
            dmgSys.headCollider = headCol;
            dmgSys.chestHitbox = chestTrans;
            dmgSys.chestCollider = chestCol;
            dmgSys.headshotMultiplier = 2.0f;
            dmgSys.chestMultiplier = 1.0f;
            dmgSys.syncHealth = true;
            dmgSys.respawnOnDeath = true;

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[CombatSceneBuilder] *** Successfully configured '{prefabPath}' with CombatHitbox components! ***");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CombatSceneBuilder] Error updating player prefab: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Tools/RaidWorld/Build Combat Test Scene", false, 10)]
    public static void BuildCombatTestScene()
    {
        Debug.Log("[CombatSceneBuilder] Starting Combat Test Scene build...");

        // 1. Ensure all UdonSharpProgramAssets exist for our combat scripts
        _EnsureAllProgramAssets();

        // 2. Create new scene or open existing
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 3. Create Lighting
        _CreateLighting();

        // 4. Create Floor & Environment
        _CreateEnvironment();

        // 5. Create VRChat World Descriptor & Spawn Point
        GameObject spawnPoint = _CreateVRCWorld();

        // 6. Create Shooting Bench / Counter Table
        GameObject counterTable = _CreateShootingCounter();

        // 7. Create Projectile Pool
        CombatProjectilePool projPool = _CreateProjectilePool();

        // 8. Create Magazines & Dispensers
        _CreateMagazinesAndDispensers(counterTable.transform);

        // 9. Create VR Physical Guns (Pistol & Carbine)
        _CreateWeapons(counterTable.transform, projPool);

        // 10. Create Target Dummies at various ranges
        _CreateTargetDummies();

        // 11. Save Scene
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[CombatSceneBuilder] *** Combat Test Scene successfully built and saved to '{ScenePath}'! ***");
    }

    private static void _EnsureAllProgramAssets()
    {
        _EnsureProgramAsset(typeof(CombatHitbox), "Assets/UdonSharp/Blacky_Scripts/Combat/CombatHitbox.cs");
        _EnsureProgramAsset(typeof(GunMagazine), "Assets/UdonSharp/Blacky_Scripts/Combat/GunMagazine.cs");
        _EnsureProgramAsset(typeof(CombatProjectile), "Assets/UdonSharp/Blacky_Scripts/Combat/CombatProjectile.cs");
        _EnsureProgramAsset(typeof(CombatProjectilePool), "Assets/UdonSharp/Blacky_Scripts/Combat/CombatProjectilePool.cs");
        _EnsureProgramAsset(typeof(SlidePhysBoneTracker), "Assets/UdonSharp/Blacky_Scripts/Combat/SlidePhysBoneTracker.cs");
        _EnsureProgramAsset(typeof(VRPhysicalGun), "Assets/UdonSharp/Blacky_Scripts/Combat/VRPhysicalGun.cs");
        _EnsureProgramAsset(typeof(TargetDummy), "Assets/UdonSharp/Blacky_Scripts/Combat/TargetDummy.cs");
        _EnsureProgramAsset(typeof(MagazineDispenser), "Assets/UdonSharp/Blacky_Scripts/Combat/MagazineDispenser.cs");
        _EnsureProgramAsset(typeof(DamageSystem), "Assets/UdonSharp/Blacky_Scripts/Player/DamageSystem.cs");

        AssetDatabase.Refresh();
    }

    private static UdonSharpProgramAsset _EnsureProgramAsset(Type scriptClass, string csPath)
    {
        UdonSharpProgramAsset asset = UdonSharpEditorUtility.GetUdonSharpProgramAsset(scriptClass);
        if (asset != null) return asset;

        string assetPath = Path.ChangeExtension(csPath, ".asset").Replace('\\', '/');
        asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath);
        if (asset != null) return asset;

        MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(csPath);
        if (monoScript == null)
        {
            Debug.LogError($"[CombatSceneBuilder] Could not find MonoScript at '{csPath}'!");
            return null;
        }

        asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        asset.sourceCsScript = monoScript;
        AssetDatabase.CreateAsset(asset, assetPath);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

        Debug.Log($"[CombatSceneBuilder] Created UdonSharpProgramAsset: {assetPath}");
        return asset;
    }

    private static void _CreateLighting()
    {
        GameObject lightGo = new GameObject("Directional Light");
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.96f, 0.9f);
        light.intensity = 1.15f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        RenderSettings.ambientLight = new Color(0.25f, 0.25f, 0.28f);
    }

    private static void _CreateEnvironment()
    {
        // Environment Root
        GameObject env = new GameObject("Environment");

        // Ground Floor (60m x 60m)
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(env.transform);
        floor.transform.position = new Vector3(0f, -0.25f, 15f);
        floor.transform.localScale = new Vector3(40f, 0.5f, 50f);

        // Firing range back wall (stops stray bullets)
        GameObject backWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backWall.name = "Backdrop Wall";
        backWall.transform.SetParent(env.transform);
        backWall.transform.position = new Vector3(0f, 4f, 40f);
        backWall.transform.localScale = new Vector3(40f, 8f, 1f);

        // Side barriers
        GameObject leftWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftWall.name = "Left Wall";
        leftWall.transform.SetParent(env.transform);
        leftWall.transform.position = new Vector3(-20f, 3f, 15f);
        leftWall.transform.localScale = new Vector3(1f, 6f, 50f);

        GameObject rightWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightWall.name = "Right Wall";
        rightWall.transform.SetParent(env.transform);
        rightWall.transform.position = new Vector3(20f, 3f, 15f);
        rightWall.transform.localScale = new Vector3(1f, 6f, 50f);

        // Material assignment if available
        Material floorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/BlackyTestingScene/GridFloor_udon.mat");
        if (floorMat != null)
        {
            floor.GetComponent<Renderer>().material = floorMat;
            backWall.GetComponent<Renderer>().material = floorMat;
        }
    }

    private static GameObject _CreateVRCWorld()
    {
        GameObject vrcWorldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.vrchat.worlds/Samples/UdonExampleScene/Prefabs/VRCWorld.prefab");
        if (vrcWorldPrefab != null)
        {
            GameObject vrcWorld = (GameObject)PrefabUtility.InstantiatePrefab(vrcWorldPrefab);
            vrcWorld.name = "VRCWorld";
            vrcWorld.transform.position = new Vector3(0f, 0f, 0f);
            return vrcWorld;
        }

        GameObject fallbackWorld = new GameObject("VRCWorld");
        fallbackWorld.transform.position = Vector3.zero;
        return fallbackWorld;
    }

    private static GameObject _CreateShootingCounter()
    {
        GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "Shooting Bench";
        table.transform.position = new Vector3(0f, 0.45f, 1.4f);
        table.transform.localScale = new Vector3(3.6f, 0.9f, 0.8f);

        Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/BlackyTestingScene/SampleAssets_matte.mat");
        if (mat != null)
        {
            table.GetComponent<Renderer>().material = mat;
        }

        return table;
    }

    private static CombatProjectilePool _CreateProjectilePool()
    {
        GameObject poolGo = new GameObject("CombatProjectilePool");
        poolGo.transform.position = new Vector3(0f, -10f, 0f);
        CombatProjectilePool pool = poolGo.AddUdonSharpComponent<CombatProjectilePool>();

        int poolSize = 24;
        CombatProjectile[] projs = new CombatProjectile[poolSize];

        for (int i = 0; i < poolSize; i++)
        {
            GameObject pGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pGo.name = $"Bullet_Projectile_{i:D2}";
            pGo.transform.SetParent(poolGo.transform);
            pGo.transform.localScale = new Vector3(0.04f, 0.04f, 0.15f);

            // Trigger or remove standard collider since raycast is used
            Collider col = pGo.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            TrailRenderer trail = pGo.AddComponent<TrailRenderer>();
            trail.time = 0.12f;
            trail.startWidth = 0.03f;
            trail.endWidth = 0.005f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.startColor = new Color(1f, 0.85f, 0.4f, 1f);
            trail.endColor = new Color(1f, 0.3f, 0.1f, 0f);

            CombatProjectile proj = pGo.AddUdonSharpComponent<CombatProjectile>();
            proj.speed = 180f;
            proj.gravityMultiplier = 0.8f;
            proj.maxLifetime = 3.5f;
            proj.trailRenderer = trail;

            pGo.SetActive(false);
            projs[i] = proj;
        }

        pool.projectiles = projs;
        return pool;
    }

    private static void _CreateMagazinesAndDispensers(Transform tableTransform)
    {
        // 9mm Ammo Dispenser
        GameObject disp9mmGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        disp9mmGo.name = "Ammo Dispenser (9mm)";
        disp9mmGo.transform.position = tableTransform.position + new Vector3(-1.3f, 0.55f, 0f);
        disp9mmGo.transform.localScale = new Vector3(0.35f, 0.2f, 0.35f);

        GameObject pt9 = new GameObject("DispensePoint");
        pt9.transform.SetParent(disp9mmGo.transform);
        pt9.transform.localPosition = new Vector3(0f, 0.2f, 0f);

        MagazineDispenser disp9mm = disp9mmGo.AddUdonSharpComponent<MagazineDispenser>();
        disp9mm.magazineType = "9mm";
        disp9mm.dispensePoint = pt9.transform;

        // Pre-create pooled 9mm magazines
        int magCount = 4;
        GunMagazine[] pool9mm = new GunMagazine[magCount];
        for (int i = 0; i < magCount; i++)
        {
            Vector3 spawnPos = tableTransform.position + new Vector3(-0.9f + (i * 0.12f), 0.55f, -0.1f);
            pool9mm[i] = _CreateMagazine("9mm", 15, spawnPos);
        }
        disp9mm.magazinePool = pool9mm;

        // 5.56mm Ammo Dispenser
        GameObject disp556Go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        disp556Go.name = "Ammo Dispenser (5.56mm)";
        disp556Go.transform.position = tableTransform.position + new Vector3(1.3f, 0.55f, 0f);
        disp556Go.transform.localScale = new Vector3(0.35f, 0.2f, 0.35f);

        GameObject pt556 = new GameObject("DispensePoint");
        pt556.transform.SetParent(disp556Go.transform);
        pt556.transform.localPosition = new Vector3(0f, 0.2f, 0f);

        MagazineDispenser disp556 = disp556Go.AddUdonSharpComponent<MagazineDispenser>();
        disp556.magazineType = "5.56mm";
        disp556.dispensePoint = pt556.transform;

        GunMagazine[] pool556 = new GunMagazine[magCount];
        for (int i = 0; i < magCount; i++)
        {
            Vector3 spawnPos = tableTransform.position + new Vector3(0.5f + (i * 0.12f), 0.55f, -0.1f);
            pool556[i] = _CreateMagazine("5.56mm", 30, spawnPos);
        }
        disp556.magazinePool = pool556;
    }

    private static GunMagazine _CreateMagazine(string type, int capacity, Vector3 position)
    {
        GameObject magGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        magGo.name = $"Mag_{type}_{capacity}rd";
        magGo.transform.position = position;

        // Dimensions for a realistic magazine
        if (type == "9mm")
        {
            magGo.transform.localScale = new Vector3(0.035f, 0.13f, 0.045f);
        }
        else
        {
            magGo.transform.localScale = new Vector3(0.035f, 0.19f, 0.075f);
        }

        Rigidbody rb = magGo.AddComponent<Rigidbody>();
        rb.mass = 0.35f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        VRCPickup pickup = magGo.AddComponent<VRCPickup>();
        pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;

        GunMagazine mag = magGo.AddUdonSharpComponent<GunMagazine>();
        mag.magazineType = type;
        mag.maxAmmo = capacity;
        mag.currentAmmo = capacity;
        mag.despawnDelay = 90f;

        return mag;
    }

    private static void _CreateWeapons(Transform tableTransform, CombatProjectilePool projPool)
    {
        // ---------------------------------------------------------------------
        // Weapon 1: Tactical Pistol (9mm Hitscan)
        // ---------------------------------------------------------------------
        GameObject pistolGo = new GameObject("Tactical Pistol (9mm Hitscan)");
        pistolGo.transform.position = tableTransform.position + new Vector3(-0.4f, 0.55f, 0.15f);
        pistolGo.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        // Physical Collider & Rigidbody
        BoxCollider pistolCol = pistolGo.AddComponent<BoxCollider>();
        pistolCol.size = new Vector3(0.05f, 0.16f, 0.22f);
        pistolCol.center = new Vector3(0f, 0.02f, 0.03f);

        Rigidbody pistolRb = pistolGo.AddComponent<Rigidbody>();
        pistolRb.mass = 0.9f;
        pistolRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        VRCPickup pistolPickup = pistolGo.AddComponent<VRCPickup>();
        pistolPickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;

        // Visual Models
        GameObject pGrip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pGrip.name = "Pistol_Grip";
        pGrip.transform.SetParent(pistolGo.transform, false);
        pGrip.transform.localPosition = new Vector3(0f, -0.04f, -0.03f);
        pGrip.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
        pGrip.transform.localScale = new Vector3(0.038f, 0.12f, 0.05f);
        GameObject.DestroyImmediate(pGrip.GetComponent<Collider>());

        GameObject pBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pBody.name = "Pistol_Frame";
        pBody.transform.SetParent(pistolGo.transform, false);
        pBody.transform.localPosition = new Vector3(0f, 0.04f, 0.04f);
        pBody.transform.localScale = new Vector3(0.04f, 0.04f, 0.18f);
        GameObject.DestroyImmediate(pBody.GetComponent<Collider>());

        // Slide
        GameObject pSlide = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pSlide.name = "Pistol_Slide";
        pSlide.transform.SetParent(pistolGo.transform, false);
        pSlide.transform.localPosition = new Vector3(0f, 0.075f, 0.04f);
        pSlide.transform.localScale = new Vector3(0.042f, 0.035f, 0.19f);
        GameObject.DestroyImmediate(pSlide.GetComponent<Collider>());

        SlidePhysBoneTracker slideTracker = pSlide.AddUdonSharpComponent<SlidePhysBoneTracker>();
        slideTracker.slideTransform = pSlide.transform;
        slideTracker.restLocalPosition = pSlide.transform.localPosition;
        slideTracker.rackedLocalPosition = pSlide.transform.localPosition + new Vector3(0f, 0f, -0.045f);

        // Muzzle & Ejection
        GameObject pMuzzle = new GameObject("MuzzlePoint");
        pMuzzle.transform.SetParent(pistolGo.transform, false);
        pMuzzle.transform.localPosition = new Vector3(0f, 0.075f, 0.14f);

        GameObject pEject = new GameObject("EjectionPort");
        pEject.transform.SetParent(pistolGo.transform, false);
        pEject.transform.localPosition = new Vector3(0.025f, 0.075f, 0.02f);

        // Magazine Socket & Snap Zone
        GameObject pSocket = new GameObject("MagazineSocket");
        pSocket.transform.SetParent(pistolGo.transform, false);
        pSocket.transform.localPosition = new Vector3(0f, -0.05f, -0.03f);
        pSocket.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);

        GameObject pSnap = new GameObject("MagazineSnapZone");
        pSnap.transform.SetParent(pistolGo.transform, false);
        pSnap.transform.localPosition = new Vector3(0f, -0.08f, -0.03f);
        BoxCollider pSnapCol = pSnap.AddComponent<BoxCollider>();
        pSnapCol.isTrigger = true;
        pSnapCol.size = new Vector3(0.08f, 0.1f, 0.08f);

        // Audio
        AudioSource pAudio = pistolGo.AddComponent<AudioSource>();
        pAudio.playOnAwake = false;
        pAudio.spatialBlend = 1f;

        // VRPhysicalGun Component
        VRPhysicalGun pistolGun = pistolGo.AddUdonSharpComponent<VRPhysicalGun>();
        pistolGun.weaponName = "Tactical Pistol 9mm";
        pistolGun.acceptedMagazineType = "9mm";
        pistolGun.fireMode = GunFireMode.SemiAuto;
        pistolGun.fireType = GunFireType.Hitscan;
        pistolGun.baseDamage = 35f;
        pistolGun.headshotMultiplier = 2.5f;
        pistolGun.roundsPerMinute = 480f;
        pistolGun.isRoundChambered = true;
        pistolGun.muzzlePoint = pMuzzle.transform;
        pistolGun.ejectionPort = pEject.transform;
        pistolGun.magazineSocket = pSocket.transform;
        pistolGun.magazineSnapZone = pSnapCol;
        pistolGun.slideTransform = pSlide.transform;
        pistolGun.slideRestLocalPos = pSlide.transform.localPosition;
        pistolGun.slideLockedLocalPos = pSlide.transform.localPosition + new Vector3(0f, 0f, -0.045f);
        pistolGun.weaponAudio = pAudio;
        pistolGun.projectilePool = projPool;

        slideTracker.gun = pistolGun;

        // ---------------------------------------------------------------------
        // Weapon 2: Assault Carbine (5.56mm Projectile / Auto)
        // ---------------------------------------------------------------------
        GameObject carbineGo = new GameObject("Assault Carbine (5.56mm Ballistic)");
        carbineGo.transform.position = tableTransform.position + new Vector3(0.4f, 0.55f, 0.15f);
        carbineGo.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        BoxCollider carbineCol = carbineGo.AddComponent<BoxCollider>();
        carbineCol.size = new Vector3(0.06f, 0.22f, 0.65f);
        carbineCol.center = new Vector3(0f, 0.04f, 0.08f);

        Rigidbody carbineRb = carbineGo.AddComponent<Rigidbody>();
        carbineRb.mass = 2.8f;
        carbineRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        VRCPickup carbinePickup = carbineGo.AddComponent<VRCPickup>();
        carbinePickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;

        // Visuals
        GameObject cReceiver = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cReceiver.name = "Carbine_Receiver";
        cReceiver.transform.SetParent(carbineGo.transform, false);
        cReceiver.transform.localPosition = new Vector3(0f, 0.06f, 0.05f);
        cReceiver.transform.localScale = new Vector3(0.048f, 0.075f, 0.26f);
        GameObject.DestroyImmediate(cReceiver.GetComponent<Collider>());

        GameObject cBarrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cBarrel.name = "Carbine_Barrel";
        cBarrel.transform.SetParent(carbineGo.transform, false);
        cBarrel.transform.localPosition = new Vector3(0f, 0.075f, 0.32f);
        cBarrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        cBarrel.transform.localScale = new Vector3(0.024f, 0.15f, 0.024f);
        GameObject.DestroyImmediate(cBarrel.GetComponent<Collider>());

        GameObject cGrip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cGrip.name = "Carbine_Grip";
        cGrip.transform.SetParent(carbineGo.transform, false);
        cGrip.transform.localPosition = new Vector3(0f, -0.06f, -0.06f);
        cGrip.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
        cGrip.transform.localScale = new Vector3(0.038f, 0.14f, 0.048f);
        GameObject.DestroyImmediate(cGrip.GetComponent<Collider>());

        GameObject cStock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cStock.name = "Carbine_Stock";
        cStock.transform.SetParent(carbineGo.transform, false);
        cStock.transform.localPosition = new Vector3(0f, 0.04f, -0.22f);
        cStock.transform.localScale = new Vector3(0.042f, 0.11f, 0.22f);
        GameObject.DestroyImmediate(cStock.GetComponent<Collider>());

        // Foregrip Secondary Pickup
        GameObject cForegrip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cForegrip.name = "Carbine_Foregrip";
        cForegrip.transform.SetParent(carbineGo.transform, false);
        cForegrip.transform.localPosition = new Vector3(0f, 0.02f, 0.26f);
        cForegrip.transform.localScale = new Vector3(0.04f, 0.08f, 0.045f);
        VRCPickup cSecPickup = cForegrip.AddComponent<VRCPickup>();
        cSecPickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
        cSecPickup.proximity = 0.15f;

        // Slide / Charging Handle
        GameObject cSlide = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cSlide.name = "Charging_Handle";
        cSlide.transform.SetParent(carbineGo.transform, false);
        cSlide.transform.localPosition = new Vector3(0f, 0.11f, -0.08f);
        cSlide.transform.localScale = new Vector3(0.06f, 0.025f, 0.04f);
        GameObject.DestroyImmediate(cSlide.GetComponent<Collider>());

        SlidePhysBoneTracker cSlideTracker = cSlide.AddUdonSharpComponent<SlidePhysBoneTracker>();
        cSlideTracker.slideTransform = cSlide.transform;
        cSlideTracker.restLocalPosition = cSlide.transform.localPosition;
        cSlideTracker.rackedLocalPosition = cSlide.transform.localPosition + new Vector3(0f, 0f, -0.065f);

        // Muzzle & Ejection
        GameObject cMuzzle = new GameObject("MuzzlePoint");
        cMuzzle.transform.SetParent(carbineGo.transform, false);
        cMuzzle.transform.localPosition = new Vector3(0f, 0.075f, 0.48f);

        GameObject cEject = new GameObject("EjectionPort");
        cEject.transform.SetParent(carbineGo.transform, false);
        cEject.transform.localPosition = new Vector3(0.03f, 0.08f, 0.03f);

        // Magazine Socket
        GameObject cSocket = new GameObject("MagazineSocket");
        cSocket.transform.SetParent(carbineGo.transform, false);
        cSocket.transform.localPosition = new Vector3(0f, -0.04f, 0.06f);

        GameObject cSnap = new GameObject("MagazineSnapZone");
        cSnap.transform.SetParent(carbineGo.transform, false);
        cSnap.transform.localPosition = new Vector3(0f, -0.09f, 0.06f);
        BoxCollider cSnapCol = cSnap.AddComponent<BoxCollider>();
        cSnapCol.isTrigger = true;
        cSnapCol.size = new Vector3(0.09f, 0.12f, 0.1f);

        AudioSource cAudio = carbineGo.AddComponent<AudioSource>();
        cAudio.playOnAwake = false;
        cAudio.spatialBlend = 1f;

        VRPhysicalGun carbineGun = carbineGo.AddUdonSharpComponent<VRPhysicalGun>();
        carbineGun.weaponName = "Assault Carbine 5.56mm";
        carbineGun.acceptedMagazineType = "5.56mm";
        carbineGun.fireMode = GunFireMode.FullAuto;
        carbineGun.fireType = GunFireType.Projectile;
        carbineGun.baseDamage = 45f;
        carbineGun.headshotMultiplier = 2.0f;
        carbineGun.roundsPerMinute = 620f;
        carbineGun.isRoundChambered = true;
        carbineGun.muzzlePoint = cMuzzle.transform;
        carbineGun.ejectionPort = cEject.transform;
        carbineGun.magazineSocket = cSocket.transform;
        carbineGun.magazineSnapZone = cSnapCol;
        carbineGun.slideTransform = cSlide.transform;
        carbineGun.slideRestLocalPos = cSlide.transform.localPosition;
        carbineGun.slideLockedLocalPos = cSlide.transform.localPosition + new Vector3(0f, 0f, -0.065f);
        carbineGun.weaponAudio = cAudio;
        carbineGun.secondaryGrip = cSecPickup;
        carbineGun.projectilePool = projPool;

        cSlideTracker.gun = carbineGun;
    }

    private static void _CreateTargetDummies()
    {
        // 4 Dummies at various ranges
        _CreateDummy("Target Dummy (Close 6m)", new Vector3(0f, 0f, 7.5f), 100f, 0f, false);
        _CreateDummy("Target Dummy (Mid 14m)", new Vector3(-3.5f, 0f, 15.5f), 100f, 50f, false);
        _CreateDummy("Target Dummy (Far 26m)", new Vector3(3.5f, 0f, 27.5f), 120f, 0f, false);
        _CreateDummy("Armored Explosive Dummy (18m)", new Vector3(0f, 0f, 19.5f), 150f, 100f, true);
    }

    private static void _CreateDummy(string name, Vector3 position, float hp, float shield, bool explosive)
    {
        GameObject root = new GameObject(name);
        root.transform.position = position;

        // Base Pedestal
        GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pedestal.name = "Stand_Pedestal";
        pedestal.transform.SetParent(root.transform, false);
        pedestal.transform.localPosition = new Vector3(0f, 0.08f, 0f);
        pedestal.transform.localScale = new Vector3(0.7f, 0.08f, 0.7f);

        // Pivot (tilts backward when shot / falls when dead)
        GameObject pivot = new GameObject("Dummy_Pivot");
        pivot.transform.SetParent(root.transform, false);
        pivot.transform.localPosition = new Vector3(0f, 0.16f, 0f);

        // Torso / Chest Hitbox
        GameObject chestGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chestGo.name = "ChestHitbox";
        chestGo.transform.SetParent(pivot.transform, false);
        chestGo.transform.localPosition = new Vector3(0f, 0.95f, 0f);
        chestGo.transform.localScale = new Vector3(0.45f, 0.65f, 0.28f);

        BoxCollider chestCol = chestGo.GetComponent<BoxCollider>();
        CombatHitbox chestHitbox = chestGo.AddUdonSharpComponent<CombatHitbox>();
        chestHitbox.hitboxType = HitboxType.Chest;
        chestHitbox.damageMultiplier = 1.0f;
        chestHitbox.hitboxName = "Chest";

        // Head Hitbox
        GameObject headGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        headGo.name = "HeadHitbox";
        headGo.transform.SetParent(pivot.transform, false);
        headGo.transform.localPosition = new Vector3(0f, 1.52f, 0f);
        headGo.transform.localScale = new Vector3(0.28f, 0.32f, 0.28f);

        SphereCollider headCol = headGo.GetComponent<SphereCollider>();
        CombatHitbox headHitbox = headGo.AddUdonSharpComponent<CombatHitbox>();
        headHitbox.hitboxType = HitboxType.Head;
        headHitbox.damageMultiplier = 2.0f;
        headHitbox.hitboxName = "Head";

        // Post / Spine pole
        GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "Support_Pole";
        post.transform.SetParent(pivot.transform, false);
        post.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        post.transform.localScale = new Vector3(0.08f, 0.45f, 0.08f);
        GameObject.DestroyImmediate(post.GetComponent<Collider>());

        // Audio
        AudioSource dummyAudio = root.AddComponent<AudioSource>();
        dummyAudio.playOnAwake = false;
        dummyAudio.spatialBlend = 1f;

        // TargetDummy Component
        TargetDummy dummy = root.AddUdonSharpComponent<TargetDummy>();
        dummy.dummyName = name;
        dummy.maxHealth = hp;
        dummy.currentHealth = hp;
        dummy.maxShield = shield;
        dummy.currentShield = shield;
        dummy.headCollider = headCol;
        dummy.chestCollider = chestCol;
        dummy.dummyPivot = pivot.transform;
        dummy.targetRenderer = chestGo.GetComponent<MeshRenderer>();
        dummy.dummyAudio = dummyAudio;
        dummy.respawnDelay = 4.0f;
        dummy.explodeOnDeath = explosive;
        dummy.explosionRadius = 5f;
        dummy.explosionDamage = 60f;

        // Wire target damage receiver on hitboxes to the TargetDummy component
        UdonBehaviour dummyUb = (UdonBehaviour)root.GetComponent(typeof(UdonBehaviour));
        chestHitbox.targetDamageReceiver = dummyUb;
        headHitbox.targetDamageReceiver = dummyUb;
    }
}
#endif
