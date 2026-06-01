using Celestia.Combat;
using UnityEngine;
using UnityEngine.Pool;

namespace Celestia.UI
{
    /// <summary>
    /// Owns the DamageNumber pool and listens for damage events on every DamageReceiver
    /// in the scene at startup. New receivers spawned later can register manually via
    /// <see cref="RegisterReceiver"/>.
    ///
    /// One spawner per scene — usually parked on the Bootstrap GameManager so it
    /// survives scene loads.
    /// </summary>
    public sealed class DamageNumberSpawner : MonoBehaviour
    {
        [SerializeField] private DamageNumber prefab;
        [SerializeField, Min(0)]  private int defaultCapacity = 16;
        [SerializeField, Min(1)]  private int maxSize         = 64;
        [SerializeField] private Vector3 hitOffset = new(0f, 0.2f, 0f);

        private ObjectPool<DamageNumber> _pool;
        private Transform _container;
        private Camera    _cam;

        public bool IsReady => _pool != null;

        private void Awake()
        {
            if (prefab == null)
            {
                Debug.LogError("[DamageNumberSpawner] prefab not assigned.");
                return;
            }

            _container = new GameObject("~Pool_DamageNumbers").transform;
            _container.SetParent(transform, false);

            _pool = new ObjectPool<DamageNumber>(
                createFunc: () =>
                {
                    var n = Instantiate(prefab, _container);
                    n.BindPool(_pool);
                    return n;
                },
                actionOnGet:     n => n.gameObject.SetActive(true),
                actionOnRelease: n => n.gameObject.SetActive(false),
                actionOnDestroy: n => { if (n != null) Destroy(n.gameObject); },
                collectionCheck: false,
                defaultCapacity: defaultCapacity,
                maxSize: maxSize);

            _cam = Camera.main;
        }

        private void Start()
        {
            // One-time bootstrap-side scan: hook all DamageReceivers in loaded scenes.
            // Future receivers (spawned post-bootstrap) call RegisterReceiver manually.
            foreach (var receiver in Object.FindObjectsByType<DamageReceiver>(FindObjectsInactive.Include))
            {
                receiver.OnDamaged += OnAnyDamaged;
            }
        }

        public void RegisterReceiver(DamageReceiver receiver)
        {
            if (receiver != null) receiver.OnDamaged += OnAnyDamaged;
        }

        public void UnregisterReceiver(DamageReceiver receiver)
        {
            if (receiver != null) receiver.OnDamaged -= OnAnyDamaged;
        }

        private void OnAnyDamaged(DamageMessage message)
        {
            if (_pool == null || _cam == null) return;

            var dn = _pool.Get();
            dn.transform.position = message.HitPoint + hitOffset;

            string text = Mathf.RoundToInt(message.Amount).ToString();
            Color color = message.DamageType != null ? message.DamageType.displayColor : Color.white;
            dn.Show(text, color, _cam.transform);
        }
    }
}
