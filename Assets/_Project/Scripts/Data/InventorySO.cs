using System;
using System.Collections.Generic;
using UnityEngine;

namespace Celestia.Data
{
    /// <summary>
    /// Slot-based inventory store. <paramref name="capacity"/> slots, each slot holds at
    /// most one item type stacked up to that item's <see cref="ItemDefinitionSO.maxStack"/>.
    ///
    /// Per CLAUDE.md §4 the SO doesn't persist runtime state — slots and event listeners
    /// live in [NonSerialized] fields and reset on every domain reload via OnEnable.
    /// </summary>
    [CreateAssetMenu(menuName = "Celestia/Inventory", fileName = "Inventory_New")]
    public sealed class InventorySO : ScriptableObject
    {
        [Serializable]
        public struct StartingItem
        {
            public ItemDefinitionSO item;
            [Min(1)] public int count;
            [Tooltip("Target slot index. -1 = first available (Add path); ≥0 = drop directly into that slot.")]
            public int slotIndex;
        }

        [Serializable]
        public struct Slot
        {
            public ItemDefinitionSO item;
            public int              count;
            public bool IsEmpty => item == null || count <= 0;
        }

        [Header("Capacity")]
        [Tooltip("Number of slots in this inventory. The HUD grid sizes itself to match.")]
        [Min(1)] public int capacity = 40;

        [Header("Starting Items")]
        [Tooltip("What the player has on a fresh play session — typically the weapon roster + zero materials. " +
                 "Counts respect each item's maxStack so a >maxStack starting count splits across slots.")]
        public List<StartingItem> startingItems = new();

        [Header("Tracked Items (HUD compat)")]
        [Tooltip("Items the legacy text HUD lists. The grid HUD walks the slot array directly " +
                 "and ignores this list — kept for backwards-compat with the v1 HUD.")]
        public List<ItemDefinitionSO> trackedItems = new();

        public int Capacity => capacity;

        // Runtime state — explicitly NonSerialized so Unity never writes it to disk
        // even if the editor recompiles mid-play.
        [NonSerialized] private Slot[]        _slots;
        [NonSerialized] private Action<int>   _onSlotChanged;            // (slot index)
        [NonSerialized] private Action<ItemDefinitionSO, int> _onChanged; // (item, total count)

        /// <summary>Fires per-slot mutation. Args: slot index that changed.</summary>
        public event Action<int> OnSlotChanged
        {
            add    => _onSlotChanged += value;
            remove => _onSlotChanged -= value;
        }

        /// <summary>Fires when an item's TOTAL count changes (legacy text HUD).</summary>
        public event Action<ItemDefinitionSO, int> OnChanged
        {
            add    => _onChanged += value;
            remove => _onChanged -= value;
        }

        private void OnEnable()  => ResetState();
        private void OnDisable() { _onSlotChanged = null; _onChanged = null; }

        public void ResetState()
        {
            _slots = new Slot[capacity];
            // Apply starting items. slotIndex >= 0 → drop directly (used to seed the
            // hotbar with weapons in the right keys). slotIndex < 0 → use Add() so the
            // item respects maxStack and spills into multiple slots.
            if (startingItems != null)
            {
                foreach (var s in startingItems)
                {
                    if (s.item == null || s.count <= 0) continue;
                    if (s.slotIndex >= 0 && s.slotIndex < _slots.Length)
                    {
                        _slots[s.slotIndex] = new Slot
                        {
                            item  = s.item,
                            count = Mathf.Min(s.count, Mathf.Max(1, s.item.maxStack)),
                        };
                        _onSlotChanged?.Invoke(s.slotIndex);
                    }
                    else
                    {
                        Add(s.item, s.count);
                    }
                }
            }
        }

        /// <summary>
        /// Moves/swaps/stacks the contents of two slots. Returns true if anything changed.
        ///   * Empty target → moves source into target.
        ///   * Same-item target → tops up target (merges from source up to maxStack), leaving
        ///     overflow in source.
        ///   * Different-item target → swaps the two stacks.
        /// </summary>
        public bool MoveOrSwap(int sourceIndex, int targetIndex)
        {
            if (_slots == null) ResetState();
            if (sourceIndex == targetIndex) return false;
            if (sourceIndex < 0 || sourceIndex >= _slots.Length) return false;
            if (targetIndex < 0 || targetIndex >= _slots.Length) return false;

            var src = _slots[sourceIndex];
            var dst = _slots[targetIndex];
            if (src.IsEmpty) return false;

            if (dst.IsEmpty)
            {
                _slots[targetIndex] = src;
                _slots[sourceIndex] = default;
            }
            else if (dst.item == src.item)
            {
                // Same type — top up dst up to maxStack, keep overflow in src.
                int maxStack = Mathf.Max(1, src.item.maxStack);
                int can = Mathf.Min(maxStack - dst.count, src.count);
                if (can <= 0) return false; // dst already full
                dst.count           += can;
                src.count           -= can;
                _slots[targetIndex] = dst;
                _slots[sourceIndex] = src.count > 0 ? src : default;
            }
            else
            {
                _slots[sourceIndex] = dst;
                _slots[targetIndex] = src;
            }

            _onSlotChanged?.Invoke(sourceIndex);
            _onSlotChanged?.Invoke(targetIndex);
            return true;
        }

        public Slot GetSlot(int index)
        {
            if (_slots == null) ResetState();
            return index >= 0 && index < _slots.Length ? _slots[index] : default;
        }

        /// <summary>Add up to <paramref name="amount"/> of the item. Returns true if all of it fit.</summary>
        public bool Add(ItemDefinitionSO item, int amount)
        {
            if (item == null || amount <= 0) return false;
            if (_slots == null) ResetState();

            int remaining = amount;
            int maxStack  = Mathf.Max(1, item.maxStack);

            // Pass 1 — top up existing partial stacks of the same item.
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i].item == item && _slots[i].count < maxStack)
                {
                    int can = Mathf.Min(maxStack - _slots[i].count, remaining);
                    _slots[i].count += can;
                    remaining        -= can;
                    _onSlotChanged?.Invoke(i);
                }
            }

            // Pass 2 — spill into empty slots, splitting at maxStack boundaries.
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i].IsEmpty)
                {
                    int can = Mathf.Min(maxStack, remaining);
                    _slots[i] = new Slot { item = item, count = can };
                    remaining -= can;
                    _onSlotChanged?.Invoke(i);
                }
            }

            _onChanged?.Invoke(item, GetTotalCount(item));
            return remaining == 0;
        }

        /// <summary>Remove up to <paramref name="amount"/> of the item, draining from any slot
        /// holding it. Returns true if the FULL amount was removed (not enough → no change).</summary>
        public bool Remove(ItemDefinitionSO item, int amount)
        {
            if (item == null || amount <= 0) return false;
            if (_slots == null) ResetState();

            // Don't mutate unless we have enough — partial removal would leave the caller
            // unsure how to recover (e.g. a crafting attempt that consumed half the wood).
            int total = GetTotalCount(item);
            if (total < amount) return false;

            int remaining = amount;
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i].item != item) continue;
                int can = Mathf.Min(_slots[i].count, remaining);
                _slots[i].count -= can;
                remaining        -= can;
                if (_slots[i].count <= 0) _slots[i] = default;
                _onSlotChanged?.Invoke(i);
            }
            _onChanged?.Invoke(item, GetTotalCount(item));
            return true;
        }

        /// <summary>Sum of this item across all slots.</summary>
        public int GetTotalCount(ItemDefinitionSO item)
        {
            if (item == null) return 0;
            if (_slots == null) ResetState();
            int total = 0;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].item == item) total += _slots[i].count;
            return total;
        }

        /// <summary>Backwards-compat alias.</summary>
        public int GetCount(ItemDefinitionSO item) => GetTotalCount(item);
    }
}
