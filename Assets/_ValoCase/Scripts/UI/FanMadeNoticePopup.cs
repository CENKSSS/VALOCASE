using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI
{
    // One-time first-launch fan-made legal notice. Shown once per local install before
    // the first-launch profile popup; on OK it persists an accepted flag and chains to
    // FirstLaunchProfilePopup. Runtime-built with zero prefab dependency, same pattern
    // as NoInternetPopup. Independent of backend; storage is local PlayerPrefs so a
    // save/data reset re-shows it.
    public sealed class FanMadeNoticePopup : MonoBehaviour
    {
        public const string AcceptedKey = "valocase.legal.fanMadeNoticeAccepted";

        const string TitleText = "Fan-Made Notice";
        const string BodyText =
            "This is a fan-made game.\nWe are not affiliated with any company, publisher, or official organization.";
        const string ButtonText = "OK";

        static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.85f);
        static readonly Color CardBg   = new Color(0.051f, 0.067f, 0.090f, 1f);
        static readonly Color Accent   = new Color(1f, 0.275f, 0.333f, 1f);
        static readonly Color TextMain = new Color(0.961f, 0.961f, 0.961f, 1f);
        static readonly Color DarkText = new Color(0.043f, 0.055f, 0.082f, 1f);

        static FanMadeNoticePopup _instance;

        public static bool IsAccepted() => PlayerPrefs.GetInt(AcceptedKey, 0) == 1;

        public static void TryShow()
        {
            if (_instance != null) return;
            if (IsAccepted()) { FirstLaunchProfilePopup.TryShow(); return; }

            var go = new GameObject("FanMadeNoticePopup");
            _instance = go.AddComponent<FanMadeNoticePopup>();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        IEnumerator Start()
        {
            // Wait a frame so scene UI (canvas, screens) finished building first.
            yield return null;

            var parent = FindPopupParent();
            if (parent == null)
            {
                Debug.LogWarning("[FanMadeNotice] No Canvas found — notice skipped this session.");
                Destroy(gameObject);
                FirstLaunchProfilePopup.TryShow();
                yield break;
            }

            transform.SetParent(parent, false);
            BuildUi();
            transform.SetAsLastSibling();
            Debug.Log("[FanMadeNotice] Popup shown (not yet accepted).");
        }

        static Transform FindPopupParent()
        {
            var safeArea = GameObject.Find("SafeArea");
            if (safeArea != null) return safeArea.transform;

            Canvas best = null;
            int bestOrder = int.MinValue;
            foreach (var c in FindObjectsOfType<Canvas>())
                if (c.isRootCanvas && c.sortingOrder > bestOrder) { bestOrder = c.sortingOrder; best = c; }
            return best != null ? best.transform : null;
        }

        void OnOkClicked()
        {
            PlayerPrefs.SetInt(AcceptedKey, 1);
            PlayerPrefs.Save();
            Destroy(gameObject);
            FirstLaunchProfilePopup.TryShow();
        }

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
            cardRt.sizeDelta = new Vector2(480f, 340f);
            card.GetComponent<Image>().color = CardBg;
            var cardOl = card.GetComponent<Outline>();
            cardOl.effectColor    = new Color(Accent.r, Accent.g, Accent.b, 0.85f);
            cardOl.effectDistance = new Vector2(2f, -2f);

            var title = UIBuild.MakeTmp(card.transform, "Title", TitleText, 24f, FontStyles.Bold, Accent);
            title.alignment = TextAlignmentOptions.Center;
            var tRt = title.rectTransform;
            tRt.anchorMin        = new Vector2(0f, 1f);
            tRt.anchorMax        = new Vector2(1f, 1f);
            tRt.pivot            = new Vector2(0.5f, 1f);
            tRt.anchoredPosition = new Vector2(0f, -34f);
            tRt.sizeDelta        = new Vector2(-40f, 34f);

            var body = UIBuild.MakeTmp(card.transform, "Body", BodyText, 17f, FontStyles.Normal, TextMain);
            body.alignment          = TextAlignmentOptions.Center;
            body.enableWordWrapping = true;
            var bRt = body.rectTransform;
            bRt.anchorMin        = new Vector2(0f, 1f);
            bRt.anchorMax        = new Vector2(1f, 1f);
            bRt.pivot            = new Vector2(0.5f, 1f);
            bRt.anchoredPosition = new Vector2(0f, -92f);
            bRt.sizeDelta        = new Vector2(-56f, 150f);

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
