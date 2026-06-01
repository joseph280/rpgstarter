using UnityEngine;

namespace RPGStarter.Data
{
    /// <summary>
    /// One equippable weapon. Per CLAUDE.md §4 — data is data.
    ///
    /// Holds:
    ///   * the visual prefab + which humanoid bone it attaches to + a local TRS so
    ///     the artist can tune the grip in Prefab Mode without touching code;
    ///   * the AnimationClip that should drive the Animator's "Attack" state when
    ///     this weapon is equipped (swapped via AnimatorOverrideController at runtime);
    ///   * the ability fired on basic attack (LMB). The ability's projectilePrefab
    ///     decides ranged vs. melee — non-null = spawn a projectile, null = overlap-sweep
    ///     in front of the player. No combat-mode enum needed.
    /// </summary>
    [CreateAssetMenu(menuName = "RPGStarter/Weapon", fileName = "Weapon_New")]
    public sealed class WeaponDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        public string displayName;

        [Header("Visual")]
        [Tooltip("Mesh prefab spawned on the configured hand bone when equipped.")]
        public GameObject mainPrefab;

        [Tooltip("Bone the weapon hangs off. RightHand for swords/axes/maces/spears, LeftHand for bows.")]
        public HumanBodyBones mainHand = HumanBodyBones.RightHand;

        [Tooltip("Local position of the weapon prop relative to its hand bone — tune in Prefab Mode.")]
        public Vector3 mainLocalPos = Vector3.zero;

        [Tooltip("Local euler rotation of the weapon prop. (-90, -90, 0) is a common Mixamo grip orientation.")]
        public Vector3 mainLocalEuler = Vector3.zero;

        [Tooltip("Local scale of the weapon prop. 0.5–1.0 is typical for the Blink RPG weapons pack.")]
        public Vector3 mainLocalScale = Vector3.one;

        [Header("Off-hand (optional — e.g., shield, dagger). Leave prefab null for one-handed weapons.")]
        public GameObject offHandPrefab;

        [Tooltip("Bone the off-hand prop hangs off. Typically LeftHand for a shield when the main weapon is in the right hand.")]
        public HumanBodyBones offHand = HumanBodyBones.LeftHand;

        public Vector3 offHandLocalPos   = Vector3.zero;
        public Vector3 offHandLocalEuler = Vector3.zero;
        public Vector3 offHandLocalScale = Vector3.one;

        [Header("Animation")]
        [Tooltip("Clip the Animator's Attack state plays when this weapon is equipped. Hot-swapped via " +
                 "AnimatorOverrideController so we don't need a state per weapon.")]
        public AnimationClip attackClip;

        [Header("Combat")]
        [Tooltip("Ability fired on LMB while this weapon is equipped. " +
                 "If its projectilePrefab is non-null, the attack is RANGED (PlayerCombat spawns the projectile). " +
                 "Otherwise the attack is MELEE — PlayerCombat sweeps an overlap sphere in front of the player.")]
        public AbilityDefinitionSO ability;

        [Header("Tool")]
        [Tooltip("Optional tool tag. RockMineable etc. consult this to gate which weapons can " +
                 "damage them (e.g. only the Pickaxe breaks rocks). Null = combat weapon, no tool role.")]
        public ToolKindSO toolKind;
    }
}
