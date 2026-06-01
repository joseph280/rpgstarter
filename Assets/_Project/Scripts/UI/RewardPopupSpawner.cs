using Celestia.World;
using UnityEngine;
using UnityEngine.Pool;

namespace Celestia.UI
{
    /// <summary>
    /// One per scene (lives on Bootstrap GameManager). Subscribes to
    /// <see cref="RockMineable.OnAnyBroken"/> and pools <see cref="RewardPopup"/>
    /// instances above the rock's break position.
    ///
    /// The "special" 1-in-20 reward lights up gold and renders at the larger font
    /// size; standard rewards use the normal-tier values.
    /// </summary>
    public sealed class RewardPopupSpawner : MonoBehaviour
    {
        [SerializeField] private RewardPopup prefab;
        [SerializeField, Min(0)] private int defaultCapacity = 4;
        [SerializeField, Min(1)] private int maxSize         = 16;
        [SerializeField] private Vector3 popupOffset = new(0f, 1.4f, 0f);

        [Header("Tiers")]
        [SerializeField] private Color normalColor      = Color.white;
        [SerializeField] private Color specialColor     = new(1f, 0.84f, 0.2f, 1f); // gold
        [SerializeField, Min(1f)] private float normalFontSize  = 5f;
        [SerializeField, Min(1f)] private float specialFontSize = 11f;

        private ObjectPool<RewardPopup> _pool;
        private Transform _container;
        private Camera    _cam;

        public bool IsReady => _pool != null;

        private void Awake()
        {
            if (prefab == null)
            {
                Debug.LogError("[RewardPopupSpawner] prefab not assigned.");
                return;
            }

            _container = new GameObject("~Pool_RewardPopups").transform;
            _container.SetParent(transform, false);

            _pool = new ObjectPool<RewardPopup>(
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

        private void OnEnable()  => RockMineable.OnAnyBroken += OnRockBroken;
        private void OnDisable() => RockMineable.OnAnyBroken -= OnRockBroken;

        private void OnRockBroken(Vector3 worldPos, int amount, bool special)
        {
            if (_pool == null) return;
            if (_cam == null) _cam = Camera.main;

            var popup = _pool.Get();
            popup.transform.position = worldPos + popupOffset;
            popup.Show(
                text:         amount.ToString(),
                color:        special ? specialColor    : normalColor,
                fontSize:     special ? specialFontSize : normalFontSize,
                cameraToFace: _cam != null ? _cam.transform : null);
        }
    }
}
