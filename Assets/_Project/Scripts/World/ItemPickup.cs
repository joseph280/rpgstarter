using RPGStarter.Data;
using UnityEngine;

namespace RPGStarter.World
{
    /// <summary>
    /// Bobbing, spinning world-space pickup. Carries a count, magnetises toward the
    /// player when nearby, and adds itself to the player's <see cref="Inventory"/> on
    /// contact.
    ///
    /// Polled in Update because pickups are sparse — adding rigidbody + trigger
    /// colliders to the player + every pickup would mean churn on the physics graph
    /// for what's effectively two AABB checks per pickup. <see cref="Inventory.Local"/>
    /// is set on player Awake so the lookup is O(1).
    /// </summary>
    public sealed class ItemPickup : MonoBehaviour
    {
        [Header("Reward")]
        [SerializeField] private ItemDefinitionSO item;
        [SerializeField, Min(1)] private int amount = 1;

        [Header("Behaviour")]
        [Tooltip("Distance at which the pickup snaps into the inventory.")]
        [SerializeField, Min(0.1f)] private float pickupRadius = 1.4f;

        [Tooltip("Distance at which the pickup starts gliding toward the player.")]
        [SerializeField, Min(0f)] private float magnetRadius = 3f;

        [SerializeField, Min(0f)] private float magnetSpeed   = 7f;
        [SerializeField]          private float spinDegPerSec = 90f;
        [SerializeField, Min(0f)] private float bobAmplitude  = 0.12f;
        [SerializeField, Min(0f)] private float bobSpeed      = 2.5f;

        private float _baseY;
        private float _aliveTime;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        public void Configure(ItemDefinitionSO definition, int count)
        {
            item   = definition;
            amount = Mathf.Max(1, count);
            if (verboseLogging)
                Debug.Log($"[ItemPickup] Configured at {transform.position} → item={(item != null ? item.name : "<null>")} amount={amount}");
        }

        private void Start()
        {
            _baseY = transform.position.y;
            if (verboseLogging)
                Debug.Log($"[ItemPickup] Spawned at {transform.position}. Inventory.Local={(Inventory.Local != null ? Inventory.Local.gameObject.name : "<NULL — pickups can't absorb>")}");
        }

        private void Update()
        {
            _aliveTime += Time.deltaTime;

            // Visual bob + spin around the (current) base height.
            Vector3 p = transform.position;
            p.y = _baseY + Mathf.Sin(_aliveTime * bobSpeed) * bobAmplitude;
            transform.position = p;
            transform.Rotate(0f, spinDegPerSec * Time.deltaTime, 0f, Space.Self);

            var inv = Inventory.Local;
            if (inv == null) return;

            Vector3 toPlayer = inv.transform.position - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;

            if (dist <= pickupRadius)
            {
                if (verboseLogging)
                    Debug.Log($"[ItemPickup] Absorbing — within {dist:F2}m of {inv.gameObject.name}. Adding {amount} {(item != null ? item.name : "<null>")}.");
                inv.Add(item, amount);
                Destroy(gameObject);
                return;
            }

            if (dist < magnetRadius && dist > 0.0001f)
            {
                // Magnetise horizontally; preserve the bob's vertical so it doesn't snap to floor.
                Vector3 target = inv.transform.position;
                target.y = transform.position.y;
                transform.position = Vector3.MoveTowards(transform.position, target, magnetSpeed * Time.deltaTime);
                _baseY = transform.position.y; // keep bob centred on the moved Y
            }
        }
    }
}
