using UnityEngine;

namespace RPGStarter.Data
{
    /// <summary>
    /// Definition of a stackable inventory item (e.g. Rock, Wood, or a weapon facing).
    /// Counts live on the runtime <see cref="InventorySO"/> — this asset stays purely
    /// descriptive per CLAUDE.md §4 ("data is data, code is code; never mutate an SO at runtime").
    ///
    /// Items can optionally link back to a <see cref="WeaponDefinitionSO"/> so the inventory
    /// can show the player's weapon roster in the same grid as materials, and so future
    /// "click to equip" work has a hook ready.
    /// </summary>
    [CreateAssetMenu(menuName = "RPGStarter/Item", fileName = "Item_New")]
    public sealed class ItemDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        public string displayName;

        [TextArea(2, 4)]
        public string description;

        [Header("UI")]
        [Tooltip("Sprite shown in the inventory grid. Should be imported as Sprite (2D and UI).")]
        public Sprite icon;

        [Tooltip("Maximum count per slot. 1 = unstackable (typical for weapons), " +
                 "99 = standard for materials. Items beyond this fill into a fresh slot.")]
        [Min(1)] public int maxStack = 99;

        [Header("Linkage")]
        [Tooltip("Optional weapon this item represents. Future use: clicking the slot equips it.")]
        public WeaponDefinitionSO linkedWeapon;
    }
}
