using RPGStarter.Data;
using RPGStarter.UI;
using RPGStarter.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RPGStarter.Player
{
    /// <summary>
    /// Bridges the inventory's hotbar row (last 10 slots by default) to the equipped weapon.
    /// Pressing digits 1–9 + 0 selects the corresponding hotbar slot; if it holds an item with
    /// a <see cref="ItemDefinitionSO.linkedWeapon"/>, that weapon goes onto
    /// <see cref="WeaponEquipment"/>. Empty / non-usable slots leave the player unarmed
    /// (or holding the previous weapon, depending on <see cref="unequipOnEmpty"/>).
    ///
    /// The HUD owns where the hotbar lives in the inventory layout — this MB just reads
    /// the same slot indices via <see cref="InventoryHUD.HotbarStartIndex"/>.
    /// </summary>
    public sealed class Hotbar : MonoBehaviour
    {
        [SerializeField] private InventorySO     inventory;
        [SerializeField] private WeaponEquipment equipment;
        [SerializeField] private InventoryHUD    hud;       // for hotbar start index/size

        [Tooltip("If the selected hotbar slot is empty / non-usable, also unequip the " +
                 "currently held weapon. Off = keep the last weapon equipped.")]
        [SerializeField] private bool unequipOnEmpty = true;

        public int CurrentHotbarSlot { get; private set; } = -1;

        private int HotbarStart => hud != null ? hud.HotbarStartIndex : 40;
        private int HotbarSize  => hud != null ? hud.HotbarSize       : 10;

        private void Start()
        {
            // Late-bind: the player MB might wake before InventoryHUD/Inventory.Local.
            EnsureInventory();
            // Auto-select hotbar slot 0 so the player starts with whatever's in slot 32.
            if (inventory != null) SelectHotbarSlot(0);
        }

        private void OnEnable()
        {
            if (inventory != null) inventory.OnSlotChanged += OnInventorySlotChanged;
        }

        private void OnDisable()
        {
            if (inventory != null) inventory.OnSlotChanged -= OnInventorySlotChanged;
        }

        private void Update()
        {
            EnsureInventory();
            if (Keyboard.current == null) return;

            // Don't intercept digit keys while the inventory panel is open — the player
            // might be typing in a chat-like UI in the future, and right now they'd
            // accidentally re-equip while shifting items around.
            if (InventoryHUD.IsAnyOpen) return;

            // 1-9 = first nine slots, 0 = tenth. Standard hotbar mapping in this genre.
            if (Keyboard.current.digit1Key.wasPressedThisFrame) SelectHotbarSlot(0);
            if (Keyboard.current.digit2Key.wasPressedThisFrame) SelectHotbarSlot(1);
            if (Keyboard.current.digit3Key.wasPressedThisFrame) SelectHotbarSlot(2);
            if (Keyboard.current.digit4Key.wasPressedThisFrame) SelectHotbarSlot(3);
            if (Keyboard.current.digit5Key.wasPressedThisFrame) SelectHotbarSlot(4);
            if (Keyboard.current.digit6Key.wasPressedThisFrame) SelectHotbarSlot(5);
            if (Keyboard.current.digit7Key.wasPressedThisFrame) SelectHotbarSlot(6);
            if (Keyboard.current.digit8Key.wasPressedThisFrame) SelectHotbarSlot(7);
            if (Keyboard.current.digit9Key.wasPressedThisFrame) SelectHotbarSlot(8);
            if (Keyboard.current.digit0Key.wasPressedThisFrame) SelectHotbarSlot(9);
        }

        public void SelectHotbarSlot(int hotbarIndex)
        {
            if (hotbarIndex < 0 || hotbarIndex >= HotbarSize) return;
            EnsureInventory();
            if (inventory == null || equipment == null) return;

            CurrentHotbarSlot = hotbarIndex;
            int absolute = HotbarStart + hotbarIndex;
            var slot = inventory.GetSlot(absolute);

            if (slot.IsEmpty || slot.item == null || slot.item.linkedWeapon == null)
            {
                if (unequipOnEmpty) equipment.Unequip();
                return;
            }
            equipment.EquipDef(slot.item.linkedWeapon);
        }

        private void OnInventorySlotChanged(int slotIndex)
        {
            // If the held hotbar slot's contents changed (item picked up there, swap into
            // it, etc.) re-equip so the held visual + ability stay in sync.
            int relative = slotIndex - HotbarStart;
            if (relative >= 0 && relative < HotbarSize && relative == CurrentHotbarSlot)
                SelectHotbarSlot(CurrentHotbarSlot);
        }

        private void EnsureInventory()
        {
            if (inventory != null) return;
            var local = Inventory.Local;
            if (local != null && local.SO != null)
            {
                inventory = local.SO;
                inventory.OnSlotChanged += OnInventorySlotChanged;
            }
        }
    }
}
