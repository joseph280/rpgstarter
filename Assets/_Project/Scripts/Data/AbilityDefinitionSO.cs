using UnityEngine;

namespace RPGStarter.Data
{
    /// <summary>
    /// Data-only ability spec (CLAUDE.md §4). Cooldown ticks per runtime AbilityCaster
    /// instance — never on this asset. Animation is referenced by state-name hash, not
    /// by trigger, per CLAUDE.md §5 ("triggers desync — use Animator.Play").
    /// </summary>
    [CreateAssetMenu(menuName = "RPGStarter/Ability", fileName = "Ability_New")]
    public sealed class AbilityDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        public string displayName;

        [TextArea(2, 5)]
        public string description;

        [Header("Combat")]
        [Min(0f)] public float damage   = 10f;
        [Min(0f)] public float range    = 30f;       // for projectile lifetime + AI awareness
        [Min(0f)] public float cooldown = 0.6f;

        [Header("Cost (W3)")]
        [Min(0f)] public float celestiaCost = 0f;    // resource cost — wired in W3

        [Header("Damage Type")]
        public DamageTypeSO damageType;

        [Header("Animation")]
        [Tooltip("Animator state name to Play() when this ability fires (e.g., \"Attack\"). " +
                 "Hashed at runtime via Animator.StringToHash. Empty = no anim drive.")]
        public string animatorStateName;

        [Tooltip("Layer index in the AnimatorController.")]
        [Min(0)] public int animatorLayer = 0;

        [Header("Aim")]
        [Tooltip("Whether the player's body rotates to face the cursor on cast. " +
                 "True for ranged (the bow needs to aim). " +
                 "False for melee — keep the body facing wherever the player's moving so the " +
                 "swing reads naturally and stray cursor flicks don't snap the character around.")]
        public bool faceAimOnCast = true;

        [Tooltip("Yaw offset applied when faceAimOnCast = true. " +
                 "0 = chest at the cursor (default). " +
                 "90 = archer stance (left shoulder + bow toward target — needed for the bow).")]
        [Range(0f, 180f)] public float aimYawOffset = 0f;

        [Header("Player Movement")]
        [Tooltip("Multiplier applied to the player's walk speed AND rotation rate while this " +
                 "ability is on cooldown. 1 = no slow (snap-fire ranged), 0 = full freeze " +
                 "(commit to a melee swing). Per CLAUDE.md §5 the lock is just movement — " +
                 "dodge stays free as an escape option.")]
        [Range(0f, 1f)] public float movementSlowFactor = 1f;

        [Header("Refs")]
        public GameObject projectilePrefab; // for ranged abilities
        public AudioClip  sfxOnCast;
        public GameObject vfxOnCast;        // muzzle flash, bow flash, etc.

        [Header("Placement")]
        [Tooltip("If set, casting this ability PLACES this prefab at the aim point instead of " +
                 "doing damage. Used for placeable items like the crafting table.")]
        public GameObject placedPrefab;

        [Tooltip("Item consumed from the player's inventory each time the place action fires. " +
                 "Leave null for unlimited placement.")]
        public ItemDefinitionSO consumeOnPlace;
    }
}
