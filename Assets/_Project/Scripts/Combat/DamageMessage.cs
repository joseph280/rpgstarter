using Celestia.Data;
using UnityEngine;

namespace Celestia.Combat
{
    /// <summary>
    /// Value-type damage payload — passed by value from attacker to receiver.
    /// Struct keeps it allocation-free in hot paths (hit-per-frame scenarios).
    /// </summary>
    public readonly struct DamageMessage
    {
        public readonly GameObject Attacker;       // who fired (player or enemy)
        public readonly Component  Source;         // which component caused it (Arrow, MeleeSwing, etc.)
        public readonly float      Amount;         // raw damage before resistances
        public readonly DamageTypeSO DamageType;   // typed for resistance lookups + display color
        public readonly Vector3    HitPoint;       // world-space contact point — for VFX + damage numbers
        public readonly Vector3    HitNormal;      // surface normal at contact — for knockback / decals

        public DamageMessage(
            GameObject attacker,
            Component source,
            float amount,
            DamageTypeSO damageType,
            Vector3 hitPoint,
            Vector3 hitNormal)
        {
            Attacker   = attacker;
            Source     = source;
            Amount     = amount;
            DamageType = damageType;
            HitPoint   = hitPoint;
            HitNormal  = hitNormal;
        }
    }
}
