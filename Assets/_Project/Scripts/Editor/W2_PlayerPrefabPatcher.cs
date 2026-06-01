using System.Collections.Generic;
using System.Linq;
using Celestia.Combat;
using Celestia.Data;
using Celestia.Player;
using UnityEditor;
using UnityEngine;

namespace Celestia.EditorTools
{
    /// <summary>
    /// Adds runtime combat components to PhantomArcher_Player.prefab:
    ///   - AbilityCaster (slot management)
    ///   - PlayerCombat (input → ability → animation + projectile/melee dispatch)
    ///   - ProjectilePool (Arrow pool — used only when a ranged weapon is equipped)
    ///   - WeaponEquipment (weapon roster + per-equip prefab spawning + animator clip swap)
    ///
    /// Idempotent — safe to re-run. Strips any LEGACY hard-attached weapon under either
    /// hand bone (left over from the old bow-only patcher) since WeaponEquipment now
    /// owns weapon prop spawning at runtime.
    /// </summary>
    public static class W2_PlayerPrefabPatcher
    {
        private static readonly string[] LEGACY_WEAPON_NAME_PREFIXES =
        {
            "Bow_", "Axe1H_", "Mace1H_", "Spear1H_", "Shield_",
            // Catches anything the live-preview tool may have left behind if the user accidentally
            // saved the player prefab while a WeaponPreview_DO_NOT_SAVE_* child was attached.
            WeaponPreviewTool.PREVIEW_PREFIX,
        };

        private static readonly string[] WEAPON_SO_PATHS =
        {
            "Assets/_Project/ScriptableObjects/Weapons/Weapon_Bow.asset",
            "Assets/_Project/ScriptableObjects/Weapons/Weapon_Axe.asset",
            "Assets/_Project/ScriptableObjects/Weapons/Weapon_Mace.asset",
            "Assets/_Project/ScriptableObjects/Weapons/Weapon_Spear.asset",
            "Assets/_Project/ScriptableObjects/Weapons/Weapon_SwordShield.asset",
        };

        [MenuItem("Celestia/W2/3 - Patch Player Prefab (combat components)")]
        public static void Patch()
        {
            var prefabPath  = W1_PlayerPrefabBuilder.PREFAB_PATH;
            var classSO     = AssetDatabase.LoadAssetAtPath<ClassDefinitionSO>(W1_AssetBuilder.CLASS_PATH);
            var arrowPrefab = AssetDatabase.LoadAssetAtPath<Projectile>(W2_AssetBuilder.PREFAB_ARROW);
            if (classSO == null || arrowPrefab == null)
            {
                Debug.LogError("[W2_PlayerPrefabPatcher] Run Celestia/W2/1 first (creates the arrow + class wiring).");
                return;
            }

            using var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath);
            var root = scope.prefabContentsRoot;

            // Layer
            SetLayerRecursively(root, W2_LayerSetup.LAYER_PLAYER);

            // AbilityCaster (root)
            var caster = root.GetComponent<AbilityCaster>() ?? root.AddComponent<AbilityCaster>();
            SetSerialized(caster, "classDefinition", classSO);

            // ProjectilePool (root) — used only by ranged weapons (currently the bow)
            var pool = root.GetComponent<ProjectilePool>() ?? root.AddComponent<ProjectilePool>();
            SetSerialized(pool, "prefab", arrowPrefab);

            // PlayerCombat (root)
            var combat      = root.GetComponent<PlayerCombat>() ?? root.AddComponent<PlayerCombat>();
            var inputReader = root.GetComponent<PlayerInputReader>();
            var animator    = root.GetComponentInChildren<Animator>();
            SetSerialized(combat, "input", inputReader);
            SetSerialized(combat, "animator", animator);
            SetSerialized(combat, "arrowPool", pool);

            // Mask: player attacks collide with Enemy + Environment (matches W2_LayerSetup matrix).
            int mask = (1 << W2_LayerSetup.LAYER_ENEMY) | (1 << W2_LayerSetup.LAYER_ENVIRONMENT);
            SetSerializedMask(combat, "attackHitMask", mask);

            // Strip any leftover hand-attached weapon props from previous patcher runs.
            // WeaponEquipment now owns weapon spawning at runtime.
            if (animator != null && animator.isHuman && animator.avatar != null)
            {
                CleanupLegacyHandProps(animator.GetBoneTransform(HumanBodyBones.LeftHand));
                CleanupLegacyHandProps(animator.GetBoneTransform(HumanBodyBones.RightHand));
            }

            // WeaponEquipment (root) — populate roster + animator placeholder clip.
            var equipment = root.GetComponent<WeaponEquipment>() ?? root.AddComponent<WeaponEquipment>();
            SetSerialized(equipment, "animator", animator);
            SetSerialized(equipment, "caster",   caster);

            var w2Weapons = LoadWeapons();
            if (w2Weapons.Count == 0)
                Debug.LogError("[W2_PlayerPrefabPatcher] No WeaponDefinitionSO assets found. Run Celestia/W2/5 first.");

            // Preserve any extras already on the roster (e.g. W3 tool weapons appended by
            // W3_PlayerPrefabPatcher). Without this merge a re-run of W2 nukes the W3 slots
            // and the user has to run W3/3 again. W2 weapons go FIRST so digit hotkeys 1-5
            // still map to the canonical W2 roster.
            var existing = ReadCurrentRoster(equipment);
            var merged   = new List<WeaponDefinitionSO>(w2Weapons);
            foreach (var w in existing)
                if (w != null && !merged.Contains(w)) merged.Add(w);

            SetSerializedObjectList(equipment, "weapons", merged);

            var placeholderClip = W2_AnimatorPatcher.LoadFirstClip(W2_AnimatorPatcher.CLIP_ATTACK_PLACEHOLDER);
            if (placeholderClip == null)
                Debug.LogError("[W2_PlayerPrefabPatcher] Attack placeholder clip not found. Run Celestia/W2/2 first.");
            SetSerialized(equipment, "attackPlaceholderClip", placeholderClip);

            // Over-head health bar (proper HUD lands in W7).
            var playerHealth = root.GetComponent<PlayerHealth>();
            if (playerHealth != null)
                HealthBarBuilder.Attach(root, playerHealth, yOffset: 2.1f,
                                        fillColor: new Color(0.25f, 0.85f, 0.35f));

            Debug.Log($"[W2_PlayerPrefabPatcher] Combat + equipment refreshed on PhantomArcher_Player.prefab. " +
                      $"Roster: {merged.Count} weapon(s) — {string.Join(", ", merged.Select(w => w != null ? w.name : "<null>"))}.");
        }

        private static List<WeaponDefinitionSO> ReadCurrentRoster(WeaponEquipment equipment)
        {
            var list = new List<WeaponDefinitionSO>();
            var so = new SerializedObject(equipment);
            var prop = so.FindProperty("weapons");
            if (prop == null || !prop.isArray) return list;
            for (int i = 0; i < prop.arraySize; i++)
            {
                var elem = prop.GetArrayElementAtIndex(i).objectReferenceValue as WeaponDefinitionSO;
                if (elem != null) list.Add(elem);
            }
            return list;
        }

        private static List<WeaponDefinitionSO> LoadWeapons()
        {
            var list = new List<WeaponDefinitionSO>();
            foreach (var path in WEAPON_SO_PATHS)
            {
                var so = AssetDatabase.LoadAssetAtPath<WeaponDefinitionSO>(path);
                if (so != null) list.Add(so);
                else Debug.LogWarning($"[W2_PlayerPrefabPatcher] Weapon SO missing at {path} — skipping.");
            }
            return list;
        }

        private static void CleanupLegacyHandProps(Transform hand)
        {
            if (hand == null) return;
            for (int i = hand.childCount - 1; i >= 0; i--)
            {
                var child = hand.GetChild(i);
                if (child == null) continue;
                foreach (var prefix in LEGACY_WEAPON_NAME_PREFIXES)
                {
                    if (child.name.StartsWith(prefix))
                    {
                        Object.DestroyImmediate(child.gameObject);
                        break;
                    }
                }
            }
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }

        private static void SetSerialized(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"Field '{fieldName}' not found on {target}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedMask(Object target, string fieldName, int mask)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"Field '{fieldName}' not found on {target}"); return; }
            prop.intValue = mask;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedObjectList<T>(Object target, string fieldName, IList<T> values) where T : Object
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null || !prop.isArray)
            {
                Debug.LogWarning($"List field '{fieldName}' not found (or not an array) on {target}");
                return;
            }
            prop.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
