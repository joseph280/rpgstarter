using System.IO;
using System.Linq;
using RPGStarter.Combat;
using RPGStarter.Data;
using RPGStarter.Enemies;
using RPGStarter.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace RPGStarter.EditorTools
{
    /// <summary>
    /// Creates the W2 combat assets:
    ///  - Damage type SOs (Physical, RPGStarter)
    ///  - Ability SO (Phantom Archer basic shot)
    ///  - Enemy SO (TargetDummy)
    ///  - Arrow prefab (Projectile component on a small cylinder)
    ///  - TargetDummy prefab (capsule + Health + DamageReceiver + Hitbox + HitFlash)
    ///  - DamageNumber prefab (TextMeshPro 3D text)
    ///
    /// Wires the basic attack onto Class_PhantomArcher so PlayerCombat picks it up.
    /// Idempotent.
    /// </summary>
    public static class W2_AssetBuilder
    {
        // SOs
        public const string DT_PHYSICAL  = "Assets/_Project/ScriptableObjects/GameConfig/DamageType_Physical.asset";
        public const string DT_CELESTIA  = "Assets/_Project/ScriptableObjects/GameConfig/DamageType_RPGStarter.asset";
        public const string ABILITY_BASIC = "Assets/_Project/ScriptableObjects/Abilities/Ability_PhantomArcher_BasicShot.asset";
        public const string ENEMY_DUMMY  = "Assets/_Project/ScriptableObjects/Enemies/Enemy_TargetDummy.asset";

        // Prefabs
        public const string PREFAB_ARROW   = "Assets/_Project/Prefabs/VFX/Arrow.prefab";
        public const string PREFAB_DUMMY   = "Assets/_Project/Prefabs/Enemies/TargetDummy.prefab";
        public const string PREFAB_DAMAGE_NUMBER = "Assets/_Project/Prefabs/UI/DamageNumber.prefab";

        // Materials
        public const string MAT_ARROW = "Assets/_Project/Art/Materials/M_Arrow.mat";
        public const string MAT_DUMMY = "Assets/_Project/Art/Materials/M_TargetDummy.mat";

        // [MenuItem stripped — single entry point is RPGStarter/Build Demo]
        public static void Build()
        {
            EnsureDir(Path.GetDirectoryName(DT_PHYSICAL));
            EnsureDir(Path.GetDirectoryName(ABILITY_BASIC));
            EnsureDir(Path.GetDirectoryName(ENEMY_DUMMY));
            EnsureDir(Path.GetDirectoryName(PREFAB_ARROW));
            EnsureDir(Path.GetDirectoryName(PREFAB_DUMMY));
            EnsureDir(Path.GetDirectoryName(PREFAB_DAMAGE_NUMBER));

            var dtPhysical = LoadOrCreate<DamageTypeSO>(DT_PHYSICAL, dt =>
            {
                dt.displayName  = "Physical";
                dt.description  = "Plain bow shots, melee impacts. No special interactions.";
                dt.displayColor = new Color(1f, 0.95f, 0.85f);
            });
            LoadOrCreate<DamageTypeSO>(DT_CELESTIA, dt =>
            {
                dt.displayName  = "RPGStarter";
                dt.description  = "Energy from the rift. Glows teal. Used by abilities (W3+).";
                dt.displayColor = new Color(0.36f, 0.91f, 0.77f);
            });

            // Build prefabs first (ability needs the arrow ref).
            var arrowPrefab = BuildArrowPrefab(dtPhysical);
            var dummyPrefab = BuildTargetDummyPrefab();
            var damageNumberPrefab = BuildDamageNumberPrefab();

            var basicShot = LoadOrCreate<AbilityDefinitionSO>(ABILITY_BASIC, a =>
            {
                a.displayName    = "Quick Shot";
                a.description    = "Phantom Archer's basic bow attack. Snap-fire arrow at the cursor.";
                a.damage         = 12f;
                a.range          = 30f;
                a.cooldown       = 0.4f;
                a.damageType     = dtPhysical;
                a.animatorStateName = "Attack";  // weapon-neutral state on the UpperBody layer
                a.animatorLayer  = 1;            // UpperBody layer
            });
            // Always refresh runtime ref (might have been re-built above) + the bow's cast
            // tunables. PlayerCombat reads these directly from the ability now, so pre-
            // existing assets need a forced re-apply rather than relying on LoadOrCreate's
            // first-run-only initialiser.
            basicShot.projectilePrefab    = arrowPrefab;
            basicShot.faceAimOnCast       = true;   // bow turns to face the cursor
            basicShot.aimYawOffset        = 90f;    // archer stance (left shoulder toward target)
            basicShot.movementSlowFactor  = 1f;     // snap-fire — no movement lock
            EditorUtility.SetDirty(basicShot);

            // Wire onto Class_PhantomArcher
            var classSO = AssetDatabase.LoadAssetAtPath<ClassDefinitionSO>(W1_AssetBuilder.CLASS_PATH);
            if (classSO != null)
            {
                classSO.basicAttack = basicShot;
                if (classSO.abilities == null || classSO.abilities.Length != 4)
                    classSO.abilities = new AbilityDefinitionSO[4];
                EditorUtility.SetDirty(classSO);
            }
            else
            {
                Debug.LogWarning($"[W2_AssetBuilder] Class_PhantomArcher missing at {W1_AssetBuilder.CLASS_PATH}; basic attack not wired.");
            }

            var enemyDummy = LoadOrCreate<EnemyDefinitionSO>(ENEMY_DUMMY, e =>
            {
                e.displayName = "Target Dummy";
                e.description = "Stationary practice target. No AI.";
                e.maxHealth   = 60;
                e.baseDamage  = 0f;
                e.baseAttackRate = 0f;
            });
            enemyDummy.prefab = dummyPrefab;
            EditorUtility.SetDirty(enemyDummy);

            // Re-bind dummy to its SO so runtime stats persist
            BindDummyToDefinition(dummyPrefab, enemyDummy);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[W2_AssetBuilder] Built damage types, abilities, enemies, prefabs.");
        }

        // ── Prefab builders ──────────────────────────────────────────────────

        private static GameObject BuildArrowPrefab(DamageTypeSO physicalType)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_ARROW);
            if (existing != null) AssetDatabase.DeleteAsset(PREFAB_ARROW);

            // Root: empty GO. Forward direction = travel direction.
            // Visual: child cylinder rotated 90° on X so its length aligns with parent's +Z.
            var go = new GameObject("Arrow");
            try
            {
                go.layer = W2_LayerSetup.LAYER_PLAYER_PROJECTILE;

                var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                visual.name = "Visual";
                Object.DestroyImmediate(visual.GetComponent<CapsuleCollider>()); // no collider on visual
                visual.transform.SetParent(go.transform, false);
                visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                visual.transform.localScale    = new Vector3(0.06f, 0.25f, 0.06f);

                var mat = LoadOrCreateMaterial(MAT_ARROW, new Color(0.7f, 0.5f, 0.3f));
                visual.GetComponent<MeshRenderer>().sharedMaterial = mat;

                var projectile = go.AddComponent<Projectile>();
                SetSerialized(projectile, "damageType", physicalType);

                var prefab = PrefabUtility.SaveAsPrefabAsset(go, PREFAB_ARROW);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static GameObject BuildTargetDummyPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_DUMMY);
            if (existing != null) AssetDatabase.DeleteAsset(PREFAB_DUMMY);

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                go.name = "TargetDummy";
                go.layer = W2_LayerSetup.LAYER_ENEMY;

                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                var mat = LoadOrCreateMaterial(MAT_DUMMY, new Color(0.55f, 0.55f, 0.6f));
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;

                go.AddComponent<Health>();
                go.AddComponent<DamageReceiver>();
                var hitbox = go.AddComponent<Hitbox>();
                SetSerialized(hitbox, "allegiance", (int)Hitbox.Allegiance.Enemy);
                go.AddComponent<HitFlash>();
                go.AddComponent<TargetDummy>();

                var prefab = PrefabUtility.SaveAsPrefabAsset(go, PREFAB_DUMMY);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static GameObject BuildDamageNumberPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_DAMAGE_NUMBER);
            if (existing != null) AssetDatabase.DeleteAsset(PREFAB_DAMAGE_NUMBER);

            var go = new GameObject("DamageNumber");
            try
            {
                var tmpGo = new GameObject("Text");
                tmpGo.transform.SetParent(go.transform, false);
                var tmp = tmpGo.AddComponent<TextMeshPro>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontSize  = 4f;
                tmp.text      = "0";
                tmp.color     = Color.white;

                var rect = tmpGo.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(2f, 1f);

                var dn = go.AddComponent<DamageNumber>();
                SetSerialized(dn, "tmp", tmp);

                var prefab = PrefabUtility.SaveAsPrefabAsset(go, PREFAB_DAMAGE_NUMBER);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void BindDummyToDefinition(GameObject dummyPrefab, EnemyDefinitionSO enemy)
        {
            if (dummyPrefab == null) return;
            using var scope = new PrefabUtility.EditPrefabContentsScope(AssetDatabase.GetAssetPath(dummyPrefab));
            var dummy = scope.prefabContentsRoot.GetComponent<TargetDummy>();
            if (dummy != null) SetSerialized(dummy, "definition", enemy);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static Material LoadOrCreateMaterial(string path, Color color)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                EnsureDir(Path.GetDirectoryName(path));
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static T LoadOrCreate<T>(string path, System.Action<T> initializer) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            EnsureDir(Path.GetDirectoryName(path));
            var so = ScriptableObject.CreateInstance<T>();
            initializer?.Invoke(so);
            AssetDatabase.CreateAsset(so, path);
            return so;
        }

        private static void SetSerialized(Object target, string fieldName, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"Field '{fieldName}' not found on {target}"); return; }

            switch (value)
            {
                case Object obj:    prop.objectReferenceValue = obj; break;
                case int i:         prop.intValue             = i;   break;
                case float f:       prop.floatValue           = f;   break;
                case bool b:        prop.boolValue            = b;   break;
                case string s:      prop.stringValue          = s;   break;
                default:
                    Debug.LogWarning($"SetSerialized: unsupported type {value?.GetType()}");
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
