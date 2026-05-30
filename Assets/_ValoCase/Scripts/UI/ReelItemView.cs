using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;
using ValoCase.Pooling;

namespace ValoCase.UI
{
    public sealed class ReelItemView : MonoBehaviour, IPoolable
    {
        [SerializeField] Image icon;
        [SerializeField] Image rarityFrame;
        [SerializeField] Image glow;
        [SerializeField] TextMeshProUGUI weaponLabel;
        [SerializeField] TextMeshProUGUI skinLabel;

        public RectTransform RectTransform { get; private set; }
        public SkinDefinitionSO Skin { get; private set; }

        void Awake() => RectTransform = transform as RectTransform;

        public void Bind(SkinDefinitionSO skin, RarityVisualSO visuals)
        {
            Skin = skin;
            if (skin == null) return;

            // ── Skin icon — always white tint, always on top of glow/background ──
            if (icon != null)
            {
                Debug.Log("[REEL_ICON] skin=" + skin.SkinName);
                Debug.Log("[REEL_ICON] icon null=" + (skin.Icon == null));

                icon.sprite        = skin.Icon;
                icon.color         = Color.white;      // never inherit rarity tint or dark pool state
                icon.enabled       = skin.Icon != null;
                icon.preserveAspect = true;
                icon.material      = null;             // default Unity UI material — no custom shaders
                icon.raycastTarget = false;

                // Push icon above rarityFrame and glow so neither overlays it.
                icon.transform.SetAsLastSibling();

                Debug.Log("[REEL_ICON] image color=" + icon.color);
                Debug.Log("[REEL_ICON] material=" + icon.material);
                Debug.Log("[REEL_ICON] enabled=" + icon.enabled);
                Debug.Log("[REEL_ICON_FIX] applied white tint for " + skin.SkinName);
                Debug.Log("[REEL_ICON_FIX] icon sibling=" + icon.transform.GetSiblingIndex());
            }

            if (weaponLabel != null) weaponLabel.text = skin.WeaponName;
            if (skinLabel   != null) skinLabel.text   = skin.SkinName;

            if (visuals != null && visuals.TryGet(skin.Rarity, out var entry))
            {
                // rarityFrame is the full-card background — use deep rarity color.
                if (rarityFrame != null)
                    rarityFrame.color = entry.cardBgColor;

                // Glow overlay: soft neon tint behind the skin icon (NOT on top).
                if (glow != null)
                {
                    var alpha = skin.Rarity >= SkinRarity.Premium ? 0.28f : 0.14f;
                    glow.color = new Color(entry.primaryColor.r, entry.primaryColor.g, entry.primaryColor.b, alpha);
                    glow.gameObject.SetActive(true);
                    // Keep glow below icon — icon's SetAsLastSibling above already handles this.
                }

                // Border glow matches rarity accent.
                var outline = GetComponent<Outline>();
                if (outline != null)
                    outline.effectColor = new Color(entry.primaryColor.r, entry.primaryColor.g, entry.primaryColor.b, 0.75f);
            }
        }

        public void OnSpawned() { }

        public void OnDespawned()
        {
            Skin = null;
            // Reset icon to a clean white state so the next Bind starts fresh.
            if (icon != null) { icon.sprite = null; icon.color = Color.white; icon.enabled = false; }
        }
    }
}
