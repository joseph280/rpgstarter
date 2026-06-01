using RPGStarter.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RPGStarter.UI
{
    /// <summary>
    /// Unified crafting screen for placed crafting tables. The table's own canvas contains
    /// everything in one window: 3×3 crafting bar + inventory grid + hotbar — no overlap
    /// with the regular inventory canvas, which is hidden while a placed table is active.
    ///
    /// Reuses <see cref="CraftingPanel"/> for recipe matching and a secondary
    /// <see cref="InventoryHUD"/> instance (with <c>ownsGlobalState = false</c>) for the
    /// in-table slot grid + hotbar. CraftingPanel.inventoryHud points at the secondary
    /// instance so selection clicks flow into the table's 3×3.
    ///
    /// Closes automatically if the player walks past <see cref="closeRadius"/> from the
    /// table they opened, or hits Esc, or clicks the on-screen close button.
    /// </summary>
    public sealed class CraftingTableUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private GameObject     panel;
        [SerializeField] private Button         closeButton;
        // Regular inventory canvas's HUD. Hidden while the table is open so the player
        // sees one unified window, not two stacked panels.
        [SerializeField] private InventoryHUD   inventoryHud;
        // Forward-compat ref so future logic (clear inputs on open, etc.) can drive the
        // panel directly. Currently informational — the CraftingPanel manages its own
        // state and re-binds on enable.
        [SerializeField] private CraftingPanel  craftingPanel;

        [Header("Auto-close")]
        [Tooltip("Player must stay within this many metres of the active table or the UI " +
                 "auto-closes. Should be larger than the table's InteractRadius so the player " +
                 "doesn't lose the panel from a small step backwards.")]
        [SerializeField, Min(0.5f)] private float closeRadius = 4f;

        public static bool IsAnyOpen { get; private set; }

        private PlacedCraftingTable _activeTable;

        private void Awake()
        {
            if (panel == null && transform.childCount > 0) panel = transform.GetChild(0).gameObject;
            if (panel != null) panel.SetActive(false);

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }
        }

        private void OnEnable()  => PlacedCraftingTable.OnAnyInteract += OnAnyTableInteract;
        private void OnDisable()
        {
            PlacedCraftingTable.OnAnyInteract -= OnAnyTableInteract;
            IsAnyOpen   = false;
            _activeTable = null;
        }

        private void Update()
        {
            if (!IsAnyOpen || panel == null) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            // Auto-close when the player wanders away. Reads the player's position from
            // Inventory.Local — same handle pickups use, so no extra cross-system ref.
            if (_activeTable == null) return;
            var inv = Inventory.Local;
            if (inv == null) return;
            float sqr = (_activeTable.transform.position - inv.transform.position).sqrMagnitude;
            if (sqr > closeRadius * closeRadius) Close();
        }

        private void OnAnyTableInteract(PlacedCraftingTable table)
        {
            if (panel == null) return;
            _activeTable = table;
            // Hide the regular inventory canvas so the player only sees this unified
            // window — the table panel has its own inventory + hotbar section built in.
            if (inventoryHud != null) inventoryHud.ClosePanel();
            panel.SetActive(true);
            IsAnyOpen = true;
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
            IsAnyOpen    = false;
            _activeTable = null;
            // Regular inventory was hidden on open and stays hidden. Player presses I
            // to reopen it separately.
        }
    }
}
