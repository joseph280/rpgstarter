using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RPGStarter.EditorTools
{
    /// <summary>
    /// Single editor menu that builds the entire starter kit in one click.
    /// Replaces what used to be 20+ separate W1/W2/W3/Demo menu entries.
    ///
    /// What it does (in order):
    ///   1. Generates Unity-primitive source assets that stand in for the
    ///      licensed asset-store packs the starter kit was forked from
    ///      (rocks, trees, weapons, table).
    ///   2. Runs the W1/W2/W3 build chain — physics layers, ScriptableObjects,
    ///      AnimatorController, weapon roster, monster, player prefab + all its
    ///      patches (combat + inventory + hotbar).
    ///   3. Syncs Addressables so the test scene loads from Bootstrap.
    ///
    /// After this finishes, open Bootstrap.unity and Play.
    ///
    /// Each per-step builder still exists as a plain static class under
    /// Assets/_Project/Scripts/Editor/ — their <c>[MenuItem]</c> attributes are
    /// stripped, so this is the only editor entry point.
    /// </summary>
    public static class Build
    {
        [MenuItem("RPGStarter/Build Demo", priority = 0)]
        public static void BuildDemo()
        {
            Debug.Log("[Build] BEGIN — building the demo from scratch.");
            try
            {
                int n = 0, total = 16;

                Step(++n, total, "Primitive source assets (rock / tree / weapons / table)");
                Demo_PrimitiveAssets.Build();

                Step(++n, total, "Physics layers (Player / Enemy / Environment / projectiles)");
                W2_LayerSetup.Configure();

                Step(++n, total, "W1 assets (class SO, event channels, scene refs)");
                W1_AssetBuilder.BuildAssets();

                Step(++n, total, "Animator base controller + Mixamo clip configure");
                W1_AnimatorBuilder.Build();

                Step(++n, total, "W2 assets (damage types, abilities, target dummy)");
                W2_AssetBuilder.Build();

                Step(++n, total, "Animator patches (Attack / Hit / Die states)");
                W2_AnimatorPatcher.Patch();

                Step(++n, total, "Weapon roster (bow / sword+shield / axe / mace / spear)");
                W2_WeaponBuilder.Build();

                Step(++n, total, "Monster prefab + EnemyDefinitionSO");
                W2_MonsterBuilder.Build();

                Step(++n, total, "Player prefab — base components (CC + movement + health)");
                W1_PlayerPrefabBuilder.Build();

                Step(++n, total, "Player prefab — combat patch (caster, projectile pool, equipment)");
                W2_PlayerPrefabPatcher.Patch();

                Step(++n, total, "W3 assets (items, recipes, rock / tree / table, VFX) — pass A");
                W3_AssetBuilder.Build();

                Step(++n, total, "Tool weapons (hammer / pickaxe / axe-tool)");
                W3_WeaponBuilder.Build();

                // W3 assets needs a second pass so the freshly-built tool weapons
                // pick up their inventory item entries + InventorySO roster slots.
                Step(++n, total, "W3 assets — pass B (weapon items + inventory roster)");
                W3_AssetBuilder.Build();

                Step(++n, total, "Player prefab — inventory + hotbar + interact patch");
                W3_PlayerPrefabPatcher.Patch();

                Step(++n, total, "Convert any Standard-shader materials to URP/Lit");
                W2_MaterialConverter.ConvertThirdPartyMaterials();

                Step(++n, total, "Addressables sync (register test scene + Bootstrap)");
                TrySyncAddressablesWithRepairFallback();

                Step(n, total, "Scene cleanup (strip stale RefPillar_* GameObjects)");
                StripLegacyRefPillars();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Build] END. Open Bootstrap.unity → Play. " +
                      "Phantom Archer spawns with the full RPG stack — movement, combat, " +
                      "inventory (I), hotbar (1-0), tree chopping, rock mining, crafting.");
        }

        private static void Step(int n, int total, string what)
        {
            EditorUtility.DisplayProgressBar("Build Demo", $"{n}/{total}  {what}", n / (float)total);
            Debug.Log($"[Build] {n}/{total}  {what}");
        }

        /// <summary>
        /// Addressables internals get corrupt periodically (missing IGroupTemplate,
        /// null PlayMode build script). When Sync throws a NullReferenceException
        /// from inside the Addressables editor code, we fall back to the nuclear
        /// Repair: wipe AddressableAssetsData/, recreate defaults, retry the sync.
        /// Idempotent — if Sync works on the first try this is a no-op.
        /// </summary>
        private static void TrySyncAddressablesWithRepairFallback()
        {
            try
            {
                W1_AddressablesSync.Sync();
            }
            catch (System.NullReferenceException nre)
            {
                Debug.LogWarning($"[Build] Addressables Sync threw NRE ({nre.Message}). " +
                                 "Falling back to Repair (nuke AddressableAssetsData/ and rebuild).");
                W1_AddressablesSync.Repair();
            }
        }

        /// <summary>
        /// Test_PlayerMovement.unity shipped with 4 "RefPillar_*" cube pillars
        /// (placed by the old W1_SceneBuilder so movement was visible against
        /// the empty floor). They're baked into the scene file, so removing the
        /// build-time placement code didn't get rid of them. This step opens
        /// the scene, destroys any GameObject whose name starts with
        /// "RefPillar_", and saves. Idempotent — no-op once the scene's clean.
        /// </summary>
        private static void StripLegacyRefPillars()
        {
            const string SCENE = "Assets/_Project/Tests/PlayMode/Test_PlayerMovement.unity";
            var scene = EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Single);
            if (!scene.IsValid()) return;

            int removed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith("RefPillar_"))
                {
                    Object.DestroyImmediate(root);
                    removed++;
                }
            }
            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[Build] Stripped {removed} legacy RefPillar GameObject(s) from {SCENE}.");
            }
        }

        // ── Manual escape hatch ──────────────────────────────────────────────

        /// <summary>
        /// Manual fallback when Build Demo's automatic Addressables repair isn't
        /// enough. Wipes Assets/AddressableAssetsData/ and lets the Addressables
        /// package recreate its defaults, then re-registers the scene entries.
        /// </summary>
        [MenuItem("RPGStarter/Repair Addressables", priority = 50)]
        public static void RepairAddressables()
        {
            W1_AddressablesSync.Repair();
        }
    }
}
