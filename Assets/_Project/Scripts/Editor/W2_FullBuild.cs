using UnityEditor;
using UnityEngine;

namespace Celestia.EditorTools
{
    /// <summary>
    /// One-shot orchestrator for W2 combat scaffolding. Runs steps 0-6 in order.
    /// Re-run any time after editing one of the W2 scripts to refresh the assets.
    ///
    /// Prereq: W1 must already be built (Bootstrap.unity, Test_PlayerMovement.unity,
    /// AC_PhantomArcher.controller, PhantomArcher_Player.prefab, Class_PhantomArcher.asset).
    /// </summary>
    public static class W2_FullBuild
    {
        [MenuItem("Celestia/W2/Build Everything (steps 0-6)", priority = 0)]
        public static void Build()
        {
            try
            {
                EditorUtility.DisplayProgressBar("W2 Build", "0/6 Layers + Physics matrix", 0.04f);
                W2_LayerSetup.Configure();

                EditorUtility.DisplayProgressBar("W2 Build", "1/6 Combat assets (SOs + prefabs)", 0.18f);
                W2_AssetBuilder.Build();

                EditorUtility.DisplayProgressBar("W2 Build", "2/6 AnimatorController patch (Attack state on UpperBody layer)", 0.34f);
                W2_AnimatorPatcher.Patch();

                EditorUtility.DisplayProgressBar("W2 Build", "3/6 Weapon roster (4 weapons + BasicSlash ability)", 0.50f);
                W2_WeaponBuilder.Build();

                EditorUtility.DisplayProgressBar("W2 Build", "4/6 Player prefab (combat + WeaponEquipment)", 0.66f);
                W2_PlayerPrefabPatcher.Patch();

                EditorUtility.DisplayProgressBar("W2 Build", "5/6 Scene patches (dummies + spawner)", 0.80f);
                W2_SceneBuilder.Patch();

                EditorUtility.DisplayProgressBar("W2 Build", "6/6 Monster1 (SO + animator + prefab + scene)", 0.93f);
                W2_MonsterBuilder.Build();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[W2_FullBuild] Complete. Open Bootstrap.unity → Play. " +
                      "LMB attacks with the equipped weapon. Keys 1-4 / scroll wheel switch weapons. " +
                      "Walk toward +Z to engage Monster1.");
        }
    }
}
