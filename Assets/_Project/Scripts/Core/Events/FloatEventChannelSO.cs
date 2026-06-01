using System;
using UnityEngine;

namespace RPGStarter.Core.Events
{
    /// <summary>
    /// SO event channel carrying a float. Used for normalized signals (HP fraction,
    /// stamina, ability cooldown progress).
    /// </summary>
    [CreateAssetMenu(menuName = "RPGStarter/Events/Float Event Channel", fileName = "Event_FloatNew")]
    public sealed class FloatEventChannelSO : ScriptableObject
    {
        [TextArea(2, 5), SerializeField]
        private string description;

        public event Action<float> OnRaised;

        public void Raise(float value)
        {
            OnRaised?.Invoke(value);
        }

        private void OnDisable()
        {
            OnRaised = null;
        }
    }
}
