using Celestia.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Celestia.EditorTools
{
    /// <summary>
    /// Editor-time helper that adds a world-space health bar above a character root.
    /// Used by both the player prefab patcher and the monster builder so the two stay
    /// visually consistent. Re-running strips any prior HealthBar child first.
    /// </summary>
    public static class HealthBarBuilder
    {
        public const string CHILD_NAME = "HealthBar";

        public static GameObject Attach(GameObject characterRoot, MonoBehaviour healthSource,
                                        float yOffset, Color fillColor)
        {
            // Idempotency: nuke any existing HealthBar before adding a fresh one.
            var existing = characterRoot.transform.Find(CHILD_NAME);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            // Canvas root — world-space, scaled down so 200x20 px reads as ~1m wide in-engine.
            var canvasGo = new GameObject(CHILD_NAME, typeof(Canvas), typeof(CanvasGroup));
            canvasGo.transform.SetParent(characterRoot.transform, worldPositionStays: false);
            canvasGo.transform.localPosition = new Vector3(0f, yOffset, 0f);
            canvasGo.transform.localRotation = Quaternion.identity;
            canvasGo.transform.localScale    = Vector3.one * 0.005f;
            canvasGo.layer = characterRoot.layer;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;

            var canvasGroup = canvasGo.GetComponent<CanvasGroup>();
            canvasGroup.interactable   = false;
            canvasGroup.blocksRaycasts = false;

            // Built-in Unity UI sprite — Image needs *some* sprite to render reliably across
            // all platforms; without one the quad can render solid white but Image.fillAmount
            // becomes a no-op. Loading the editor-time built-in keeps us out of Resources/.
            var uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            // Background plate (centered, full width)
            var bgGo = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(canvasGo.transform, false);
            bgGo.layer = characterRoot.layer;
            var bgRect = (RectTransform)bgGo.transform;
            bgRect.anchorMin = bgRect.anchorMax = new Vector2(0.5f, 0.5f);
            bgRect.pivot     = new Vector2(0.5f, 0.5f);
            bgRect.anchoredPosition = Vector2.zero;
            bgRect.sizeDelta = new Vector2(200f, 24f);
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = uiSprite;
            bgImg.type   = Image.Type.Sliced;
            bgImg.color  = new Color(0f, 0f, 0f, 0.65f);

            // Fill — pivot pinned to the left so localScale.x = fraction shrinks right→left.
            // WorldHealthBar drives localScale.x in LateUpdate.
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(canvasGo.transform, false);
            fillGo.layer = characterRoot.layer;
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.anchorMin = fillRect.anchorMax = new Vector2(0.5f, 0.5f);
            fillRect.pivot     = new Vector2(0f, 0.5f);                  // left-pivot
            fillRect.anchoredPosition = new Vector2(-96f, 0f);           // 192/2 left of canvas centre
            fillRect.sizeDelta = new Vector2(192f, 16f);
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.sprite = uiSprite;
            fillImg.type   = Image.Type.Sliced;
            fillImg.color  = fillColor;

            var bar = canvasGo.AddComponent<WorldHealthBar>();
            SetSerialized(bar, "healthSource", healthSource);
            SetSerialized(bar, "fill",         fillImg);
            SetSerialized(bar, "canvasGroup",  canvasGroup);

            return canvasGo;
        }

        private static void SetSerialized(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogWarning($"[HealthBarBuilder] Field '{fieldName}' not found on {target}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
