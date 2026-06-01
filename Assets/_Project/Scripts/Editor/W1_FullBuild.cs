using UnityEditor;
using UnityEngine;

namespace Celestia.EditorTools
{
    /// <summary>
    /// Orchestrator: runs the full W1 scaffold in order. Use this when starting clean.
    /// Individual steps under Celestia/W1/ are exposed for re-running just one piece.
    /// </summary>
    public static class W1_FullBuild
    {
        [MenuItem("Celestia/W1/Build Everything (steps 1-4)", priority = 0)]
        public static void Build()
        {
            try
            {
                EditorUtility.DisplayProgressBar("W1 Build", "1/4 Assets (SOs + materials)", 0.05f);
                W1_AssetBuilder.BuildAssets();

                EditorUtility.DisplayProgressBar("W1 Build", "2/4 AnimatorController", 0.30f);
                W1_AnimatorBuilder.Build();

                EditorUtility.DisplayProgressBar("W1 Build", "3/4 Player Prefab", 0.55f);
                W1_PlayerPrefabBuilder.Build();

                EditorUtility.DisplayProgressBar("W1 Build", "4/4 Scenes", 0.85f);
                W1_SceneBuilder.Build();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[W1_FullBuild] Complete. Open Bootstrap.unity → press Play.");
        }
    }
}
