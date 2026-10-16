#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace RaidWorld.Editor
{
    /// <summary>
    /// Utility to configure and restore RaidWorld combat layers (22-26) and their Physics Collision Matrix.
    /// Can be re-run at any time from Tools > RaidWorld > Apply Combat Layers & Collision Matrix
    /// whenever VRChat's "Setup Layers for VRChat" resets custom layers.
    /// </summary>
    public static class SetupCombatLayers
    {
        public const int LAYER_HITBOX_PLAYER = 22;
        public const int LAYER_HITBOX_AI = 23;
        public const int LAYER_DAMAGE = 24;
        public const int LAYER_DAMAGE_AI = 25;
        public const int LAYER_DAMAGE_ZONE = 26;

        [MenuItem("Tools/RaidWorld/Apply Combat Layers & Collision Matrix")]
        public static void ApplyLayersAndCollision()
        {
            // 1. Configure TagManager layer names
            UnityEngine.Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets != null && tagManagerAssets.Length > 0)
            {
                SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
                SerializedProperty layersProp = tagManager.FindProperty("layers");

                if (layersProp != null)
                {
                    SetLayerName(layersProp, LAYER_HITBOX_PLAYER, "HitboxPlayer");
                    SetLayerName(layersProp, LAYER_HITBOX_AI, "HitboxAI");
                    SetLayerName(layersProp, LAYER_DAMAGE, "Damage");
                    SetLayerName(layersProp, LAYER_DAMAGE_AI, "DamageAI");
                    SetLayerName(layersProp, LAYER_DAMAGE_ZONE, "DamageZone");

                    tagManager.ApplyModifiedProperties();
                }
            }

            // 2. Clear all collisions for layers 22..26 against all layers
            for (int l = 22; l <= 26; l++)
            {
                for (int other = 0; other < 32; other++)
                {
                    Physics.IgnoreLayerCollision(l, other, true);
                }
            }

            // 3. Set up specific combat collisions:
            // HitboxPlayer (22) collides with DamageAI (25) and DamageZone (26)
            Physics.IgnoreLayerCollision(LAYER_HITBOX_PLAYER, LAYER_DAMAGE_AI, false);
            Physics.IgnoreLayerCollision(LAYER_HITBOX_PLAYER, LAYER_DAMAGE_ZONE, false);

            // HitboxAI (23) collides with Damage (24) and DamageZone (26)
            Physics.IgnoreLayerCollision(LAYER_HITBOX_AI, LAYER_DAMAGE, false);
            Physics.IgnoreLayerCollision(LAYER_HITBOX_AI, LAYER_DAMAGE_ZONE, false);

            // Damage (24 - Player attacks) collides with HitboxAI (23), Environment (11), Default (0)
            Physics.IgnoreLayerCollision(LAYER_DAMAGE, LAYER_HITBOX_AI, false);
            Physics.IgnoreLayerCollision(LAYER_DAMAGE, 11, false);
            Physics.IgnoreLayerCollision(LAYER_DAMAGE, 0, false);

            // DamageAI (25 - Enemy attacks) collides with HitboxPlayer (22), Environment (11), Default (0)
            Physics.IgnoreLayerCollision(LAYER_DAMAGE_AI, LAYER_HITBOX_PLAYER, false);
            Physics.IgnoreLayerCollision(LAYER_DAMAGE_AI, 11, false);
            Physics.IgnoreLayerCollision(LAYER_DAMAGE_AI, 0, false);

            // DamageZone (26 - Hazards) collides with HitboxPlayer (22), HitboxAI (23)
            Physics.IgnoreLayerCollision(LAYER_DAMAGE_ZONE, LAYER_HITBOX_PLAYER, false);
            Physics.IgnoreLayerCollision(LAYER_DAMAGE_ZONE, LAYER_HITBOX_AI, false);

            // Save settings directly in Unity
            AssetDatabase.SaveAssets();

            Debug.Log("[RaidWorld] Combat Layers (22: HitboxPlayer, 23: HitboxAI, 24: Damage, 25: DamageAI, 26: DamageZone) and Physics Collision Matrix applied and saved successfully!");
            EditorUtility.DisplayDialog("RaidWorld Layers", "Combat Layers (22-26) and Physics Collision Matrix have been applied and saved!", "OK");
        }

        private static void SetLayerName(SerializedProperty layersProp, int index, string name)
        {
            if (index < layersProp.arraySize)
            {
                SerializedProperty elem = layersProp.GetArrayElementAtIndex(index);
                elem.stringValue = name;
            }
        }
    }
}
#endif
