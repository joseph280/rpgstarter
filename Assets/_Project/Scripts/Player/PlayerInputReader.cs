using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RPGStarter.Player
{
    /// <summary>
    /// Reads the PlayerInput action asset and surfaces its values as plain C# events
    /// + properties. Other player components (PlayerMovement, ability casters) listen
    /// here instead of touching the Input System directly — keeps rebinding/replacement
    /// to one place.
    /// </summary>
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [Header("Input Asset")]
        [SerializeField] private InputActionAsset inputAsset;
        [SerializeField] private string actionMapName = "Gameplay";

        public Vector2 MoveInput { get; private set; }
        public bool    AimHeld   { get; private set; }

        public event Action OnDodgePressed;
        public event Action OnBasicAttackPressed;
        public event Action OnAbility1Pressed;
        public event Action OnAbility2Pressed;
        public event Action OnAbility3Pressed;
        public event Action OnAbility4Pressed;
        public event Action OnPausePressed;

        private InputActionMap _map;
        private InputAction _move, _aim, _dodge, _basicAttack;
        private InputAction _ability1, _ability2, _ability3, _ability4;
        private InputAction _pause;

        private void Awake()
        {
            if (inputAsset == null)
            {
                Debug.LogError("[PlayerInputReader] inputAsset not assigned.");
                enabled = false;
                return;
            }

            _map = inputAsset.FindActionMap(actionMapName, throwIfNotFound: true);

            _move        = _map.FindAction("Move",        throwIfNotFound: true);
            _aim         = _map.FindAction("Aim",         throwIfNotFound: true);
            _dodge       = _map.FindAction("Dodge",       throwIfNotFound: true);
            _basicAttack = _map.FindAction("BasicAttack", throwIfNotFound: true);
            _ability1    = _map.FindAction("Ability1",    throwIfNotFound: true);
            _ability2    = _map.FindAction("Ability2",    throwIfNotFound: true);
            _ability3    = _map.FindAction("Ability3",    throwIfNotFound: true);
            _ability4    = _map.FindAction("Ability4",    throwIfNotFound: true);
            _pause       = _map.FindAction("Pause",       throwIfNotFound: true);
        }

        private void OnEnable()
        {
            if (_map == null) return;

            _move.performed += OnMove;
            _move.canceled  += OnMove;

            _aim.performed += OnAim;
            _aim.canceled  += OnAim;

            _dodge.performed       += _ => OnDodgePressed?.Invoke();
            _basicAttack.performed += _ => OnBasicAttackPressed?.Invoke();
            _ability1.performed    += _ => OnAbility1Pressed?.Invoke();
            _ability2.performed    += _ => OnAbility2Pressed?.Invoke();
            _ability3.performed    += _ => OnAbility3Pressed?.Invoke();
            _ability4.performed    += _ => OnAbility4Pressed?.Invoke();
            _pause.performed       += _ => OnPausePressed?.Invoke();

            _map.Enable();
        }

        private void OnDisable()
        {
            if (_map == null) return;

            _move.performed -= OnMove;
            _move.canceled  -= OnMove;
            _aim.performed  -= OnAim;
            _aim.canceled   -= OnAim;

            _map.Disable();
        }

        private void OnMove(InputAction.CallbackContext ctx) => MoveInput = ctx.ReadValue<Vector2>();
        private void OnAim (InputAction.CallbackContext ctx) => AimHeld   = ctx.ReadValueAsButton();
    }
}
