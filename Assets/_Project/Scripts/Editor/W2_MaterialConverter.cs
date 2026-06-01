using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RPGStarter.EditorTools
{
    /// <summary>
    /// Quick-fix: convert third-party Built-in pipeline materials to URP equivalents.
    /// Magenta materials in the Game view almost always = a Standard/Diffuse shader the
    /// URP renderer can't draw. We swap the shader to URP/Lit and copy the main color +
    /// main texture so the look approximates the original.
    ///
    /// Run this once after importing a third-party asset pack made for Built-in.
    /// Idempotent — only touches materials whose current shader is one of the known
    /// Built-in shaders.
    /// </summary>
    public static class W2_MaterialConverter
    {
        private static readonly HashSet<string> LEGACY_SHADERS = new()
        {
            "Standard",
            "Standard (Specular setup)",
            "Legacy Shaders/Diffuse",
            "Legacy Shaders/Bumped Diffuse",
            "Legacy Shaders/Bumped Specular",
            "Legacy Shaders/Specular",
            "Legacy Shaders/Transparent/Diffuse",
            "Mobile/Diffuse",
            "Mobile/VertexLit",
            "Diffuse",
        };

        // [MenuItem stripped — single entry point is RPGStarter/Build Demo]
        public static void ConvertThirdPartyMaterials()
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                Debug.LogError("[W2_MaterialConverter] URP/Lit shader not found. Is URP installed?");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/ThirdParty" });
            int converted = 0, skipped = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat  = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) { skipped++; continue; }
                if (!LEGACY_SHADERS.Contains(mat.shader.name)) { skipped++; continue; }

                Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Texture bump    = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;

                mat.shader = urpLit;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_BaseMap"))   mat.SetTexture("_BaseMap", mainTex);
                if (bump != null && mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", bump);

                EditorUtility.SetDirty(mat);
                converted++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[W2_MaterialConverter] Converted {converted} materials to URP/Lit. Skipped {skipped} (already URP or unknown shader). Pink magentas in ThirdParty/ should now render correctly.");
        }
    }
}
