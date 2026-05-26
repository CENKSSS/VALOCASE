#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.UI;

namespace ValoCase.Editor
{
    // Ortak UI helper metodlari — Builder'in butun dosyalari bu yardimcilara baglidir.
    public static partial class ValoCaseUIBuilder
    {
        static RectTransform CreateScreenPanel(RectTransform parent, string name, ScreenType type, out CanvasGroup group)
        {
            var rt = CreateRect(name, parent, Vector2.zero);
            StretchFull(rt);
            GetOrAddImage(rt).color = BgDark;
            group = rt.gameObject.AddComponent<CanvasGroup>();
            return rt;
        }

        static Button CreateMenuButton(RectTransform parent, string name, string label, Color color, Vector2 pos, Vector2 size)
        {
            var rt = CreateRect(name, parent, size);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = GetOrAddImage(rt);
            img.color = color;
            var btn = rt.gameObject.AddComponent<Button>();
            ApplyNeonInteraction(btn, NeonCyan);

            var text = CreateTmp("Text", rt, label, 22, TextAlignmentOptions.Center);
            StretchFull(text);
            text.rectTransform.offsetMin = new Vector2(8, 4);
            text.rectTransform.offsetMax = new Vector2(-8, -4);
            text.raycastTarget = false;
            return btn;
        }

        // Outlines a UI element with a soft neon halo. Larger `distance`
        // = thicker glow ring (visual cost is 4 extra graphic verts per element).
        static void AddNeonGlow(GameObject go, Color color, float distance)
        {
            // Outline kaldırıldı. Valorant tasarımı flat (düz) olduğu için
            // bu fonksiyon referans hatalarını önlemek adına boş bırakıldı.
        }

        // Applies a subtle neon outline to a TMP title via the font shader's
        // built-in outline channel. Width is conservative so titles stay legible.
        // Falls back gracefully on font assets that don't support outline.
        static void ApplyTitleGlow(TextMeshProUGUI tmp, Color glow)
        {
            if (tmp == null) return;
            tmp.fontStyle = FontStyles.Bold;
            // Dış çizgi (outline) parlaması kaldırıldı, sadece keskin bir kalın font kullanılıyor.
        }

        // Neon outline kaldırıldı. Valorant stili flat geçişler eklendi.
        static void ApplyNeonInteraction(Button btn, Color glow)
        {
            var colors = btn.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.pressedColor     = new Color(0.6f, 0.6f, 0.6f, 1f);
            colors.selectedColor    = Color.white;
            colors.disabledColor    = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            colors.fadeDuration     = 0.05f;
            btn.colors = colors;
        }

        static RectTransform CreateHorizontalScrollContent(string name, RectTransform parent, Vector2 size, Vector2 pos, Vector2 cellSize, float spacing)
        {
            var root = CreateRect(name, parent, size);
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = pos;
            root.sizeDelta = size;
            root.GetComponent<Image>().color = new Color(0, 0, 0, 0.22f);

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = CreateRect("Viewport", root, Vector2.zero);
            StretchFull(viewport);
            viewport.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = CreateRect("Content", viewport, new Vector2(0, cellSize.y));
            content.anchorMin = new Vector2(0, 0.5f);
            content.anchorMax = new Vector2(0, 0.5f);
            content.pivot = new Vector2(0, 0.5f);
            content.anchoredPosition = Vector2.zero;
            DestroyImage(content);

            var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = spacing;
            layout.padding = new RectOffset(12, 12, 0, 0);

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        static RectTransform CreateVerticalGridScrollContent(string name, RectTransform parent, Vector2 offsetMin, Vector2 offsetMax, Vector2 cellSize, Vector2 spacing, RectOffset padding)
        {
            var root = CreateRect(name, parent, Vector2.zero);
            StretchFull(root);
            root.offsetMin = offsetMin;
            root.offsetMax = offsetMax;
            root.GetComponent<Image>().color = new Color(0, 0, 0, 0.18f);

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = CreateRect("Viewport", root, Vector2.zero);
            StretchFull(viewport);
            viewport.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = CreateRect("Grid", viewport, Vector2.zero);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            DestroyImage(content);

            var gridLayout = content.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = cellSize;
            gridLayout.spacing = spacing;
            gridLayout.padding = padding;
            gridLayout.childAlignment = TextAnchor.UpperLeft;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        static Toggle CreateToggle(RectTransform parent, string name, string label, Vector2 pos)
        {
            var rt = CreateRect(name, parent, new Vector2(520, 46));
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.GetComponent<Image>().color = new Color(0, 0, 0, 0.18f);

            var toggle = rt.gameObject.AddComponent<Toggle>();
            var box = CreateRect("Box", rt, new Vector2(34, 34));
            box.anchorMin = new Vector2(0, 0.5f);
            box.anchorMax = new Vector2(0, 0.5f);
            box.pivot = new Vector2(0, 0.5f);
            box.anchoredPosition = new Vector2(16, 0);
            var boxImage = box.GetComponent<Image>();
            boxImage.color = new Color(0.05f, 0.07f, 0.09f, 1f);

            var check = CreateRect("Checkmark", box, new Vector2(22, 22));
            check.anchorMin = new Vector2(0.5f, 0.5f);
            check.anchorMax = new Vector2(0.5f, 0.5f);
            check.pivot = new Vector2(0.5f, 0.5f);
            check.anchoredPosition = Vector2.zero;
            var checkImage = check.GetComponent<Image>();
            checkImage.color = AccentRed;

            var text = CreateTmp("Label", rt, label, 18, TextAlignmentOptions.Left);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(64, 4);
            text.rectTransform.offsetMax = new Vector2(-16, -4);
            text.raycastTarget = false;

            toggle.targetGraphic = boxImage;
            toggle.graphic = checkImage;
            toggle.isOn = true;
            return toggle;
        }

        static TMP_InputField CreateInputField(RectTransform parent, string name, string placeholder, Vector2 pos, Vector2 size)
        {
            var rt = CreateRect(name, parent, size);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            var image = rt.GetComponent<Image>();
            image.color = new Color(0.05f, 0.07f, 0.09f, 1f);

            var input = rt.gameObject.AddComponent<TMP_InputField>();
            var text = CreateTmp("Text", rt, "", 20, TextAlignmentOptions.Left);
            StretchFull(text);
            text.rectTransform.offsetMin = new Vector2(16, 8);
            text.rectTransform.offsetMax = new Vector2(-16, -8);

            var placeholderText = CreateTmp("Placeholder", rt, placeholder, 20, TextAlignmentOptions.Left);
            StretchFull(placeholderText);
            placeholderText.rectTransform.offsetMin = new Vector2(16, 8);
            placeholderText.rectTransform.offsetMax = new Vector2(-16, -8);
            placeholderText.color = new Color(0.75f, 0.8f, 0.85f, 0.55f);

            input.textComponent = text;
            input.placeholder = placeholderText;
            input.targetGraphic = image;
            return input;
        }

        static TMP_Dropdown CreateDropdown(RectTransform parent, string name, Vector2 pos, string[] options)
        {
            var go = CreateRect(name, parent, new Vector2(220, 48));
            go.anchorMin = new Vector2(0.5f, 1);
            go.anchorMax = new Vector2(0.5f, 1);
            go.pivot = new Vector2(0.5f, 1);
            go.anchoredPosition = pos;
            go.sizeDelta = new Vector2(220, 48);
            GetOrAddImage(go).color = Panel;
            var dropdown = go.gameObject.AddComponent<TMP_Dropdown>();
            var label = CreateTmp("Label", go, options.Length > 0 ? options[0] : string.Empty, 16, TextAlignmentOptions.Left);
            var labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(12, 4);
            labelRt.offsetMax = new Vector2(-12, -4);
            dropdown.captionText = label;
            dropdown.template = CreateDropdownTemplate(go, out var itemLabel);
            dropdown.itemText = itemLabel;
            dropdown.options.Clear();
            foreach (var option in options)
                dropdown.options.Add(new TMP_Dropdown.OptionData(option));
            return dropdown;
        }

        static RectTransform CreateDropdownTemplate(RectTransform parent, out TextMeshProUGUI itemLabel)
        {
            var template = CreateRect("Template", parent, new Vector2(0, 240));
            template.anchorMin = new Vector2(0, 0);
            template.anchorMax = new Vector2(1, 0);
            template.pivot = new Vector2(0.5f, 1);
            template.anchoredPosition = new Vector2(0, -2);
            template.sizeDelta = new Vector2(0, 240);
            template.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.11f, 1f);

            var scroll = template.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;

            var viewport = CreateRect("Viewport", template, Vector2.zero);
            StretchFull(viewport);
            viewport.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = CreateRect("Content", viewport, Vector2.zero);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            DestroyImage(content);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var item = CreateRect("Item", content, new Vector2(0, 36));
            item.anchorMin = new Vector2(0, 1);
            item.anchorMax = new Vector2(1, 1);
            item.pivot = new Vector2(0.5f, 1);
            item.sizeDelta = new Vector2(0, 36);
            item.GetComponent<Image>().color = new Color(1, 1, 1, 0.04f);

            var toggle = item.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = item.GetComponent<Image>();

            var check = CreateRect("Item Checkmark", item, new Vector2(18, 18));
            check.anchorMin = new Vector2(0, 0.5f);
            check.anchorMax = new Vector2(0, 0.5f);
            check.pivot = new Vector2(0, 0.5f);
            check.anchoredPosition = new Vector2(10, 0);
            var checkImage = check.GetComponent<Image>();
            checkImage.color = AccentRed;
            toggle.graphic = checkImage;

            itemLabel = CreateTmp("Item Label", item, "Option", 16, TextAlignmentOptions.Left);
            StretchFull(itemLabel);
            itemLabel.rectTransform.offsetMin = new Vector2(36, 4);
            itemLabel.rectTransform.offsetMax = new Vector2(-8, -4);
            itemLabel.raycastTarget = false;

            scroll.viewport = viewport;
            scroll.content = content;
            template.gameObject.SetActive(false);
            return template;
        }

        static RectTransform CreateRect(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            if (size == Vector2.zero) StretchFull(rt);
            if (go.GetComponent<Image>() == null && name != "ReelContent" && name != "Grid")
                go.AddComponent<Image>();
            return rt;
        }

        static Image GetOrAddImage(RectTransform rt)
        {
            var img = rt.GetComponent<Image>();
            return img != null ? img : rt.gameObject.AddComponent<Image>();
        }

        static TextMeshProUGUI CreateTmp(string name, RectTransform parent, string text, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = TextWhite;
            tmp.alignment = align;
            var defaultFont = TryGetTmpDefaultFont();
            if (defaultFont != null)
                tmp.font = defaultFont;
            return tmp;
        }

        static TMP_FontAsset TryGetTmpDefaultFont()
        {
            try
            {
                return TMP_Settings.defaultFontAsset;
            }
            catch (System.NullReferenceException)
            {
                return null;
            }
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void StretchFull(TextMeshProUGUI t) => StretchFull(t.rectTransform);

        static void StretchTop(RectTransform rt, float top, float height)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -top);
            rt.sizeDelta = new Vector2(0, height);
        }

        static void StretchTop(TextMeshProUGUI t, float top, float height) => StretchTop(t.rectTransform, top, height);

        static void StretchBottom(RectTransform rt, float bottom, float height)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, bottom);
            rt.sizeDelta = new Vector2(0, height);
        }

        static void StretchBottom(TextMeshProUGUI t, float bottom, float height) => StretchBottom(t.rectTransform, bottom, height);

        static void StretchCenter(RectTransform rt, float y, float xPad, float w, float h)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static void StretchCenter(TextMeshProUGUI t, float y, float xPad, float w, float h) =>
            StretchCenter(t.rectTransform, y, xPad, w, h);

        static void DisableRaycast(RectTransform rt)
        {
            var img = rt.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
        }

        static void DestroyImage(RectTransform rt)
        {
            var img = rt.GetComponent<Image>();
            if (img != null) Object.DestroyImmediate(img);
        }

        static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Null-safe SerializedProperty setter — skips if field not found (screen may have been refactored).</summary>
        static void SetObjRef(SerializedObject so, string field, Object value)
        {
            var prop = so.FindProperty(field);
            if (prop != null) prop.objectReferenceValue = value;
        }

        static GameObject SavePrefab(GameObject source, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);
            return prefab;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
