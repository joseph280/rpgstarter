using System.IO;
using System.Linq;
using RPGStarter.Combat;
using RPGStarter.Data;
using UnityEditor;
using UnityEngine;

namespace RPGStarter.EditorTools
{
    /// <summary>
    /// Builds the weapon roster ScriptableObjects + the melee BasicSlash ability.
    /// One SO per weapon — the data model is in <see cref="WeaponDefinitionSO"/>.
    ///
    /// Idempotent: re-running will load existing assets and re-apply the default
    /// transforms / clip / ability references. The artist's hand-tuning of
    /// mainLocalPos / mainLocalEuler / mainLocalScale survives because that lives
    /// on the SO instance itself — but defaults below are what re-run sets when
    /// the SO is first created.
    /// </summary>
    public static class W2_WeaponBuilder
    {
        private const string DIR_WEAPONS  = "Assets/_Project/ScriptableObjects/Weapons";
        private const string DIR_ABILITY  = "Assets/_Project/ScriptableObjects/Abilities";
        private const string DIR_DAMAGE   = "Assets/_Project/ScriptableObjects/GameConfig";

        private const string PATH_BOW          = DIR_WEAPONS  + "/Weapon_Bow.asset";
        private const string PATH_SWORD_SHIELD = DIR_WEAPONS  + "/Weapon_SwordShield.asset";

        private const string PATH_SLASH_AB = DIR_ABILITY  + "/Ability_PhantomArcher_BasicSlash.asset";
        private const string PATH_SHOT_AB  = DIR_ABILITY  + "/Ability_PhantomArcher_BasicShot.asset";
        private const string PATH_DMG_PHYS = DIR_DAMAGE   + "/DamageType_Physical.asset";

        // Bow visual = Demo_PrimitiveAssets' thin tall cube. Reads as a stick more
        // than a bow but the gameplay function (BasicShot ability → pooled arrow)
        // works against any visual.
        private const string PREFAB_BOW    = Demo_PrimitiveAssets.SRC_BOW;

        // Sword + shield are in _Project/Art/Weapons. Wrapper prefabs are generated
        // below with URP/Lit materials bound to the loose albedo PNGs that ship
        // alongside the FBXs.
        private const string SWORD_FBX     = "Assets/_Project/Art/Weapons/Sword/sword.fbx";
        private const string SHIELD_FBX    = "Assets/_Project/Art/Weapons/Shield/shield.fbx";
        private const string SWORD_ALBEDO  = "Assets/_Project/Art/Weapons/Sword/sword_albedo.png";
        private const string SHIELD_ALBEDO = "Assets/_Project/Art/Weapons/Shield/shield_albedo.png";
        private const string SHIELD_BUMP   = "Assets/_Project/Art/Weapons/Shield/shield_bump.png";
        private const string MAT_SWORD     = "Assets/_Project/Art/Materials/M_Sword.mat";
        private const string MAT_SHIELD    = "Assets/_Project/Art/Materials/M_Shield.mat";
        private const string PREFAB_SWORD_WRAPPER  = "Assets/_Project/Prefabs/Weapons/Sword.prefab";
        private const string PREFAB_SHIELD_WRAPPER = "Assets/_Project/Prefabs/Weapons/Shield.prefab";

        // Default attach transforms — first-run only. Tweak in the Inspector once it's in-engine.
        // Mixamo right hand grip: weapon's long axis runs along the forearm. (90, -90, 0) holds
        // the weapon vertically with the tip pointing toward the sky in idle stance — the
        // original (-90, -90, 0) put the blade along the forearm so the tip pointed down at
        // the ground in idle. (0, 0.05, 0.02) seats the hilt in the palm.
        private static readonly Vector3 RIGHTHAND_POS   = new(0f, 0.05f, 0.02f);
        private static readonly Vector3 RIGHTHAND_EULER = new(90f, -90f, 0f);
        private static readonly Vector3 RIGHTHAND_SCALE = new(0.5f, 0.5f, 0.5f);

        // Bow grip — LeftHand. Tip-up orientation so the primitive cube reads as a
        // vertical bow stave.
        private static readonly Vector3 LEFTHAND_BOW_POS   = new(0f, 0.05f, 0.02f);
        private static readonly Vector3 LEFTHAND_BOW_EULER = new(-90f, 90f, 0f);
        private static readonly Vector3 LEFTHAND_BOW_SCALE = new(0.5f, 0.5f, 0.5f);

        // [MenuItem stripped — single entry point is RPGStarter/Build Demo]
        public static void Build()
        {
            EnsureDir(DIR_WEAPONS);
            EnsureDir(DIR_ABILITY);

            var slashAbility = EnsureSlashAbility();
            var shotAbility  = AssetDatabase.LoadAssetAtPath<AbilityDefinitionSO>(PATH_SHOT_AB);
            if (shotAbility == null)
                Debug.LogWarning("[W2_WeaponBuilder] BasicShot ability not found — Bow will reference null until W2_AssetBuilder runs.");

            // Animation clips. Bow uses the bow-fire clip; sword & shield share the slash.
            var slashClip = W2_AnimatorPatcher.LoadFirstClip(W2_AnimatorPatcher.CLIP_ATTACK_PLACEHOLDER);
            var bowClip   = W2_AnimatorPatcher.LoadFirstClip(W2_AnimatorPatcher.CLIP_BOW);
            if (slashClip == null) Debug.LogError("[W2_WeaponBuilder] Slash clip missing — animator step hasn't run.");
            if (bowClip   == null) Debug.LogError("[W2_WeaponBuilder] Bow clip missing — animator step hasn't run.");

            // Bow — primitive thin-cube visual, BasicShot ability spawns the pooled arrow.
            var bowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_BOW);
            EnsureWeapon(PATH_BOW, "Bow", bowPrefab, HumanBodyBones.LeftHand,
                         LEFTHAND_BOW_POS, LEFTHAND_BOW_EULER, LEFTHAND_BOW_SCALE,
                         bowClip, shotAbility);

            // Sword & Shield: dual-equip — sword in right hand, shield in left.
            var (swordWrapper, shieldWrapper) = EnsureSwordAndShieldWrappers();
            EnsureSwordShieldWeapon(swordWrapper, shieldWrapper, slashClip, slashAbility);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[W2_WeaponBuilder] 5 weapons + BasicSlash ability built. Tune attach transforms in Inspector if needed.");
        }

        // ── Sword & Shield (dual-equip) ──────────────────────────────────────

        private static (GameObject sword, GameObject shield) EnsureSwordAndShieldWrappers()
        {
            EnsureDir(Path.GetDirectoryName(PREFAB_SWORD_WRAPPER));
            EnsureDir(Path.GetDirectoryName(MAT_SWORD));

            // Bake axis conversion into mesh data — guarantees the mesh is oriented correctly
            // when we strip it out of the FBX and put it on a fresh GameObject (FBXs from
            // Maya/Blender often store axis-correction on the root transform; without bake,
            // extracting just the mesh leaves it rotated wrong → renders below the floor).
            EnsureBakedAxisConversion(SWORD_FBX);
            EnsureBakedAxisConversion(SHIELD_FBX);

            var swordAlbedo  = AssetDatabase.LoadAssetAtPath<Texture2D>(SWORD_ALBEDO);
            var shieldAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(SHIELD_ALBEDO);
            var shieldBump   = AssetDatabase.LoadAssetAtPath<Texture2D>(SHIELD_BUMP);
            ConfigureNormalMap(SHIELD_BUMP);

            var swordMat  = LoadOrCreateUrpMaterial(MAT_SWORD,  swordAlbedo,  null);
            var shieldMat = LoadOrCreateUrpMaterial(MAT_SHIELD, shieldAlbedo, shieldBump);

            var sword  = BuildWeaponWrapperPrefab(SWORD_FBX,  PREFAB_SWORD_WRAPPER,  "Sword",  swordMat);
            var shield = BuildWeaponWrapperPrefab(SHIELD_FBX, PREFAB_SHIELD_WRAPPER, "Shield", shieldMat);
            return (sword, shield);
        }

        private static void EnsureBakedAxisConversion(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;
            if (!importer.bakeAxisConversion)
            {
                importer.bakeAxisConversion = true;
                importer.SaveAndReimport();
            }
        }

        private static GameObject BuildWeaponWrapperPrefab(string fbxPath, string prefabPath, string rootName, Material mat)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null)
            {
                Debug.LogError($"[W2_WeaponBuilder] FBX not found at {fbxPath} — wrapper prefab not built.");
                return null;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                AssetDatabase.DeleteAsset(prefabPath);

            // Pull the largest mesh out of the FBX and bind it onto a fresh GameObject —
            // no nested prefab, no inherited transform from the FBX root, no chance of axis
            // weirdness. With bakeAxisConversion=on (set above), the mesh data is already
            // oriented for Unity's coordinate system.
            var meshes = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Mesh>().ToArray();
            Mesh chosen = PickLargestMesh(meshes);

            var root = new GameObject(rootName);
            try
            {
                if (chosen != null)
                {
                    // Normalize the mesh to ~1m on its longest axis so SO mainLocalScale (≈0.4
                    // for a half-meter weapon) is the only knob the artist needs. Crucially the
                    // autoScale lives on a CHILD transform — WeaponEquipment overrides the root's
                    // localScale on equip; if autoScale were on the root it would be wiped.
                    var sz = chosen.bounds.size;
                    float maxDim = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z));
                    float autoScale = (maxDim > 0.0001f) ? 1f / maxDim : 1f;

                    var meshGo = new GameObject("Mesh");
                    meshGo.transform.SetParent(root.transform, false);
                    meshGo.transform.localScale = new Vector3(autoScale, autoScale, autoScale);
                    // Re-center the mesh so the wrapper's pivot lands at the mesh's centroid in
                    // wrapper-local space. Stops off-center FBX pivots from yanking the weapon
                    // away from the hand bone.
                    meshGo.transform.localPosition = -chosen.bounds.center * autoScale;
                    var mf = meshGo.AddComponent<MeshFilter>();
                    mf.sharedMesh = chosen;
                    var mr = meshGo.AddComponent<MeshRenderer>();
                    if (mat != null) mr.sharedMaterial = mat;

                    Debug.Log($"[W2_WeaponBuilder] {rootName}: mesh '{chosen.name}', verts={chosen.vertexCount}, " +
                              $"raw bounds size={sz:F3}, autoScale={autoScale:F3} (on Mesh child, root stays at 1×). " +
                              $"Material={(mat != null ? mat.name : "<none>")}.");
                }
                else
                {
                    // Fallback: magenta debug cube. If you see this in-engine, the FBX
                    // didn't yield any mesh sub-asset — open the FBX importer and check
                    // for import errors.
                    Debug.LogError($"[W2_WeaponBuilder] {fbxPath} contains no Mesh sub-asset — using debug cube placeholder.");
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.transform.SetParent(root.transform, false);
                    cube.transform.localScale = new Vector3(0.1f, 0.5f, 0.1f);
                    Object.DestroyImmediate(cube.GetComponent<Collider>());
                    var debugMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    debugMat.color = Color.magenta;
                    cube.GetComponent<MeshRenderer>().sharedMaterial = debugMat;
                }

                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Mesh PickLargestMesh(Mesh[] meshes)
        {
            Mesh best = null;
            int bestVerts = -1;
            foreach (var m in meshes)
            {
                if (m == null) continue;
                if (m.vertexCount > bestVerts) { best = m; bestVerts = m.vertexCount; }
            }
            return best;
        }

        private static WeaponDefinitionSO EnsureSwordShieldWeapon(
            GameObject swordWrapper, GameObject shieldWrapper,
            AnimationClip attackClip, AbilityDefinitionSO ability)
        {
            var w = AssetDatabase.LoadAssetAtPath<WeaponDefinitionSO>(PATH_SWORD_SHIELD);
            bool firstRun = w == null;
            if (firstRun)
            {
                w = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
                AssetDatabase.CreateAsset(w, PATH_SWORD_SHIELD);
            }

            w.displayName = "Sword & Shield";

            // Main hand: sword (right hand). Off-hand: shield (left hand).
            w.mainPrefab    = swordWrapper;
            w.mainHand      = HumanBodyBones.RightHand;
            w.offHandPrefab = shieldWrapper;
            w.offHand       = HumanBodyBones.LeftHand;

            // First-run defaults — won't trample manual tuning on rebuild.
            if (firstRun)
            {
                w.mainLocalPos      = RIGHTHAND_POS;
                w.mainLocalEuler    = RIGHTHAND_EULER;
                w.mainLocalScale    = Vector3.one * 0.4f;
                w.offHandLocalPos   = new Vector3(0f, 0.05f, 0.02f);
                w.offHandLocalEuler = new Vector3(-90f, 90f, 90f);
                w.offHandLocalScale = Vector3.one * 0.4f;
            }

            w.attackClip = attackClip;
            w.ability    = ability;

            EditorUtility.SetDirty(w);
            return w;
        }

        // ── Material helpers ─────────────────────────────────────────────────

        private static Material LoadOrCreateUrpMaterial(string path, Texture2D baseMap, Texture2D bumpMap)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                EnsureDir(Path.GetDirectoryName(path));
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                var urpLit = Shader.Find("Universal Render Pipeline/Lit");
                if (urpLit != null && mat.shader != urpLit) mat.shader = urpLit;
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (baseMap != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", baseMap);
            if (baseMap != null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", baseMap);
            if (bumpMap != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", bumpMap);
                mat.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Forces a PNG to be imported as a NormalMap so URP/Lit's _BumpMap reads it correctly.</summary>
        private static void ConfigureNormalMap(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            if (importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }

        private static AbilityDefinitionSO EnsureSlashAbility()
        {
            var ab = AssetDatabase.LoadAssetAtPath<AbilityDefinitionSO>(PATH_SLASH_AB);
            if (ab == null)
            {
                ab = ScriptableObject.CreateInstance<AbilityDefinitionSO>();
                AssetDatabase.CreateAsset(ab, PATH_SLASH_AB);
            }
            ab.displayName       = "Slash";
            ab.description       = "Phantom Archer's basic melee swing. Hits everything in front of her at short range.";
            ab.damage            = 14f;
            ab.range             = 2.5f;          // melee reach in metres
            ab.cooldown          = 0.5f;
            ab.celestiaCost      = 0f;
            ab.damageType        = AssetDatabase.LoadAssetAtPath<DamageTypeSO>(PATH_DMG_PHYS);
            ab.animatorStateName = W2_AnimatorPatcher.ATTACK_STATE_NAME;
            ab.animatorLayer     = 1;             // UpperBody layer
            ab.projectilePrefab  = null;          // null = melee path in PlayerCombat

            // Per-ability cast behaviour (read by PlayerCombat):
            //   * faceAimOnCast = false → don't snap the body toward the cursor on click;
            //     the swing follows the player's current movement-facing instead.
            //   * aimYawOffset stays 0 (unused while faceAimOnCast is false).
            //   * movementSlowFactor = 0 → freeze in place during the swing's cooldown
            //     so the player commits to the strike.
            ab.faceAimOnCast      = false;
            ab.aimYawOffset       = 0f;
            ab.movementSlowFactor = 0f;

            EditorUtility.SetDirty(ab);
            return ab;
        }

        private static WeaponDefinitionSO EnsureWeapon(
            string path, string display, GameObject prefab, HumanBodyBones hand,
            Vector3 pos, Vector3 euler, Vector3 scale,
            AnimationClip attackClip, AbilityDefinitionSO ability)
        {
            var w = AssetDatabase.LoadAssetAtPath<WeaponDefinitionSO>(path);
            bool firstRun = w == null;
            if (firstRun)
            {
                w = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
                AssetDatabase.CreateAsset(w, path);
            }

            w.displayName    = display;
            w.mainPrefab     = prefab;
            w.mainHand       = hand;
            // Only set transform defaults on first creation — avoid trampling artist hand-tuning.
            if (firstRun)
            {
                w.mainLocalPos   = pos;
                w.mainLocalEuler = euler;
                w.mainLocalScale = scale;
            }
            w.attackClip     = attackClip;
            w.ability        = ability;

            EditorUtility.SetDirty(w);
            return w;
        }

        private static void EnsureDir(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }
}
