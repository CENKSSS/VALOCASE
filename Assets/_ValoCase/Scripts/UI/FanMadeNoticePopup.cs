using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Services.Backend;

namespace ValoCase.UI
{
    // One-time first-launch fan-made legal notice. Shown once per local install before
    // the first-launch profile popup; accepting persists a flag and chains to
    // FirstLaunchProfilePopup. Runtime-built with zero prefab dependency, same pattern
    // as NoInternetPopup. Independent of backend; storage is local PlayerPrefs so a
    // save/data reset re-shows it.
    //
    // Any tap accepts — the OK button, the card body, or the backdrop. Real installs
    // showed players who saw this screen and never pressed the one button on it, in two
    // sessions two days apart; a notice must inform, not gatekeep, so every touch is the
    // acknowledgement. The statement itself still renders, which is what the flag means.
    public sealed class FanMadeNoticePopup : MonoBehaviour
    {
        public const string AcceptedKey = "valocase.legal.fanMadeNoticeAccepted";

        // Always English, by the owner's call — 1.0.29 briefly localized this to the
        // device language and that was reverted: one audience-wide wording, one string
        // to reason about. Still deliberately one short statement: the ad-driven player
        // deciding whether to stay reads a line, not a paragraph of legal distancing.
        const string TitleText  = "Fan-Made Notice";
        const string BodyText   =
            "This is an unofficial fan-made game.\nNot affiliated with any company or organization.";
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
            // Nothing spawns behind the mandatory update wall. A popup built under it can
            // never be touched, yet its "shown" telemetry would still fire — the Aug 9-10
            // cohort's funnel read "saw the notice" for players who only ever saw the
            // wall. The walled player updates and relaunches; the chain runs then.
            if (UpdateAvailablePopup.IsWallActive) return;

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

            // Same reasoning as the setup panel it chains to: the notice is shown on a
            // canvas built for the purpose rather than skipped, so the accepted flag is
            // only ever written by a player who actually saw it.
            var parent = FindPopupParent();
            if (parent == null)
            {
                Debug.LogWarning("[FanMadeNotice] No Canvas found — building a fallback overlay canvas.");
                parent = UIBuild.CreateFallbackOverlayCanvas("FanMadeNoticeCanvas");
            }

            transform.SetParent(parent, false);
            BuildUi();
            transform.SetAsLastSibling();
            // Emitted after the UI exists, not before: the event is meant to mean the
            // player saw the notice, and the no-canvas path above is precisely the case
            // where they did not.
            OnboardingTelemetry.Emit(OnboardingTelemetry.FanNoticeShown);
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

        // Guards the tap-anywhere close: Destroy is deferred to end of frame, so two
        // touches landing in the same frame would otherwise both run this — double
        // telemetry and a double TryShow.
        bool _accepted;

        void OnOkClicked()
        {
            if (_accepted) return;
            _accepted = true;

            OnboardingTelemetry.Emit(OnboardingTelemetry.FanNoticeAccepted);
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

            // The whole overlay accepts. Taps on the card body have no handler of their
            // own, so UGUI bubbles them up to this root button; the OK button below keeps
            // its own listener and consumes its taps first. One path, one flag write.
            var dimBtn = gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(OnOkClicked);

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
