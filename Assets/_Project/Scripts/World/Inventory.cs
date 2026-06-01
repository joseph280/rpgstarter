using RPGStarter.Data;
using UnityEngine;

namespace RPGStarter.World
{
    /// <summary>
    /// Player-side handle on an <see cref="InventorySO"/>. The SO holds the (runtime-only)
    /// counts; this MonoBehaviour exists so:
    ///   * pickups can find the player without scanning the scene — <see cref="Local"/>
    ///     is set in Awake and cleared in OnDestroy;
    ///   * the player prefab carries its inventory reference like any other dependency
    ///     (no Resources.Load, no Find — see CLAUDE.md §5).
    ///
    /// One instance per game (sits on the player). Not a singleton in the GameManager
    /// sense — destroyed with the player and re-created on respawn.
    /// </summary>
    public sealed class Inventory : MonoBehaviour
    {
        [Tooltip("Inventory store. Counts are runtime-only on the SO; reset on domain reload.")]
        [SerializeField] private InventorySO inventory;

        public InventorySO SO => inventory;

        public static Inventory Local { get; private set; }

        private void Awake()
        {
            Local = this;
            if (inventory != null) inventory.ResetState();
            Debug.Log($"[Inventory] Awake on '{gameObject.name}'. SO={(inventory != null ? inventory.name : "<NULL>")}. Inventory.Local now bound.");
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        public void Add(ItemDefinitionSO item, int amount)
        {
            if (inventory == null) { Debug.LogError("[Inventory] Add called but SO ref is null."); return; }
            int before = inventory.GetTotalCount(item);
            inventory.Add(item, amount);
            int after  = inventory.GetTotalCount(item);
            Debug.Log($"[Inventory] +{amount} {(item != null ? item.name : "<null item>")} → {before} → {after}");
        }

        public int GetCount(ItemDefinitionSO item) =>
            inventory != null ? inventory.GetTotalCount(item) : 0;
    }
}
