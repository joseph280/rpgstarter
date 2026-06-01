using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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

                Step(n, total, "Scatter harvest nodes (rocks + trees)");
                ScatterHarvestNodes();

                // Land the editor on Bootstrap — the playable entry point that holds the
                // GameManager + inventory/crafting UI (the harvest/strip steps above leave the
                // TEST scene open, which has no HUD). Also pin it as the Play-mode start scene
                // so pressing Play boots Bootstrap no matter which scene is open afterward —
                // kills the recurring "played the wrong scene, no inventory" confusion.
                Step(n, total, "Open Bootstrap + set it as the Play-mode start scene");
                OpenBootstrapAsStartScene();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Build] END. Bootstrap.unity is open and set as the Play start scene — " +
                      "just press Play. Phantom Archer spawns with the full RPG stack — movement, " +
                      "combat, inventory (I) with item icons, hotbar (1-0), tree chopping, rock " +
                      "mining, crafting.");
        }

        /// <summary>
        /// Opens Bootstrap.unity and pins it as <see cref="EditorSceneManager.playModeStartScene"/>
        /// so Play always boots the entry scene (with the inventory/crafting HUD), regardless of
        /// which scene the user has open in the editor.
        /// </summary>
        private static void OpenBootstrapAsStartScene()
        {
            string boot = W1_SceneBuilder.BOOTSTRAP_PATH;
            if (!File.Exists(boot))
            {
                Debug.LogWarning($"[Build] {boot} not found — can't open/pin it. Run W1 scene build first.");
                return;
            }
            EditorSceneManager.OpenScene(boot, OpenSceneMode.Single);
            var bootAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(boot);
            if (bootAsset != null)
            {
                EditorSceneManager.playModeStartScene = bootAsset;
                Debug.Log("[Build] Bootstrap.unity opened + set as Play-mode start scene.");
            }
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
        /// Test_PlayerMovement.unity shipped with content that doesn't belong in
        /// the demo any more: 4 "RefPillar_*" reference cubes (placed by the old
        /// W1_SceneBuilder so movement was visible against the empty floor) and
        /// 3 TargetDummy capsule training enemies + their DummyParent
        /// (placed by W2_SceneBuilder for combat-practice). The Monster1 makes
        /// the dummies redundant.
        ///
        /// This step opens the scene and destroys any root GameObject that's
        /// in the strip list — by name prefix for the pillars, by exact name
        /// for the dummy group. Idempotent — no-op once the scene's clean.
        /// </summary>
        private static void StripLegacyRefPillars()
        {
            const string SCENE = "Assets/_Project/Tests/PlayMode/Test_PlayerMovement.unity";
            var scene = EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Single);
            if (!scene.IsValid()) return;

            int removed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                bool kill = root.name.StartsWith("RefPillar_")
                         || root.name == "DummyParent"
                         || root.name == "TargetDummy";
                if (kill)
                {
                    Object.DestroyImmediate(root);
                    removed++;
                }
            }
            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[Build] Scene cleanup: stripped {removed} legacy GameObject(s) " +
                          $"(RefPillar_* / DummyParent / TargetDummy) from {SCENE}.");
            }
        }

        /// <summary>
        /// Rebuilds the harvest-node scatter in Test_PlayerMovement. Previously the
        /// rocks ended up clustered on the west side of the floor (a few metres of
        /// the player) and there were zero trees in the scene at all. This wipes
        /// any existing MineableRocks / ChoppableTrees roots and places fresh
        /// instances at random world positions in a ring around the origin, with a
        /// minimum spacing so they don't pile on top of each other.
        ///
        /// Idempotent — re-running gives a fresh scatter every time.
        /// </summary>
        private static void ScatterHarvestNodes()
        {
            const string SCENE = "Assets/_Project/Tests/PlayMode/Test_PlayerMovement.unity";
            const string ROCK_PREFAB = "Assets/_Project/Prefabs/Environment/Rock_Mineable.prefab";
            const string TREE_PREFAB = "Assets/_Project/Prefabs/Environment/Tree_Choppable.prefab";

            var scene = EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Single);
            if (!scene.IsValid()) return;

            var rockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ROCK_PREFAB);
            var treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TREE_PREFAB);

            // Tear down old scatter roots — handles re-running, and also catches the
            // pre-existing badly-clustered rocks that this builder previously left
            // baked into the scene file.
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "MineableRocks" || root.name == "ChoppableTrees")
                    Object.DestroyImmediate(root);
            }

            int rockCount = 0, treeCount = 0;
            if (rockPrefab != null) rockCount = Scatter(scene, rockPrefab, "MineableRocks",
                                                       count: 7, minRadius: 5f,  maxRadius: 16f, spacing: 3f);
            else Debug.LogWarning($"[Build] {ROCK_PREFAB} missing — skipping rock scatter.");

            if (treePrefab != null) treeCount = Scatter(scene, treePrefab, "ChoppableTrees",
                                                       count: 8, minRadius: 7f, maxRadius: 18f, spacing: 3.5f);
            else Debug.LogWarning($"[Build] {TREE_PREFAB} missing — skipping tree scatter.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Build] Scattered {rockCount} rocks + {treeCount} trees on the test floor.");
        }

        private static int Scatter(Scene scene, GameObject prefab, string parentName,
                                   int count, float minRadius, float maxRadius, float spacing)
        {
            var parent = new GameObject(parentName);
            SceneManager.MoveGameObjectToScene(parent, scene);

            var placed = new List<Vector2>(count);
            int safety = count * 40;
            int spawned = 0;
            while (spawned < count && safety-- > 0)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float r     = UnityEngine.Random.Range(minRadius, maxRadius);
                float x = Mathf.Cos(angle) * r;
                float z = Mathf.Sin(angle) * r;

                // Reject candidate if too close to any already-placed sibling.
                bool tooClose = false;
                float sqSpacing = spacing * spacing;
                for (int i = 0; i < placed.Count; i++)
                {
                    float dx = placed[i].x - x;
                    float dz = placed[i].y - z;
                    if (dx * dx + dz * dz < sqSpacing) { tooClose = true; break; }
                }
                if (tooClose) continue;

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                inst.transform.position = new Vector3(x, 0f, z);
                inst.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                placed.Add(new Vector2(x, z));
                spawned++;
            }
            return spawned;
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
