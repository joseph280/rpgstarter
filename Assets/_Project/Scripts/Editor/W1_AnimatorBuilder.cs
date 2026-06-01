using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Celestia.EditorTools
{
    /// <summary>
    /// Builds AC_PhantomArcher.controller wired to Mixamo clips from W0 imports.
    /// State machine: Locomotion (1D blend Idle↔Run by Speed), Dodge, Hit, Die.
    /// Idempotent — re-running rebuilds from the same clip references.
    ///
    /// Mixamo source FBXs are reconfigured to:
    ///   - Animation Type = Humanoid
    ///   - Avatar Setup   = Create From This Model (auto-generate per FBX)
    ///   - Per-clip Loop Time set per state (idle/run loop, dodge/hit/die one-shot)
    /// Generates a per-FBX Humanoid avatar — Unity's auto-mapper recognizes Mixamo's
    /// `mixamorig:` bone prefix and produces a clean rig. We considered Copy From Other
    /// using PlayerArmature's avatar but it fails for Mixamo: the StarterAssets armature
    /// has unprefixed bone names ("Hips") while Mixamo uses "mixamorig:Hips", and the
    /// CopyFromOther path matches bones by string, not by humanoid role.
    /// </summary>
    public static class W1_AnimatorBuilder
    {
        public const string CONTROLLER_PATH = "Assets/_Project/Prefabs/Characters/AC_PhantomArcher.controller";

        // Source clips — Pro Longbow Pack chosen for archetype fit.
        private const string DIR = "Assets/_Project/Art/Characters/PhantomArcher/Animations/Pro Longbow Pack/";
        private const string CLIP_IDLE  = DIR + "standing idle 01.fbx";
        private const string CLIP_RUN   = DIR + "standing run forward.fbx";
        private const string CLIP_DODGE = DIR + "standing dodge forward.fbx";
        private const string CLIP_HIT   = DIR + "standing react small from front.fbx";
        private const string CLIP_DIE   = DIR + "standing death backward 01.fbx";

        [MenuItem("Celestia/W1/2 - Build AnimatorController")]
        public static void Build()
        {
            // Pass 1: reconfigure each Mixamo source FBX (Humanoid + per-FBX avatar + loops).
            // Always SaveAndReimport — we're switching from auto to custom clipAnimations,
            // which Unity needs to persist regardless of whether other fields changed.
            ConfigureClip(CLIP_IDLE,  loopTime: true);
            ConfigureClip(CLIP_RUN,   loopTime: true);
            ConfigureClip(CLIP_DODGE, loopTime: false);
            ConfigureClip(CLIP_HIT,   loopTime: false);
            ConfigureClip(CLIP_DIE,   loopTime: false);

            // Pass 2: verify each clip loaded correctly (catch any silent import failures).
            var idle  = LoadFirstClip(CLIP_IDLE);
            var run   = LoadFirstClip(CLIP_RUN);
            var dodge = LoadFirstClip(CLIP_DODGE);
            var hit   = LoadFirstClip(CLIP_HIT);
            var die   = LoadFirstClip(CLIP_DIE);

            if (idle == null || run == null || dodge == null || hit == null || die == null)
            {
                Debug.LogError("[W1_AnimatorBuilder] One or more source clips missing after reimport. " +
                    "Confirm each FBX exists at the configured paths and check the Console for import errors.");
                return;
            }

            VerifyHumanoid(CLIP_IDLE);
            VerifyHumanoid(CLIP_RUN);
            VerifyHumanoid(CLIP_DODGE);
            VerifyHumanoid(CLIP_HIT);
            VerifyHumanoid(CLIP_DIE);

            EnsureDir(Path.GetDirectoryName(CONTROLLER_PATH));
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH);
            if (existing != null) AssetDatabase.DeleteAsset(CONTROLLER_PATH);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(CONTROLLER_PATH);

            controller.AddParameter("Speed",     AnimatorControllerParameterType.Float);
            controller.AddParameter("IsDodging", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Hit",       AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die",       AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            // Locomotion blend tree: Idle ↔ Run on Speed
            var blendTree = new BlendTree
            {
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                name = "Locomotion",
                useAutomaticThresholds = false
            };
            blendTree.AddChild(idle, 0f);
            blendTree.AddChild(run,  4f);
            AssetDatabase.AddObjectToAsset(blendTree, controller);

            var locomotion = sm.AddState("Locomotion", new Vector3(300, 100));
            locomotion.motion = blendTree;

            var dodgeState = sm.AddState("Dodge", new Vector3(550, 100));
            dodgeState.motion = dodge;

            var hitState = sm.AddState("Hit", new Vector3(550, 220));
            hitState.motion = hit;

            var dieState = sm.AddState("Die", new Vector3(550, 340));
            dieState.motion = die;

            sm.defaultState = locomotion;

            // Locomotion → Dodge
            var t = locomotion.AddTransition(dodgeState);
            t.AddCondition(AnimatorConditionMode.If, 0, "IsDodging");
            t.hasExitTime = false;
            t.duration    = 0.05f;

            // Dodge → Locomotion
            var t2 = dodgeState.AddTransition(locomotion);
            t2.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDodging");
            t2.hasExitTime = false;
            t2.duration    = 0.1f;

            // Any State → Hit (one-shot, exit on time)
            var tHit = sm.AddAnyStateTransition(hitState);
            tHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
            tHit.duration    = 0.05f;
            tHit.canTransitionToSelf = false;

            var tHitOut = hitState.AddTransition(locomotion);
            tHitOut.hasExitTime = true;
            tHitOut.exitTime    = 0.9f;
            tHitOut.duration    = 0.1f;

            // Any State → Die (terminal)
            var tDie = sm.AddAnyStateTransition(dieState);
            tDie.AddCondition(AnimatorConditionMode.If, 0, "Die");
            tDie.duration = 0.05f;
            tDie.canTransitionToSelf = false;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[W1_AnimatorBuilder] Built {CONTROLLER_PATH}");
        }

        private static AnimationClip LoadFirstClip(string path)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            return assets.OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        /// <summary>
        /// Two-pass reconfigure. The trick: switching animationType from Generic to
        /// Human is destructive — Unity discards any clipAnimations carried over from
        /// the previous type. So we MUST commit the type change first (reimport once),
        /// THEN read defaultClipAnimations against the now-Humanoid importer and assign
        /// the per-clip overrides (reimport again).
        ///
        /// One reimport instead of two when the FBX is already Humanoid with a
        /// CreateFromThisModel avatar.
        /// </summary>
        private static void ConfigureClip(string fbxPath, bool loopTime)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[W1_AnimatorBuilder] No ModelImporter at {fbxPath}");
                return;
            }

            // Pass 1: ensure Humanoid + CreateFromThisModel. Reimport only if needed.
            bool typeNeedsCommit = importer.animationType != ModelImporterAnimationType.Human
                                || importer.avatarSetup   != ModelImporterAvatarSetup.CreateFromThisModel
                                || importer.sourceAvatar  != null;
            if (typeNeedsCommit)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar  = null;
                importer.SaveAndReimport();
                importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter; // re-fetch after reimport
            }

            // Pass 2: now defaultClipAnimations reflects the Humanoid take. Override loop.
            var clips = importer.defaultClipAnimations;
            if (clips.Length == 0)
            {
                Debug.LogError($"[W1_AnimatorBuilder] {fbxPath}: defaultClipAnimations is empty after Humanoid reimport. " +
                    "FBX may have no animation curves — check the source file.");
                return;
            }
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].loopTime = loopTime;
                clips[i].loopPose = loopTime;
                clips[i].lockRootRotation = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Logs a clear error if a clip didn't end up Humanoid after reimport, or if
        /// the auto-generated Avatar isn't valid. Catches silent failures so we don't
        /// ship a broken AnimatorController.
        /// </summary>
        private static void VerifyHumanoid(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                Debug.LogError($"[W1_AnimatorBuilder] {fbxPath} is still {importer.animationType} after reimport. " +
                    "Animation will not retarget to PlayerArmature.");
            }
            if (importer.clipAnimations.Length == 0)
            {
                Debug.LogError($"[W1_AnimatorBuilder] {fbxPath} has no custom clipAnimations after reimport. " +
                    "Loop Time will revert to default (OFF) and animation will freeze.");
            }

            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null)
            {
                Debug.LogError($"[W1_AnimatorBuilder] {fbxPath} produced no Avatar after reimport. " +
                    "Humanoid auto-generation failed — open the FBX and check Rig → Configure.");
            }
            else if (!avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[W1_AnimatorBuilder] {fbxPath} avatar is invalid (isValid={avatar.isValid}, " +
                    $"isHuman={avatar.isHuman}). Animation will retarget incorrectly. " +
                    "Open the FBX and check Rig → Configure for unmapped bones.");
            }
        }

        private static void EnsureDir(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }
}
