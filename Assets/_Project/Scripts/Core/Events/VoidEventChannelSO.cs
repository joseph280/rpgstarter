using System;
using UnityEngine;

namespace RPGStarter.Core.Events
{
    /// <summary>
    /// SO event channel with no payload. Systems publish via Raise() and listeners
    /// subscribe via OnRaised. Replaces ad-hoc singleton/Find lookups for cross-system signals.
    /// Per CLAUDE.md §4: SOs never mutate runtime state — the OnRaised event is fine because
    /// it's a C# delegate field, not serialized.
    /// </summary>
    [CreateAssetMenu(menuName = "RPGStarter/Events/Void Event Channel", fileName = "Event_VoidNew")]
    public sealed class VoidEventChannelSO : ScriptableObject
    {
        [TextArea(2, 5), SerializeField]
        private string description;

        public event Action OnRaised;

        public void Raise()
        {
            OnRaised?.Invoke();
        }

        private void OnDisable()
        {
            // Clear stale subscriptions when domain reloads in the editor.
            OnRaised = null;
        }
    }
}
