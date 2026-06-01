using Celestia.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Celestia.UI
{
    /// <summary>
    /// World-space billboard health bar. Sits above a character, tracks its IHealthSource,
    /// and rotates each frame to face the active camera so the player always reads the bar
    /// straight-on regardless of yaw.
    ///
    /// HUD/UI proper lands in W7 — this is the over-head bar combat-test scenes need now.
    /// </summary>
    public sealed class WorldHealthBar : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("MonoBehaviour that implements IHealthSource (Health or PlayerHealth).")]
        [SerializeField] private MonoBehaviour healthSource;
        [SerializeField] private Image fill;

        [Header("Behaviour")]
        [Tooltip("Hide the bar while HP is full and the character is alive — declutters the scene.")]
        [SerializeField] private bool hideWhenFull = false;
        [SerializeField] private CanvasGroup canvasGroup;

        private IHealthSource _src;
        private RectTransform _fillRect;
        private Transform     _camTr;

        private void Awake()
        {
            _src = healthSource as IHealthSource;
            if (_src == null && healthSource != null)
                Debug.LogError($"[WorldHealthBar] {healthSource.GetType().Name} on {name} is not IHealthSource.");
            if (fill != null) _fillRect = fill.rectTransform;
        }

        private void OnEnable()
        {
            // Camera.main allocates only on the first call after a scene change — cache it.
            var cam = Camera.main;
            if (cam != null) _camTr = cam.transform;
        }

        private void LateUpdate()
        {
            if (_src != null && _fillRect != null)
            {
                // Drive scale.x rather than Image.fillAmount: fillAmount is a no-op when the
                // Image has no sprite, and we'd rather not depend on the built-in Resources at
                // runtime. Pivot is set to left in HealthBarBuilder so this shrinks right→left.
                var s = _fillRect.localScale;
                s.x = Mathf.Clamp01(_src.Fraction01);
                _fillRect.localScale = s;
            }

            if (hideWhenFull && canvasGroup != null && _src != null)
                canvasGroup.alpha = (!_src.IsDead && _src.Fraction01 >= 0.999f) ? 0f : 1f;

            // Re-acquire the camera lazily — Bootstrap loads scenes additively, so Camera.main
            // can be null on Awake but valid a frame later.
            if (_camTr == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _camTr = cam.transform;
            }
            transform.rotation = _camTr.rotation;
        }
    }
}
