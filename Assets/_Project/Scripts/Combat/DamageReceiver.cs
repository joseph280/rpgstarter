using System;
using UnityEngine;

namespace Celestia.Combat
{
    /// <summary>
    /// Bridges DamageMessage → Health. Implements IDamageable so attackers don't need
    /// to know about Health directly. Sits on the same GameObject as Health (or a
    /// parent — searched on Awake).
    ///
    /// Resistance / armor logic goes here when we have it. For W2 it's straight
    /// pass-through.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class DamageReceiver : MonoBehaviour, IDamageable
    {
        public bool IsDead => _health != null && _health.IsDead;

        public event Action<DamageMessage> OnDamaged;

        private Health _health;

        private void Awake()
        {
            _health = GetComponent<Health>();
        }

        public void ApplyDamage(in DamageMessage message)
        {
            if (_health == null || _health.IsDead) return;

            int finalDamage = Mathf.Max(0, Mathf.RoundToInt(message.Amount));
            if (finalDamage <= 0) return;

            _health.Damage(finalDamage);
            OnDamaged?.Invoke(message);
        }
    }
}
