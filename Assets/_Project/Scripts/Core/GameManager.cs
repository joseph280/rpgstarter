using RPGStarter.Core.Audio;
using RPGStarter.Core.SceneManagement;
using UnityEngine;

namespace RPGStarter.Core
{
    /// <summary>
    /// The single approved singleton (CLAUDE.md §5, §9). Lives in the Bootstrap scene,
    /// survives all scene loads, owns references to long-lived subsystems.
    ///
    /// Other systems do NOT reach into GameManager during gameplay — they listen to
    /// SO event channels. GameManager is the entry point for app lifecycle only.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Subsystems")]
        [SerializeField] private SceneLoader sceneLoader;
        [SerializeField] private AudioMixerController audioMixer;

        [Header("Bootstrap Flow")]
        [Tooltip("Addressable key of the first scene to load additively after Bootstrap.")]
        [SerializeField] private string firstSceneKey = "Test_PlayerMovement";

        public SceneLoader SceneLoader => sceneLoader;
        public AudioMixerController AudioMixer => audioMixer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private async void Start()
        {
            if (sceneLoader == null)
            {
                Debug.LogError("[GameManager] SceneLoader not assigned.");
                return;
            }

            if (string.IsNullOrEmpty(firstSceneKey))
            {
                Debug.Log("[GameManager] No first scene configured — Bootstrap is standalone.");
                return;
            }

            await sceneLoader.LoadAdditiveAsync(firstSceneKey);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
