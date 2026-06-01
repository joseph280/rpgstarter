using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Celestia.Core.SceneManagement
{
    /// <summary>
    /// Loads/unloads scenes via Addressables, additively on top of Bootstrap (CLAUDE.md §9).
    /// All gameplay scenes are loaded by Addressable key, never by Build Settings index.
    /// Bootstrap scene is never unloaded.
    /// </summary>
    public sealed class SceneLoader : MonoBehaviour
    {
        private readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> _loaded = new();

        public bool IsLoaded(string addressableKey) => _loaded.ContainsKey(addressableKey);

        public async Task LoadAdditiveAsync(string addressableKey)
        {
            if (_loaded.ContainsKey(addressableKey))
            {
                Debug.LogWarning($"[SceneLoader] '{addressableKey}' already loaded.");
                return;
            }

            var handle = Addressables.LoadSceneAsync(addressableKey, LoadSceneMode.Additive);
            await handle.Task;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[SceneLoader] Failed to load '{addressableKey}': {handle.OperationException}");
                return;
            }

            _loaded[addressableKey] = handle;
            SceneManager.SetActiveScene(handle.Result.Scene);
            Debug.Log($"[SceneLoader] Loaded '{addressableKey}'.");
        }

        public async Task UnloadAsync(string addressableKey)
        {
            if (!_loaded.TryGetValue(addressableKey, out var handle))
            {
                Debug.LogWarning($"[SceneLoader] '{addressableKey}' not loaded; nothing to unload.");
                return;
            }

            var unload = Addressables.UnloadSceneAsync(handle);
            await unload.Task;

            _loaded.Remove(addressableKey);
            Debug.Log($"[SceneLoader] Unloaded '{addressableKey}'.");
        }

        public async Task SwapAsync(string unloadKey, string loadKey)
        {
            if (!string.IsNullOrEmpty(unloadKey)) await UnloadAsync(unloadKey);
            if (!string.IsNullOrEmpty(loadKey))   await LoadAdditiveAsync(loadKey);
        }
    }
}
