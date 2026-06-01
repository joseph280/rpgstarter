using RPGStarter.UI;
using RPGStarter.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RPGStarter.Player
{
    /// <summary>
    /// Listens for the interact key (default <c>E</c>) and triggers the nearest placed
    /// <see cref="PlacedCraftingTable"/> within reach. Kept tiny on purpose — once we
    /// have a real interactable taxonomy this can grow into IInteractable.
    /// </summary>
    public sealed class PlayerInteract : MonoBehaviour
    {
        [SerializeField] private Key interactKey = Key.E;

        [Tooltip("Search radius for nearby interactables. Should match the table's own " +
                 "InteractRadius so the player can't open a table they can't visually reach.")]
        [SerializeField, Min(0.1f)] private float searchRadius = 3f;

        private void Update()
        {
            if (Keyboard.current == null) return;
            if (!Keyboard.current[interactKey].wasPressedThisFrame) return;

            // Don't react to E while the inventory or table UI is open — E might be
            // rebound later as a "close" key, but for now we just bail.
            if (InventoryHUD.IsAnyOpen || CraftingTableUI.IsAnyOpen) return;

            // O(n) scan over the static registry — placed tables are sparse (one or two
            // in a scene). Avoids FindObjectsByType so we stay clear of CLAUDE.md §5.
            var tables = PlacedCraftingTable.All;
            PlacedCraftingTable best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < tables.Count; i++)
            {
                var t = tables[i];
                if (t == null) continue;
                float sqr = (t.transform.position - transform.position).sqrMagnitude;
                if (sqr <= searchRadius * searchRadius && sqr < bestSqr)
                {
                    best    = t;
                    bestSqr = sqr;
                }
            }

            if (best != null && best.IsWithinReach(transform.position))
                best.OpenUI();
        }
    }
}
