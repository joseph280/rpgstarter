using TMPro;
using UnityEngine;
using UnityEngine.Pool;

namespace Celestia.UI
{
    /// <summary>
    /// World-space "+N" popup floating above a broken rock. Same lifecycle shape as
    /// <see cref="DamageNumber"/> (rise, fade, return to pool) but exposes font size +
    /// colour per-show so the special 1-in-20 reward can render gold + bigger.
    /// </summary>
    public sealed class RewardPopup : MonoBehaviour
    {
        [SerializeField] private TextMeshPro tmp;
        [SerializeField, Min(0.05f)] private float lifetime    = 1.4f;
        [SerializeField]             private float floatSpeed  = 1.2f;
        [SerializeField] private AnimationCurve    alphaCurve  = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        private float _aliveSince;
        private Color _baseColor;
        private Transform _cam;
        private IObjectPool<RewardPopup> _pool;

        public void BindPool(IObjectPool<RewardPopup> pool) => _pool = pool;

        public void Show(string text, Color color, float fontSize, Transform cameraToFace)
        {
            if (tmp == null) tmp = GetComponentInChildren<TextMeshPro>();
            if (tmp != null)
            {
                tmp.text     = text;
                tmp.color    = color;
                tmp.fontSize = fontSize;
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
                transform.rotation = Quaternion.LookRotation(transform.position - _cam.position, Vector3.up);

            if (_aliveSince >= lifetime)
            {
                if (_pool != null) _pool.Release(this);
                else Destroy(gameObject);
            }
        }
    }
}
