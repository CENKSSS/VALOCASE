using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;

namespace ValoCase.UI
{
    public sealed class CaseListItemView : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] Image icon;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] TextMeshProUGUI price;
        [SerializeField] GameObject lockedOverlay;
        [SerializeField] GameObject selectedFrame;

        public CaseDefinitionSO Case { get; private set; }

        Action<CaseDefinitionSO> _onSelect;

        // Procedural "crossed chains + padlock" lock visual, built once on first lock.
        GameObject      _lockVisual;
        TextMeshProUGUI _lockLevelLabel;
        static Sprite   s_chainLinkSprite;
        static Sprite   s_padlockSprite;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(() => _onSelect?.Invoke(Case));
        }

        public void Bind(CaseDefinitionSO caseDef, bool unlocked, Action<CaseDefinitionSO> onSelect)
        {
            Case = caseDef;
            _onSelect = onSelect;
            if (title != null) title.text = caseDef.DisplayName;
            if (price != null) price.text = $"{caseDef.VpPrice:N0} VP";
            if (icon != null)
            {
                icon.sprite = caseDef.CaseIcon;
                // When a real sprite is present, show it untinted. Fall back to the
                // theme color only as a placeholder swatch when no icon is set.
                icon.color = caseDef.CaseIcon != null ? Color.white : caseDef.ThemeColor;
                icon.preserveAspect = true;
            }

            bool locked = !unlocked;
            if (lockedOverlay != null) lockedOverlay.SetActive(locked);

            if (locked)
            {
                EnsureLockVisual();   // built lazily — only locked cards pay for it
                // When the prefab dim overlay exists the chains live under it and follow
                // its visibility; otherwise toggle the chains directly here.
                if (_lockVisual != null && lockedOverlay == null) _lockVisual.SetActive(true);
                UpdateLockLevelLabel(caseDef);
            }
            else if (_lockVisual != null && lockedOverlay == null)
            {
                _lockVisual.SetActive(false);
            }

            if (button != null) button.interactable = unlocked;
        }

        public void SetSelected(bool selected)
        {
            if (selectedFrame != null) selectedFrame.SetActive(selected);
        }

        // ── Locked visual (crossed chains + padlock) ──────────────────────────────
        // Built once, parented under the dim overlay when present so it shows/hides with
        // the lock state. Unlocked cases simply hide the overlay → normal appearance.
        void EnsureLockVisual()
        {
            if (_lockVisual != null) return;

            var parent = lockedOverlay != null ? lockedOverlay.transform : transform;
            var cardRt = (RectTransform)transform;
            float w = cardRt.rect.width  > 1f ? cardRt.rect.width  : 260f;
            float h = cardRt.rect.height > 1f ? cardRt.rect.height : 100f;
            float diag  = Mathf.Sqrt(w * w + h * h);
            float angle = Mathf.Atan2(h, w) * Mathf.Rad2Deg;

            var go = new GameObject("LockChains", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();
            _lockVisual = go;

            // Two diagonal chains crossing corner-to-corner form the "X".
            MakeChainBar(rt,  angle, diag);
            MakeChainBar(rt, -angle, diag);
            MakeLockBadge(rt, Mathf.Clamp(h * 0.46f, 28f, 46f));

            // Small "unlocks at level N" caption beneath the padlock.
            var lblGo = new GameObject("LockLevel", typeof(RectTransform));
            lblGo.transform.SetParent(rt, false);
            var lrt = (RectTransform)lblGo.transform;
            lrt.anchorMin = new Vector2(0.5f, 0f);
            lrt.anchorMax = new Vector2(0.5f, 0f);
            lrt.pivot     = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = new Vector2(0f, 6f);
            lrt.sizeDelta = new Vector2(w, 16f);
            _lockLevelLabel = lblGo.AddComponent<TextMeshProUGUI>();
            _lockLevelLabel.alignment          = TextAlignmentOptions.Center;
            _lockLevelLabel.fontSize           = 12f;
            _lockLevelLabel.fontStyle          = FontStyles.Bold;
            _lockLevelLabel.color              = new Color(0.98f, 0.84f, 0.45f, 1f);
            _lockLevelLabel.raycastTarget      = false;
            _lockLevelLabel.enableWordWrapping = false;
        }

        void UpdateLockLevelLabel(CaseDefinitionSO caseDef)
        {
            if (_lockLevelLabel == null) return;
            bool showLevel = caseDef != null
                          && caseDef.UnlockType == CaseUnlockType.Level
                          && caseDef.UnlockRequirement > 0;
            if (showLevel) _lockLevelLabel.text = $"LEVEL {caseDef.UnlockRequirement}";
            _lockLevelLabel.gameObject.SetActive(showLevel);
        }

        // A diagonal chain: a rotated bar filled with overlapping oval links.
        void MakeChainBar(RectTransform parent, float angleDeg, float length)
        {
            const float thickness = 22f, pitch = 26f;

            var bar = new GameObject("Chain", typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            var rt = (RectTransform)bar.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(length, thickness);
            rt.localEulerAngles = new Vector3(0f, 0f, angleDeg);

            int   n      = Mathf.Max(1, Mathf.CeilToInt(length / pitch));
            float startX = -length * 0.5f + pitch * 0.5f;
            var   sprite = ChainLinkSprite();
            var   steel  = new Color(0.87f, 0.89f, 0.94f, 1f);

            for (int i = 0; i < n; i++)
            {
                var lk  = new GameObject("Link", typeof(RectTransform), typeof(Image));
                lk.transform.SetParent(rt, false);
                var lrt = (RectTransform)lk.transform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                lrt.pivot            = new Vector2(0.5f, 0.5f);
                lrt.anchoredPosition = new Vector2(startX + i * pitch, 0f);
                lrt.sizeDelta        = new Vector2(pitch + 6f, thickness);   // slight overlap
                var img = lk.GetComponent<Image>();
                img.sprite        = sprite;
                img.color         = steel;
                img.raycastTarget = false;
            }
        }

        void MakeLockBadge(RectTransform parent, float size)
        {
            var go = new GameObject("LockBadge", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(size * (40f / 48f), size);
            var img = go.GetComponent<Image>();
            img.sprite        = PadlockSprite();
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        // ── Procedural sprites (cached) ───────────────────────────────────────────
        // internal so the visible ShopScreen card builder can reuse the exact same
        // chain-link + padlock visuals instead of duplicating the texture generation.
        internal static Sprite ChainLinkSprite()
        {
            if (s_chainLinkSprite != null) return s_chainLinkSprite;

            const int w = 32, h = 24;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px  = new Color32[w * h];
            float cx = w * 0.5f, cy = h * 0.5f, rx = 14f, ry = 10f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float nx = (x - cx) / rx, ny = (y - cy) / ry;
                float v  = nx * nx + ny * ny;            // 1 == ellipse edge
                float a  = 0f;
                if (v <= 1.0f && v >= 0.42f)             // ring band around the edge
                {
                    float edge = Mathf.Min(1.0f - v, v - 0.42f);
                    a = Mathf.Clamp01(edge * 7f);        // soft inner/outer borders
                }
                px[y * w + x] = new Color32(238, 240, 247, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            s_chainLinkSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return s_chainLinkSprite;
        }

        internal static Sprite PadlockSprite()
        {
            if (s_padlockSprite != null) return s_padlockSprite;

            const int w = 40, h = 48;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px  = new Color32[w * h];
            var gold = new Color32(245, 205, 90, 255);
            float cx = w * 0.5f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool on = false;
                // Body (rounded-ish rectangle)
                if (x >= 6 && x <= 33 && y >= 3 && y <= 27) on = true;
                // Shackle: upper half of an annulus sitting on the body
                float dx = x - cx, dy = y - 27f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                if (y >= 27 && d <= 12.5f && d >= 7.5f) on = true;
                // Keyhole cutout
                float kx = x - cx, ky = y - 17f;
                if (on && (kx * kx + ky * ky) <= 9f) on = false;
                if (on && x >= (int)cx - 1 && x <= (int)cx + 1 && y >= 8 && y <= 17) on = false;
                px[y * w + x] = on ? gold : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(px);
            tex.Apply();
            s_padlockSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return s_padlockSprite;
        }
    }
}
