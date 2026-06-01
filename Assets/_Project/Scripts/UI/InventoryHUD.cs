using RPGStarter.Data;
using RPGStarter.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RPGStarter.UI
{
    /// <summary>
    /// 40-slot inventory grid HUD, toggled with the <c>I</c> key. Builds slot views from
    /// <see cref="slotViewPrefab"/> at runtime so the count can change without rebuilding
    /// the scene.
    ///
    /// Late-bind fallback: if the scene's serialized <see cref="inventory"/> ref is null
    /// (Bootstrap was patched by an older builder that wrote the wrong field type), pulls
    /// the SO from <c>Inventory.Local.SO</c> on the next Update tick where the player MB
    /// has come alive. Stops the dreaded "press I, nothing happens" loop after a half-baked
    /// rebuild.
    /// </summary>
    public sealed class InventoryHUD : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private InventorySO    inventory;
        [SerializeField] private GameObject     panel;                // root toggled visible/hidden
        [SerializeField] private RectTransform  slotsContainer;       // main inventory grid parent
        [SerializeField] private RectTransform  hotbarContainer;      // hotbar row parent (optional)
        [SerializeField] private GameObject     slotViewPrefab;       // contains InventorySlotView
        [SerializeField] private GameObject     craftingBarSection;   // optional 4-slot bar; hidden while a table UI is up

        [Header("Hotbar")]
        [Tooltip("First inventory slot index that lives in the hotbar (last 10 by default). " +
                 "Slots from this index onward render in hotbarContainer + carry number labels " +
                 "+ refuse non-usable items.")]
        [SerializeField, Min(0)] private int hotbarStartIndex = 40;

        [Tooltip("How many hotbar slots — must match the digit keys we wire (1-9 + 0 = 10 slots).")]
        [SerializeField, Min(1)] private int hotbarSize = 10;

        [Header("Behaviour")]
        [SerializeField] private bool startVisible = false;
        [SerializeField] private Key  toggleKey    = Key.I;
        [Tooltip("Whether this HUD listens for the toggleKey and manages the global " +
                 "IsAnyOpen flag. The regular inventory canvas needs true; the secondary " +
                 "instance on the crafting-table canvas needs false (so I doesn't toggle " +
                 "it while a placed table's panel is up).")]
        [SerializeField] private bool ownsGlobalState = true;

        // Static "is any panel open" — PlayerCombat reads this to suppress attacks while
        // the player is interacting with UI.
        public static bool IsAnyOpen { get; private set; }
        public bool       IsOpen     => panel != null && panel.activeInHierarchy;
        public int        HotbarStartIndex => hotbarStartIndex;
        public int        HotbarSize       => hotbarSize;
        public bool IsHotbarSlot(int slotIndex) =>
            slotIndex >= hotbarStartIndex && slotIndex < hotbarStartIndex + hotbarSize;

        private InventorySlotView[] _slotViews;
        private bool _hookedInventory;

        // Selection — exposed so CraftingPanel can read which item the player picked.
        public ItemDefinitionSO SelectedItem { get; private set; }
        private int _selectedSlotIndex = -1;

        private void Awake()
        {
            // Defensive auto-bind: if the scene patcher didn't serialize the panel ref
            // (e.g. the Bootstrap scene was built before this script existed in current
            // form), fall back to the first child.
            if (panel == null && transform.childCount > 0)
                panel = transform.GetChild(0).gameObject;
        }

        private void Start()
        {
            EnsureInventoryHooked();
            if (panel != null) panel.SetActive(startVisible);
            BuildSlotViews();
            RefreshAll();
        }

        private void OnDestroy()
        {
            if (inventory != null && _hookedInventory)
                inventory.OnSlotChanged -= OnSlotChanged;
        }

        private void Update()
        {
            // Late-bind in case the player Inventory MB came alive after our Start fired
            // (Bootstrap → additive scene load order can run the HUD before the player).
            if (inventory == null) EnsureInventoryHooked();

            // Secondary HUDs (table canvas) ignore the toggle key; their visibility is
            // driven by CraftingTableUI rather than I.
            if (!ownsGlobalState) return;

            if (Keyboard.current == null) return;

            // Crafting-table UI owns the inventory panel while it's up — pressing I
            // shouldn't let the player accidentally hide the items they're picking from.
            if (CraftingTableUI.IsAnyOpen) return;

            if (!Keyboard.current[toggleKey].wasPressedThisFrame) return;

            if (panel == null)
            {
                Debug.LogError("[InventoryHUD] I pressed but no panel bound — re-run RPGStarter/W3/4.");
                return;
            }

            if (panel.activeSelf) ClosePanel();
            else                  OpenPanel(includeCraftingBar: true);
            Debug.Log($"[InventoryHUD] {toggleKey} pressed — panel now {(panel.activeSelf ? "VISIBLE" : "HIDDEN")}.");
        }

        /// <summary>Programmatically show the panel.
        /// <paramref name="includeCraftingBar"/> = false hides the inventory's own 4-slot
        /// crafting bar — used when CraftingTableUI is up so the two crafting UIs don't
        /// stack visually (the table's 3×3 grid replaces the bar's role).</summary>
        public void OpenPanel(bool includeCraftingBar = true)
        {
            if (panel == null) return;
            SetCraftingBarVisible(includeCraftingBar);
            if (panel.activeSelf) { RefreshAll(); if (ownsGlobalState) IsAnyOpen = true; return; }
            panel.SetActive(true);
            if (ownsGlobalState) IsAnyOpen = true;
            RefreshAll();
        }

        /// <summary>Programmatic counterpart of <see cref="OpenPanel"/>. Always clears selection
        /// and restores the crafting bar so a subsequent I-key open shows it again.</summary>
        public void ClosePanel()
        {
            if (panel == null) return;
            panel.SetActive(false);
            if (ownsGlobalState) IsAnyOpen = false;
            ClearSelection();
            SetCraftingBarVisible(true); // restore for next open
        }

        private void SetCraftingBarVisible(bool visible)
        {
            if (craftingBarSection != null) craftingBarSection.SetActive(visible);
        }

        private void OnDisable()
        {
            // Defensive: if the canvas itself goes inactive (shouldn't but…), don't leave
            // the global IsAnyOpen flag stuck on. Only the primary HUD touches IsAnyOpen.
            if (ownsGlobalState && IsOpen) IsAnyOpen = false;
        }

        // ── Inventory binding ────────────────────────────────────────────────

        private void EnsureInventoryHooked()
        {
            if (inventory == null)
            {
                var local = Inventory.Local;
                if (local != null && local.SO != null)
                {
                    inventory = local.SO;
                    Debug.Log($"[InventoryHUD] Late-bound inventory from Inventory.Local → {inventory.name}");
                    BuildSlotViews(); // capacity may differ from whatever was wired before
                    RefreshAll();
                }
            }

            if (inventory != null && !_hookedInventory)
            {
                inventory.OnSlotChanged += OnSlotChanged;
                _hookedInventory = true;
            }
        }

        // ── Grid build ───────────────────────────────────────────────────────

        private void BuildSlotViews()
        {
            if (slotsContainer == null || slotViewPrefab == null || inventory == null) return;

            // Wipe both containers — handles re-build on late-bind with a different SO.
            for (int i = slotsContainer.childCount - 1; i >= 0; i--)
                Destroy(slotsContainer.GetChild(i).gameObject);
            if (hotbarContainer != null)
                for (int i = hotbarContainer.childCount - 1; i >= 0; i--)
                    Destroy(hotbarContainer.GetChild(i).gameObject);

            int n = inventory.Capacity;
            _slotViews = new InventorySlotView[n];
            for (int i = 0; i < n; i++)
            {
                bool isHotbar = IsHotbarSlot(i) && hotbarContainer != null;
                var parent = isHotbar ? hotbarContainer : slotsContainer;
                var go = Instantiate(slotViewPrefab, parent);
                go.name = isHotbar ? $"Hotbar_{i - hotbarStartIndex + 1}" : $"Slot_{i:D2}";

                var view = go.GetComponent<InventorySlotView>();
                if (view == null)
                {
                    Debug.LogError($"[InventoryHUD] slotViewPrefab '{slotViewPrefab.name}' has no InventorySlotView component.");
                    continue;
                }
                view.Init(i, OnInventorySlotClicked);
                if (isHotbar)
                {
                    // 1-9 then 0 (matches the 10-key hotbar binding 1234567890).
                    int hotbarPos = i - hotbarStartIndex;
                    view.SetSlotNumber(hotbarPos < 9 ? (hotbarPos + 1).ToString() : "0");
                }
                else view.SetSlotNumber(null);
                _slotViews[i] = view;
            }
        }

        private void OnInventorySlotClicked(int slotIndex)
        {
            if (inventory == null) return;
            var clickedSlot = inventory.GetSlot(slotIndex);

            // Click on the already-selected slot → deselect.
            if (_selectedSlotIndex == slotIndex)
            {
                ClearSelection();
                return;
            }

            // No active selection → start one (only if the clicked slot has something).
            if (_selectedSlotIndex < 0)
            {
                if (clickedSlot.IsEmpty) return;
                _selectedSlotIndex = slotIndex;
                SelectedItem       = clickedSlot.item;
                UpdateSelectionVisuals();
                Debug.Log($"[InventoryHUD] Selected slot {slotIndex} ({SelectedItem.name})");
                return;
            }

            // Have a selection + clicked a different slot → move/swap/stack.
            var sourceSlot = inventory.GetSlot(_selectedSlotIndex);
            if (sourceSlot.IsEmpty)
            {
                ClearSelection();
                return;
            }

            // Hotbar gating: only items with a linkedWeapon are "usable" — refuse to move
            // raw materials (rocks, wood) into hotbar slots.
            if (IsHotbarSlot(slotIndex) && sourceSlot.item.linkedWeapon == null)
            {
                Debug.Log($"[InventoryHUD] '{sourceSlot.item.displayName}' isn't usable — only weapons / placeables go in the hotbar.");
                return;
            }
            // The reverse: can't pull a usable INTO the hotbar from a hotbar slot if the
            // target is the main inventory? That should be allowed (player removing weapon
            // from hotbar). No restriction needed.

            inventory.MoveOrSwap(_selectedSlotIndex, slotIndex);
            ClearSelection();
        }

        private void UpdateSelectionVisuals()
        {
            if (_slotViews == null) return;
            for (int i = 0; i < _slotViews.Length; i++)
                if (_slotViews[i] != null) _slotViews[i].SetSelected(i == _selectedSlotIndex);
        }

        /// <summary>Programmatic clear — called after a craft consumes the selected stack.</summary>
        public void ClearSelection()
        {
            SelectedItem       = null;
            _selectedSlotIndex = -1;
            UpdateSelectionVisuals();
        }

        private void OnSlotChanged(int slotIndex)
        {
            if (_slotViews == null || slotIndex < 0 || slotIndex >= _slotViews.Length) return;
            if (_slotViews[slotIndex] != null && inventory != null)
                _slotViews[slotIndex].Bind(inventory.GetSlot(slotIndex));
        }

        private void RefreshAll()
        {
            if (_slotViews == null || inventory == null) return;
            for (int i = 0; i < _slotViews.Length; i++)
            {
                if (_slotViews[i] != null) _slotViews[i].Bind(inventory.GetSlot(i));
            }
        }
    }
}
