using Celestia.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Celestia.EditorTools
{
    /// <summary>
    /// Custom inspector for WeaponDefinitionSO. Adds a live-preview workflow on top of
    /// the default fields so the artist can see the weapon in the player's hand while
    /// they tune the local transform — no Play-mode round-trip required.
    ///
    /// Workflow:
    ///   1. Click "Preview / Refresh on Player Prefab" — opens PhantomArcher_Player.prefab
    ///      in Prefab Mode and spawns the weapon under the configured hand bone.
    ///   2. Either edit the SO fields here (the preview updates automatically) OR grab
    ///      the Move/Rotate/Scale gizmo on the spawned weapon in the Scene view.
    ///   3. If you used the gizmo, click "Save spawned Transform → SO" to write the
    ///      manual transform back into mainLocalPos / mainLocalEuler / mainLocalScale.
    ///   4. Click "Remove Preview" when done — the spawned object is intentionally NOT
    ///      saved as part of the player prefab; you'll see a "Save? Don't Save?" prompt
    ///      on close — choose Don't Save.
    /// </summary>
    [CustomEditor(typeof(WeaponDefinitionSO))]
    public class WeaponDefinitionSOEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool fieldsChanged = EditorGUI.EndChangeCheck();

            var so = (WeaponDefinitionSO)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Live Preview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Spawns the weapon under the player's hand inside Prefab Mode so you can see " +
                "the grip while you tune mainLocal*. Use the Scene view gizmos for fine-tuning, " +
                "then click 'Save spawned Transform → SO'. Click 'Remove Preview' before closing " +
                "the prefab — and choose Don't Save when prompted.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview / Refresh on Player Prefab"))
                    WeaponPreviewTool.SpawnOrRefresh(so);
                if (GUILayout.Button("Save spawned Transform → SO"))
                    WeaponPreviewTool.SaveBackToSO(so);
                if (GUILayout.Button("Remove Preview"))
                    WeaponPreviewTool.RemovePreview(so);
            }

            // Auto-refresh: editing the SO fields should immediately update the spawned preview.
            if (fieldsChanged) WeaponPreviewTool.RefreshIfPresent(so);
        }
    }

    /// <summary>Editor-only helper that spawns a weapon prop under the player's hand bone.</summary>
    public static class WeaponPreviewTool
    {
        public const string PREVIEW_PREFIX = "WeaponPreview_DO_NOT_SAVE_";

        public static void SpawnOrRefresh(WeaponDefinitionSO so)
        {
            if (so == null || so.mainPrefab == null)
            {
                Debug.LogError("[WeaponPreview] SO or its mainPrefab is null.");
                return;
            }

            var hand = ResolveOrOpenHand(so);
            if (hand == null) return;

            // Strip every existing preview (any weapon, in case the user is hopping SOs).
            StripPreviewsUnder(hand);
            // Also strip from the OTHER hand — if the SO's mainHand changed, the previous
            // preview lives on the old bone.
            var animator = ResolvePlayerAnimator();
            if (animator != null)
            {
                StripPreviewsUnder(animator.GetBoneTransform(HumanBodyBones.LeftHand));
                StripPreviewsUnder(animator.GetBoneTransform(HumanBodyBones.RightHand));
            }

            var mainGo = SpawnPreviewProp(so.mainPrefab, hand,
                                          so.mainLocalPos, so.mainLocalEuler, so.mainLocalScale,
                                          $"{PREVIEW_PREFIX}{so.name}__main");

            // Off-hand (e.g., shield) — only spawn when the SO has one configured.
            if (so.offHandPrefab != null && animator != null)
            {
                var offHand = animator.GetBoneTransform(so.offHand);
                if (offHand != null)
                    SpawnPreviewProp(so.offHandPrefab, offHand,
                                     so.offHandLocalPos, so.offHandLocalEuler, so.offHandLocalScale,
                                     $"{PREVIEW_PREFIX}{so.name}__off");
            }

            if (mainGo != null) Selection.activeGameObject = mainGo;
            SceneView.RepaintAll();

            Debug.Log($"[WeaponPreview] Spawned preview for '{so.name}'. " +
                      "Move/Rotate/Scale in the Scene view, then click 'Save spawned Transform → SO'.");
        }

        public static void RefreshIfPresent(WeaponDefinitionSO so)
        {
            var main = FindPreview(so, "main");
            if (main != null)
            {
                main.transform.localPosition    = so.mainLocalPos;
                main.transform.localEulerAngles = so.mainLocalEuler;
                main.transform.localScale       = so.mainLocalScale;
            }
            var off = FindPreview(so, "off");
            if (off != null)
            {
                off.transform.localPosition    = so.offHandLocalPos;
                off.transform.localEulerAngles = so.offHandLocalEuler;
                off.transform.localScale       = so.offHandLocalScale;
            }
            if (main != null || off != null) SceneView.RepaintAll();
        }

        public static void SaveBackToSO(WeaponDefinitionSO so)
        {
            var main = FindPreview(so, "main");
            var off  = FindPreview(so, "off");
            if (main == null && off == null)
            {
                Debug.LogError("[WeaponPreview] No preview spawned for this SO. Click 'Preview / Refresh' first.");
                return;
            }
            Undo.RecordObject(so, "Save Weapon Transform");
            if (main != null)
            {
                so.mainLocalPos   = main.transform.localPosition;
                so.mainLocalEuler = main.transform.localEulerAngles;
                so.mainLocalScale = main.transform.localScale;
            }
            if (off != null)
            {
                so.offHandLocalPos   = off.transform.localPosition;
                so.offHandLocalEuler = off.transform.localEulerAngles;
                so.offHandLocalScale = off.transform.localScale;
            }
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssetIfDirty(so);
            Debug.Log($"[WeaponPreview] Saved transforms to {so.name}.");
        }

        public static void RemovePreview(WeaponDefinitionSO so)
        {
            var main = FindPreview(so, "main");
            if (main != null) Object.DestroyImmediate(main);
            var off  = FindPreview(so, "off");
            if (off != null) Object.DestroyImmediate(off);
            SceneView.RepaintAll();
        }

        // ── Spawn helpers ────────────────────────────────────────────────────

        private static GameObject SpawnPreviewProp(GameObject prefab, Transform parent,
                                                   Vector3 pos, Vector3 euler, Vector3 scale, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.localPosition    = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale       = scale;

            // Mark every spawned object as DontSave so an accidental "Save changes?" prompt
            // in Prefab Mode doesn't bake the preview into the player prefab on disk.
            // Combined with the WeaponPreview_DO_NOT_SAVE_ name prefix that the patcher's
            // CleanupLegacyHandProps strips, this is belt + braces.
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.hideFlags = HideFlags.DontSaveInEditor;
            }
            return go;
        }

        // ── Plumbing ─────────────────────────────────────────────────────────

        private static Transform ResolveOrOpenHand(WeaponDefinitionSO so)
        {
            // Make sure we're inside Prefab Mode for the player prefab.
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.assetPath != W1_PlayerPrefabBuilder.PREFAB_PATH)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(W1_PlayerPrefabBuilder.PREFAB_PATH);
                if (asset == null)
                {
                    Debug.LogError($"[WeaponPreview] Player prefab missing at {W1_PlayerPrefabBuilder.PREFAB_PATH}. Run Celestia/W1/3 first.");
                    return null;
                }
                AssetDatabase.OpenAsset(asset);
                stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null)
                {
                    Debug.LogError("[WeaponPreview] Could not enter Prefab Mode for the player prefab.");
                    return null;
                }
            }

            var animator = ResolvePlayerAnimator();
            if (animator == null || !animator.isHuman)
            {
                Debug.LogError("[WeaponPreview] Player prefab has no humanoid Animator with a valid avatar.");
                return null;
            }

            var hand = animator.GetBoneTransform(so.mainHand);
            if (hand == null)
                Debug.LogError($"[WeaponPreview] Hand bone {so.mainHand} not found on the avatar.");
            return hand;
        }

        private static Animator ResolvePlayerAnimator()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage?.prefabContentsRoot.GetComponentInChildren<Animator>();
        }

        private static GameObject FindPreview(WeaponDefinitionSO so, string suffix)
        {
            var animator = ResolvePlayerAnimator();
            if (animator == null) return null;
            string targetName = $"{PREVIEW_PREFIX}{so.name}__{suffix}";
            foreach (var bone in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
            {
                var t = animator.GetBoneTransform(bone);
                if (t == null) continue;
                foreach (Transform child in t)
                    if (child.name == targetName) return child.gameObject;
            }
            return null;
        }

        private static void StripPreviewsUnder(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child != null && child.name.StartsWith(PREVIEW_PREFIX))
                    Object.DestroyImmediate(child.gameObject);
            }
        }
    }
}
