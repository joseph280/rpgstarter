using RPGStarter.Data;
using UnityEngine;
using UnityEngine.Pool;

namespace RPGStarter.Combat
{
    /// <summary>
    /// Pooled projectile. Each tick raycasts from previous to current position so
    /// fast projectiles can't tunnel through targets. Returns to its pool on hit
    /// or lifetime expiry.
    ///
    /// Owned by ProjectilePool; never instantiate Projectile directly — go through
    /// the pool's Get() / Release().
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Damage type for the DamageMessage built on hit.")]
        [SerializeField] private DamageTypeSO damageType;

        [Header("Tuning")]
        [SerializeField, Min(0.1f)] private float speed         = 25f;
        [SerializeField, Min(0.1f)] private float lifetime      = 4f;
        [SerializeField, Min(0.01f)] private float skinRadius   = 0.05f; // SphereCast radius — thin arrow
        [SerializeField] private LayerMask hitMask = ~0;                  // configure per spawner

        [Header("Behaviour")]
        [SerializeField] private bool destroyOnHit = true;

        // Set per-shot by the spawner.
        private GameObject _attacker;
        private float      _damage;
        private Vector3    _previousPosition;
        private float      _aliveSince;
        private IObjectPool<Projectile> _pool;
        private bool _live;

        public DamageTypeSO DamageType => damageType;
        public float Speed             => speed;

        /// <summary>
        /// Called by the pool factory once at creation — wires the pool reference
        /// so the projectile can release itself.
        /// </summary>
        public void BindPool(IObjectPool<Projectile> pool) => _pool = pool;

        /// <summary>
        /// Configure for a specific shot. Call AFTER positioning the projectile in
        /// world space. The projectile flies along its current forward direction.
        /// </summary>
        public void Launch(GameObject attacker, float damage, LayerMask mask)
        {
            _attacker          = attacker;
            _damage            = damage;
            hitMask            = mask;
            _previousPosition  = transform.position;
            _aliveSince        = 0f;
            _live              = true;
        }

        private void Update()
        {
            if (!_live) return;

            _aliveSince += Time.deltaTime;
            if (_aliveSince >= lifetime)
            {
                ReleaseToPool();
                return;
            }

            Vector3 current = transform.position;
            Vector3 next    = current + transform.forward * (speed * Time.deltaTime);
            Vector3 sweep   = next - _previousPosition;
            float   sweepLen = sweep.magnitude;

            // Swept hit detection. SphereCast > Raycast for arrows because the
            // collider isn't a single point — better edge behaviour at high speeds.
            if (sweepLen > 0.0001f &&
                Physics.SphereCast(_previousPosition, skinRadius, sweep / sweepLen,
                                   out RaycastHit hit, sweepLen, hitMask, QueryTriggerInteraction.Collide))
            {
                transform.position = hit.point;
                ResolveHit(hit);
                if (destroyOnHit) { ReleaseToPool(); return; }
            }
            else
            {
                transform.position = next;
            }
            _previousPosition = transform.position;
        }

        protected virtual void ResolveHit(RaycastHit hit)
        {
            Debug.Log($"[Projectile] hit {hit.collider.name} (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)})");

            // Look for a Hitbox on the collider OR on parents (collider could be on a child mesh).
            var hitbox = hit.collider.GetComponent<Hitbox>() ?? hit.collider.GetComponentInParent<Hitbox>();
            if (hitbox != null)
            {
                if (!hitbox.IsActive) { Debug.Log("[Projectile] hitbox inactive — no damage."); return; }
                var msg = new DamageMessage(_attacker, this, _damage, damageType, hit.point, hit.normal);
                hitbox.ReceiveDamage(msg);
                Debug.Log($"[Projectile] dealt {_damage} via hitbox {hitbox.name}");
                return;
            }

            // Fallback: IDamageable on the rigidbody.
            if (hit.collider.attachedRigidbody != null &&
                hit.collider.attachedRigidbody.TryGetComponent(out IDamageable receiver))
            {
                var msg = new DamageMessage(_attacker, this, _damage, damageType, hit.point, hit.normal);
                receiver.ApplyDamage(msg);
                Debug.Log($"[Projectile] dealt {_damage} via IDamageable on {hit.collider.attachedRigidbody.name}");
                return;
            }

            Debug.Log($"[Projectile] hit {hit.collider.name} but no Hitbox or IDamageable found — environment hit.");
        }

        private void ReleaseToPool()
        {
            _live = false;
            if (_pool != null) _pool.Release(this);
            else Destroy(gameObject); // fallback if not pooled
        }
    }
}
