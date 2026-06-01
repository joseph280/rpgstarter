using System;
using UnityEngine;

namespace Celestia.Combat
{
    /// <summary>
    /// Generic health pool. Used by both player and enemies via composition
    /// (CLAUDE.md §5). Doesn't know about damage messages — that's DamageReceiver's
    /// job. Keeps Health reusable for non-combat HP (e.g., destructibles).
    /// </summary>
    public sealed class Health : MonoBehaviour, IHealthSource
    {
        [SerializeField, Min(1)] private int max = 100;
        [SerializeField, Min(0)] private int initial = -1;   // -1 = use Max at Awake

        public int  Max     { get; private set; }
        public int  Current { get; private set; }
        public bool IsDead  => Current <= 0;
        public float Fraction01 => Max <= 0 ? 0f : (float)Current / Max;

        public event Action<int, int> OnChanged; // (current, max)
        public event Action            OnDied;

        private void Awake()
        {
            Max     = max;
            Current = initial < 0 ? Max : Mathf.Min(initial, Max);
        }

        /// <summary>Set the max at runtime (e.g., level-up). Clamps current to new max.</summary>
        public void SetMax(int newMax, bool refill = false)
        {
            Max = Mathf.Max(1, newMax);
            Current = refill ? Max : Mathf.Min(Current, Max);
            OnChanged?.Invoke(Current, Max);
        }

        public void Damage(int amount)
        {
            if (IsDead || amount <= 0) return;
            Current = Mathf.Max(0, Current - amount);
            OnChanged?.Invoke(Current, Max);
            if (Current == 0) OnDied?.Invoke();
        }

        public void Heal(int amount)
        {
            if (IsDead || amount <= 0) return;
            Current = Mathf.Min(Max, Current + amount);
            OnChanged?.Invoke(Current, Max);
        }

        public void Revive(int healthAmount)
        {
            Current = Mathf.Clamp(healthAmount, 1, Max);
            OnChanged?.Invoke(Current, Max);
        }
    }
}
