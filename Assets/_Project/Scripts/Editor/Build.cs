using UnityEditor;
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
                W1_AddressablesSync.Sync();
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
    }
}
