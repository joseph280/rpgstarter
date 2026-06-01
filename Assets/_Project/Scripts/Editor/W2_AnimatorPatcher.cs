using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Celestia.EditorTools
{
    /// <summary>
    /// Adds a single weapon-neutral "Attack" state to AC_PhantomArcher on a dedicated
    /// "UpperBody" layer with a humanoid avatar mask. The mask blocks legs + root, so
    /// the Attack clip drives the spine/arms/head while the base layer keeps Locomotion
    /// running on the legs. The clip itself is HOT-SWAPPED at runtime by
    /// <see cref="Celestia.Player.WeaponEquipment"/> via AnimatorOverrideController, so
    /// the same state plays a sword slash, an axe swing, a bow recoil, etc., depending
    /// on which weapon is currently equipped.
    ///
    /// PlayerCombat fires it via Animator.Play("Attack", layer:1).
    ///
    /// Default placeholder clip: the sword-and-shield slash. Equipping the bow swaps
    /// it to the bow recoil clip via override controller. No state-per-weapon needed.
    /// </summary>
    public static class W2_AnimatorPatcher
    {
        // Default placeholder — runtime override controller substitutes per weapon.
        public  const string CLIP_ATTACK_PLACEHOLDER = "Assets/_Project/Art/Characters/PhantomArcher/Animations/Sword and Shield Pack/sword and shield slash.fbx";
        // Bow recoil clip is still imported here so prefab patching can wire the bow weapon to it.
        public  const string CLIP_BOW                = "Assets/_Project/Art/Characters/PhantomArcher/Animations/Pro Longbow Pack/standing aim recoil.fbx";
        private const string MASK_UPPER_BODY_PATH    = "Assets/_Project/Art/Characters/PhantomArcher/Animations/AM_UpperBody.mask";
        private const string UPPER_BODY_LAYER_NAME   = "UpperBody";
        public  const string ATTACK_STATE_NAME       = "Attack";

        [MenuItem("Celestia/W2/2 - Patch AnimatorController (Attack state)")]
        public static void Patch()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(W1_AnimatorBuilder.CONTROLLER_PATH);
            if (controller == null)
            {
                Debug.LogError($"[W2_AnimatorPatcher] {W1_AnimatorBuilder.CONTROLLER_PATH} missing. Run Celestia/W1/2 first.");
                return;
            }

            ConfigureClipForHumanoid(CLIP_ATTACK_PLACEHOLDER, loopTime: false);
            ConfigureClipForHumanoid(CLIP_BOW,                loopTime: false);

            var placeholderClip = LoadFirstClip(CLIP_ATTACK_PLACEHOLDER);
            if (placeholderClip == null)
            {
                Debug.LogError($"[W2_AnimatorPatcher] No AnimationClip found at {CLIP_ATTACK_PLACEHOLDER}.");
                return;
            }

            // 1. Drop any legacy "BowAttack" / "Attack" states left over on either layer.
            var baseSM = controller.layers[0].stateMachine;
            RemoveStateIfPresent(baseSM, "BowAttack");
            RemoveStateIfPresent(baseSM, ATTACK_STATE_NAME);

            // 2. Ensure the avatar mask exists + matches the intended bone set.
            var mask = EnsureUpperBodyMask();

            // 3. Ensure the UpperBody layer exists with mask + full weight + override blend.
            int upperIdx = EnsureUpperBodyLayer(controller, mask);

            // 4. Rebuild the UpperBody state machine: Empty (default) + Attack.
            var upperSM = controller.layers[upperIdx].stateMachine;
            foreach (var s in upperSM.states.ToArray())
                upperSM.RemoveState(s.state);

            var emptyState = upperSM.AddState("Empty", new Vector3(300, 100));
            // Empty has no motion — on a masked Override layer this contributes nothing and the
            // base layer plays through fully.
            upperSM.defaultState = emptyState;

            var attack = upperSM.AddState(ATTACK_STATE_NAME, new Vector3(550, 100));
            attack.motion             = placeholderClip;
            attack.writeDefaultValues = false;

            var exit = attack.AddTransition(emptyState);
            exit.hasExitTime = true;
            exit.exitTime    = 0.85f;
            exit.duration    = 0.1f;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[W2_AnimatorPatcher] '{ATTACK_STATE_NAME}' state on '{UPPER_BODY_LAYER_NAME}' layer (idx {upperIdx}) " +
                      $"using placeholder '{Path.GetFileName(CLIP_ATTACK_PLACEHOLDER)}'. WeaponEquipment swaps the clip per-weapon.");
        }

        public static AnimationClip LoadFirstClip(string fbxPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        private static void RemoveStateIfPresent(AnimatorStateMachine sm, string name)
        {
            var s = sm.states.FirstOrDefault(x => x.state.name == name).state;
            if (s != null) sm.RemoveState(s);
        }

        /// <summary>Creates or reasserts the humanoid upper-body mask: spine/arms/head/fingers/handIK on, legs/root/footIK off.</summary>
        private static AvatarMask EnsureUpperBodyMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MASK_UPPER_BODY_PATH);
            if (mask == null)
            {
                EnsureDir(Path.GetDirectoryName(MASK_UPPER_BODY_PATH));
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, MASK_UPPER_BODY_PATH);
            }

            // Re-apply every patch so a hand-edited mask snaps back to the intended state.
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root,         false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body,         true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head,         true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg,      false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg,     false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm,      true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm,     true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers,  true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK,   false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK,  false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK,   true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK,  true);

            EditorUtility.SetDirty(mask);
            return mask;
        }

        /// <summary>Adds the UpperBody layer if missing and (re-)configures mask + override + weight 1.</summary>
        private static int EnsureUpperBodyLayer(AnimatorController controller, AvatarMask mask)
        {
            var layers = controller.layers;
            int idx = System.Array.FindIndex(layers, l => l.name == UPPER_BODY_LAYER_NAME);

            if (idx < 0)
            {
                controller.AddLayer(UPPER_BODY_LAYER_NAME);
                layers = controller.layers;
                idx = layers.Length - 1;
                // The new layer's state machine is created automatically but isn't yet a sub-asset.
                // Persist it under the controller so the layer survives a save/reload.
                if (layers[idx].stateMachine != null
                    && AssetDatabase.GetAssetPath(layers[idx].stateMachine) == string.Empty)
                {
                    AssetDatabase.AddObjectToAsset(layers[idx].stateMachine, controller);
                }
            }

            layers[idx].avatarMask    = mask;
            layers[idx].blendingMode  = AnimatorLayerBlendingMode.Override;
            layers[idx].defaultWeight = 1f;
            // Reassigning the array commits any field changes Unity may have copied-by-value.
            controller.layers = layers;
            return idx;
        }

        private static void EnsureDir(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        // Reused for any FBX clip we want to drive the player's humanoid rig — sets
        // animationType + avatar setup, then applies the loop / position-lock overrides
        // that prevent root motion from translating the player.
        public static void ConfigureClipForHumanoid(string fbxPath, bool loopTime)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            bool typeNeedsCommit = importer.animationType != ModelImporterAnimationType.Human
                                || importer.avatarSetup   != ModelImporterAvatarSetup.CreateFromThisModel;
            if (typeNeedsCommit)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar  = null;
                importer.SaveAndReimport();
                importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            }

            var clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].loopTime = loopTime;
                clips[i].lockRootRotation = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }
}
