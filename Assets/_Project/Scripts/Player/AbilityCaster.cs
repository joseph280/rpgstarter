using System;
using Celestia.Data;
using UnityEngine;

namespace Celestia.Player
{
    /// <summary>
    /// Owns slots 0-4 (slot 0 = basic attack, 1-4 = abilities). Tracks cooldowns
    /// per-instance — never on the SO (would corrupt the asset).
    ///
    /// PlayerCombat owns the spawning side; this component owns slot ↔ cooldown
    /// bookkeeping and raises an event when a slot is fired so the spawner runs.
    /// </summary>
    public sealed class AbilityCaster : MonoBehaviour
    {
        public const int SLOT_BASIC = 0;
        public const int SLOT_COUNT = 5; // 0 = basic, 1-4 = abilities

        [SerializeField] private ClassDefinitionSO classDefinition;

        // Per-instance cooldown remaining, indexed by slot.
        private readonly float[] _cooldownRemaining = new float[SLOT_COUNT];

        public event Action<int, AbilityDefinitionSO> OnAbilityFired;

        /// <summary>
        /// Optional per-weapon override for slot 0 (basic attack). Set by WeaponEquipment
        /// when a weapon is equipped — keeps the slot-0 ability tied to the held weapon
        /// rather than the class definition. Slots 1-4 stay class-driven.
        /// </summary>
        public AbilityDefinitionSO BasicOverride { get; set; }

        private void Update()
        {
            // Tick cooldowns down. Plain float subtraction — no GC.
            float dt = Time.deltaTime;
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                if (_cooldownRemaining[i] > 0f)
                    _cooldownRemaining[i] = Mathf.Max(0f, _cooldownRemaining[i] - dt);
            }
        }

        public bool IsReady(int slot) =>
            slot >= 0 && slot < SLOT_COUNT && _cooldownRemaining[slot] <= 0f;

        public float CooldownRemaining(int slot) =>
            slot >= 0 && slot < SLOT_COUNT ? _cooldownRemaining[slot] : 0f;

        /// <summary>
        /// Returns the SO for the requested slot, or null if unfilled (W3 ability slots).
        /// </summary>
        public AbilityDefinitionSO GetAbility(int slot)
        {
            if (slot == SLOT_BASIC)
            {
                if (BasicOverride != null) return BasicOverride;
                return classDefinition != null ? classDefinition.basicAttack : null;
            }
            if (classDefinition == null) return null;
            int abilityIndex = slot - 1;
            if (abilityIndex < 0 || classDefinition.abilities == null ||
                abilityIndex >= classDefinition.abilities.Length) return null;
            return classDefinition.abilities[abilityIndex];
        }

        /// <summary>
        /// Try to fire a slot. Returns true if the ability went off (cooldown was ready
        /// AND the slot has a definition). Sets the cooldown immediately on success.
        /// </summary>
        public bool TryCast(int slot)
        {
            if (!IsReady(slot)) return false;
            var ability = GetAbility(slot);
            if (ability == null) return false;

            _cooldownRemaining[slot] = ability.cooldown;
            OnAbilityFired?.Invoke(slot, ability);
            return true;
        }
    }
}
