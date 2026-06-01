using TMPro;
using UnityEngine;
using UnityEngine.Pool;

namespace RPGStarter.UI
{
    /// <summary>
    /// World-space damage number. Spawned + positioned + colored by DamageNumberSpawner,
    /// then animates upward and fades over its lifetime, releasing itself to the pool
    /// when done.
    ///
    /// Use TextMeshPro (CLAUDE.md §1 stack lock — TMP is canonical).
    /// </summary>
    public sealed class DamageNumber : MonoBehaviour
    {
        [SerializeField] private TextMeshPro tmp;
        [SerializeField, Min(0.05f)] private float lifetime  = 0.9f;
        [SerializeField] private float           floatSpeed  = 1.5f;
        [SerializeField] private AnimationCurve  alphaCurve  = AnimationCurve.Linear(0, 1, 1, 0);

        private float _aliveSince;
        private IObjectPool<DamageNumber> _pool;
        private Transform _cam;
        private Color _baseColor;

        public void BindPool(IObjectPool<DamageNumber> pool) => _pool = pool;

        public void Show(string text, Color color, Transform cameraToFace)
        {
            if (tmp == null) tmp = GetComponentInChildren<TextMeshPro>();
            if (tmp != null)
            {
                tmp.text = text;
                tmp.color = color;
            }
            _baseColor  = color;
            _cam        = cameraToFace;
            _aliveSince = 0f;
        }

        private void Update()
        {
            _aliveSince += Time.deltaTime;
            transform.position += Vector3.up * (floatSpeed * Time.deltaTime);

            if (tmp != null)
            {
                float t = Mathf.Clamp01(_aliveSince / lifetime);
                Color c = _baseColor;
                c.a = alphaCurve.Evaluate(t);
                tmp.color = c;
            }

            if (_cam != null)
            {
                // Billboard toward camera.
                transform.rotation = Quaternion.LookRotation(transform.position - _cam.position, Vector3.up);
            }

            if (_aliveSince >= lifetime)
            {
                if (_pool != null) _pool.Release(this);
                else Destroy(gameObject);
            }
        }
    }
}
