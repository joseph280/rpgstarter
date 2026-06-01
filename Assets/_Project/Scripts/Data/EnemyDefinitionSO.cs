using UnityEngine;

namespace RPGStarter.Data
{
    /// <summary>
    /// Per CLAUDE.md §4: enemy stats are SOs. Resistances by damage type, base HP,
    /// damage output (when AI lands in W5+). MVP starter values; designer tunes later.
    ///
    /// Resistance map: simple parallel arrays. For MVP we won't have many damage
    /// types, so arrays beat dictionaries (no GC, easier to author in Inspector).
    /// </summary>
    [CreateAssetMenu(menuName = "RPGStarter/Enemy", fileName = "Enemy_New")]
    public sealed class EnemyDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        public string displayName;

        [TextArea(2, 5)]
        public string description;

        [Header("Combat")]
        [Min(1)]    public int   maxHealth        = 30;
        [Min(0f)]   public float baseDamage       = 5f;     // when AI starts attacking (W5+)
        [Min(0f)]   public float baseAttackRate   = 1f;     // attacks per second

        [Header("AI (used by MonsterAI; ignored by passive enemies)")]
        [Tooltip("Walk/chase speed in m/s. Should be < player.moveSpeed for the player to outrun the monster.")]
        [Min(0f)]   public float moveSpeed        = 3.5f;
        [Tooltip("Player must enter this radius for the monster to start chasing.")]
        [Min(0f)]   public float detectionRadius  = 12f;
        [Tooltip("Distance at which the monster gives up the chase and returns to idle. " +
                 "Should be larger than detectionRadius — typically 2-3× — so the player " +
                 "can outrun the monster but not by walking 1m past the detection edge.")]
        [Min(0f)]   public float loseInterestRadius = 24f;
        [Tooltip("Once the player is this close, the monster switches from chase to attack.")]
        [Min(0f)]   public float attackRange      = 1.8f;
        [Tooltip("Seconds from attack start to damage application — synced to the swing animation.")]
        [Min(0f)]   public float attackWindup     = 0.35f;
        [Tooltip("Total seconds of the attack animation before the monster can move/attack again.")]
        [Min(0.05f)] public float attackDuration  = 0.85f;
        [Tooltip("Seconds the death animation takes before the GameObject despawns.")]
        [Min(0.05f)] public float deathDuration   = 2.5f;

        [Header("Resistances")]
        [Tooltip("Damage types this enemy resists. Parallel to resistMultipliers.")]
        public DamageTypeSO[] resistTypes;

        [Tooltip("Multiplier per resistType (1 = full damage, 0 = immune, 2 = double).")]
        public float[] resistMultipliers;

        [Header("Refs")]
        public GameObject prefab;       // visual prefab (mesh + collider)
        public AudioClip  hitSound;
        public AudioClip  deathSound;

        /// <summary>
        /// Returns the damage multiplier for the given type (1 if no entry).
        /// </summary>
        public float GetMultiplier(DamageTypeSO type)
        {
            if (type == null || resistTypes == null) return 1f;
            for (int i = 0; i < resistTypes.Length; i++)
            {
                if (resistTypes[i] == type)
                {
                    return i < (resistMultipliers?.Length ?? 0) ? resistMultipliers[i] : 1f;
                }
            }
            return 1f;
        }
    }
}
