namespace Celestia.Combat
{
    /// <summary>
    /// Anything that can take damage implements this. Interface dispatch keeps the
    /// damage pipeline decoupled from concrete enemy/player types and avoids
    /// allocation (no boxing on a struct DamageMessage).
    /// </summary>
    public interface IDamageable
    {
        /// <summary>True once the receiver has hit zero HP. Hit code should early-out.</summary>
        bool IsDead { get; }

        void ApplyDamage(in DamageMessage message);
    }
}
