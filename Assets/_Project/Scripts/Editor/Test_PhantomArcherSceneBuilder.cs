using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RPGStarter.EditorTools
{
    /// <summary>
    /// One-shot scene builder for the W0 Phantom Archer shader/character validation scene.
    /// Re-running overwrites the scene and refreshes the character + floor materials.
    /// </summary>
    public static class Test_PhantomArcherSceneBuilder
    {
        private const string SCENE_PATH      = "Assets/_Project/Tests/PlayMode/Test_PhantomArcher_Shader.unity";
        private const string CHAR_MAT_PATH   = "Assets/_Project/Art/Materials/M_PhantomArcher.mat";
        private const string FLOOR_MAT_PATH  = "Assets/_Project/Art/Materials/M_TestFloor.mat";

        private const string FBX_PATH        = "Assets/_Project/Art/Characters/PhantomArcher/SK_PhantomArcher.fbx";
        private const string TEX_BASE        = "Assets/_Project/Art/Characters/PhantomArcher/T_PhantomArcher_BaseColor.png";
        private const string TEX_NORMAL      = "Assets/_Project/Art/Characters/PhantomArcher/T_PhantomArcher_Normal.png";
        private const string TEX_METALLIC    = "Assets/_Project/Art/Characters/PhantomArcher/T_PhantomArcher_Metallic.png";
        private const string TEX_EMISSIVE    = "Assets/_Project/Art/Characters/PhantomArcher/T_PhantomArcher_Emissive.png";

        private const string URP_LIT_SHADER  = "Universal Render Pipeline/Lit";

        // [MenuItem stripped — single entry point is RPGStarter/Build Demo]
        public static void Build()
        {
            var urpLit = Shader.Find(URP_LIT_SHADER);
            if (urpLit == null)
            {
                Debug.LogError($"[{nameof(Test_PhantomArcherSceneBuilder)}] '{URP_LIT_SHADER}' shader not found. Is URP installed?");
                return;
            }

            var charMat  = EnsureCharacterMaterial(urpLit);
            var floorMat = EnsureFloorMaterial(urpLit);
            if (charMat == null) return;

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FBX_PATH);
            if (fbx == null)
            {
                Debug.LogError($"[{nameof(Test_PhantomArcherSceneBuilder)}] FBX not found at {FBX_PATH}");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            BuildFloor(floorMat);
            BuildCharacter(fbx, charMat);
            PositionCamera();
            PositionDirectionalLight();

            EnsureDirectory(Path.GetDirectoryName(SCENE_PATH));
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[{nameof(Test_PhantomArcherSceneBuilder)}] Built {SCENE_PATH}");
        }

        private static void BuildFloor(Material floorMat)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(2f, 1f, 2f); // 20m x 20m
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
        }

        private static void BuildCharacter(GameObject fbx, Material charMat)
        {
            var character = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            character.name = "PhantomArcher";
            character.transform.position = Vector3.zero;
            character.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            foreach (var renderer in character.GetComponentsInChildren<Renderer>())
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = charMat;
                renderer.sharedMaterials = mats;
            }
        }

        private static void PositionCamera()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 1.6f, -3.5f);
                cam.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            }
        }

        private static void PositionDirectionalLight()
        {
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (light.type == LightType.Directional)
                {
                    light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                    light.intensity = 1.2f;
                    break;
                }
            }
        }

        private static Material EnsureCharacterMaterial(Shader urpLit)
        {
            var mat = LoadOrCreateMaterial(CHAR_MAT_PATH, urpLit);

            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture>(TEX_BASE));

            var normal = AssetDatabase.LoadAssetAtPath<Texture>(TEX_NORMAL);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }

            // NOTE: Meshy exports separate metallic + roughness PNGs. URP/Lit's
            // _MetallicGlossMap expects R=metallic, A=smoothness (=1-roughness).
            // We feed the metallic texture and use a uniform smoothness for the
            // test scene; proper PBR will need a repacked map (TODO follow-up).
            var metallic = AssetDatabase.LoadAssetAtPath<Texture>(TEX_METALLIC);
            if (metallic != null)
            {
                mat.SetTexture("_MetallicGlossMap", metallic);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            mat.SetFloat("_Smoothness", 0.4f);

            var emissive = AssetDatabase.LoadAssetAtPath<Texture>(TEX_EMISSIVE);
            if (emissive != null)
            {
                mat.SetTexture("_EmissionMap", emissive);
                mat.SetColor("_EmissionColor", Color.white);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material EnsureFloorMaterial(Shader urpLit)
        {
            var mat = LoadOrCreateMaterial(FLOOR_MAT_PATH, urpLit);
            mat.SetColor("_BaseColor", new Color(0.35f, 0.35f, 0.35f, 1f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                EnsureDirectory(Path.GetDirectoryName(path));
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        private static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }
}
