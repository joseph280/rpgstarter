using System.IO;
using System.Linq;
using RPGStarter.Core.Events;
using RPGStarter.Data;
using RPGStarter.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RPGStarter.EditorTools
{
    /// <summary>
    /// Builds Assets/_Project/Prefabs/Characters/PhantomArcher_Player.prefab
    /// using PlayerArmature from StarterAssets as the placeholder visual.
    ///
    /// Structure:
    ///   PhantomArcher_Player (CharacterController + all gameplay components)
    ///   ├── Visual            (empty holder for the model)
    ///   │   └── PlayerArmature
    ///   └── Hitboxes          (empty parent for future hit volumes)
    ///
    /// When the real rigged Phantom Archer arrives: open prefab → delete PlayerArmature
    /// child of Visual → drag SK_PhantomArcher.fbx in → reassign Avatar on Animator.
    /// </summary>
    public static class W1_PlayerPrefabBuilder
    {
        public const string PREFAB_PATH       = "Assets/_Project/Prefabs/Characters/PhantomArcher_Player.prefab";
        public const string INPUT_ASSET       = "Assets/_Project/Settings/Input/PlayerInput.inputactions";
        public const string SK_PHANTOM_ARCHER = "Assets/_Project/Art/Characters/PhantomArcher/SK_PhantomArcher.fbx";
        public const string M_PHANTOM_ARCHER  = "Assets/_Project/Art/Materials/M_PhantomArcher.mat";

        private static readonly string[] PLAYER_ARMATURE_CANDIDATES =
        {
            // This project's actual install path (Starter Assets under ThirdParty/).
            "Assets/ThirdParty/Starter Assets/Runtime/ThirdPersonController/Prefabs/PlayerArmature.prefab",
            // No-space variants (older or renamed installs)
            "Assets/StarterAssets/ThirdPersonController/Prefabs/PlayerArmature.prefab",
            "Assets/StarterAssets/Runtime/ThirdPersonController/Prefabs/PlayerArmature.prefab",
            "Assets/StarterAssets/Prefabs/PlayerArmature.prefab",
            "Assets/Imports/StarterAssets/ThirdPersonController/Prefabs/PlayerArmature.prefab",
            // Default Asset Store install ("Starter Assets" with a space)
            "Assets/Starter Assets/ThirdPersonController/Prefabs/PlayerArmature.prefab",
            "Assets/Starter Assets/Runtime/ThirdPersonController/Prefabs/PlayerArmature.prefab",
            "Assets/Starter Assets/Prefabs/PlayerArmature.prefab",
        };

        /// <summary>
        /// When true, <see cref="ResolveVisualSource"/> skips the SK_PhantomArcher
        /// preference and always uses the StarterAssets PlayerArmature mannequin.
        /// Set by the simple-RPG-demo build (Demo_PlayerBuild) so the shareable
        /// branch ships a redistributable, asset-pack-free character. Resets to
        /// false after each build so the normal pipeline is unaffected.
        /// </summary>
        public static bool ForceMannequinVisual = false;

        // [MenuItem stripped — single entry point is RPGStarter/Build Demo]
        public static void Build()
        {
            var classSO       = AssetDatabase.LoadAssetAtPath<ClassDefinitionSO>(W1_AssetBuilder.CLASS_PATH);
            var deathChannel  = AssetDatabase.LoadAssetAtPath<VoidEventChannelSO>(W1_AssetBuilder.EV_PLAYER_DIED);
            var healthChannel = AssetDatabase.LoadAssetAtPath<FloatEventChannelSO>(W1_AssetBuilder.EV_HEALTH_CHG);
            var controller    = AssetDatabase.LoadAssetAtPath<AnimatorController>(W1_AnimatorBuilder.CONTROLLER_PATH);
            var inputAsset    = AssetDatabase.LoadAssetAtPath<InputActionAsset>(INPUT_ASSET);

            if (classSO == null || controller == null || inputAsset == null)
            {
                Debug.LogError("[W1_PlayerPrefabBuilder] Run steps 1 and 2 first " +
                    "(RPGStarter/W1/1 - Build Assets, RPGStarter/W1/2 - Build AnimatorController). Also confirm PlayerInput.inputactions exists.");
                return;
            }

            // Cache animator controller reference on the class SO too (handy elsewhere).
            classSO.animatorController = controller;
            EditorUtility.SetDirty(classSO);

            // Prefer the real Phantom Archer FBX if present; fall back to PlayerArmature placeholder.
            var (visualSource, isRealCharacter) = ResolveVisualSource();
            if (visualSource == null)
            {
                Debug.LogError(
                    "[W1_PlayerPrefabBuilder] No visual source found. " +
                    $"Looked for SK_PhantomArcher at {SK_PHANTOM_ARCHER}, then PlayerArmature at: " +
                    string.Join(", ", PLAYER_ARMATURE_CANDIDATES));
                return;
            }
            Debug.Log($"[W1_PlayerPrefabBuilder] Visual source: {AssetDatabase.GetAssetPath(visualSource)} " +
                      $"({(isRealCharacter ? "real Phantom Archer" : "PlayerArmature placeholder")})");

            EnsureDir(Path.GetDirectoryName(PREFAB_PATH));

            // Build the prefab in-memory.
            var root = new GameObject("PhantomArcher_Player");
            try
            {
                root.layer = LayerMask.NameToLayer("Default"); // user can move to Player layer later

                var cc = root.AddComponent<CharacterController>();
                cc.center = new Vector3(0, 0.9f, 0);
                cc.height = 1.8f;
                cc.radius = 0.3f;
                cc.skinWidth = 0.02f;

                var inputReader = root.AddComponent<PlayerInputReader>();
                SetSerialized(inputReader, "inputAsset", inputAsset);

                var movement = root.AddComponent<PlayerMovement>();
                SetSerialized(movement, "input", inputReader);
                SetSerialized(movement, "classDefinition", classSO);

                var animatorBridge = root.AddComponent<PlayerAnimator>();
                var health = root.AddComponent<PlayerHealth>();
                SetSerialized(health, "classDefinition", classSO);
                if (healthChannel != null) SetSerialized(health, "onHealthChanged01", healthChannel);
                if (deathChannel  != null) SetSerialized(health, "onDied", deathChannel);

                // Visual child holds the swappable model.
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                var visualInstance = (GameObject)PrefabUtility.InstantiatePrefab(visualSource, visual.transform);
                visualInstance.name = isRealCharacter ? "SK_PhantomArcher" : "PlayerArmature";

                // Force the visual to local origin so the model's feet sit at the root's
                // local Y=0. Source FBX/prefab can ship with a non-zero local position
                // that produces a "floating" character.
                visualInstance.transform.localPosition = Vector3.zero;
                visualInstance.transform.localRotation = Quaternion.identity;
                visualInstance.transform.localScale    = Vector3.one;

                // Wire the M_PhantomArcher material onto every SkinnedMeshRenderer on the visual.
                // Only when using the real character — PlayerArmature's StarterAssets materials
                // stay as-is for the placeholder.
                if (isRealCharacter)
                {
                    var phantomMat = AssetDatabase.LoadAssetAtPath<Material>(M_PHANTOM_ARCHER);
                    if (phantomMat != null)
                    {
                        foreach (var smr in visualInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            var mats = smr.sharedMaterials;
                            for (int i = 0; i < mats.Length; i++) mats[i] = phantomMat;
                            smr.sharedMaterials = mats;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[W1_PlayerPrefabBuilder] {M_PHANTOM_ARCHER} not found; SkinnedMeshRenderer keeps its imported materials.");
                    }
                }

                // The Animator: prefer one already on the visual; if there isn't one (raw FBX
                // import that didn't generate an Animator), add one with the correct Avatar.
                var armatureAnimator = visualInstance.GetComponentInChildren<Animator>();
                if (armatureAnimator == null)
                {
                    armatureAnimator = visualInstance.AddComponent<Animator>();
                }
                if (isRealCharacter)
                {
                    var avatar = AssetDatabase.LoadAllAssetsAtPath(SK_PHANTOM_ARCHER).OfType<Avatar>().FirstOrDefault();
                    if (avatar != null) armatureAnimator.avatar = avatar;
                    else Debug.LogWarning("[W1_PlayerPrefabBuilder] SK_PhantomArcher Avatar not found; assign manually in Animator.");
                }
                armatureAnimator.runtimeAnimatorController = controller;
                armatureAnimator.applyRootMotion = false; // movement comes from CharacterController
                SetSerialized(animatorBridge, "animator", armatureAnimator);

                // Strip StarterAssets-only controller scripts (only relevant when falling back to PlayerArmature).
                if (!isRealCharacter) StripStarterAssetsScripts(visualInstance);

                // Hitboxes parent
                new GameObject("Hitboxes").transform.SetParent(root.transform, false);

                // Save as prefab
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
                Debug.Log($"[W1_PlayerPrefabBuilder] Built {PREFAB_PATH}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Returns (model GameObject, isRealCharacter). Prefers SK_PhantomArcher.fbx when present
        /// AND configurable as Humanoid; falls back to PlayerArmature otherwise. Side-effect:
        /// reconfigures SK_PhantomArcher.fbx to Humanoid + CreateFromThisModel if it's still Generic.
        /// </summary>
        private static (GameObject source, bool isRealCharacter) ResolveVisualSource()
        {
            if (ForceMannequinVisual)
            {
                Debug.Log("[W1_PlayerPrefabBuilder] ForceMannequinVisual — using StarterAssets PlayerArmature.");
                return (FindPlayerArmatureSource(), false);
            }

            var sk = AssetDatabase.LoadAssetAtPath<GameObject>(SK_PHANTOM_ARCHER);
            if (sk != null)
            {
                if (EnsureHumanoid(SK_PHANTOM_ARCHER))
                {
                    sk = AssetDatabase.LoadAssetAtPath<GameObject>(SK_PHANTOM_ARCHER); // re-fetch after reimport
                    return (sk, true);
                }
                Debug.LogWarning($"[W1_PlayerPrefabBuilder] {SK_PHANTOM_ARCHER} could not be made Humanoid. " +
                                 "Falling back to PlayerArmature placeholder.");
            }
            return (FindPlayerArmatureSource(), false);
        }

        /// <summary>
        /// Reconfigures the given FBX to Humanoid + CreateFromThisModel + applyRootMotion-friendly
        /// clip settings, if not already. Returns true if the resulting Avatar is valid+human.
        /// </summary>
        private static bool EnsureHumanoid(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return false;

            bool needsCommit = importer.animationType != ModelImporterAnimationType.Human
                            || importer.avatarSetup   != ModelImporterAvatarSetup.CreateFromThisModel
                            || importer.sourceAvatar  != null;
            if (needsCommit)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar  = null;
                importer.SaveAndReimport();
                importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            }

            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[W1_PlayerPrefabBuilder] {fbxPath} avatar invalid (avatar={(avatar==null?"null":"set")}, " +
                               $"valid={(avatar!=null && avatar.isValid)}, human={(avatar!=null && avatar.isHuman)}). " +
                               "Open the FBX → Rig tab → Configure to map missing bones.");
                return false;
            }
            return true;
        }

        private static GameObject FindPlayerArmatureSource()
        {
            foreach (var candidate in PLAYER_ARMATURE_CANDIDATES)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(candidate);
                if (go != null) return go;
            }
            // Fallback: search by name across the project. Match anything ending in
            // PlayerArmature.prefab — covers both "StarterAssets" and "Starter Assets" installs
            // and any custom subfolder layout the user may have moved it into.
            var guids = AssetDatabase.FindAssets("PlayerArmature t:Prefab");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/PlayerArmature.prefab"))
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        private static void StripStarterAssetsScripts(GameObject armature)
        {
            // Remove any StarterAssets script — we don't depend on their controller logic.
            // The check is lenient: any script whose namespace contains "StarterAssets" is yanked.
            var monos = armature.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var mb in monos)
            {
                if (mb == null) continue;
                var ns = mb.GetType().Namespace ?? "";
                if (ns.Contains("StarterAssets"))
                    Object.DestroyImmediate(mb, allowDestroyingAssets: false);
            }
            // Also remove any CharacterController on the armature itself — we have our own on root.
            var armCC = armature.GetComponent<CharacterController>();
            if (armCC != null) Object.DestroyImmediate(armCC, allowDestroyingAssets: false);
        }

        private static void SetSerialized(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"Field '{fieldName}' not found on {target}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureDir(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }
}
