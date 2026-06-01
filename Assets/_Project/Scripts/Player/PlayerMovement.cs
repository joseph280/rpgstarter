using Celestia.Data;
using UnityEngine;

namespace Celestia.Player
{
    /// <summary>
    /// Camera-relative isometric WASD movement + dodge dash.
    /// Reads input from PlayerInputReader (composition per CLAUDE.md §5).
    /// All tunables come from ClassDefinitionSO — no magic numbers in code.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private ClassDefinitionSO classDefinition;
        [Tooltip("Transform whose forward is treated as 'screen up' for camera-relative movement. Usually the Cinemachine target/Main Camera.")]
        [SerializeField] private Transform cameraReference;

        [Header("Tuning")]
        [Tooltip("Degrees per second the character rotates to face movement direction.")]
        [SerializeField] private float rotationSpeed = 720f;
        [Tooltip("Gravity applied while not dodging.")]
        [SerializeField] private float gravity = -20f;

        private CharacterController _controller;
        private PlayerCombat        _combat;             // optional — used for attack-time movement slow
        private float _verticalVelocity;
        private float _dodgeTimeRemaining;
        private float _dodgeCooldownRemaining;
        private Vector3 _dodgeDirection;

        public bool IsDodging => _dodgeTimeRemaining > 0f;
        public Vector3 CurrentVelocity { get; private set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            // PlayerCombat lives on the same GameObject; auto-find avoids needing another
            // serialized ref. Optional — movement still works if combat isn't present.
            _combat     = GetComponent<PlayerCombat>();
            if (input == null)           Debug.LogError("[PlayerMovement] input not assigned.");
            if (classDefinition == null) Debug.LogError("[PlayerMovement] classDefinition not assigned.");
            if (cameraReference == null) Debug.LogWarning("[PlayerMovement] cameraReference not set; using world-space WASD.");
        }

        private void OnEnable()  { if (input != null) input.OnDodgePressed += TryStartDodge; }
        private void OnDisable() { if (input != null) input.OnDodgePressed -= TryStartDodge; }

        private void Update()
        {
            float dt = Time.deltaTime;
            _dodgeCooldownRemaining = Mathf.Max(0f, _dodgeCooldownRemaining - dt);

            Vector3 horizontal = IsDodging
                ? TickDodge(dt)
                : TickWalk(dt);

            ApplyGravity(dt);

            Vector3 motion = horizontal + Vector3.up * _verticalVelocity;
            _controller.Move(motion * dt);
            CurrentVelocity = motion;
        }

        // ── Walking ─────────────────────────────────────────────────────────

        private Vector3 TickWalk(float dt)
        {
            Vector2 raw = input != null ? input.MoveInput : Vector2.zero;
            if (raw.sqrMagnitude < 0.0001f) return Vector3.zero;

            // Attack-time slow: PlayerCombat exposes a 0..1 multiplier on cast and clears
            // it when the cooldown elapses. Applied to both translation AND rotation rate
            // so a melee swing also visually locks the body (rotation lerping toward
            // movement input was the other half of the "feels mushy mid-swing" symptom).
            float speedMult = _combat != null ? _combat.MovementSpeedMultiplier : 1f;

            Vector3 worldDir = ProjectInputToCameraPlane(raw);
            FaceDirection(worldDir, dt * speedMult);
            return worldDir * (classDefinition.moveSpeed * speedMult);
        }

        private Vector3 ProjectInputToCameraPlane(Vector2 raw)
        {
            if (cameraReference == null) return new Vector3(raw.x, 0f, raw.y);

            Vector3 fwd   = cameraReference.forward; fwd.y = 0f;   fwd.Normalize();
            Vector3 right = cameraReference.right;   right.y = 0f; right.Normalize();
            return (fwd * raw.y + right * raw.x).normalized * Mathf.Min(1f, raw.magnitude);
        }

        private void FaceDirection(Vector3 worldDir, float dt)
        {
            if (worldDir.sqrMagnitude < 0.0001f) return;
            Quaternion target = Quaternion.LookRotation(worldDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotationSpeed * dt);
        }

        // ── Dodge ───────────────────────────────────────────────────────────

        private void TryStartDodge()
        {
            if (IsDodging || _dodgeCooldownRemaining > 0f) return;

            Vector2 raw = input != null ? input.MoveInput : Vector2.zero;
            _dodgeDirection = raw.sqrMagnitude > 0.0001f
                ? ProjectInputToCameraPlane(raw)
                : transform.forward;

            _dodgeTimeRemaining     = classDefinition.dodgeDuration;
            _dodgeCooldownRemaining = classDefinition.dodgeCooldown;
        }

        private Vector3 TickDodge(float dt)
        {
            _dodgeTimeRemaining = Mathf.Max(0f, _dodgeTimeRemaining - dt);
            FaceDirection(_dodgeDirection, dt);
            return _dodgeDirection * classDefinition.dodgeSpeed;
        }

        // ── Gravity ─────────────────────────────────────────────────────────

        private void ApplyGravity(float dt)
        {
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;            // small downward bias to stay grounded
            else
                _verticalVelocity += gravity * dt;
        }
    }
}
