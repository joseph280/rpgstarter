using System;
using UnityEngine;

namespace RPGStarter.Core.Events
{
    /// <summary>
    /// SO event channel carrying a Vector3. Used for spatial signals (player spawn position,
    /// hit location, ability cast point). See VoidEventChannelSO for usage pattern.
    /// </summary>
    [CreateAssetMenu(menuName = "RPGStarter/Events/Vector3 Event Channel", fileName = "Event_Vector3New")]
    public sealed class Vector3EventChannelSO : ScriptableObject
    {
        [TextArea(2, 5), SerializeField]
        private string description;

        public event Action<Vector3> OnRaised;

        public void Raise(Vector3 value)
        {
            OnRaised?.Invoke(value);
        }

        private void OnDisable()
        {
            OnRaised = null;
        }
    }
}
