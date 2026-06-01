using UnityEngine;
using UnityEngine.Pool;

namespace RPGStarter.Combat
{
    /// <summary>
    /// Wraps UnityEngine.Pool.ObjectPool&lt;T&gt; for a specific Projectile prefab.
    /// One pool per projectile type. Held by the spawner (e.g., PlayerCombat) — not
    /// a singleton, no static state. Place on the same GameObject as the spawner
    /// or any persistent scene root.
    ///
    /// CLAUDE.md §5: no `new GameObject` / `Instantiate` per frame in hot paths.
    /// </summary>
    public sealed class ProjectilePool : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private Projectile prefab;

        [Header("Pool Sizing")]
        [SerializeField, Min(0)]  private int defaultCapacity = 16;
        [SerializeField, Min(1)]  private int maxSize         = 64;
        [SerializeField] private bool collectionChecks = true; // disable in builds for perf

        private ObjectPool<Projectile> _pool;
        private Transform _container;

        public bool IsReady => _pool != null;

        private void Awake()
        {
            if (prefab == null)
            {
                Debug.LogError("[ProjectilePool] prefab not assigned.");
                return;
            }

            _container = new GameObject($"~Pool_{prefab.name}").transform;
            _container.SetParent(transform, false);

            _pool = new ObjectPool<Projectile>(
                createFunc:    OnCreate,
                actionOnGet:   OnGet,
                actionOnRelease: OnRelease,
                actionOnDestroy: OnDestroyEntry,
                collectionCheck: collectionChecks,
                defaultCapacity: defaultCapacity,
                maxSize: maxSize);
        }

        private void OnDestroy()
        {
            _pool?.Clear();
        }

        public Projectile Get(Vector3 position, Quaternion rotation)
        {
            var p = _pool.Get();
            // Detach from the pool container before positioning. The pool lives under
            // the spawner (player), so an attached projectile inherits player rotation
            // every frame — arrows would curve when the player turns mid-flight.
            p.transform.SetParent(null, worldPositionStays: false);
            p.transform.SetPositionAndRotation(position, rotation);
            return p;
        }

        // ── pool callbacks ────────────────────────────────────────────────────

        private Projectile OnCreate()
        {
            var p = Instantiate(prefab, _container);
            p.BindPool(_pool);
            return p;
        }

        private void OnGet(Projectile p)
        {
            p.gameObject.SetActive(true);
        }

        private void OnRelease(Projectile p)
        {
            p.gameObject.SetActive(false);
            // Reparent back under the container so the inactive hierarchy stays tidy.
            if (_container != null) p.transform.SetParent(_container, worldPositionStays: false);
        }

        private void OnDestroyEntry(Projectile p)
        {
            if (p != null) Destroy(p.gameObject);
        }
    }
}
