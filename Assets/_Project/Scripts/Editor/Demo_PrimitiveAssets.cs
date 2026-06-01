using System.IO;
using UnityEditor;
using UnityEngine;

namespace RPGStarter.EditorTools
{
    /// <summary>
    /// Generates Unity-primitive "source" prefabs that stand in for the licensed
    /// asset-store packs (Blink weapons, PolyquestWorlds rocks, Synty trees, the
    /// Table pack, Stylized 3D Tools) on the shareable simple-RPG-demo branch.
    ///
    /// The W2/W3 builders harvest a mesh + material from a "source prefab" (or
    /// instantiate it whole) to build the final gameplay prefab. They don't care
    /// whether that source is a Synty model or a Unity sphere — it just needs a
    /// MeshFilter + MeshRenderer. So instead of rewriting those builders, we point
    /// their source-path constants at the primitive prefabs this script creates.
    ///
    /// Run order on the demo branch:
    ///   1. RPGStarter → Demo → 1. Build Primitive Source Assets   (this script)
    ///   2. RPGStarter → W1 → Build Everything
    ///   3. RPGStarter → W2 → Build Everything
    ///   4. RPGStarter → W3 → Build Everything
    ///   5. RPGStarter → Demo → Build Mannequin Player
    ///
    /// Everything here is Unity-primitive geometry + flat URP/Lit colours — zero
    /// third-party content, fully redistributable.
    /// </summary>
    public static class Demo_PrimitiveAssets
    {
        public const string DIR_PREFABS = "Assets/_Project/Prefabs/Demo";
        public const string DIR_MATS    = "Assets/_Project/Art/Materials/Demo";

        // Public so the W2/W3 builders can reference these paths as their sources.
        public const string SRC_ROCK    = DIR_PREFABS + "/Demo_Src_Rock.prefab";
        public const string SRC_TREE    = DIR_PREFABS + "/Demo_Src_Tree.prefab";
        public const string SRC_TABLE   = DIR_PREFABS + "/Demo_Src_Table.prefab";
        public const string SRC_BOW     = DIR_PREFABS + "/Demo_Src_Bow.prefab";
        public const string SRC_AXE1H   = DIR_PREFABS + "/Demo_Src_Axe1H.prefab";
        public const string SRC_MACE    = DIR_PREFABS + "/Demo_Src_Mace.prefab";
        public const string SRC_SPEAR   = DIR_PREFABS + "/Demo_Src_Spear.prefab";
        public const string SRC_HAMMER  = DIR_PREFABS + "/Demo_Src_Hammer.prefab";
        public const string SRC_PICKAXE = DIR_PREFABS + "/Demo_Src_Pickaxe.prefab";
        public const string SRC_AXETOOL = DIR_PREFABS + "/Demo_Src_AxeTool.prefab";

        // [MenuItem stripped — single entry point is RPGStarter/Build Demo]
        public static void Build()
        {
            Debug.Log("[Demo_PrimitiveAssets] BEGIN");
            EnsureDir(DIR_PREFABS);
            EnsureDir(DIR_MATS);

            // ── Materials ────────────────────────────────────────────────────
            var rock   = Mat("M_Demo_Rock",   new Color(0.45f, 0.45f, 0.48f));
            var bark   = Mat("M_Demo_Bark",   new Color(0.34f, 0.22f, 0.12f));
            var leaves = Mat("M_Demo_Leaves", new Color(0.22f, 0.45f, 0.18f));
            var wood   = Mat("M_Demo_Wood",   new Color(0.55f, 0.38f, 0.20f));
            var metal  = Mat("M_Demo_Metal",  new Color(0.40f, 0.42f, 0.48f));
            var bstring = Mat("M_Demo_String", new Color(0.86f, 0.84f, 0.78f));

            // ── Rock: a squashed sphere ─────────────────────────────────────
            BuildSingle(SRC_ROCK, "Demo_Src_Rock", PrimitiveType.Sphere,
                        new Vector3(1.4f, 1.0f, 1.2f), Vector3.zero, rock);

            // ── Tree: cylinder trunk + sphere canopy ────────────────────────
            BuildTree();

            // ── Table: a low wide cube ──────────────────────────────────────
            BuildSingle(SRC_TABLE, "Demo_Src_Table", PrimitiveType.Cube,
                        new Vector3(1.2f, 0.9f, 0.8f), new Vector3(0f, 0.45f, 0f), wood);

            // ── Weapons (W2) — bow + the three placeholder melee weapons ─────
            // The demo only really uses the bow; axe/mace/spear are built by
            // W2_WeaponBuilder regardless, so they get primitives too.
            //
            // The bow is a multi-primitive root (curved stave + grip + string) —
            // NOT a single scaled cube. WeaponEquipment overwrites the equipped
            // prop's ROOT localScale with the SO's uniform mainLocalScale, so any
            // shape baked into the root scale (as the old single cube did) gets
            // flattened back into a box. Carrying the silhouette on child
            // transforms — like BuildTree/BuildTool — survives that uniform scale.
            BuildBow(wood, bark, bstring);
            BuildSingle(SRC_AXE1H, "Demo_Src_Axe1H", PrimitiveType.Cube,
                        new Vector3(0.10f, 0.9f, 0.10f), Vector3.zero, metal);
            BuildSingle(SRC_MACE,  "Demo_Src_Mace",  PrimitiveType.Cube,
                        new Vector3(0.12f, 0.9f, 0.12f), Vector3.zero, metal);
            BuildSingle(SRC_SPEAR, "Demo_Src_Spear", PrimitiveType.Cylinder,
                        new Vector3(0.06f, 1.1f, 0.06f), Vector3.zero, metal);

            // ── Tools (W3) — hammer / pickaxe / axe ─────────────────────────
            BuildTool(SRC_HAMMER,  "Demo_Src_Hammer",  wood, metal);
            BuildTool(SRC_PICKAXE, "Demo_Src_Pickaxe", wood, metal);
            BuildTool(SRC_AXETOOL, "Demo_Src_AxeTool", wood, metal);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Demo_PrimitiveAssets] END — primitive source prefabs built under " + DIR_PREFABS);
        }

        // ── Builders ─────────────────────────────────────────────────────────

        private static void BuildSingle(string path, string name, PrimitiveType prim,
                                        Vector3 scale, Vector3 localPos, Material mat)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                AssetDatabase.DeleteAsset(path);

            var go = GameObject.CreatePrimitive(prim);
            try
            {
                go.name = name;
                StripCollider(go);
                go.transform.localScale = scale;
                go.transform.localPosition = localPos;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static void BuildTree()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SRC_TREE) != null)
                AssetDatabase.DeleteAsset(SRC_TREE);

            var root = new GameObject("Demo_Src_Tree");
            try
            {
                // Trunk — cylinder primitive is 2m tall at scale 1; scale to a ~3m trunk.
                var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                StripCollider(trunk);
                trunk.name = "Trunk";
                trunk.transform.SetParent(root.transform, false);
                trunk.transform.localScale    = new Vector3(0.35f, 1.5f, 0.35f);
                trunk.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                trunk.GetComponent<MeshRenderer>().sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(DIR_MATS + "/M_Demo_Bark.mat");

                // Canopy — sphere sitting on top of the trunk.
                var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                StripCollider(canopy);
                canopy.name = "Canopy";
                canopy.transform.SetParent(root.transform, false);
                canopy.transform.localScale    = new Vector3(2.4f, 2.4f, 2.4f);
                canopy.transform.localPosition = new Vector3(0f, 3.6f, 0f);
                canopy.GetComponent<MeshRenderer>().sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(DIR_MATS + "/M_Demo_Leaves.mat");

                PrefabUtility.SaveAsPrefabAsset(root, SRC_TREE);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildTool(string path, string name, Material handleMat, Material headMat)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                AssetDatabase.DeleteAsset(path);

            var root = new GameObject(name);
            try
            {
                // Handle — thin cylinder.
                var handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                StripCollider(handle);
                handle.name = "Handle";
                handle.transform.SetParent(root.transform, false);
                handle.transform.localScale    = new Vector3(0.05f, 0.4f, 0.05f);
                handle.transform.localPosition = new Vector3(0f, 0.4f, 0f);
                handle.GetComponent<MeshRenderer>().sharedMaterial = handleMat;

                // Head — small cube near the top.
                var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                StripCollider(head);
                head.name = "Head";
                head.transform.SetParent(root.transform, false);
                head.transform.localScale    = new Vector3(0.28f, 0.14f, 0.12f);
                head.transform.localPosition = new Vector3(0f, 0.8f, 0f);
                head.GetComponent<MeshRenderer>().sharedMaterial = headMat;

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildBow(Material wood, Material grip, Material stringMat)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SRC_BOW) != null)
                AssetDatabase.DeleteAsset(SRC_BOW);

            var root = new GameObject("Demo_Src_Bow");
            try
            {
                const float halfH = 0.55f;  // bow reaches ±0.55m along local Y (a "D" stave)
                const float depth = 0.20f;  // how far the stave bellies out on -Z
                const int   segs  = 8;      // straight rods approximating the curved stave

                // Parabolic stave: z(t) = -depth*(1-t²) for t∈[-1,1]. Flat string side
                // sits at z=0; the limbs bow out to -Z and the nocks meet the string
                // exactly at the tips (z=0 when t=±1).
                Vector3 Arc(float t) => new Vector3(0f, t * halfH, -depth * (1f - t * t));

                for (int i = 0; i < segs; i++)
                {
                    float t0 = Mathf.Lerp(-1f, 1f, i       / (float)segs);
                    float t1 = Mathf.Lerp(-1f, 1f, (i + 1) / (float)segs);
                    AddRod(root.transform, "Stave_" + i, Arc(t0), Arc(t1), 0.05f, wood);
                }

                // Leather grip — a short, fatter band over the belly centre.
                var handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                StripCollider(handle);
                handle.name = "Grip";
                handle.transform.SetParent(root.transform, false);
                handle.transform.localPosition = new Vector3(0f, 0f, -depth);
                handle.transform.localScale    = new Vector3(0.07f, 0.12f, 0.07f);
                handle.GetComponent<MeshRenderer>().sharedMaterial = grip;

                // Bowstring — a thin straight line down the flat side, nock to nock.
                AddRod(root.transform, "String",
                       new Vector3(0f, -halfH, 0f), new Vector3(0f, halfH, 0f),
                       0.012f, stringMat);

                PrefabUtility.SaveAsPrefabAsset(root, SRC_BOW);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Adds a thin cylinder "rod" spanning local points <paramref name="a"/>→<paramref name="b"/>.
        /// Unity's cylinder primitive is 2 units tall along local Y, so Y-scale = length/2 and
        /// the rod is rotated to align its Y axis with the segment direction.
        /// </summary>
        private static void AddRod(Transform parent, string name, Vector3 a, Vector3 b,
                                   float thickness, Material mat)
        {
            var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            StripCollider(rod);
            rod.name = name;
            rod.transform.SetParent(parent, false);

            Vector3 dir = b - a;
            float len = dir.magnitude;
            rod.transform.localPosition = (a + b) * 0.5f;
            rod.transform.localScale    = new Vector3(thickness, len * 0.5f, thickness);
            if (len > 1e-5f)
                rod.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            rod.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Material Mat(string name, Color color)
        {
            string path = DIR_MATS + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void StripCollider(GameObject go)
        {
            // The W2/W3 builders add their own gameplay colliders; the primitive's
            // default collider would just double up. Remove it.
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        private static void EnsureDir(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }
}
