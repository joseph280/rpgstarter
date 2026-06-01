using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPGStarter.World
{
    /// <summary>
    /// In-world crafting table the player has dropped via the place-ability path. Owns
    /// nothing more than a "is the player close enough to interact" check + a static
    /// event the UI subscribes to.
    ///
    /// The 3×3 crafting UI lives in Bootstrap (built once, persistent). When the player
    /// presses the interact key while inside <see cref="interactRadius"/>, this component
    /// raises <see cref="OnAnyInteract"/>; the UI panel toggles itself on.
    ///
    /// Maintains a static registry (<see cref="All"/>) so PlayerInteract can find nearby
    /// tables without calling <c>FindObjectsByType</c> on every keypress (CLAUDE.md §5).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlacedCraftingTable : MonoBehaviour
    {
        [Tooltip("Maximum distance from the player at which interact opens this table.")]
        [SerializeField, Min(0.1f)] private float interactRadius = 2.5f;

        public float InteractRadius => interactRadius;

        /// <summary>Raised when any placed table opens its UI. Args: the table that fired.</summary>
        public static event Action<PlacedCraftingTable> OnAnyInteract;

        // Registry — populated by OnEnable, cleared by OnDisable / OnDestroy. Replaces
        // FindObjectsByType on every interact press.
        private static readonly List<PlacedCraftingTable> s_all = new();
        public static IReadOnlyList<PlacedCraftingTable> All => s_all;

        private void OnEnable()  { if (!s_all.Contains(this)) s_all.Add(this); }
        private void OnDisable() => s_all.Remove(this);

        public bool IsWithinReach(Vector3 worldPoint) =>
            (worldPoint - transform.position).sqrMagnitude <= interactRadius * interactRadius;

        public void OpenUI() => OnAnyInteract?.Invoke(this);
    }
}
