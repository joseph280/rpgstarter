using UnityEngine;

namespace Celestia.Data
{
    /// <summary>
    /// Tag-like SO marking a weapon as a particular kind of tool (Pickaxe, Hammer, Axe, …).
    /// Per CLAUDE.md §4 we use SO instances instead of an enum so designers can add new
    /// tool kinds without code changes — and so other systems (RockMineable, future Lumber
    /// nodes, etc.) can reference them by asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Celestia/Tool Kind", fileName = "ToolKind_New")]
    public sealed class ToolKindSO : ScriptableObject
    {
        [Header("Identity")]
        public string displayName;

        [TextArea(2, 4)]
        public string description;
    }
}
