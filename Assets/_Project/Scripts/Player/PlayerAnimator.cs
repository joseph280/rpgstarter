using UnityEngine;

namespace Celestia.Player
{
    /// <summary>
    /// Bridges PlayerMovement state into Animator parameters. Keeps gameplay code
    /// decoupled from Animator parameter strings (centralizes them here).
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public sealed class PlayerAnimator : MonoBehaviour
    {
        private static readonly int SpeedHash    = Animator.StringToHash("Speed");
        private static readonly int IsDodgingHash = Animator.StringToHash("IsDodging");
        private static readonly int HitTrigger   = Animator.StringToHash("Hit");
        private static readonly int DieTrigger   = Animator.StringToHash("Die");

        [SerializeField] private Animator animator;
        [SerializeField, Min(0.01f)] private float speedDamping = 0.1f;

        private PlayerMovement _movement;

        private void Awake()
        {
            _movement = GetComponent<PlayerMovement>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
                if (animator == null)
                    Debug.LogError("[PlayerAnimator] No Animator found on this object or children.");
            }
        }

        private void Update()
        {
            if (animator == null) return;

            float horizontalSpeed = new Vector2(_movement.CurrentVelocity.x, _movement.CurrentVelocity.z).magnitude;
            animator.SetFloat(SpeedHash, horizontalSpeed, speedDamping, Time.deltaTime);
            animator.SetBool (IsDodgingHash, _movement.IsDodging);
        }

        public void PlayHit()  { if (animator != null) animator.SetTrigger(HitTrigger); }
        public void PlayDeath(){ if (animator != null) animator.SetTrigger(DieTrigger); }
    }
}
