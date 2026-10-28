# VR Physical Combat System (Boneworks & Destiny 2 Style)

Modular, high-performance, VR-physical gun and combat architecture for VRChat worlds built with **UdonSharp** for **SDK 3.10.5**.

---

## 🎯 Architecture Overview

```
Assets/UdonSharp/Blacky_Scripts/Combat/
├── VRPhysicalGun.cs            # Master firearm controller (handling, fire modes, damage, reloading)
├── GunMagazine.cs              # Physical magazine (pickup, socket snapping, physics ejection, ammo sync)
├── SlidePhysBoneTracker.cs     # Slide/bolt cycling tracker (Boneworks-style chambering & unspent live ejection)
├── CombatHitbox.cs             # Modular hitbox (Head, Chest, Weakpoints, dynamic resizing/offsets)
├── CombatProjectile.cs         # Ballistic swept-ray projectile (velocity, gravity drop, drag, impact sparks)
├── CombatProjectilePool.cs     # Zero-allocation object pool for projectiles
├── TargetDummy.cs              # Network-synced reactive dummy (reactive wobble, headshot ping, knockdown, auto-respawn)
├── MagazineDispenser.cs        # Ammo station / mag pouch (dispenses pre-pooled magazines)
└── Editor/
    └── CombatSceneBuilder.cs   # Automation tool: Tools -> RaidWorld -> Build Combat Test Scene
```

---

## 🔫 Core Features

### 1. Boneworks-Style Physical Reloading
- **Manual Mag Insertion**: Bring a compatible `GunMagazine` near the weapon's magwell trigger zone (`magazineSnapZone`) to snap it into place.
- **Physical Mag Drop/Ejection**: Trigger the mag release (Desktop `G`/`E` or VR grip action) to physically eject the magazine with downwards/outwards velocity.
- **Round in Chamber Logic**: The weapon can fire 1 round if chambered, even with **no magazine inserted**.
- **Slide Lock on Empty**: When firing the final round, the slide locks back to its open position.

### 2. Physical Slide Racking
- Slide/charging-handle movement tracked via `SlidePhysBoneTracker.cs` (supports VRChat PhysBones, direct grab, or Desktop `R` key).
- **Extracting Unspent Rounds**: Pulling the slide back while a live round is chambered extracts the unspent round through the ejection port.
- **Chambering Fresh Rounds**: Releasing the slide forward feeds the top round from an inserted magazine into the chamber.

### 3. Destiny 2-Inspired Weapon Handling & Fire Modes
- **Fire Modes**: Semi-Auto, Full-Auto, Burst Fire, Safe mode.
- **Firing Types**:
  - `Hitscan`: Instant raycast with spread and range limits.
  - `Projectile`: Physical ballistic projectile with gravity arc and swept collision.
  - `Hybrid`: Instant hitscan damage combined with visual tracer projectile.
- **Shotgun Spread**: Configurable `pelletsPerShot` for multi-pellet spread weapons.
- **Two-Handed Stabilization**: Optional secondary foregrip pickup reduces recoil and tightens bullet spread by 50%.

### 4. Networked Hitboxes, Teams & Target Filtering
- **Headshot Multipliers**: 2.0x (or custom) for Head hitboxes, 1.0x for Chest, 0.75x for Limbs, 3.0x for Weakpoints.
- **Avatar & NPC Resizing**: `CombatHitbox._SetHitboxDimensions(offset, dimensions)` resizes hitboxes at runtime to match any avatar or NPC model.
- **Team IDs & Friendly Fire**: Filter damage by `teamId`. If `allowFriendlyFire` is false, friendly fire deals zero damage.
- **Target Mode Toggle**: Quickly filter whether guns hit Players only, AI only, or both.

### 5. Weapon Durability & Damage Taking
- Weapons have `weaponHealth` / `maxWeaponHealth` and can take damage from other weapons, melee, or abilities.
- If destroyed, weapons can smoke, jam, or drop from the player's hands.

### 6. Target Dummies with Auto-Respawn
- Reactive Boneworks-style wobble reaction on hit.
- Distinct audio feedback: critical "ping" on headshot, thud on body hit.
- Network-synced knockdown upon reaching 0 HP.
- Synced auto-respawn timer (default 4 seconds): automatically stands back up with full health and shields.

---

## 📡 Cross-Script Damage Interface (`SetProgramVariable`)

All damage events use `<UdonBehaviour>().SetProgramVariable` to guarantee compatibility across scripts:

```csharp
UdonBehaviour target = (UdonBehaviour)hitCollider.GetComponent(typeof(UdonBehaviour));
if (target != null)
{
    target.SetProgramVariable("incomingDamage", 35.0f);
    target.SetProgramVariable("damage", 35.0f);
    target.SetProgramVariable("damageAmount", 35.0f);
    target.SetProgramVariable("damageMultiplier", 2.0f);
    target.SetProgramVariable("hitCollider", hitCollider);
    target.SetProgramVariable("isHeadshot", true);
    target.SetProgramVariable("isChest", false);
    target.SetProgramVariable("hitPart", "Head");
    target.SetProgramVariable("attackerPlayer", Networking.LocalPlayer);
    target.SetProgramVariable("attackerName", Networking.LocalPlayer.displayName);
    target.SetProgramVariable("attackerTeamId", 1);

    target.SendCustomEvent("_TakeDamage");
    target.SendCustomEvent("TakeDamage");
}
```

---

## 🎮 Desktop Controls (When Holding Weapon)
- **Left Mouse Click**: Fire trigger (`OnPickupUseDown`)
- **R**: Rack slide / chamber fresh round
- **G** or **E**: Eject magazine
- **F**: Cycle fire mode (Semi-Auto → Full-Auto → Burst → Safe)
- **T**: Toggle target filter (Players & AI → Players Only → AI Only)

---

## 🧪 Test Scene: `Assets/Scenes/CombatTestScene.unity`
- Pre-built shooting range with floor, walls, lighting, spawn point, and counter bench.
- Weapons included:
  - **Tactical Pistol 9mm** (Semi-Auto Hitscan, slide action, 15rd mags)
  - **Assault Carbine 5.56mm** (Full-Auto Ballistic Projectile, two-handed foregrip, 30rd mags)
- 4 Synced Target Dummies placed at 6m, 14m, 18m (Armored/Explosive), and 26m.
- Infinite 9mm and 5.56mm ammo dispensers.
