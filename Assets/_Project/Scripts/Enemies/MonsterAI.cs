using RPGStarter.Combat;
using RPGStarter.Data;
using RPGStarter.Player;
using UnityEngine;

namespace RPGStarter.Enemies
{
    /// <summary>
    /// Simple chase-and-melee AI used by Monster1. State machine: Idle → Chase → Attack → Dead.
    /// All tunables come from EnemyDefinitionSO so designers can rebalance without touching code.
    ///
    /// Movement uses CharacterController (no NavMesh required for the open-floor W2 test scene).
    /// Damage to the player goes through PlayerHealth.TakeDamage directly — the player-side
    /// DamageReceiver pipeline lands later (W3+); this is a deliberate stop-gap for W2 combat.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(Health), typeof(DamageReceiver))]
    public sealed class MonsterAI : MonoBehaviour
    {
        private static readonly int LocomotionState = Animator.StringToHash("Locomotion");
        private static readonly int AttackState     = Animator.StringToHash("Attack");
        private static readonly int DeathState      = Animator.StringToHash("Death");
        private static readonly int SpeedParam      = Animator.StringToHash("Speed");

        [Header("Refs")]
        [SerializeField] private EnemyDefinitionSO definition;
        [SerializeField] private Animator animator;
        [SerializeField] private HitFlash hitFlash;

        [Tooltip("Player target. Auto-resolved from the active scene at Start if left empty.")]
        [SerializeField] private PlayerHealth target;

        [Header("Tuning")]
        [Tooltip("Degrees per second the monster rotates to face its target.")]
        [SerializeField] private float rotationSpeed = 540f;
        [Tooltip("Gravity applied each frame (negative = downward).")]
        [SerializeField] private float gravity = -20f;
        [Tooltip("Padding added to attackRange when checking 'is the player still in melee?' at hit time. Stops a player who barely moves out of range from cheesing the swing.")]
        [SerializeField] private float attackHitPadding = 0.4f;

        private CharacterController _cc;
        private Health _health;
        private DamageReceiver _receiver;

        private enum Phase { Idle, Chase, Attack, Dead }
        private Phase _phase;
        private float _verticalVelocity;
        private float _attackStartedAt;
        private float _attackCooldownUntil;
        private bool  _attackHitApplied;
        private float _deadSince;

        private void Awake()
        {
            _cc       = GetComponent<CharacterController>();
            _health   = GetComponent<Health>();
            _receiver = GetComponent<DamageReceiver>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (hitFlash == null) hitFlash = GetComponentInChildren<HitFlash>();

            if (definition != null) _health.SetMax(definition.maxHealth, refill: true);
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

        private void Start()
        {
            // Bootstrap-only auto-discovery (CLAUDE.md §5 carve-out): MonsterAI needs a player
            // reference, but we don't want every spawner to wire it manually. Find once at scene
            // load — never in Update.
            if (target == null) target = Object.FindAnyObjectByType<PlayerHealth>();
        }

        private void Update()
        {
            if (_phase == Phase.Dead) { TickDead(); return; }
            if (definition == null || target == null || target.IsDead) { TickIdle(); return; }

            float dist = Vector3.Distance(transform.position, target.transform.position);

            switch (_phase)
            {
                case Phase.Idle:
                    if (dist <= definition.detectionRadius) _phase = Phase.Chase;
                    TickIdle();
                    break;

                case Phase.Chase:
                    // Disengage when the player has run far enough away. loseInterestRadius
                    // is intentionally larger than detectionRadius so a player who walks just
                    // past the detection edge isn't ping-ponging the monster between Idle and
                    // Chase every frame.
                    if (dist > definition.loseInterestRadius)
                    {
                        _phase = Phase.Idle;
                        TickIdle();
                        break;
                    }
                    if (dist <= definition.attackRange && Time.time >= _attackCooldownUntil) StartAttack();
                    else TickChase();
                    break;

                case Phase.Attack:
                    TickAttack();
                    break;
            }
        }

        // ── Phase ticks ──────────────────────────────────────────────────────

        private void TickIdle()
        {
            ApplyGravityOnly();
            if (animator != null) animator.SetFloat(SpeedParam, 0f);
        }

        private void TickChase()
        {
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude < 0.0001f) { TickIdle(); return; }

            Vector3 dir = toTarget.normalized;
            FaceDirection(dir);

            ApplyGravity();
            Vector3 motion = dir * definition.moveSpeed + Vector3.up * _verticalVelocity;
            _cc.Move(motion * Time.deltaTime);

            if (animator != null) animator.SetFloat(SpeedParam, definition.moveSpeed);
        }

        private void StartAttack()
        {
            _phase = Phase.Attack;
            _attackStartedAt = Time.time;
            _attackHitApplied = false;
            if (animator != null)
            {
                animator.SetFloat(SpeedParam, 0f);
                animator.Play(AttackState, 0, 0f);
            }
        }

        private void TickAttack()
        {
            // Stand still during the swing — face the target so the strike lands forward.
            if (target != null)
            {
                Vector3 toTarget = target.transform.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f) FaceDirection(toTarget.normalized);
            }
            ApplyGravityOnly();

            float elapsed = Time.time - _attackStartedAt;
            if (!_attackHitApplied && elapsed >= definition.attackWindup)
            {
                _attackHitApplied = true;
                TryDealDamage();
            }
            if (elapsed >= definition.attackDuration)
            {
                float rate = Mathf.Max(0.01f, definition.baseAttackRate);
                _attackCooldownUntil = Time.time + (1f / rate);
                _phase = Phase.Chase;
            }
        }

        private void TickDead()
        {
            ApplyGravityOnly();
            _deadSince += Time.deltaTime;
            if (_deadSince >= definition.deathDuration) Destroy(gameObject);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void TryDealDamage()
        {
            if (target == null || target.IsDead) return;
            float dist = Vector3.Distance(transform.position, target.transform.position);
            if (dist > definition.attackRange + attackHitPadding) return; // player rolled out of range
            target.TakeDamage(Mathf.Max(1, Mathf.RoundToInt(definition.baseDamage)));
        }

        private void FaceDirection(Vector3 worldDir)
        {
            if (worldDir.sqrMagnitude < 0.0001f) return;
            Quaternion target = Quaternion.LookRotation(worldDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotationSpeed * Time.deltaTime);
        }

        private void ApplyGravity()
        {
            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            else _verticalVelocity += gravity * Time.deltaTime;
        }

        private void ApplyGravityOnly()
        {
            ApplyGravity();
            _cc.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
        }

        // ── Events ───────────────────────────────────────────────────────────

        private void HandleDamaged(DamageMessage _)
        {
            if (hitFlash != null) hitFlash.Flash();
            // First hit aggros even if outside detection radius — feels right and matches the W2 spec spirit.
            if (_phase == Phase.Idle) _phase = Phase.Chase;
        }

        private void HandleDied()
        {
            if (_phase == Phase.Dead) return;
            _phase = Phase.Dead;
            _deadSince = 0f;

            if (animator != null)
            {
                animator.SetFloat(SpeedParam, 0f);
                animator.Play(DeathState, 0, 0f);
            }

            // Stop hits + collisions during the death animation.
            foreach (var hb in GetComponentsInChildren<Hitbox>()) hb.IsActive = false;
            if (_cc != null) _cc.enabled = false;
        }
    }
}
