using System.IO;
using System.Linq;
using Celestia.Combat;
using Celestia.Data;
using Celestia.Enemies;
using Celestia.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Celestia.EditorTools
{
    /// <summary>
    /// W2 monster scaffolding — uses the imported base_monster_1.fbx (humanoid) as a
    /// roaming melee enemy that chases the player. Builds:
    ///  - Enemy_Monster1.asset (EnemyDefinitionSO with AI stats)
    ///  - AC_Monster1.controller (Locomotion blend tree + Attack + Death states)
    ///  - Monster1.prefab (CharacterController, Health, DamageReceiver, Hitbox, MonsterAI + FBX visual)
    ///  - Adds one Monster1 instance to Test_PlayerMovement.unity, far from the player spawn
    ///
    /// Animation source clips are reused from the existing Phantom Archer animation packs:
    /// both the player and the monster FBXs are humanoid, so Mixamo-style retargeting Just Works.
    /// Idempotent — re-runnable from the menu without leaving duplicates.
    /// </summary>
    public static class W2_MonsterBuilder
    {
        // Asset paths
        public const string MONSTER_FBX        = "Assets/_Project/Art/Characters/Monster1/base_monster_1.fbx";
        public const string ENEMY_SO_PATH      = "Assets/_Project/ScriptableObjects/Enemies/Enemy_Monster1.asset";
        public const string CONTROLLER_PATH    = "Assets/_Project/Prefabs/Enemies/AC_Monster1.controller";
        public const string PREFAB_PATH        = "Assets/_Project/Prefabs/Enemies/Monster1.prefab";
        public const string TEX_FOLDER         = "Assets/_Project/Art/Characters/Monster1/Textures";

        // Reuse Phantom Archer humanoid clips — both rigs are humanoid so retargeting works.
        private const string CLIP_IDLE  = "Assets/_Project/Art/Characters/PhantomArcher/Animations/Sword and Shield Pack/sword and shield idle.fbx";
        private const string CLIP_WALK  = "Assets/_Project/Art/Characters/PhantomArcher/Animations/Sword and Shield Pack/sword and shield walk.fbx";
        private const string CLIP_ATTACK = "Assets/_Project/Art/Characters/PhantomArcher/Animations/Sword and Shield Pack/sword and shield slash.fbx";
        private const string CLIP_DEATH = "Assets/_Project/Art/Characters/PhantomArcher/Animations/Sword and Shield Pack/sword and shield death.fbx";

        // Spawn position in Test_PlayerMovement: outside the 12m detection radius but INSIDE
        // the 40x40 Floor (edges at ±20). z=18 → distance 18 from player at origin, so the
        // player has ~6m to walk before aggro. z=30 (the original) sat past the floor edge.
        private static readonly Vector3 SPAWN_POSITION = new(0f, 0f, 18f);

        // Fallback material so the monster never renders pink even if the FBX ships
        // Built-in/Standard materials that URP can't draw.
        private const string FALLBACK_MAT_PATH = "Assets/_Project/Art/Materials/M_Monster1.mat";

        [MenuItem("Celestia/W2/6 - Build Monster1 (asset + prefab + scene)")]
        public static void Build()
        {
            EnsureFbxIsHumanoid(MONSTER_FBX, requireValid: true);
            ExtractFbxTextures(MONSTER_FBX, TEX_FOLDER);
            ConfigureClipForHumanoid(CLIP_IDLE,   loopTime: true);
            ConfigureClipForHumanoid(CLIP_WALK,   loopTime: true);
            ConfigureClipForHumanoid(CLIP_ATTACK, loopTime: false);
            ConfigureClipForHumanoid(CLIP_DEATH,  loopTime: false);

            var idle  = LoadFirstClip(CLIP_IDLE);
            var walk  = LoadFirstClip(CLIP_WALK);
            var atk   = LoadFirstClip(CLIP_ATTACK);
            var death = LoadFirstClip(CLIP_DEATH);
            if (idle == null || walk == null || atk == null || death == null)
            {
                Debug.LogError("[W2_MonsterBuilder] One or more source clips missing. Check Console for import errors.");
                return;
            }

            var enemySO   = BuildEnemySO();
            var controller = BuildAnimatorController(idle, walk, atk, death);
            var prefab    = BuildPrefab(enemySO, controller);
            enemySO.prefab = prefab;
            EditorUtility.SetDirty(enemySO);

            PatchTestScene(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[W2_MonsterBuilder] Monster1 built. Open Test_PlayerMovement.unity → Play, walk toward +Z to engage.");
        }

        // ── SO ───────────────────────────────────────────────────────────────

        private static EnemyDefinitionSO BuildEnemySO()
        {
            EnsureDir(Path.GetDirectoryName(ENEMY_SO_PATH));
            var so = AssetDatabase.LoadAssetAtPath<EnemyDefinitionSO>(ENEMY_SO_PATH);
            if (so == null)
            {
                so = ScriptableObject.CreateInstance<EnemyDefinitionSO>();
                AssetDatabase.CreateAsset(so, ENEMY_SO_PATH);
            }
            so.displayName     = "Monster";
            so.description     = "Melee chaser — walks toward the player and swings when in range. Slower than the Phantom Archer.";
            so.maxHealth       = 80;
            so.baseDamage      = 8f;
            so.baseAttackRate  = 1f;
            so.moveSpeed       = 3.5f;          // player.moveSpeed = 6, so player can outrun
            so.detectionRadius = 12f;
            so.attackRange     = 1.8f;
            so.attackWindup    = 0.35f;
            so.attackDuration  = 0.85f;
            so.deathDuration   = 2.5f;
            EditorUtility.SetDirty(so);
            return so;
        }

        // ── Animator ─────────────────────────────────────────────────────────

        private static AnimatorController BuildAnimatorController(
            AnimationClip idle, AnimationClip walk, AnimationClip atk, AnimationClip death)
        {
            EnsureDir(Path.GetDirectoryName(CONTROLLER_PATH));
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH) != null)
                AssetDatabase.DeleteAsset(CONTROLLER_PATH);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(CONTROLLER_PATH);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var sm = controller.layers[0].stateMachine;

            // Locomotion = 1D blend tree (Idle ↔ Walk on Speed). Mirrors AC_PhantomArcher.
            var blend = new BlendTree
            {
                name                   = "Locomotion",
                blendType              = BlendTreeType.Simple1D,
                blendParameter         = "Speed",
                useAutomaticThresholds = false
            };
            blend.AddChild(idle, 0f);
            blend.AddChild(walk, 3.5f);
            AssetDatabase.AddObjectToAsset(blend, controller);

            var locomotion = sm.AddState("Locomotion", new Vector3(300, 100));
            locomotion.motion = blend;
            sm.defaultState   = locomotion;

            // Attack — fired by Animator.Play(AttackState). Auto-returns to Locomotion via exit time.
            var attackState = sm.AddState("Attack", new Vector3(550, 100));
            attackState.motion = atk;
            attackState.writeDefaultValues = false;
            var attackExit = attackState.AddTransition(locomotion);
            attackExit.hasExitTime = true;
            attackExit.exitTime    = 0.9f;
            attackExit.duration    = 0.1f;

            // Death — fired by Animator.Play(DeathState). Terminal: no outgoing transitions.
            var deathState = sm.AddState("Death", new Vector3(550, 220));
            deathState.motion = death;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        // ── Prefab ───────────────────────────────────────────────────────────

        private static GameObject BuildPrefab(EnemyDefinitionSO def, AnimatorController controller)
        {
            EnsureDir(Path.GetDirectoryName(PREFAB_PATH));
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH) != null)
                AssetDatabase.DeleteAsset(PREFAB_PATH);

            var fbxSource = AssetDatabase.LoadAssetAtPath<GameObject>(MONSTER_FBX);
            if (fbxSource == null)
            {
                Debug.LogError($"[W2_MonsterBuilder] Monster FBX not found at {MONSTER_FBX}.");
                return null;
            }

            var root = new GameObject("Monster1");
            try
            {
                root.layer = W2_LayerSetup.LAYER_ENEMY;

                var cc = root.AddComponent<CharacterController>();
                cc.center    = new Vector3(0f, 0.9f, 0f);
                cc.height    = 1.8f;
                cc.radius    = 0.4f;
                cc.skinWidth = 0.02f;

                var health = root.AddComponent<Health>();
                SetSerialized(health, "max", def.maxHealth);

                root.AddComponent<DamageReceiver>();

                var hitbox = root.AddComponent<Hitbox>();
                SetSerialized(hitbox, "allegiance", (int)Hitbox.Allegiance.Enemy);

                var hitFlash = root.AddComponent<HitFlash>();

                var ai = root.AddComponent<MonsterAI>();
                SetSerialized(ai, "definition", def);
                SetSerialized(ai, "hitFlash", hitFlash);

                // Visual child = FBX prefab instance.
                var visualHolder = new GameObject("Visual");
                visualHolder.transform.SetParent(root.transform, false);
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(fbxSource, visualHolder.transform);
                visual.name = "base_monster_1";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale    = Vector3.one;

                // Animator: prefer one already on the FBX; otherwise add. Wire avatar + controller.
                var animator = visual.GetComponentInChildren<Animator>();
                if (animator == null) animator = visual.AddComponent<Animator>();

                var avatar = AssetDatabase.LoadAllAssetsAtPath(MONSTER_FBX).OfType<Avatar>().FirstOrDefault();
                if (avatar != null) animator.avatar = avatar;
                else Debug.LogWarning("[W2_MonsterBuilder] Monster avatar not found on FBX; assign manually.");

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                SetSerialized(ai, "animator", animator);

                EnsureRenderableVisual(visual);

                HealthBarBuilder.Attach(root, health, yOffset: 2.4f,
                                        fillColor: new Color(0.85f, 0.2f, 0.2f));

                // Save as prefab.
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Forces every SkinnedMeshRenderer on the visual to use M_Monster1 (URP/Lit + the
        /// FBX-embedded shaded.png that ExtractFbxTextures pulled out). This is the reliable
        /// fix for two failure modes seen in the wild:
        ///  - FBX imports its own materials in the *Standard* shader → magenta in URP.
        ///  - FBX imports URP materials but the embedded texture isn't extracted → solid white.
        /// Also forces updateWhenOffscreen so a stale bind-pose bounding box can't cull the
        /// renderer at oblique camera angles.
        /// </summary>
        private static void EnsureRenderableVisual(GameObject visual)
        {
            var renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogError($"[W2_MonsterBuilder] No SkinnedMeshRenderer on {MONSTER_FBX}. " +
                               "The FBX may have a non-skinned mesh or the rig is broken.");
                return;
            }

            var baseTex = FindFirstTextureIn(TEX_FOLDER);
            var material = LoadOrCreateMonsterMaterial(baseTex);

            Bounds combined = new(visual.transform.position, Vector3.zero);
            bool boundsInit = false;

            foreach (var smr in renderers)
            {
                smr.updateWhenOffscreen = true;

                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                smr.sharedMaterials = mats;

                if (!boundsInit) { combined = smr.bounds; boundsInit = true; }
                else combined.Encapsulate(smr.bounds);
            }

            Debug.Log($"[W2_MonsterBuilder] Visual ready — {renderers.Length} skinned renderer(s), " +
                      $"combined bounds size {combined.size} centered at {combined.center}. " +
                      $"Texture: {(baseTex != null ? AssetDatabase.GetAssetPath(baseTex) : "<none — material is solid colour>")}");
        }

        private static Material LoadOrCreateMonsterMaterial(Texture2D baseTex)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(FALLBACK_MAT_PATH);
            if (mat == null)
            {
                EnsureDir(Path.GetDirectoryName(FALLBACK_MAT_PATH));
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, FALLBACK_MAT_PATH);
            }

            // White tint so the texture renders as-authored. If the texture wasn't extracted,
            // fall back to a slate colour so the model isn't pure white.
            Color tint = baseTex != null ? Color.white : new Color(0.55f, 0.18f, 0.22f);

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);

            if (baseTex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", baseTex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", baseTex);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Texture2D FindFirstTextureIn(string folder)
        {
            if (!Directory.Exists(folder)) return null;
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var tex  = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null) return tex;
            }
            return null;
        }

        /// <summary>
        /// Extract textures embedded in the FBX (Mixamo-mini style exports ship a single
        /// shaded.png inside the .fbm sidecar; Unity doesn't extract it by default, leaving
        /// the SkinnedMeshRenderer's material with a null _BaseMap → solid white in-engine).
        /// Idempotent: only runs when the target folder is empty.
        /// </summary>
        private static void ExtractFbxTextures(string fbxPath, string folder)
        {
            EnsureDir(folder);
            bool alreadyExtracted = Directory.GetFiles(folder)
                .Any(f => !f.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase));
            if (alreadyExtracted) return;

            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            importer.ExtractTextures(folder);
            AssetDatabase.WriteImportSettingsIfDirty(fbxPath);
            AssetDatabase.ImportAsset(folder, ImportAssetOptions.ImportRecursive);
            importer.SaveAndReimport();
        }

        // ── Scene patch ──────────────────────────────────────────────────────

        private static void PatchTestScene(GameObject prefab)
        {
            if (prefab == null) return;

            var scene = EditorSceneManager.OpenScene(W1_SceneBuilder.TEST_PATH, OpenSceneMode.Single);

            // Remove a previously placed Monster1 instance.
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Monster1") Object.DestroyImmediate(root);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = SPAWN_POSITION;
            instance.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up); // face the player

            // Wire the player ref so MonsterAI doesn't have to FindAnyObjectByType at runtime.
            var ph = Object.FindAnyObjectByType<PlayerHealth>();
            if (ph != null)
            {
                var ai = instance.GetComponent<MonsterAI>();
                if (ai != null) SetSerialized(ai, "target", ph);
            }

            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[W2_MonsterBuilder] Monster1 placed at {SPAWN_POSITION} in {W1_SceneBuilder.TEST_PATH}.");
        }

        // ── Import helpers ───────────────────────────────────────────────────

        private static AnimationClip LoadFirstClip(string fbxPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        private static bool EnsureFbxIsHumanoid(string fbxPath, bool requireValid)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[W2_MonsterBuilder] No ModelImporter at {fbxPath}");
                return false;
            }

            bool needsCommit = importer.animationType != ModelImporterAnimationType.Human
                            || importer.avatarSetup   != ModelImporterAvatarSetup.CreateFromThisModel
                            || importer.sourceAvatar  != null;
            if (needsCommit)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar  = null;
                importer.SaveAndReimport();
            }

            if (!requireValid) return true;
            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[W2_MonsterBuilder] {fbxPath} has no valid humanoid avatar after reimport. " +
                               "Open the FBX → Rig → Configure to map missing bones.");
                return false;
            }
            return true;
        }

        private static void ConfigureClipForHumanoid(string fbxPath, bool loopTime)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) { Debug.LogWarning($"[W2_MonsterBuilder] No ModelImporter at {fbxPath}"); return; }

            bool needsCommit = importer.animationType != ModelImporterAnimationType.Human
                            || importer.avatarSetup   != ModelImporterAvatarSetup.CreateFromThisModel;
            if (needsCommit)
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
                clips[i].loopTime              = loopTime;
                clips[i].loopPose              = loopTime;
                clips[i].lockRootRotation      = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        // ── Misc ─────────────────────────────────────────────────────────────

        private static void SetSerialized(Object target, string fieldName, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"[W2_MonsterBuilder] Field '{fieldName}' not found on {target}"); return; }
            switch (value)
            {
                case Object obj: prop.objectReferenceValue = obj; break;
                case int i:      prop.intValue             = i;   break;
                case float f:    prop.floatValue           = f;   break;
                case bool b:     prop.boolValue            = b;   break;
                case string s:   prop.stringValue          = s;   break;
                default:
                    Debug.LogWarning($"[W2_MonsterBuilder] SetSerialized: unsupported type {value?.GetType()}");
                    break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureDir(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }
}
