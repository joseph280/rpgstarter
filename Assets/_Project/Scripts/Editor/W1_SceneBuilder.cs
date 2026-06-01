using System.IO;
using Celestia.Core;
using Celestia.Core.Audio;
using Celestia.Core.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Cinemachine;

namespace Celestia.EditorTools
{
    /// <summary>
    /// Builds the two W1 scenes:
    ///  - Bootstrap.unity         (entry; persistent GameManager + subsystems)
    ///  - Test_PlayerMovement.unity (additive gameplay test scene)
    ///
    /// Bootstrap loads the test scene additively at Start() via SceneLoader.
    /// </summary>
    public static class W1_SceneBuilder
    {
        public const string BOOTSTRAP_PATH = "Assets/_Project/Scenes/Bootstrap/Bootstrap.unity";
        public const string TEST_PATH      = "Assets/_Project/Tests/PlayMode/Test_PlayerMovement.unity";

        private const string FLOOR_MAT_PATH = "Assets/_Project/Art/Materials/M_TestFloor.mat";

        [MenuItem("Celestia/W1/4 - Build Scenes (Bootstrap + Test)")]
        public static void Build()
        {
            BuildBootstrap();
            BuildTestScene();
            Debug.Log("[W1_SceneBuilder] Scenes built.");
        }

        private static void BuildBootstrap()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var gmGo = new GameObject("GameManager");
            var sceneLoaderGo = new GameObject("SceneLoader");
            var audioGo = new GameObject("AudioMixerController");

            sceneLoaderGo.transform.SetParent(gmGo.transform, false);
            audioGo.transform.SetParent(gmGo.transform, false);

            var sceneLoader = sceneLoaderGo.AddComponent<SceneLoader>();
            var audio = audioGo.AddComponent<AudioMixerController>();
            var gm = gmGo.AddComponent<GameManager>();

            SetSerialized(gm, "sceneLoader", sceneLoader);
            SetSerialized(gm, "audioMixer", audio);

            // Audio listener lives on Bootstrap so the player camera doesn't double up.
            gmGo.AddComponent<AudioListener>();

            EnsureDir(Path.GetDirectoryName(BOOTSTRAP_PATH));
            EditorSceneManager.SaveScene(scene, BOOTSTRAP_PATH);
            Debug.Log($"[W1_SceneBuilder] Built {BOOTSTRAP_PATH}");
        }

        private static void BuildTestScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Strip the default Main Camera + AudioListener — we'll provide camera via Cinemachine
            // and rely on Bootstrap's AudioListener at runtime.
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Main Camera") Object.DestroyImmediate(root);
            }

            // Floor
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(4f, 1f, 4f); // 40x40m
            var floorMat = AssetDatabase.LoadAssetAtPath<Material>(FLOOR_MAT_PATH);
            if (floorMat != null) floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            // Reference grid: a few primitives so movement is visible
            for (int i = 0; i < 4; i++)
            {
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = $"RefPillar_{i}";
                pillar.transform.localScale = new Vector3(0.5f, 2f, 0.5f);
                pillar.transform.position   = new Vector3(Mathf.Cos(i * 1.57f) * 5f, 1f, Mathf.Sin(i * 1.57f) * 5f);
            }

            // Player spawn
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(W1_PlayerPrefabBuilder.PREFAB_PATH);
            GameObject playerInstance = null;
            if (playerPrefab != null)
            {
                playerInstance = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                playerInstance.transform.position = Vector3.zero;
            }
            else
            {
                Debug.LogWarning("[W1_SceneBuilder] Player prefab not found; test scene will spawn empty.");
            }

            // Camera: real Camera + CinemachineCamera follow rig at iso angle.
            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 40f;
            camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camGo.AddComponent<CinemachineBrain>();

            var virtCamGo = new GameObject("CM_PlayerFollow");
            var virtCam = virtCamGo.AddComponent<CinemachineCamera>();
            virtCam.Lens.FieldOfView = 40f;

            // Iso: ~30° pitch, fixed yaw, 12m distance
            virtCamGo.transform.position = new Vector3(0f, 10f, -10f);
            virtCamGo.transform.rotation = Quaternion.Euler(35f, 0f, 0f);

            if (playerInstance != null)
            {
                virtCam.Follow = playerInstance.transform;
                virtCam.LookAt = playerInstance.transform;

                var follow = virtCamGo.AddComponent<CinemachineFollow>();
                follow.FollowOffset = new Vector3(0f, 10f, -10f);
                // Tight follow — Cinemachine 3.x defaults to 1s damping which feels
                // like the camera is glued in place for short player moves.
                var tracker = follow.TrackerSettings;
                tracker.PositionDamping  = new Vector3(0.1f, 0.1f, 0.1f);
                tracker.RotationDamping  = Vector3.zero;
                tracker.QuaternionDamping = 0f;
                follow.TrackerSettings = tracker;

                // Tell the player movement which transform is the camera reference (for camera-relative WASD).
                var movement = playerInstance.GetComponent<Celestia.Player.PlayerMovement>();
                if (movement != null) SetSerialized(movement, "cameraReference", virtCamGo.transform);
            }

            // Bake a directional light into a known angle (W0 art bible: outline + toon work best with consistent key)
            var light = Object.FindAnyObjectByType<Light>();
            if (light != null && light.type == LightType.Directional)
            {
                light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                light.intensity = 1.2f;
                light.shadows   = LightShadows.Soft;
            }

            EnsureDir(Path.GetDirectoryName(TEST_PATH));
            EditorSceneManager.SaveScene(scene, TEST_PATH);
            Debug.Log($"[W1_SceneBuilder] Built {TEST_PATH}");
        }

        private static void SetSerialized(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"Field '{fieldName}' not found on {target}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureDir(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }
}
