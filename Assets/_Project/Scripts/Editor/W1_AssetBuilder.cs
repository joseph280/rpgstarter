using System.IO;
using RPGStarter.Core.Events;
using RPGStarter.Data;
using UnityEditor;
using UnityEngine;

namespace RPGStarter.EditorTools
{
    /// <summary>
    /// Creates the SO instances + materials needed by the W1 player setup. Idempotent.
    /// </summary>
    public static class W1_AssetBuilder
    {
        public const string CLASS_PATH       = "Assets/_Project/ScriptableObjects/Classes/Class_PhantomArcher.asset";
        public const string EV_PLAYER_DIED   = "Assets/_Project/ScriptableObjects/GameConfig/Event_PlayerDied.asset";
        public const string EV_HEALTH_CHG    = "Assets/_Project/ScriptableObjects/GameConfig/Event_PlayerHealthChanged01.asset";
        public const string M_TOON_PLACEHOLDER = "Assets/_Project/Art/Materials/M_PlayerToon_Placeholder.mat";
        public const string OUTLINE_SHADER_PATH = "Assets/_Project/Art/Shaders/Outline.shader";
        public const string TOON_SHADER_PATH    = "Assets/_Project/Art/Shaders/Toon.shader";

        // [MenuItem stripped — single entry point is RPGStarter/Build Demo]
        public static void BuildAssets()
        {
            var classSO = LoadOrCreate<ClassDefinitionSO>(CLASS_PATH, c =>
            {
                c.displayName    = "Phantom Archer";
                c.description    = "Ranged glass cannon. Cloak + bow. Dodge-heavy. Subclass branch TBD.";
                c.maxHealth      = 100;
                c.moveSpeed      = 6f;
                c.dodgeSpeed     = 14f;
                c.dodgeDuration  = 0.25f;
                c.dodgeCooldown  = 0.6f;
            });

            LoadOrCreate<VoidEventChannelSO>(EV_PLAYER_DIED, _ => { });
            LoadOrCreate<FloatEventChannelSO>(EV_HEALTH_CHG, _ => { });

            // Toon material on the placeholder (PlayerArmature). When the real Phantom Archer
            // arrives, swap the material's textures; the shader binding stays.
            var toonShader = AssetDatabase.LoadAssetAtPath<Shader>(TOON_SHADER_PATH);
            if (toonShader == null)
            {
                Debug.LogError($"[W1_AssetBuilder] Toon shader not found at {TOON_SHADER_PATH}. Skipping placeholder material.");
            }
            else
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(M_TOON_PLACEHOLDER);
                if (mat == null)
                {
                    EnsureDirectory(Path.GetDirectoryName(M_TOON_PLACEHOLDER));
                    mat = new Material(toonShader);
                    AssetDatabase.CreateAsset(mat, M_TOON_PLACEHOLDER);
                }
                else if (mat.shader != toonShader)
                {
                    mat.shader = toonShader;
                }
                mat.color = new Color(0.55f, 0.55f, 0.62f);
                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[W1_AssetBuilder] Assets built / refreshed.");
        }

        public static T LoadOrCreate<T>(string path, System.Action<T> initializer) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            EnsureDirectory(Path.GetDirectoryName(path));
            var so = ScriptableObject.CreateInstance<T>();
            initializer?.Invoke(so);
            AssetDatabase.CreateAsset(so, path);
            return so;
        }

        public static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }
}
