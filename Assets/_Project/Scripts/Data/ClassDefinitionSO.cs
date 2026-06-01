using UnityEngine;

namespace RPGStarter.Data
{
    /// <summary>
    /// Per CLAUDE.md §4: data is data. All tunable class stats live here.
    ///
    /// MVP scope: Phantom Archer is the sole class. Subclasses unlock at level 125
    /// (post-MVP, MVP cap is 20). No branch indirection.
    /// </summary>
    [CreateAssetMenu(menuName = "RPGStarter/Class", fileName = "Class_New")]
    public sealed class ClassDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        public string displayName;

        [TextArea(2, 6)]
        public string description;

        [Header("Base Stats")]
        [Min(1)]    public int   maxHealth        = 100;
        [Min(0.1f)] public float moveSpeed        = 6f;
        [Min(0.1f)] public float dodgeSpeed       = 14f;
        [Min(0.05f)]public float dodgeDuration    = 0.25f;
        [Min(0.1f)] public float dodgeCooldown    = 0.6f;

        [Header("Animator")]
        [Tooltip("AnimatorController used by the player prefab for this class.")]
        public RuntimeAnimatorController animatorController;

        [Header("Combat")]
        [Tooltip("Slot 0 — fires on BasicAttack input (LMB / right trigger).")]
        public AbilityDefinitionSO basicAttack;

        [Tooltip("Slots 1-4 — fire on Ability1-4 inputs. Spec'd in W3 once designer locks names.")]
        public AbilityDefinitionSO[] abilities = new AbilityDefinitionSO[4];
    }
}
