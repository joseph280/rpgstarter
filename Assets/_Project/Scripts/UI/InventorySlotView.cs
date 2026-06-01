using System;
using Celestia.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Celestia.UI
{
    /// <summary>
    /// One cell in the inventory or crafting grid. Bound by <see cref="InventoryHUD"/> /
    /// <see cref="CraftingPanel"/> with the slot's item + count, and reports clicks back
    /// to its owner via <see cref="Init"/>.
    ///
    /// The selection highlight is just an Image overlay this view enables/disables — no
    /// per-slot state machine needed; the owner decides which one's "selected".
    /// </summary>
    public sealed class InventorySlotView : MonoBehaviour
    {
        [SerializeField] private Image            iconImage;
        [SerializeField] private TextMeshProUGUI  countText;
        [SerializeField] private Button           button;
        [SerializeField] private Image            selectionHighlight;
        [SerializeField] private TextMeshProUGUI  numberText;        // hotbar "1"–"8" label, optional

        private Action<int> _onClick;
        private int         _slotIndex = -1;

        /// <summary>Wires this view to a click callback. Called once at HUD/Panel build time.</summary>
        public void Init(int index, Action<int> onClick)
        {
            _slotIndex = index;
            _onClick   = onClick;
            if (button != null)
            {
                // Replace any previous listener so a re-init (HUD rebuild on late-bind)
                // doesn't fire the click N times.
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => _onClick?.Invoke(_slotIndex));
            }
            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (selectionHighlight != null) selectionHighlight.enabled = selected;
        }

        /// <summary>Show a hotkey label in the slot's corner. Pass null to hide it.</summary>
        public void SetSlotNumber(string label)
        {
            if (numberText == null) return;
            if (string.IsNullOrEmpty(label))
            {
                numberText.gameObject.SetActive(false);
            }
            else
            {
                numberText.gameObject.SetActive(true);
                numberText.text = label;
            }
        }

        public void Bind(InventorySO.Slot slot)
        {
            if (slot.IsEmpty)
            {
                if (iconImage != null)
                {
                    iconImage.sprite  = null;
                    iconImage.enabled = false;
                }
                if (countText != null) countText.text = string.Empty;
                return;
            }

            if (iconImage != null)
            {
                iconImage.enabled = true;
                iconImage.sprite  = slot.item != null ? slot.item.icon : null;
                // If an item has no icon yet, leave the image enabled but tinted half so
                // designers can spot un-iconned items at a glance.
                iconImage.color   = slot.item != null && slot.item.icon != null
                    ? Color.white
                    : new Color(1f, 1f, 1f, 0.35f);
            }

            if (countText != null)
                countText.text = slot.count > 1 ? slot.count.ToString() : string.Empty;
        }
    }
}
