using RPGStarter.Combat;
using RPGStarter.Core.Events;
using RPGStarter.Data;
using UnityEngine;

namespace RPGStarter.Player
{
    /// <summary>
    /// Foundation health component. W1 scope: track HP, raise SO event channels on
    /// damage / heal / death. No damage application yet — Hitbox + DamageMessage pipeline
    /// lands in W2 with combat.
    /// </summary>
    public sealed class PlayerHealth : MonoBehaviour, IHealthSource
    {
        [Header("Refs")]
        [SerializeField] private ClassDefinitionSO classDefinition;

        [Header("Event Channels (optional)")]
        [SerializeField] private FloatEventChannelSO onHealthChanged01;
        [SerializeField] private VoidEventChannelSO  onDied;

        public int Current { get; private set; }
        public int Max     => classDefinition != null ? classDefinition.maxHealth : 1;
        public float Fraction01 => Max <= 0 ? 0f : (float)Current / Max;
        public bool  IsDead => Current <= 0;

        private void Awake()
        {
            if (classDefinition == null)
            {
                Debug.LogError("[PlayerHealth] classDefinition not assigned.");
                return;
            }
            Current = classDefinition.maxHealth;
        }

        public void TakeDamage(int amount)
        {
            if (IsDead || amount <= 0) return;
            Current = Mathf.Max(0, Current - amount);
            onHealthChanged01?.Raise(Fraction01);
            if (Current == 0) onDied?.Raise();
        }

        public void Heal(int amount)
        {
            if (IsDead || amount <= 0) return;
            Current = Mathf.Min(Max, Current + amount);
            onHealthChanged01?.Raise(Fraction01);
        }
    }
}
