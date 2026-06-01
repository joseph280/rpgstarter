using UnityEditor;
using UnityEngine;

namespace Celestia.EditorTools
{
    /// <summary>
    /// One-shot orchestrator for W3 mining scaffolding. Mirrors W2_FullBuild.
    /// Prereqs: W2 must already be built (weapons, animator, player prefab, scenes).
    /// </summary>
    public static class W3_FullBuild
    {
        // Bumped whenever any W3 builder's behaviour changes — gives us a single place
        // to confirm the user is running fresh-compiled code (as opposed to a stale DLL
        // Unity hasn't recompiled yet).
        private const string FULL_BUILD_VERSION = "v3 (Wood + chip-burst + scene-ref verification)";

        [MenuItem("Celestia/W3/Build Everything (steps 1-4)", priority = 0)]
        public static void Build()
        {
            Debug.Log($"[W3_FullBuild] BEGIN — {FULL_BUILD_VERSION}");
            try
            {
                EditorUtility.DisplayProgressBar("W3 Build", "1/5 Mining assets — pass A (tool kinds + materials + prefabs)", 0.05f);
                W3_AssetBuilder.Build();

                EditorUtility.DisplayProgressBar("W3 Build", "2/5 Tool weapons (hammer/pickaxe/axe)", 0.25f);
                W3_WeaponBuilder.Build();

                // Re-run AssetBuilder so the freshly-built W3 weapons get their inventory
                // item assets + the InventorySO startingItems list picks them up. Idempotent
                // (LoadOrCreate) — pass A's outputs are unaffected.
                EditorUtility.DisplayProgressBar("W3 Build", "3/5 Mining assets — pass B (weapon items + inventory roster)", 0.50f);
                W3_AssetBuilder.Build();

                EditorUtility.DisplayProgressBar("W3 Build", "4/5 Player prefab (W3 weapons + inventory)", 0.70f);
                W3_PlayerPrefabPatcher.Patch();

                EditorUtility.DisplayProgressBar("W3 Build", "5/5 Scenes (rocks + spawner + HUD)", 0.90f);
                W3_SceneBuilder.Patch();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[W3_FullBuild] END — {FULL_BUILD_VERSION}. " +
                      "Open Bootstrap.unity → Play. " +
                      "Press 7 to equip the Pickaxe, hit a rock 4×, watch the +N popup, press I to view your inventory. " +
                      "Hammer = digit 6, Axe = digit 8.");
        }
    }
}
