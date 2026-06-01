using Celestia.Combat;
using Celestia.Data;
using UnityEngine;

namespace Celestia.Enemies
{
    /// <summary>
    /// Passive enemy used for combat testing. No AI — just stands there, takes damage,
    /// flashes on hit, plays a fall-over death animation, then despawns.
    /// </summary>
    [RequireComponent(typeof(Health), typeof(DamageReceiver))]
    public sealed class TargetDummy : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private EnemyDefinitionSO definition;
        [SerializeField] private HitFlash hitFlash;

        [Header("Death")]
        [Tooltip("Seconds the fall-over animation takes before the GameObject despawns.")]
        [SerializeField, Min(0.05f)] private float deathDuration = 0.6f;

        [Tooltip("Direction to topple on death (local right). Randomized at death.")]
        [SerializeField] private bool randomizeFallDirection = true;

        private Health _health;
        private DamageReceiver _receiver;
        private bool _dying;
        private float _dyingSince;
        private Quaternion _deathStartRotation;
        private Quaternion _deathEndRotation;
        private Vector3 _deathStartScale;

        private void Awake()
        {
            _health   = GetComponent<Health>();
            _receiver = GetComponent<DamageReceiver>();
            if (hitFlash == null) hitFlash = GetComponentInChildren<HitFlash>();

            if (definition != null)
            {
                _health.SetMax(definition.maxHealth, refill: true);
            }
        }

        private void OnEnable()
        {
            _receiver.OnDamaged += HandleDamaged;
            _health.OnDied      += HandleDied;
        }

        private void OnDisable()
        {
            _receiver.OnDamaged -= HandleDamaged;
            _health.OnDied      -= HandleDied;
        }

        private void HandleDamaged(DamageMessage _)
        {
            if (hitFlash != null) hitFlash.Flash();
        }

        private void HandleDied()
        {
            if (_dying) return;
            _dying = true;
            _dyingSince = 0f;
            _deathStartRotation = transform.rotation;
            _deathStartScale    = transform.localScale;

            // Topple sideways (local +X or -X) by 90°.
            Vector3 axis = transform.right * (randomizeFallDirection && Random.value < 0.5f ? -1f : 1f);
            _deathEndRotation = Quaternion.AngleAxis(90f, axis) * _deathStartRotation;

            // Disable hit detection during the death animation.
            foreach (var hb in GetComponentsInChildren<Hitbox>())
                hb.IsActive = false;
            foreach (var col in GetComponentsInChildren<Collider>())
                col.enabled = false;
        }

        private void Update()
        {
            if (!_dying) return;

            _dyingSince += Time.deltaTime;
            float t = Mathf.Clamp01(_dyingSince / deathDuration);
            float ease = 1f - (1f - t) * (1f - t); // ease-out quad

            transform.rotation   = Quaternion.Slerp(_deathStartRotation, _deathEndRotation, ease);
            transform.localScale = Vector3.Lerp(_deathStartScale, _deathStartScale * 0.6f, ease);

            if (t >= 1f) Destroy(gameObject);
        }
    }
}
