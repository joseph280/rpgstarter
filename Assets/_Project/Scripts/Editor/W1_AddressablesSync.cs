using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace RPGStarter.EditorTools
{
    /// <summary>
    /// Scans _Project/Scenes/** and _Project/Tests/PlayMode/** for .unity files and
    /// registers each as an Addressable in the Default group, with the file name (no
    /// extension, no path) as its address. Idempotent — re-running just refreshes addresses.
    ///
    /// Also keeps the editor's Play Mode Script pinned to BuildScriptFastMode so
    /// Bootstrap → Test_PlayerMovement loads via AssetDatabase (no content build
    /// required). The setting is stored in EditorPrefs (per-user), so the auto-set
    /// in <see cref="EnsureFastModeOnLoad"/> runs on every editor reload to keep
    /// freshly-cloned machines / wiped EditorPrefs out of the
    /// "InvalidKeyException: No Location found for Key=Test_PlayerMovement" pit.
    ///
    /// Result: GameManager.firstSceneKey = "Test_PlayerMovement" resolves cleanly,
    /// without manually dragging anything in the Addressables Groups window.
    /// </summary>
    public static class W1_AddressablesSync
    {
        [InitializeOnLoadMethod]
        private static void EnsureFastModeOnLoad()
        {
            // Defer so the AddressableAssetSettings asset has a chance to load before
            // we touch it — running synchronously from InitializeOnLoad can race with
            // package init.
            EditorApplication.delayCall += () =>
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null) return;
                int idx = FindFastModeIndex(settings);
                if (idx < 0) return;
                if (settings.ActivePlayModeDataBuilderIndex == idx) return;
                settings.ActivePlayModeDataBuilderIndex = idx;
                Debug.Log("[W1_AddressablesSync] Editor load — Play Mode Script auto-set to FastMode " +
                          "so Addressables resolve from AssetDatabase (no content build needed).");
            };
        }

        private static int FindFastModeIndex(AddressableAssetSettings settings)
        {
            for (int i = 0; i < settings.DataBuilders.Count; i++)
            {
                var b = settings.DataBuilders[i];
                if (b != null && b.GetType().Name == "BuildScriptFastMode") return i;
            }
            return -1;
        }

        /// <summary>
        /// Nuclear repair when AddressableAssetsData/ assets have lost their script refs
        /// (m_Script: fileID 0 across DataBuilders + schemas). This is what happens after
        /// some merges / package upgrades / Unity quirks — the asset YAML remains but its
        /// MonoScript binding is dead, so FastModeInitializationOperation.GetBuilderOfType
        /// throws NRE and Test_PlayerMovement never resolves.
        ///
        /// Wipes Assets/AddressableAssetsData/, lets Unity recreate defaults via
        /// GetSettings(create: true), then re-registers the scene entries.
        /// </summary>
        // [MenuItem stripped — single entry point is RPGStarter/Build Demo]
        public static void Repair()
        {
            const string ADDR_DIR = "Assets/AddressableAssetsData";

            // Delete the broken contents but keep the parent folder structure — Addressables'
            // GetSettings(create:true) refuses to mkdir-p its parents, so wiping the whole
            // folder yields "Parent directory must exist" UnityExceptions when it tries to
            // create DefaultObject.asset.
            if (AssetDatabase.IsValidFolder(ADDR_DIR))
            {
                if (!AssetDatabase.DeleteAsset(ADDR_DIR))
                {
                    Debug.LogError("[W1_AddressablesSync] DeleteAsset failed on " + ADDR_DIR +
                                   ". Close any Addressables-related editor windows and re-run.");
                    return;
                }
            }
            if (!AssetDatabase.IsValidFolder(ADDR_DIR))
                AssetDatabase.CreateFolder("Assets", "AddressableAssetsData");

            // Clear the EditorPrefs pointer so GetSettings(create:true) starts blank.
            AddressableAssetSettingsDefaultObject.Settings = null;

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(create: true);
            if (settings == null)
            {
                Debug.LogError("[W1_AddressablesSync] Could not recreate AddressableAssetSettings — Unity refused.");
                return;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[W1_AddressablesSync] AddressableAssetsData/ rebuilt from scratch. Re-syncing scene entries…");
            Sync();
        }

        private static readonly string[] SCAN_ROOTS =
        {
            "Assets/_Project/Scenes",
            "Assets/_Project/Tests/PlayMode"
        };

        // [MenuItem stripped — single entry point is RPGStarter/Build Demo]
        public static void Sync()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings
                           ?? AddressableAssetSettingsDefaultObject.GetSettings(create: true);

            if (settings == null)
            {
                Debug.LogError("[W1_AddressablesSync] Could not create or load Addressables settings.");
                return;
            }

            var group = settings.DefaultGroup;
            if (group == null)
            {
                Debug.LogError("[W1_AddressablesSync] No Default group found in Addressables settings.");
                return;
            }

            int registered = 0, updated = 0;

            foreach (var root in SCAN_ROOTS)
            {
                if (!Directory.Exists(root)) continue;

                var sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { root });
                foreach (var guid in sceneGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var desiredAddress = Path.GetFileNameWithoutExtension(path);

                    var entry = settings.FindAssetEntry(guid);
                    if (entry == null)
                    {
                        entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
                        registered++;
                    }
                    else if (entry.parentGroup != group)
                    {
                        entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: false);
                    }

                    if (entry.address != desiredAddress)
                    {
                        entry.SetAddress(desiredAddress, postEvent: false);
                        updated++;
                    }
                }
            }

            // Force the editor's play-mode data builder to FastMode (BuildScriptFastMode).
            // Without this, Play loads via PackedPlayMode and throws InvalidKeyException
            // until the user manually runs a content build — wrong default for an
            // iteration-heavy MVP. Shared with the InitializeOnLoad hook above.
            int fastModeIdx = FindFastModeIndex(settings);
            if (fastModeIdx >= 0 && settings.ActivePlayModeDataBuilderIndex != fastModeIdx)
            {
                settings.ActivePlayModeDataBuilderIndex = fastModeIdx;
                Debug.Log($"[W1_AddressablesSync] Play Mode Script set to FastMode (index {fastModeIdx}).");
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true, true);
            AssetDatabase.SaveAssets();

            Debug.Log($"[W1_AddressablesSync] {registered} new entries, {updated} addresses updated. " +
                      "Scenes registered with filename-only addresses (e.g. 'Test_PlayerMovement').");
        }
    }
}
