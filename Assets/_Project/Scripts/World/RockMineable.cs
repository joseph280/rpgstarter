using System;
using RPGStarter.Combat;
using RPGStarter.Data;
using RPGStarter.Player;
using UnityEngine;

namespace RPGStarter.World
{
    /// <summary>
    /// Breakable rock node. Implements <see cref="IDamageable"/> directly so the
    /// PlayerCombat melee fallback (col.attachedRigidbody → IDamageable) finds it
    /// without us having to bolt on a full Health/DamageReceiver/Hitbox stack — the
    /// rock has no death anim, no resistance, no hit-flash; counting hits is the
    /// whole behaviour.
    ///
    /// The node is gated on tool kind: if <see cref="requiredToolKind"/> is non-null
    /// the attacker must be holding a weapon with that <see cref="WeaponDefinitionSO.toolKind"/>
    /// or the hit is silently rejected (hits don't count). This is what makes "only
    /// the pickaxe breaks rocks" work.
    ///
    /// On break:
    ///   1. roll the reward — random in [minDrop, maxDrop], or <see cref="specialDrop"/>
    ///      with probability <see cref="specialDropChance"/>;
    ///   2. raise <see cref="OnAnyBroken"/> so the scene's <c>RewardPopupSpawner</c>
    ///      can float a number above the rock (gold + bigger if the special hit);
    ///   3. spawn a single <c>ItemPickup</c> carrying the full reward count;
    ///   4. destroy self.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RockMineable : MonoBehaviour, IDamageable
    {
        [Header("Damage Gate")]
        [Tooltip("Required tool kind. If null, any weapon damages this rock. " +
                 "If set, only weapons whose toolKind == this asset will count hits.")]
        [SerializeField] private ToolKindSO requiredToolKind;

        [Tooltip("How many qualifying hits break the rock.")]
        [SerializeField, Min(1)] private int hitsToBreak = 4;

        [Header("Drop")]
        [Tooltip("Item type added to inventory when the rock breaks.")]
        [SerializeField] private ItemDefinitionSO dropItem;

        [Tooltip("Pickup prefab spawned on break — its ItemPickup component is configured " +
                 "with dropItem + the rolled amount.")]
        [SerializeField] private GameObject pickupPrefab;

        [Tooltip("Inclusive lower bound for the standard reward roll.")]
        [SerializeField, Min(1)] private int minDrop = 2;

        [Tooltip("Inclusive upper bound for the standard reward roll.")]
        [SerializeField, Min(1)] private int maxDrop = 5;

        [Tooltip("Special-roll amount. Awarded with probability specialDropChance.")]
        [SerializeField, Min(1)] private int specialDrop = 8;

        [Tooltip("Probability of the special roll firing instead of the standard range.")]
        [SerializeField, Range(0f, 1f)] private float specialDropChance = 0.05f; // 1 / 20

        [Tooltip("Pickup spawn vertical offset (so it doesn't clip into the floor).")]
        [SerializeField] private float pickupSpawnHeight = 0.4f;

        [Header("VFX")]
        [Tooltip("Tiny chip-burst spawned at the hit point on every successful (gated) strike.")]
        [SerializeField] private GameObject hitChipPrefab;

        [Tooltip("Larger debris burst spawned at the rock's centre when it shatters.")]
        [SerializeField] private GameObject breakDebrisPrefab;

        [Tooltip("Vertical offset for the break-debris burst (rock pivot is on the floor).")]
        [SerializeField] private float breakDebrisHeight = 0.4f;

        [Tooltip("Residual prop left behind after the node breaks — a stump for trees, " +
                 "a small rubble piece for rocks. Persistent; not interactable. Null = no residual.")]
        [SerializeField] private GameObject residualPrefab;

        [Header("Debug")]
        [Tooltip("Log every ApplyDamage call + gating result. Turn on while you're debugging " +
                 "'I press / chips / break do nothing' to see whether hits even reach the rock.")]
        [SerializeField] private bool verboseLogging = true;

        /// <summary>
        /// Fires once when any rock in the scene breaks. Args: world position, reward
        /// amount, whether the special (gold) roll fired. Subscribed by RewardPopupSpawner.
        /// Static event keeps the spawner from having to scan/track every rock — same
        /// approach DamageNumberSpawner uses for DamageReceiver.OnDamaged on a per-instance
        /// basis, scaled down to a project-wide tap because rocks are sparse and stateless.
        /// </summary>
        public static event Action<Vector3, int, bool> OnAnyBroken;

        public bool IsDead => _broken;

        private int  _hitsTaken;
        private bool _broken;

        public void ApplyDamage(in DamageMessage msg)
        {
            if (_broken) return;

            bool allowed = IsAttackerAllowed(msg);
            if (verboseLogging)
            {
                string equippedKind = "<none>";
                if (msg.Attacker != null && msg.Attacker.TryGetComponent(out WeaponEquipment we))
                    equippedKind = we.CurrentWeapon != null
                        ? (we.CurrentWeapon.toolKind != null ? we.CurrentWeapon.toolKind.name : "(weapon, no toolKind)")
                        : "(no weapon equipped)";
                Debug.Log($"[RockMineable] {name} hit. allowed={allowed} required={(requiredToolKind != null ? requiredToolKind.name : "<any>")} attacker={msg.Attacker?.name} equippedKind={equippedKind}");
            }
            if (!allowed) return;

            _hitsTaken++;

            // Per-hit chip burst at the actual contact point (not the rock pivot) so it
            // reads as "the pickaxe knocked stuff off here". Skipped on the breaking hit
            // because the break debris already covers it visually.
            if (_hitsTaken < hitsToBreak && hitChipPrefab != null)
                Instantiate(hitChipPrefab, msg.HitPoint, Quaternion.identity);
            else if (_hitsTaken < hitsToBreak && hitChipPrefab == null && verboseLogging)
                Debug.LogWarning($"[RockMineable] {name}: hitChipPrefab not assigned — re-run RPGStarter/W3/1.");

            if (_hitsTaken >= hitsToBreak) Break();
        }

        private bool IsAttackerAllowed(in DamageMessage msg)
        {
            if (requiredToolKind == null) return true;     // open node — any weapon works
            if (msg.Attacker == null)     return false;

            // PlayerCombat passes the player root as Attacker; WeaponEquipment lives there.
            // Cheap GetComponent — only runs at hit time, not per frame.
            if (!msg.Attacker.TryGetComponent(out WeaponEquipment equipment)) return false;
            var current = equipment.CurrentWeapon;
            return current != null && current.toolKind == requiredToolKind;
        }

        private void Break()
        {
            _broken = true;

            bool special = UnityEngine.Random.value < specialDropChance;
            int  amount  = special
                ? specialDrop
                : UnityEngine.Random.Range(minDrop, maxDrop + 1); // upper-exclusive → +1

            Vector3 pos = transform.position;
            OnAnyBroken?.Invoke(pos, amount, special);

            // Shatter — bigger / longer-lived than the per-hit burst. Spawned slightly above
            // the floor so chunks fly outward instead of intersecting the ground.
            if (breakDebrisPrefab != null)
                Instantiate(breakDebrisPrefab, pos + Vector3.up * breakDebrisHeight, Quaternion.identity);
            else if (verboseLogging)
                Debug.LogWarning($"[RockMineable] {name}: breakDebrisPrefab not assigned — re-run RPGStarter/W3/1.");

            // Leftover stump / rubble piece — sits at the node's base so the world reads
            // "something used to be here". Random yaw so identical residuals don't tile.
            if (residualPrefab != null)
                Instantiate(residualPrefab, pos,
                            Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));

            if (verboseLogging)
                Debug.Log($"[RockMineable] {name} BROKE — amount={amount} special={special}");

            SpawnPickup(pos, amount);
            Destroy(gameObject);
        }

        private void SpawnPickup(Vector3 rockPos, int amount)
        {
            if (pickupPrefab == null)
            {
                if (verboseLogging) Debug.LogWarning($"[RockMineable] {name}: pickupPrefab not assigned — nothing to absorb. Re-run RPGStarter/W3/1.");
                return;
            }
            if (dropItem == null)
            {
                if (verboseLogging) Debug.LogWarning($"[RockMineable] {name}: dropItem not assigned — pickup will carry no item. Re-run RPGStarter/W3/1.");
                return;
            }

            Vector3 spawnPos = rockPos + Vector3.up * pickupSpawnHeight;
            var go = Instantiate(pickupPrefab, spawnPos, Quaternion.identity);
            if (go.TryGetComponent(out ItemPickup pickup))
            {
                pickup.Configure(dropItem, amount);
                if (verboseLogging) Debug.Log($"[RockMineable] {name}: spawned pickup '{go.name}' at {spawnPos} with {amount} {dropItem.name}.");
            }
            else if (verboseLogging)
            {
                Debug.LogWarning($"[RockMineable] {name}: spawned '{go.name}' but it has no ItemPickup component — it'll just sit there.");
            }
        }
    }
}
