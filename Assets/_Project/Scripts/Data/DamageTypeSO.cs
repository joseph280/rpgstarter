using UnityEngine;

namespace Celestia.Data
{
    /// <summary>
    /// Damage type definition (CLAUDE.md §4 — all damage types are SOs).
    /// Used by EnemyDefinitionSO for resistances and by damage numbers for color.
    /// MVP starter set: Physical, Celestia. Designer can add Fire/Cold/etc. as instances.
    /// </summary>
    [CreateAssetMenu(menuName = "Celestia/Damage Type", fileName = "DamageType_New")]
    public sealed class DamageTypeSO : ScriptableObject
    {
        [Header("Identity")]
        public string displayName;

        [TextArea(2, 4)]
        public string description;

        [Header("Presentation")]
        [Tooltip("Tint applied to floating damage numbers and hit VFX.")]
        public Color displayColor = Color.white;

        [Tooltip("Audio played when this damage type lands (optional).")]
        public AudioClip hitSound;
    }
}
