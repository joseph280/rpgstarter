using RPGStarter.Data;
using RPGStarter.World;
using UnityEngine;
using UnityEngine.UI;

namespace RPGStarter.UI
{
    /// <summary>
    /// Crafting bar above the inventory grid: 4 input slots → arrow → 1 output slot
    /// + a Craft button.
    ///
    /// Click model:
    ///   * Player clicks an item in the inventory → <see cref="InventoryHUD"/> caches that
    ///     item as <see cref="InventoryHUD.SelectedItem"/>.
    ///   * Player clicks an empty input slot → that item lands in the slot (1 instance).
    ///     Clicking an already-filled slot clears it.
    ///   * Player clicks Craft → recipes are scanned for a match against the inputs.
    ///     On a match, the inventory is checked + drained, the output slot fills, the
    ///     inputs clear.
    ///   * Player clicks the output slot → its contents land in the inventory.
    ///
    /// Crafting boxes hold "ghosts" — they don't reserve from inventory until Craft is
    /// pressed, so the player can experiment without losing materials to a typo.
    /// </summary>
    public sealed class CraftingPanel : MonoBehaviour
    {
        /// <summary>Default input count for the inventory crafting bar (4 slots in a row).
        /// The 3×3 table panel uses a different inputSlotViews length — actual size is
        /// derived from the assigned array, not this constant.</summary>
        public const int DEFAULT_INPUT_COUNT = 4;

        [Header("Refs")]
        [SerializeField] private InventorySO         inventory;
        [SerializeField] private InventoryHUD        inventoryHud;     // for SelectedItem
        [SerializeField] private InventorySlotView[] inputSlotViews;   // any length: 4 (bar) or 9 (table)
        [SerializeField] private InventorySlotView   outputSlotView;
        [SerializeField] private Button              craftButton;
        [SerializeField] private RecipeSO[]          recipes;

        // Sized at Awake from inputSlotViews.Length so the panel works for both the
        // inventory's 4-cell crafting bar and the placed-table's 3×3 grid.
        private ItemDefinitionSO[] _inputs;
        private ItemDefinitionSO   _outputItem;
        private int                _outputCount;

        private void Awake()
        {
            int inputCount = inputSlotViews != null ? inputSlotViews.Length : 0;
            _inputs = new ItemDefinitionSO[inputCount];

            for (int i = 0; i < inputCount; i++)
            {
                int idx = i;
                if (inputSlotViews[i] != null)
                    inputSlotViews[i].Init(idx, OnInputSlotClicked);
            }

            if (outputSlotView != null)
                outputSlotView.Init(0, _ => OnOutputSlotClicked());

            if (craftButton != null)
            {
                craftButton.onClick.RemoveAllListeners();
                craftButton.onClick.AddListener(OnCraftButtonClicked);
            }
        }

        private void Start()
        {
            // Late-bind inventory the same way InventoryHUD does — handles the case where
            // the scene's serialized ref is null because Bootstrap was patched by an older
            // build of this script.
            if (inventory == null && Inventory.Local != null) inventory = Inventory.Local.SO;
            RefreshInputs();
            RefreshOutput();
        }

        private void Update()
        {
            // Same fallback for cases where Inventory MB came alive after our Start fired.
            if (inventory == null && Inventory.Local != null)
            {
                inventory = Inventory.Local.SO;
                RefreshInputs();
                RefreshOutput();
            }
        }

        // ── Click handlers ───────────────────────────────────────────────────

        public void OnInputSlotClicked(int slotIndex)
        {
            if (_inputs == null || slotIndex < 0 || slotIndex >= _inputs.Length) return;

            var selected = inventoryHud != null ? inventoryHud.SelectedItem : null;
            // With a selection: place it (overwriting anything already there).
            // Without a selection: clear the box. Lets the player undo a misclick without
            // having to first select something else.
            _inputs[slotIndex] = selected;
            RefreshInput(slotIndex);
            Debug.Log($"[CraftingPanel] Input slot {slotIndex} ← {(selected != null ? selected.name : "<cleared>")}");
        }

        public void OnCraftButtonClicked()
        {
            if (inventory == null)
            {
                Debug.LogWarning("[CraftingPanel] No inventory bound — Craft can't run.");
                return;
            }
            if (_outputItem != null)
            {
                Debug.LogWarning("[CraftingPanel] Output slot is occupied — pick it up first.");
                return;
            }

            RecipeSO matched = null;
            if (recipes != null)
            {
                for (int i = 0; i < recipes.Length; i++)
                    if (recipes[i] != null && recipes[i].Matches(_inputs)) { matched = recipes[i]; break; }
            }
            if (matched == null)
            {
                Debug.Log("[CraftingPanel] No recipe matches the current inputs.");
                return;
            }

            // Validate inventory before mutating it — partial drain on a failed craft would
            // leave the player worse off than before.
            for (int i = 0; i < matched.inputs.Length; i++)
            {
                var ing = matched.inputs[i];
                if (ing.item == null) continue;
                if (inventory.GetTotalCount(ing.item) < ing.count)
                {
                    Debug.LogWarning($"[CraftingPanel] Not enough {ing.item.name} in inventory " +
                                     $"({inventory.GetTotalCount(ing.item)} < {ing.count}).");
                    return;
                }
            }

            // All checks passed — consume + produce.
            for (int i = 0; i < matched.inputs.Length; i++)
            {
                var ing = matched.inputs[i];
                if (ing.item != null) inventory.Remove(ing.item, ing.count);
            }
            _outputItem  = matched.outputItem;
            _outputCount = matched.outputCount;

            if (_inputs != null) for (int i = 0; i < _inputs.Length; i++) _inputs[i] = null;
            RefreshInputs();
            RefreshOutput();

            // Drop the player's current selection — the stack they'd selected may now be
            // empty, and the highlight on it would be stale.
            if (inventoryHud != null) inventoryHud.ClearSelection();

            Debug.Log($"[CraftingPanel] Crafted {matched.outputCount}× {matched.outputItem.name}.");
        }

        public void OnOutputSlotClicked()
        {
            if (_outputItem == null || _outputCount <= 0) return;
            if (inventory == null) return;

            if (inventory.Add(_outputItem, _outputCount))
            {
                _outputItem  = null;
                _outputCount = 0;
                RefreshOutput();
            }
            else
            {
                Debug.LogWarning("[CraftingPanel] Inventory full — couldn't add craft output.");
            }
        }

        // ── View refresh ─────────────────────────────────────────────────────

        private void RefreshInput(int i)
        {
            if (_inputs == null || i < 0 || i >= _inputs.Length) return;
            if (inputSlotViews == null || i >= inputSlotViews.Length || inputSlotViews[i] == null) return;
            var slot = new InventorySO.Slot
            {
                item  = _inputs[i],
                count = _inputs[i] != null ? 1 : 0,
            };
            inputSlotViews[i].Bind(slot);
        }

        private void RefreshInputs()
        {
            if (_inputs == null) return;
            for (int i = 0; i < _inputs.Length; i++) RefreshInput(i);
        }

        private void RefreshOutput()
        {
            if (outputSlotView == null) return;
            var slot = new InventorySO.Slot
            {
                item  = _outputItem,
                count = _outputCount,
            };
            outputSlotView.Bind(slot);
        }
    }
}
