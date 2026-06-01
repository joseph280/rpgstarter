using UnityEditor;
using UnityEngine;

namespace Celestia.EditorTools
{
    /// <summary>
    /// Adds Celestia gameplay layers to the project + configures the physics
    /// collision matrix so projectiles only interact with intended layers.
    ///
    /// Idempotent — safe to re-run.
    /// </summary>
    public static class W2_LayerSetup
    {
        // Layer slots 8-12 are the first available after Unity's reserved 0-7.
        public const int LAYER_PLAYER             = 8;
        public const int LAYER_PLAYER_PROJECTILE  = 9;
        public const int LAYER_ENEMY              = 10;
        public const int LAYER_ENEMY_PROJECTILE   = 11;
        public const int LAYER_ENVIRONMENT        = 12;

        private static readonly (int index, string name)[] LAYERS =
        {
            (LAYER_PLAYER,            "Player"),
            (LAYER_PLAYER_PROJECTILE, "PlayerProjectile"),
            (LAYER_ENEMY,             "Enemy"),
            (LAYER_ENEMY_PROJECTILE,  "EnemyProjectile"),
            (LAYER_ENVIRONMENT,       "Environment"),
        };

        [MenuItem("Celestia/W2/0 - Configure Layers + Physics")]
        public static void Configure()
        {
            EnsureLayerNames();
            ConfigureCollisionMatrix();
            Debug.Log("[W2_LayerSetup] Layers + collision matrix configured.");
        }

        private static void EnsureLayerNames()
        {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            if (layers == null || !layers.isArray)
            {
                Debug.LogError("[W2_LayerSetup] Could not access TagManager.layers.");
                return;
            }

            foreach (var (index, name) in LAYERS)
            {
                var slot = layers.GetArrayElementAtIndex(index);
                if (slot.stringValue != name)
                {
                    slot.stringValue = name;
                }
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureCollisionMatrix()
        {
            // Reset everything between the new layers, then explicitly enable wanted pairs.
            // Layers vs themselves and the rest of the project default behaviour are left alone.
            int[] all = { LAYER_PLAYER, LAYER_PLAYER_PROJECTILE, LAYER_ENEMY,
                          LAYER_ENEMY_PROJECTILE, LAYER_ENVIRONMENT };

            // Disable all interactions among our new layers first
            foreach (int a in all)
                foreach (int b in all)
                    Physics.IgnoreLayerCollision(a, b, true);

            // Enable specific desired interactions:
            //  - Player           ↔ Environment, Enemy, EnemyProjectile
            //  - PlayerProjectile ↔ Enemy, Environment
            //  - Enemy            ↔ Environment
            //  - EnemyProjectile  ↔ Environment
            EnablePair(LAYER_PLAYER,            LAYER_ENVIRONMENT);
            EnablePair(LAYER_PLAYER,            LAYER_ENEMY);
            EnablePair(LAYER_PLAYER,            LAYER_ENEMY_PROJECTILE);
            EnablePair(LAYER_PLAYER_PROJECTILE, LAYER_ENEMY);
            EnablePair(LAYER_PLAYER_PROJECTILE, LAYER_ENVIRONMENT);
            EnablePair(LAYER_ENEMY,             LAYER_ENVIRONMENT);
            EnablePair(LAYER_ENEMY_PROJECTILE,  LAYER_ENVIRONMENT);
        }

        private static void EnablePair(int a, int b)
        {
            Physics.IgnoreLayerCollision(a, b, false);
        }
    }
}
