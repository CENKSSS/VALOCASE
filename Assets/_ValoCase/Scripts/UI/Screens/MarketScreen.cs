using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Market screen — Coming Soon placeholder.
    /// UI built procedurally in BuildOnce(); no Inspector setup needed.
    /// </summary>
    public sealed class MarketScreen : UIScreenBase
    {
        [SerializeField] UINavigator navigator;
        [SerializeField] Button backButton;

        static readonly Color ActiveRed = new Color(1f, 0.122f, 0.224f, 1f);
        static readonly Color TextBright= new Color(0.925f, 0.910f, 0.882f, 1f);
        static readonly Color TextDim   = new Color(1f, 1f, 1f, 0.38f);

        bool _built;

        void Awake()
        {
            if (backButton != null)
                backButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.MainMenu));
        }

        protected override void OnShown()
        {
            BuildOnce();
        }

        void BuildOnce()
        {
            if (_built) return;
            _built = true;

            var rt = (RectTransform)transform;

            // Shared section background (cover image, aspect preserved)
            FullscreenBackground.AttachShared(gameObject);

            // MARKET title
            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(rt, false);
            var tRt = (RectTransform)titleGo.transform;
            tRt.anchorMin        = new Vector2(0f, 0.5f);
            tRt.anchorMax        = new Vector2(1f, 0.5f);
            tRt.pivot            = new Vector2(0.5f, 0.5f);
            tRt.anchoredPosition = new Vector2(0f, 40f);
            tRt.sizeDelta        = new Vector2(0f, 52f);
            var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text             = "MARKET";
            titleTmp.fontSize         = 34f;
            titleTmp.fontStyle        = FontStyles.Bold;
            titleTmp.alignment        = TextAlignmentOptions.Center;
            titleTmp.color            = ActiveRed;
            titleTmp.characterSpacing = 6f;
            titleTmp.raycastTarget    = false;
            titleTmp.enableWordWrapping = false;

            // Thin divider line under title
            var divGo = new GameObject("Div", typeof(RectTransform), typeof(Image));
            divGo.transform.SetParent(rt, false);
            var dRt = (RectTransform)divGo.transform;
            dRt.anchorMin        = new Vector2(0.1f, 0.5f);
            dRt.anchorMax        = new Vector2(0.9f, 0.5f);
            dRt.pivot            = new Vector2(0.5f, 0.5f);
            dRt.anchoredPosition = new Vector2(0f, 4f);
            dRt.sizeDelta        = new Vector2(0f, 1f);
            divGo.GetComponent<Image>().color = new Color(1f, 0.122f, 0.224f, 0.25f);
            divGo.GetComponent<Image>().raycastTarget = false;

            // "Coming Soon" sub-label
            var subGo = new GameObject("Sub", typeof(RectTransform));
            subGo.transform.SetParent(rt, false);
            var sRt = (RectTransform)subGo.transform;
            sRt.anchorMin        = new Vector2(0f, 0.5f);
            sRt.anchorMax        = new Vector2(1f, 0.5f);
            sRt.pivot            = new Vector2(0.5f, 0.5f);
            sRt.anchoredPosition = new Vector2(0f, -26f);
            sRt.sizeDelta        = new Vector2(0f, 36f);
            var subTmp = subGo.AddComponent<TextMeshProUGUI>();
            subTmp.text             = "Coming Soon";
            subTmp.fontSize         = 18f;
            subTmp.fontStyle        = FontStyles.Normal;
            subTmp.alignment        = TextAlignmentOptions.Center;
            subTmp.color            = TextDim;
            subTmp.raycastTarget    = false;
            subTmp.enableWordWrapping = false;
        }
    }
}
