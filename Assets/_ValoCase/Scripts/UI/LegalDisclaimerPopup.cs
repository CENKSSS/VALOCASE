using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI
{
    // On-demand Legal Disclaimer / IP Notice, opened from the Settings screen.
    // Unlike FanMadeNoticePopup (first-launch, PlayerPrefs-gated), this can be
    // reopened any number of times and has no persisted state.
    public sealed class LegalDisclaimerPopup : MonoBehaviour
    {
        const string TitleText = "LEGAL DISCLAIMER";
        const string BodyText =
            "ValoCase is an independent, fan-made entertainment app. It is not affiliated with, " +
            "endorsed, sponsored, or approved by any company, publisher, or official organization.\n\n" +
            "Any referenced game-related names, trademarks, artwork, or assets belong to their respective owners.\n\n" +
            "ValoCase does not offer real-money gambling, betting, cash-out, or trading. All in-game currency " +
            "and items are virtual, for entertainment purposes only, non-transferable, and have no real-world " +
            "monetary value.";
        const string ButtonText = "OK";

        static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.85f);
        static readonly Color CardBg   = new Color(0.051f, 0.067f, 0.090f, 1f);
        static readonly Color Accent   = new Color(1f, 0.275f, 0.333f, 1f);
        static readonly Color TextMain = new Color(0.961f, 0.961f, 0.961f, 1f);
        static readonly Color DarkText = new Color(0.043f, 0.055f, 0.082f, 1f);

        static LegalDisclaimerPopup _instance;

        public static void Show(Transform parent)
        {
            if (_instance != null) return;
            if (parent == null) return;

            var go = new GameObject("LegalDisclaimerPopup");
            _instance = go.AddComponent<LegalDisclaimerPopup>();
            go.transform.SetParent(parent, false);
            _instance.BuildUi();
            go.transform.SetAsLastSibling();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void OnOkClicked() => Destroy(gameObject);

        void BuildUi()
        {
            var rootRt = gameObject.GetComponent<RectTransform>();
            if (rootRt == null) rootRt = gameObject.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;

            var dim = gameObject.AddComponent<Image>();
            dim.color         = Backdrop;
            dim.raycastTarget = true;

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Outline));
            card.transform.SetParent(transform, false);
            var cardRt = (RectTransform)card.transform;
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(520f, 460f);
            card.GetComponent<Image>().color = CardBg;
            var cardOl = card.GetComponent<Outline>();
            cardOl.effectColor    = new Color(Accent.r, Accent.g, Accent.b, 0.85f);
            cardOl.effectDistance = new Vector2(2f, -2f);

            var title = UIBuild.MakeTmp(card.transform, "Title", TitleText, 22f, FontStyles.Bold, Accent);
            title.alignment       = TextAlignmentOptions.Center;
            title.characterSpacing = 2f;
            var tRt = title.rectTransform;
            tRt.anchorMin        = new Vector2(0f, 1f);
            tRt.anchorMax        = new Vector2(1f, 1f);
            tRt.pivot            = new Vector2(0.5f, 1f);
            tRt.anchoredPosition = new Vector2(0f, -30f);
            tRt.sizeDelta        = new Vector2(-40f, 30f);

            var body = UIBuild.MakeTmp(card.transform, "Body", BodyText, 15f, FontStyles.Normal, TextMain);
            body.alignment          = TextAlignmentOptions.TopLeft;
            body.enableWordWrapping = true;
            var bRt = body.rectTransform;
            bRt.anchorMin        = new Vector2(0f, 1f);
            bRt.anchorMax        = new Vector2(1f, 1f);
            bRt.pivot            = new Vector2(0.5f, 1f);
            bRt.anchoredPosition = new Vector2(0f, -72f);
            bRt.sizeDelta        = new Vector2(-56f, 300f);

            var btnGo = new GameObject("OkBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(card.transform, false);
            var btnRt = (RectTransform)btnGo.transform;
            btnRt.anchorMin        = new Vector2(0.5f, 0f);
            btnRt.anchorMax        = new Vector2(0.5f, 0f);
            btnRt.pivot            = new Vector2(0.5f, 0f);
            btnRt.anchoredPosition = new Vector2(0f, 30f);
            btnRt.sizeDelta        = new Vector2(220f, 56f);
            var btnImg = btnGo.GetComponent<Image>();
            btnImg.color = Accent;
            var btn = btnGo.GetComponent<Button>();
            btn.transition    = Selectable.Transition.None;
            btn.targetGraphic = btnImg;
            UIBuild.WireButtonClick(btn);
            btn.onClick.AddListener(OnOkClicked);

            var btnLbl = UIBuild.MakeTmp(btnGo.transform, "Lbl", ButtonText, 18f, FontStyles.Bold, DarkText);
            btnLbl.alignment = TextAlignmentOptions.Center;
            UIBuild.Stretch(btnLbl.rectTransform);
        }
    }
}
