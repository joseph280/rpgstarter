using UnityEngine;
using UnityEngine.Audio;

namespace RPGStarter.Core.Audio
{
    /// <summary>
    /// Owns the AudioMixer and exposes volume controls. Lives on the Bootstrap scene's
    /// GameManager (CLAUDE.md §9). Stub for W1; UI bindings + persistence land in W7+.
    /// </summary>
    public sealed class AudioMixerController : MonoBehaviour
    {
        private const float MIN_DB = -80f;
        private const float MAX_DB = 0f;

        [Header("Mixer")]
        [SerializeField] private AudioMixer mixer;

        [Header("Exposed Mixer Parameter Names")]
        [SerializeField] private string masterParam = "MasterVolume";
        [SerializeField] private string musicParam  = "MusicVolume";
        [SerializeField] private string sfxParam    = "SfxVolume";

        public void SetMaster(float linear01) => SetVolume(masterParam, linear01);
        public void SetMusic (float linear01) => SetVolume(musicParam,  linear01);
        public void SetSfx   (float linear01) => SetVolume(sfxParam,    linear01);

        private void SetVolume(string param, float linear01)
        {
            if (mixer == null || string.IsNullOrEmpty(param)) return;

            // Linear → dB curve. log10(0) is undefined, so floor at 0.0001.
            float db = linear01 <= 0.0001f
                ? MIN_DB
                : Mathf.Clamp(Mathf.Log10(linear01) * 20f, MIN_DB, MAX_DB);

            mixer.SetFloat(param, db);
        }
    }
}
