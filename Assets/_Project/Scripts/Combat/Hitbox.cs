using UnityEngine;

namespace Celestia.Combat
{
    /// <summary>
    /// Receiving-side collider tagged with an allegiance and pointing at a DamageReceiver.
    /// Attackers (projectiles, melee swings) raycast/sweep into hitboxes, then call
    /// <see cref="ReceiveDamage"/> with a populated DamageMessage. The hitbox forwards
    /// to its DamageReceiver — splitting the "where you can be hit" colliders from the
    /// "what owns the HP" component (multiple hitboxes, one receiver).
    ///
    /// Use Layer-based filtering (Player / Enemy / Environment) for hot-path collision
    /// rejection — see Project Settings → Physics → Layer Collision Matrix.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Hitbox : MonoBehaviour
    {
        public enum Allegiance { Player, Enemy, Neutral }

        [SerializeField] private Allegiance allegiance = Allegiance.Enemy;
        [SerializeField] private bool isActive = true;

        [Tooltip("Damage receiver this hitbox forwards to. If null, searched in parents on Awake.")]
        [SerializeField] private DamageReceiver receiver;

        [Tooltip("Damage multiplier (1 = body shot, 2 = headshot, etc.).")]
        [SerializeField, Min(0.1f)] private float damageMultiplier = 1f;

        public Allegiance Side => allegiance;
        public bool IsActive { get => isActive; set => isActive = value; }
        public DamageReceiver Receiver => receiver;

        private void Awake()
        {
            if (receiver == null) receiver = GetComponentInParent<DamageReceiver>();
        }

        /// <summary>
        /// Forward an inbound hit. Multiplier applied; receiver may be null if this
        /// hitbox is decorative (e.g., a destructible chunk that just plays VFX).
        /// </summary>
        public void ReceiveDamage(in DamageMessage message)
        {
            if (!isActive || receiver == null || receiver.IsDead) return;

            // Apply multiplier without allocating a new DamageMessage if multiplier is 1.
            if (Mathf.Approximately(damageMultiplier, 1f))
            {
                receiver.ApplyDamage(message);
                return;
            }

            var amplified = new DamageMessage(
                message.Attacker,
                message.Source,
                message.Amount * damageMultiplier,
                message.DamageType,
                message.HitPoint,
                message.HitNormal);
            receiver.ApplyDamage(amplified);
        }
    }
}
