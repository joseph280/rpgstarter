using UnityEngine;

namespace Celestia.World
{
    /// <summary>
    /// One-shot chip burst: spawns N small mesh fragments with a rigidbody, applies an
    /// outward+upward impulse, and self-destroys after a delay.
    ///
    /// Variants used in W3:
    ///   * RockChipBurst_Hit / WoodChipBurst_Hit — small/few/short, every successful hit
    ///   * RockChipBurst_Break / WoodChipBurst_Break — larger/more/longer, on shatter
    ///
    /// Chunks intentionally have NO collider. Earlier passes added BoxColliders so chunks
    /// could land on the floor, but they also collided with the player's CharacterController
    /// — every per-hit burst kicked the CC vertically (player visibly "jumped" mid-swing).
    /// Without colliders the chunks just gravity-fall through the floor after their arc, no
    /// CC interaction. By the time they'd reach the floor their lifetime has expired anyway.
    ///
    /// Chunks are unparented at spawn so the burst's own Destroy() doesn't take them out
    /// before they finish falling. Each chunk gets its own Destroy(go, lifetime).
    /// </summary>
    public sealed class RockChipBurst : MonoBehaviour
    {
        [Header("Chunk visual")]
        [Tooltip("Mesh used for each chunk. Cube/sphere primitives or a tiny rock fragment work; " +
                 "the W3_AssetBuilder reuses the source rock's mesh so chips read as 'pieces of this rock'.")]
        [SerializeField] private Mesh chunkMesh;
        [SerializeField] private Material chunkMaterial;

        [Header("Burst")]
        [SerializeField, Min(1)] private int   chunkCount      = 6;
        [SerializeField, Min(0f)] private float chunkScaleMin  = 0.06f;
        [SerializeField, Min(0f)] private float chunkScaleMax  = 0.14f;
        [SerializeField, Min(0f)] private float burstSpeed     = 4f;
        [Tooltip("Bias added to a unit-sphere direction along +Y. >0 = chunks fly up before falling.")]
        [SerializeField, Min(0f)] private float burstUpBias    = 1.2f;
        [SerializeField, Min(0f)] private float spawnRadius    = 0.15f;
        [SerializeField, Min(0.1f)] private float chunkLifetime = 1.6f;

        [Header("Self")]
        [SerializeField, Min(0.05f)] private float despawnDelay = 0.1f;

        private void Start()
        {
            for (int i = 0; i < chunkCount; i++) SpawnChunk();
            Destroy(gameObject, despawnDelay);
        }

        private void SpawnChunk()
        {
            var go = new GameObject("Chunk");
            // Don't parent — the burst object Destroy()s itself almost immediately. Parented
            // chunks would get killed with it before they could fall.
            go.transform.position = transform.position + Random.insideUnitSphere * spawnRadius;
            go.transform.rotation = Random.rotation;
            float scale = Random.Range(chunkScaleMin, chunkScaleMax);
            go.transform.localScale = Vector3.one * scale;
            go.layer = gameObject.layer;

            if (chunkMesh != null)
            {
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = chunkMesh;
                var mr = go.AddComponent<MeshRenderer>();
                if (chunkMaterial != null) mr.sharedMaterial = chunkMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // tiny + ephemeral, no shadow
            }

            // No collider — see class doc. Rigidbody-only so gravity arcs the chunk; it
            // disappears below the floor at lifetime-end and the player never feels it.
            var rb = go.AddComponent<Rigidbody>();

            Vector3 dir = (Random.onUnitSphere + Vector3.up * burstUpBias).normalized;
            rb.linearVelocity   = dir * burstSpeed;
            rb.angularVelocity  = Random.insideUnitSphere * 8f;

            Destroy(go, chunkLifetime);
        }
    }
}
