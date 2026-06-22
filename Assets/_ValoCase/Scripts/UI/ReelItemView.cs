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

        // Neutral spin slot — no rarity color so the outcome stays hidden until reveal.
        // Panel is noticeably lighter than the spin background so each card reads as a
        // distinct slot/placeholder.
        static readonly Color SlotBg     = new Color(0.094f, 0.122f, 0.180f, 1f);
        static readonly Color SlotBorder = new Color(1f, 1f, 1f, 0.20f);
        const float CardScale = 0.6f;
        static readonly Vector2 IconSize = new Vector2(170f, 120f);

        void Awake() => RectTransform = transform as RectTransform;

        public void Bind(SkinDefinitionSO skin, RarityVisualSO visuals)
        {
            Skin = skin;
            ApplyNeutralStyle();
            if (skin == null) return;

            if (icon != null)
            {
                var sprite          = ResolveRollSprite(skin);
                icon.sprite         = sprite;
                icon.color          = Color.white;
                icon.enabled        = sprite != null;
                icon.preserveAspect = true;
                icon.material       = null;
                icon.raycastTarget  = false;
                icon.transform.SetAsLastSibling();
            }

            if (skinLabel != null) skinLabel.text = skin.SkinName;
        }

        // Roll-only presentation override: Melee rewards spin behind a generic golden
        // mystery icon; the real skin is revealed in the result popup and inventory.
        static Sprite _meleeMysteryIcon;
        static bool   _meleeMysteryResolved;

        static Sprite ResolveRollSprite(SkinDefinitionSO skin)
        {
            if (string.Equals(skin.WeaponName, "Melee", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!_meleeMysteryResolved)
                {
                    _meleeMysteryResolved = true;
                    _meleeMysteryIcon = Resources.Load<Sprite>(ProjectPaths.MeleeMysteryIconPath);
                }
                if (_meleeMysteryIcon != null) return _meleeMysteryIcon;
            }
            return skin.Icon;
        }

        // Same neutral dark slot for every card during the spin — rarity is only
        // revealed afterward in the result panel, never on the reel itself.
        void ApplyNeutralStyle()
        {
            if (RectTransform != null)
                RectTransform.localScale = new Vector3(CardScale, CardScale, 1f);

            if (rarityFrame != null)
            {
                rarityFrame.sprite = null;
                rarityFrame.type   = Image.Type.Simple;
                rarityFrame.color  = SlotBg;

                // Prefab root has no Outline; put the slot border on the panel itself.
                var border = rarityFrame.GetComponent<Outline>();
                if (border == null) border = rarityFrame.gameObject.AddComponent<Outline>();
                border.effectColor    = SlotBorder;
                border.effectDistance = new Vector2(2f, -2f);
            }

            if (glow != null) glow.gameObject.SetActive(false);
            if (weaponLabel != null) weaponLabel.gameObject.SetActive(false);

            if (icon != null) icon.rectTransform.sizeDelta = IconSize;
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
