using System.Collections.Generic;
using System.Linq;
using Celestia.Data;
using Celestia.Player;
using Celestia.World;
using UnityEditor;
using UnityEngine;

namespace Celestia.EditorTools
{
    /// <summary>
    /// Extends the player prefab with W3 mining + crafting gear:
    ///   * appends Hammer / Pickaxe / Axe-Tool / Crafting-Table weapon SOs to the
    ///     <see cref="WeaponEquipment.weapons"/> roster (legacy scroll-cycle compat);
    ///   * adds an <see cref="Inventory"/> MonoBehaviour bound to <c>Inventory_Player.asset</c>
    ///     so pickups can find the player via <see cref="Inventory.Local"/>;
    ///   * adds a <see cref="Hotbar"/> MB so digit keys 1-9 + 0 equip the linkedWeapon of
    ///     the inventory's hotbar slots;
    ///   * adds a <see cref="PlayerInteract"/> MB so <c>E</c> opens the nearest placed
    ///     crafting table.
    ///
    /// Idempotent — re-running de-dupes the roster (preserving order) and overwrites the
    /// component's serialized refs.
    /// </summary>
    public static class W3_PlayerPrefabPatcher
    {
        // W3 weapons appended to the static roster. Hotbar's equip-by-SO bypass works
        // either way, so this list is purely for the legacy mouse-scroll cycle.
        private static readonly string[] W3_WEAPON_SO_PATHS =
        {
            W3_WeaponBuilder.PATH_HAMMER,
            W3_WeaponBuilder.PATH_PICKAXE,
            W3_WeaponBuilder.PATH_W3_AXE,
            W3_AssetBuilder.WEAPON_CRAFTING_TABLE,
        };

        [MenuItem("Celestia/W3/3 - Patch Player Prefab (W3 weapons + inventory)")]
        public static void Patch()
        {
            var prefabPath = W1_PlayerPrefabBuilder.PREFAB_PATH;
            var inventorySO = AssetDatabase.LoadAssetAtPath<InventorySO>(W3_AssetBuilder.INVENTORY_PLAYER);
            if (inventorySO == null)
            {
                Debug.LogError($"[W3_PlayerPrefabPatcher] Inventory_Player missing at {W3_AssetBuilder.INVENTORY_PLAYER}. Run Celestia/W3/1 first.");
                return;
            }

            using var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath);
            var root = scope.prefabContentsRoot;

            // Inventory binding (player-side handle on the InventorySO).
            var inventoryMb = root.GetComponent<Inventory>() ?? root.AddComponent<Inventory>();
            SetSerialized(inventoryMb, "inventory", inventorySO);

            // PlayerInteract: E key → activate nearest placed crafting table.
            if (root.GetComponent<PlayerInteract>() == null) root.AddComponent<PlayerInteract>();

            // Extend the WeaponEquipment roster.
            var equipment = root.GetComponent<WeaponEquipment>();
            if (equipment == null)
            {
                Debug.LogError("[W3_PlayerPrefabPatcher] WeaponEquipment missing on player prefab. Run Celestia/W2/3 first.");
                return;
            }

            var w3Weapons = LoadW3Weapons();
            if (w3Weapons.Count == 0)
            {
                Debug.LogError("[W3_PlayerPrefabPatcher] No W3 weapon SOs found. Run Celestia/W3/2 first.");
                return;
            }

            var combined = ReadCurrentRoster(equipment);
            foreach (var w in w3Weapons)
            {
                if (w == null) continue;
                if (!combined.Contains(w)) combined.Add(w);
            }
            SetSerializedObjectList(equipment, "weapons", combined);

            // Hotbar MB: digits 1-9 + 0 read inventory slots 40-49 + equip the linkedWeapon.
            var hotbar = root.GetComponent<Hotbar>() ?? root.AddComponent<Hotbar>();
            SetSerialized(hotbar, "inventory", inventorySO);
            SetSerialized(hotbar, "equipment", equipment);
            // The hud field stays null here — the player prefab can't reference a scene
            // GameObject. Hotbar's defaults (HotbarStart=40, HotbarSize=10) match the
            // builder's INVENTORY_HOTBAR_START / INVENTORY_HOTBAR_SIZE.

            Debug.Log($"[W3_PlayerPrefabPatcher] Roster now has {combined.Count} weapon(s) — " +
                      $"{string.Join(", ", combined.Select(w => w != null ? w.name : "<null>"))}. " +
                      $"Inventory + Hotbar + PlayerInteract bound.");
        }

        private static List<WeaponDefinitionSO> LoadW3Weapons()
        {
            var list = new List<WeaponDefinitionSO>();
            foreach (var path in W3_WEAPON_SO_PATHS)
            {
                var so = AssetDatabase.LoadAssetAtPath<WeaponDefinitionSO>(path);
                if (so != null) list.Add(so);
                else Debug.LogWarning($"[W3_PlayerPrefabPatcher] Weapon SO missing at {path} — skipping.");
            }
            return list;
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

        private static void SetSerialized(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"Field '{fieldName}' not found on {target}"); return; }
            prop.objectReferenceValue = value;
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
