using System;
using System.Collections.Generic;
using Celestia.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Celestia.Player
{
    /// <summary>
    /// Owns the player's weapon roster + which one is currently equipped.
    /// On equip:
    ///   1. tears down the previous weapon visual,
    ///   2. spawns the new weapon prefab on its configured hand bone,
    ///   3. hot-swaps the Animator's Attack-state clip via AnimatorOverrideController,
    ///   4. sets <see cref="AbilityCaster.BasicOverride"/> so LMB fires this weapon's ability.
    ///
    /// Input: number keys 1-N select directly, mouse scroll cycles. Reads
    /// <see cref="Keyboard.current"/> / <see cref="Mouse.current"/> directly so we don't
    /// need to maintain weapon-switch entries in the InputActionAsset.
    /// </summary>
    public sealed class WeaponEquipment : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Animator animator;
        [SerializeField] private AbilityCaster caster;

        [Header("Roster")]
        [Tooltip("Equippable weapons in order. Slot 1 = first, etc. Player starts with slot 0 equipped.")]
        [SerializeField] private List<WeaponDefinitionSO> weapons = new();

        [Header("Animator Override")]
        [Tooltip("The placeholder AnimationClip currently set on the Animator's Attack state. " +
                 "WeaponEquipment overrides THIS clip per equipped weapon. Set by W2_PlayerPrefabPatcher " +
                 "to match what W2_AnimatorPatcher put on the Attack state.")]
        [SerializeField] private AnimationClip attackPlaceholderClip;

        [Header("Behaviour")]
        [Tooltip("Layer applied to spawned weapon visuals (so they don't get hit by friendly projectiles).")]
        [SerializeField] private string weaponLayer = "Player";

        [Tooltip("Scroll deltas below this magnitude are ignored — Unity's mouse scroll value is unitless and OS-dependent.")]
        [SerializeField, Min(1f)] private float scrollDeadzone = 10f;

        public WeaponDefinitionSO CurrentWeapon => _currentWeapon;
        public int CurrentIndex => _currentIndex;
        public event Action<WeaponDefinitionSO> OnEquipped;

        private int _currentIndex = -1;
        private WeaponDefinitionSO _currentWeapon;
        private GameObject _currentMainProp;
        private GameObject _currentOffHandProp;
        private AnimatorOverrideController _override;

        private void Awake()
        {
            if (animator == null) Debug.LogError("[WeaponEquipment] animator not assigned.");
            if (caster   == null) Debug.LogError("[WeaponEquipment] caster not assigned.");

            // Wrap the runtime animator controller in an override so we can hot-swap
            // the Attack state's clip per weapon. One-time at Awake — calling it again
            // would discard previous overrides.
            if (animator != null && animator.runtimeAnimatorController != null
                && !(animator.runtimeAnimatorController is AnimatorOverrideController))
            {
                _override = new AnimatorOverrideController(animator.runtimeAnimatorController);
                animator.runtimeAnimatorController = _override;
            }
            else
            {
                _override = animator != null
                    ? animator.runtimeAnimatorController as AnimatorOverrideController
                    : null;
            }
        }

        private void Start()
        {
            // Equip slot 0 if any weapon is configured. Done in Start (not Awake) so
            // the Animator has rebound its avatar before we look up bone transforms.
            if (weapons.Count > 0) Equip(0);
        }

        private void Update()
        {
            // Digit-key equip moved to Celestia.Player.Hotbar so it can read from the
            // inventory's hotbar row + obey item-driven linkedWeapons. Scroll-wheel cycle
            // stays here as a quick shortcut through the static roster.
            if (Mouse.current != null)
            {
                Vector2 scroll = Mouse.current.scroll.ReadValue();
                if (scroll.y >  scrollDeadzone) Cycle(+1);
                if (scroll.y < -scrollDeadzone) Cycle(-1);
            }
        }

        public void Equip(int index)
        {
            if (index < 0 || index >= weapons.Count) return;
            if (index == _currentIndex) return;

            var def = weapons[index];
            if (def == null)
            {
                Debug.LogWarning($"[WeaponEquipment] Slot {index} is null — skipping.");
                return;
            }

            EquipDefInternal(def);
            _currentIndex = index;
        }

        /// <summary>
        /// Equip any weapon SO directly — bypasses the legacy roster index. Used by the
        /// Hotbar component, which derives the equipped weapon from the player's inventory
        /// rather than from the fixed <see cref="weapons"/> list.
        /// </summary>
        public void EquipDef(WeaponDefinitionSO def)
        {
            if (def == null) return;
            if (def == CurrentWeapon) return;
            EquipDefInternal(def);
            _currentIndex = -1; // not in roster
        }

        public void Unequip()
        {
            TearDownCurrent();
            ApplyAbilityToCaster(null);
            _currentIndex = -1;
            OnEquipped?.Invoke(null);
        }

        private void EquipDefInternal(WeaponDefinitionSO def)
        {
            TearDownCurrent();
            SpawnWeaponProp(def);
            ApplyAttackClipOverride(def);
            ApplyAbilityToCaster(def);
            _currentWeapon = def;
            OnEquipped?.Invoke(def);
            Debug.Log($"[WeaponEquipment] Equipped {def.displayName} (ability {(def.ability != null ? def.ability.name : "null")}).");
        }

        public void Cycle(int delta)
        {
            if (weapons.Count == 0) return;
            int next = (_currentIndex + delta + weapons.Count) % weapons.Count;
            Equip(next);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private void TearDownCurrent()
        {
            if (_currentMainProp    != null) { Destroy(_currentMainProp);    _currentMainProp    = null; }
            if (_currentOffHandProp != null) { Destroy(_currentOffHandProp); _currentOffHandProp = null; }
            _currentWeapon = null;
        }

        private void SpawnWeaponProp(WeaponDefinitionSO def)
        {
            _currentMainProp    = SpawnHandProp(def.mainPrefab,    def.mainHand,
                                                def.mainLocalPos,    def.mainLocalEuler,    def.mainLocalScale,
                                                "main",  def.displayName);
            _currentOffHandProp = SpawnHandProp(def.offHandPrefab, def.offHand,
                                                def.offHandLocalPos, def.offHandLocalEuler, def.offHandLocalScale,
                                                "offhand", def.displayName);
        }

        private GameObject SpawnHandProp(GameObject prefab, HumanBodyBones bone,
                                         Vector3 pos, Vector3 euler, Vector3 scale,
                                         string slot, string weaponName)
        {
            if (prefab == null) return null;
            if (animator == null || !animator.isHuman) return null;

            var hand = animator.GetBoneTransform(bone);
            if (hand == null)
            {
                Debug.LogWarning($"[WeaponEquipment] {bone} bone not found on avatar — {weaponName} {slot} prop will not be visible.");
                return null;
            }

            var go = Instantiate(prefab, hand);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale    = scale;

            // Strip every collider (visual prop, not a physics object) and put it on the Player
            // layer so player projectiles don't self-hit.
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                Destroy(col);

            int layer = LayerMask.NameToLayer(weaponLayer);
            if (layer >= 0) SetLayerRecursively(go, layer);

            return go;
        }

        private void ApplyAttackClipOverride(WeaponDefinitionSO def)
        {
            if (_override == null || attackPlaceholderClip == null || def.attackClip == null) return;
            // The override controller maps source clips → override clips by reference.
            // Setting the placeholder's override redirects every state that uses it.
            _override[attackPlaceholderClip] = def.attackClip;
        }

        private void ApplyAbilityToCaster(WeaponDefinitionSO def)
        {
            if (caster == null) return;
            // def is null when called from Unequip — clear the override so the basic
            // attack falls back to the class definition (or no-op if neither exists).
            caster.BasicOverride = def != null ? def.ability : null;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
