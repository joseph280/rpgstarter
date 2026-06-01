using System.Collections;
using Celestia.Combat;
using Celestia.Data;
using Celestia.UI;
using Celestia.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Celestia.Player
{
    /// <summary>
    /// Glue between input → AbilityCaster → animation + damage. Sits on the player root.
    ///
    /// Combat dispatch is implicit in the ability:
    ///   - <c>ability.projectilePrefab != null</c> → ranged, spawn the arrow from the
    ///     bow-hand bone (or <see cref="spawnPointOverride"/>).
    ///   - <c>ability.projectilePrefab == null</c> → melee, sweep an OverlapSphere in
    ///     front of the player after a tunable wind-up delay.
    ///
    /// Aim: each cast we raycast camera-through-cursor onto the player's horizontal
    /// plane to compute a world-space target point and rotate the body to face it.
    /// </summary>
    [RequireComponent(typeof(AbilityCaster))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private Animator animator;
        [SerializeField] private ProjectilePool arrowPool;

        [Header("Spawn")]
        [Tooltip("If null, the bow weapon's transform (or LeftHand bone) is used for ranged shots.")]
        [SerializeField] private Transform spawnPointOverride;

        [Header("Filtering")]
        [Tooltip("Layers attacks (projectiles + melee sweeps) can hit. Configure to exclude the player itself.")]
        [SerializeField] private LayerMask attackHitMask = ~0;

        [Tooltip("How far ahead of the player to aim if the cursor isn't over a hit-able plane (fallback).")]
        [SerializeField, Min(1f)] private float fallbackAimDistance = 12f;

        [Header("Melee")]
        [Tooltip("Seconds between the swing starting and damage registering — should match the impact frame of the swing animation.")]
        [SerializeField, Min(0f)] private float meleeImpactDelay = 0.25f;

        [Tooltip("How wide the melee overlap is, as a fraction of ability.range. 0.6 gives a slightly forgiving cone-ish shape.")]
        [SerializeField, Range(0.1f, 1f)] private float meleeRadiusFraction = 0.6f;

        [Tooltip("Vertical offset of the melee sweep centre relative to the player root.")]
        [SerializeField, Min(0f)] private float meleeSweepHeight = 1.0f;

        private AbilityCaster _caster;
        private Transform     _bowHandBone;   // LeftHand — fallback ranged spawn point
        private Transform     _bowProp;       // Bow_* GameObject under LeftHand, if equipped — preferred ranged spawn point
        private Camera        _cam;

        // Hard input lock-out so click-spam can't queue overlapping swings while one is in
        // flight — cooldown alone wasn't enough at higher click rates (every press still ran
        // ComputeAimTarget + the InputSystem callback path even when the cooldown rejected
        // the cast). Cleared by the ability's own cooldown, which TryCast sets on success.
        private float _basicAttackBusyUntil;

        // Movement-slow window driven by the most recent cast. PlayerMovement reads
        // <see cref="MovementSpeedMultiplier"/> each frame and scales walk speed + rotation
        // by it — that's how a melee swing locks the body in place mid-strike.
        private float _movementSlowFactor = 1f;
        private float _movementSlowUntil;

        public float MovementSpeedMultiplier =>
            Time.time < _movementSlowUntil ? _movementSlowFactor : 1f;

        private void Awake()
        {
            _caster = GetComponent<AbilityCaster>();
            if (animator != null && animator.avatar != null && animator.isHuman)
            {
                _bowHandBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            }
        }

        private void OnEnable()
        {
            _cam = Camera.main;
            if (input != null)
            {
                input.OnBasicAttackPressed += OnBasicAttack;
                input.OnAbility1Pressed    += OnAbility1;
                input.OnAbility2Pressed    += OnAbility2;
                input.OnAbility3Pressed    += OnAbility3;
                input.OnAbility4Pressed    += OnAbility4;
            }
            if (_caster != null) _caster.OnAbilityFired += HandleAbilityFired;
        }

        private void OnDisable()
        {
            if (input != null)
            {
                input.OnBasicAttackPressed -= OnBasicAttack;
                input.OnAbility1Pressed    -= OnAbility1;
                input.OnAbility2Pressed    -= OnAbility2;
                input.OnAbility3Pressed    -= OnAbility3;
                input.OnAbility4Pressed    -= OnAbility4;
            }
            if (_caster != null) _caster.OnAbilityFired -= HandleAbilityFired;
        }

        // ── input → cast ─────────────────────────────────────────────────────

        private void OnBasicAttack()
        {
            // Drop the click if any inventory/crafting panel is open OR the pointer is
            // sitting over UI — otherwise clicking buttons in the inventory would also
            // swing the equipped weapon.
            if (InventoryHUD.IsAnyOpen || CraftingTableUI.IsAnyOpen) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            // Click-to-interact: a click that lands on a placed crafting table within
            // reach opens its UI instead of swinging at it. Out-of-reach clicks fall
            // through to the attack path.
            if (TryInteractOnClick()) return;

            // Hard busy-gate: drops every click that arrives while a swing is in flight.
            // Cleared automatically when the ability's cooldown elapses (set on TryCast success).
            if (Time.time < _basicAttackBusyUntil) return;

            if (_caster.TryCast(AbilityCaster.SLOT_BASIC))
            {
                var ability = _caster.GetAbility(AbilityCaster.SLOT_BASIC);
                if (ability != null) _basicAttackBusyUntil = Time.time + ability.cooldown;
            }
        }

        private bool TryInteractOnClick()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null || Mouse.current == null) return false;

            Vector2 mouseScreen = Mouse.current.position.ReadValue();
            Ray ray = _cam.ScreenPointToRay(mouseScreen);
            if (!Physics.Raycast(ray, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Collide))
                return false;

            var table = hit.collider.GetComponentInParent<PlacedCraftingTable>();
            if (table == null) return false;
            if (!table.IsWithinReach(transform.position)) return false;

            table.OpenUI();
            return true;
        }

        private void OnAbility1()    => _caster.TryCast(1);
        private void OnAbility2()    => _caster.TryCast(2);
        private void OnAbility3()    => _caster.TryCast(3);
        private void OnAbility4()    => _caster.TryCast(4);

        // ── cast → world ─────────────────────────────────────────────────────

        private void HandleAbilityFired(int slot, AbilityDefinitionSO ability)
        {
            Vector3 aimTarget = ComputeAimTarget();

            // Body-rotate-on-cast is per-ability:
            //   * Bow (faceAimOnCast=true, aimYawOffset=90) → archer stance toward cursor.
            //   * Melee (faceAimOnCast=false) → don't snap the body around; the swing goes
            //     wherever the player is already facing (their movement direction).
            if (ability.faceAimOnCast)
            {
                Vector3 toTarget = aimTarget - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    Vector3 bodyForward = Quaternion.AngleAxis(ability.aimYawOffset, Vector3.up) * toTarget.normalized;
                    transform.rotation = Quaternion.LookRotation(bodyForward, Vector3.up);
                }
            }

            // Movement slow / freeze for the cooldown window — melee abilities set this
            // to ~0 so the player commits to the swing instead of strafing through it.
            // PlayerMovement reads MovementSpeedMultiplier each frame.
            _movementSlowFactor = ability.movementSlowFactor;
            _movementSlowUntil  = Time.time + ability.cooldown;

            // Drive animation. CLAUDE.md §5: Animator.Play with state name, never SetTrigger.
            if (animator != null && !string.IsNullOrEmpty(ability.animatorStateName))
            {
                int stateHash = Animator.StringToHash(ability.animatorStateName);
                animator.Play(stateHash, ability.animatorLayer, 0f);
            }

            // Dispatch — placement > ranged > melee. Placement wins because a placeable
            // weapon should never accidentally do damage on its "swing".
            if (ability.placedPrefab != null)
                PlacePrefab(ability, aimTarget);
            else if (ability.projectilePrefab != null)
                FireRanged(ability, aimTarget);
            else
                StartCoroutine(FireMelee(ability));
        }

        private void PlacePrefab(AbilityDefinitionSO ability, Vector3 aimTarget)
        {
            // Optional consume — drop placement if the player doesn't have the required item.
            // (When the held item itself is the consumed one, this is what makes "use the
            // crafting table to plant a crafting table" finite.)
            var inv = Inventory.Local;
            if (ability.consumeOnPlace != null)
            {
                if (inv == null || inv.GetCount(ability.consumeOnPlace) <= 0)
                {
                    Debug.Log($"[PlayerCombat] Place {ability.name}: no {ability.consumeOnPlace.name} in inventory.");
                    return;
                }
            }

            // Drop the prefab at the aim point. Y is left at aimTarget.y (chest plane);
            // round it to the floor by zeroing the Y component — placeables sit on the
            // ground, not floating at chest height.
            Vector3 spawnPos = aimTarget;
            spawnPos.y = transform.position.y;

            // Face the prefab away from the player so the user-facing side reads correctly.
            Vector3 lookDir = aimTarget - transform.position;
            lookDir.y = 0f;
            Quaternion rot = lookDir.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
                : Quaternion.identity;

            Instantiate(ability.placedPrefab, spawnPos, rot);
            if (ability.consumeOnPlace != null && inv != null)
                inv.SO.Remove(ability.consumeOnPlace, 1);

            Debug.Log($"[PlayerCombat] Placed {ability.placedPrefab.name} at {spawnPos}.");
        }

        // ── ranged ───────────────────────────────────────────────────────────

        private void FireRanged(AbilityDefinitionSO ability, Vector3 aimTarget)
        {
            if (arrowPool == null || !arrowPool.IsReady)
            {
                Debug.LogWarning($"[PlayerCombat] {ability.name}: ranged but arrowPool not ready.");
                return;
            }

            Vector3 origin = GetRangedSpawnPoint();
            Vector3 dir    = aimTarget - origin;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            Quaternion facing = Quaternion.LookRotation(dir.normalized, Vector3.up);

            var projectile = arrowPool.Get(origin, facing);
            projectile.Launch(gameObject, ability.damage, attackHitMask);
        }

        private Vector3 GetRangedSpawnPoint()
        {
            if (spawnPointOverride != null) return spawnPointOverride.position;

            // Look for a bow currently equipped on the LeftHand. WeaponEquipment spawns
            // it on equip, so this is dynamic — re-resolved each shot in case the player
            // swapped weapons between shots.
            if (_bowHandBone != null)
            {
                _bowProp = FindBowChild(_bowHandBone);
                if (_bowProp != null) return _bowProp.position;
                return _bowHandBone.position;
            }

            return transform.position + Vector3.up * 1.4f;
        }

        private static Transform FindBowChild(Transform hand)
        {
            for (int i = 0; i < hand.childCount; i++)
            {
                var c = hand.GetChild(i);
                if (c != null && c.name.StartsWith("Bow_")) return c;
            }
            return null;
        }

        // ── melee ────────────────────────────────────────────────────────────

        // Single overlap query at impact time. Conservative for MVP — a real swing arc
        // would need either animation events, multi-frame swept queries, or a per-weapon
        // tracked hitbox collider. Good enough for a 9-week MVP.
        //
        // The swing direction is just transform.forward at impact: with the per-ability
        // movementSlowFactor freezing the body during the wind-up, "where you're facing
        // when the click lands" and "where you're facing 0.25s later" are the same — and
        // melee feels far more natural keyed off the body than off cursor flicks.
        private IEnumerator FireMelee(AbilityDefinitionSO ability)
        {
            if (meleeImpactDelay > 0f) yield return new WaitForSeconds(meleeImpactDelay);

            Vector3 origin  = transform.position + Vector3.up * meleeSweepHeight;
            Vector3 forward = transform.forward;
            Vector3 centre  = origin + forward * (ability.range * 0.5f);
            float   radius  = Mathf.Max(0.1f, ability.range * meleeRadiusFraction);

            // Reuse the project's allocator-friendly buffer (16 hits is plenty for melee).
            var hits = Physics.OverlapSphere(centre, radius, attackHitMask, QueryTriggerInteraction.Collide);

            int dealt = 0;
            foreach (var col in hits)
            {
                // Skip self.
                if (col.transform.IsChildOf(transform)) continue;

                var hitbox = col.GetComponent<Hitbox>() ?? col.GetComponentInParent<Hitbox>();
                if (hitbox != null && hitbox.IsActive && hitbox.Side != Hitbox.Allegiance.Player)
                {
                    Vector3 hitPoint  = col.ClosestPoint(centre);
                    Vector3 hitNormal = (hitPoint - centre).normalized;
                    var msg = new DamageMessage(gameObject, this, ability.damage, ability.damageType, hitPoint, hitNormal);
                    hitbox.ReceiveDamage(msg);
                    dealt++;
                    continue;
                }

                // Fallback: IDamageable on the rigidbody.
                if (col.attachedRigidbody != null && col.attachedRigidbody.TryGetComponent(out IDamageable receiver))
                {
                    Vector3 hitPoint  = col.ClosestPoint(centre);
                    Vector3 hitNormal = (hitPoint - centre).normalized;
                    var msg = new DamageMessage(gameObject, this, ability.damage, ability.damageType, hitPoint, hitNormal);
                    receiver.ApplyDamage(msg);
                    dealt++;
                }
            }
            if (dealt > 0) Debug.Log($"[PlayerCombat] Melee {ability.name}: {dealt} target(s) for {ability.damage} dmg.");
        }

        // ── aim helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Casts camera-through-cursor onto a horizontal plane at the player's chest height
        /// and returns the world-space hit. Falls back to a point ahead of the player if the
        /// cursor doesn't intersect the plane (cursor pointing at sky from a low camera).
        /// </summary>
        private Vector3 ComputeAimTarget()
        {
            if (_cam == null) _cam = Camera.main;

            Vector3 chest = transform.position + Vector3.up * 1.4f;

            if (_cam != null && Mouse.current != null)
            {
                Vector2 mouseScreen = Mouse.current.position.ReadValue();
                Ray ray = _cam.ScreenPointToRay(mouseScreen);
                Plane aimPlane = new(Vector3.up, chest);
                if (aimPlane.Raycast(ray, out float enter))
                    return ray.GetPoint(enter);
            }

            return transform.position + transform.forward * fallbackAimDistance + Vector3.up * 1.4f;
        }
    }
}
