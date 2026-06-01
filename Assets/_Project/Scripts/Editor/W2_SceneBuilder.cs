using RPGStarter.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RPGStarter.EditorTools
{
    /// <summary>
    /// Adds W2 combat content to the existing scenes:
    ///  - Test_PlayerMovement.unity → 3 TargetDummy instances around the floor
    ///  - Bootstrap.unity → DamageNumberSpawner under GameManager
    ///
    /// Re-runnable. Removes a previous "DummyParent" / "DamageNumberSpawner" before
    /// adding so we don't end up with duplicates.
    /// </summary>
    public static class W2_SceneBuilder
    {
        // [MenuItem stripped — single entry point is RPGStarter/Build Demo]
        public static void Patch()
        {
            PatchTestScene();
            PatchBootstrap();
            Debug.Log("[W2_SceneBuilder] Scenes patched. Open Bootstrap.unity → Play.");
        }

        private static void PatchTestScene()
        {
            var dummyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(W2_AssetBuilder.PREFAB_DUMMY);
            if (dummyPrefab == null)
            {
                Debug.LogError("[W2_SceneBuilder] TargetDummy prefab missing. Run RPGStarter/W2/1 first.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(W1_SceneBuilder.TEST_PATH, OpenSceneMode.Single);

            // Remove a previous DummyParent if present
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "DummyParent") Object.DestroyImmediate(root);
            }

            var parent = new GameObject("DummyParent");
            Vector3[] positions =
            {
                new(  6f, 0f,  3f),
                new(  0f, 0f,  8f),
                new( -6f, 0f,  3f),
            };
            foreach (var pos in positions)
            {
                var d = (GameObject)PrefabUtility.InstantiatePrefab(dummyPrefab, parent.transform);
                d.transform.position = pos + Vector3.up; // capsule pivot is centre, lift to floor
            }

            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[W2_SceneBuilder] {positions.Length} dummies added to {W1_SceneBuilder.TEST_PATH}.");
        }

        private static void PatchBootstrap()
        {
            var spawnerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(W2_AssetBuilder.PREFAB_DAMAGE_NUMBER);
            if (spawnerPrefab == null)
            {
                Debug.LogError("[W2_SceneBuilder] DamageNumber prefab missing. Run RPGStarter/W2/1 first.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(W1_SceneBuilder.BOOTSTRAP_PATH, OpenSceneMode.Single);

            GameObject gm = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "GameManager") { gm = root; break; }
            }
            if (gm == null)
            {
                Debug.LogError("[W2_SceneBuilder] GameManager root not found in Bootstrap.unity. Run RPGStarter/W1/4 first.");
                return;
            }

            // Remove previous DamageNumberSpawner if present
            var existing = gm.transform.Find("DamageNumberSpawner");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var spawnerGo = new GameObject("DamageNumberSpawner");
            spawnerGo.transform.SetParent(gm.transform, false);
            var spawner = spawnerGo.AddComponent<DamageNumberSpawner>();
            SetSerialized(spawner, "prefab", spawnerPrefab.GetComponent<DamageNumber>());

            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[W2_SceneBuilder] DamageNumberSpawner added to {W1_SceneBuilder.BOOTSTRAP_PATH}.");
        }

        private static void SetSerialized(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"Field '{fieldName}' not found on {target}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
