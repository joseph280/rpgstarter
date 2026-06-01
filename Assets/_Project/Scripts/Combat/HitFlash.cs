using UnityEngine;

namespace Celestia.Combat
{
    /// <summary>
    /// Flashes a target's renderers a color for a brief moment via MaterialPropertyBlock.
    /// MPB doesn't instance materials — same shared material, per-renderer override only,
    /// no extra draw calls, no GC alloc.
    ///
    /// Hook to DamageReceiver.OnDamaged or call Flash() manually.
    /// </summary>
    public sealed class HitFlash : MonoBehaviour
    {
        private static readonly int BaseColorID  = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorID      = Shader.PropertyToID("_Color"); // legacy

        [SerializeField] private Color flashColor = new(1f, 0.3f, 0.3f, 1f);
        [SerializeField, Min(0.01f)] private float duration = 0.12f;

        [Tooltip("If empty, gathered from children at Awake.")]
        [SerializeField] private Renderer[] renderers;

        private MaterialPropertyBlock _mpb;
        private Color[] _originalColors;
        private float   _flashEndsAt;
        private bool    _flashing;

        private void Awake()
        {
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);

            _mpb = new MaterialPropertyBlock();
            _originalColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var mat = renderers[i].sharedMaterial;
                _originalColors[i] = mat != null && mat.HasProperty(BaseColorID)
                    ? mat.GetColor(BaseColorID)
                    : (mat != null && mat.HasProperty(ColorID) ? mat.GetColor(ColorID) : Color.white);
            }
        }

        public void Flash()
        {
            _flashEndsAt = Time.time + duration;
            if (!_flashing)
            {
                _flashing = true;
                ApplyColor(flashColor, useOriginalIndex: false);
            }
        }

        private void Update()
        {
            if (!_flashing) return;
            if (Time.time >= _flashEndsAt)
            {
                _flashing = false;
                ApplyColor(default, useOriginalIndex: true);
            }
        }

        private void ApplyColor(Color color, bool useOriginalIndex)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                Color c = useOriginalIndex ? _originalColors[i] : color;
                _mpb.SetColor(BaseColorID, c);
                _mpb.SetColor(ColorID,     c);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
