using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.Services.Ads
{
    public enum RewardedAdResult { Completed, Cancelled, Failed }

    // Server-known reward placement ids. The backend grants the reward; these strings
    // are the only contract the client needs to address each placement.
    public static class AdRewardTypes
    {
        public const string EarnVp2x      = "EARN_VP_2X";
        public const string UpgradePlus5  = "UPGRADE_PLUS_5";
        public const string MarketVp2500  = "MARKET_VP_2500";
    }

    // Maps a backend unavailableReason code to a clean player-facing Turkish label. Never
    // shows the raw code; an unknown/empty reason falls back to a generic message.
    public static class AdRewardMessages
    {
        public static string MapUnavailable(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "Şu anda kullanılamıyor.";
            switch (reason.ToUpperInvariant())
            {
                case "COOLDOWN":                   return "Biraz bekle, tekrar hazır olacak.";
                case "ALREADY_ACTIVE":             return "Bonus zaten aktif.";
                case "ALREADY_USED":
                case "ALREADY_USED_FOR_CONTEXT":   return "Bu yükseltme için kullanıldı.";
                case "EARN_VP_NO_ACTIVE_SESSION":  return "2X bonus icin reklam izle.";
                case "AUTH_PENDING":               return "Kimlik dogrulaniyor...";
                case "NO_CONTEXT":
                case "INVALID_CONTEXT":
                case "NO_SELECTION":               return "Önce skin ve hedef seç.";
                default:                           return "Şu anda kullanılamıyor.";
            }
        }
    }

    // Rewarded-ad provider abstraction. A real SDK adapter (AdMob / Unity Ads / ironSource /
    // AppLovin) implements this same interface and is swapped in without touching callers.
    // onResult delivers a clean terminal state and, on Completed, the verification token the
    // backend claim should carry (an SSV token from a real SDK; a mock token here).
    public interface IRewardedAdService
    {
        bool IsReady { get; }
        void Show(string placementId, Action<RewardedAdResult, string> onResult);
    }

    // Development mock: no real ad SDK. Plays a short dark overlay with a fill bar and a
    // skip control, then reports Completed (with a mock token), Cancelled, or Failed.
    public sealed class MockRewardedAdService : MonoBehaviour, IRewardedAdService
    {
        const float PlaySeconds = 1.6f;

        static readonly Color Backdrop = new Color(0.020f, 0.027f, 0.039f, 0.92f);
        static readonly Color Card     = new Color(0.051f, 0.067f, 0.090f, 1f);
        static readonly Color Accent   = new Color(1.000f, 0.275f, 0.333f, 1f);
        static readonly Color TextMain = new Color(0.961f, 0.961f, 0.961f, 1f);
        static readonly Color TextSub  = new Color(0.541f, 0.569f, 0.651f, 1f);
        static readonly Color TrackBg  = new Color(0.13f, 0.15f, 0.19f, 1f);

        bool _showing;
        public bool IsReady => !_showing;

        public static MockRewardedAdService Create(Transform parent)
        {
            var go = new GameObject("MockRewardedAdService");
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<MockRewardedAdService>();
        }

        public void Show(string placementId, Action<RewardedAdResult, string> onResult)
        {
            if (_showing) { onResult?.Invoke(RewardedAdResult.Failed, null); return; }
            _showing = true;
            StartCoroutine(PlayRoutine(placementId, onResult));
        }

        IEnumerator PlayRoutine(string placementId, Action<RewardedAdResult, string> onResult)
        {
            bool cancelled = false;
            var overlay = BuildOverlay(out var fill, () => cancelled = true);

            float t = 0f;
            while (t < PlaySeconds && !cancelled)
            {
                t += Time.unscaledDeltaTime;
                if (fill != null) fill.fillAmount = Mathf.Clamp01(t / PlaySeconds);
                yield return null;
            }

            if (overlay != null) Destroy(overlay);
            _showing = false;

            if (cancelled) { onResult?.Invoke(RewardedAdResult.Cancelled, null); yield break; }
            onResult?.Invoke(RewardedAdResult.Completed, $"mock-{placementId}-{DateTime.UtcNow.Ticks}");
        }

        GameObject BuildOverlay(out Image fill, Action onCancel)
        {
            var canvasGo = new GameObject("MockAdOverlay",
                typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight  = 0.5f;

            var root = (RectTransform)canvasGo.transform;
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero; root.offsetMax = Vector2.zero;

            var bg = canvasGo.AddComponent<Image>();
            bg.color = Backdrop;

            var card = NewRect(root, "Card", new Vector2(0.5f, 0.5f));
            card.sizeDelta = new Vector2(620f, 320f);
            var cardImg = card.gameObject.AddComponent<Image>();
            cardImg.color = Card;
            var cardOl = card.gameObject.AddComponent<Outline>();
            cardOl.effectColor    = new Color(Accent.r, Accent.g, Accent.b, 0.8f);
            cardOl.effectDistance = new Vector2(2f, -2f);

            var tag = CreateTmp(card, "Tag", "MOCK REKLAM", 16f, TextSub, FontStyles.Bold);
            tag.characterSpacing = 6f;
            Place(tag.rectTransform, new Vector2(0f, 96f), new Vector2(560f, 28f));

            var title = CreateTmp(card, "Title", "Ödül için reklam izleniyor", 30f, TextMain, FontStyles.Bold);
            Place(title.rectTransform, new Vector2(0f, 40f), new Vector2(560f, 44f));

            var track = NewRect(card, "Track", new Vector2(0.5f, 0.5f));
            Place(track, new Vector2(0f, -28f), new Vector2(500f, 16f));
            var trackImg = track.gameObject.AddComponent<Image>();
            trackImg.color = TrackBg;

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(track, false);
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(2f, 2f); fillRt.offsetMax = new Vector2(-2f, -2f);
            fill = fillGo.GetComponent<Image>();
            fill.color       = Accent;
            fill.type        = Image.Type.Filled;
            fill.fillMethod  = Image.FillMethod.Horizontal;
            fill.fillOrigin  = 0;
            fill.fillAmount  = 0f;

            var skipGo = new GameObject("Skip", typeof(RectTransform), typeof(Image), typeof(Button));
            skipGo.transform.SetParent(card, false);
            var skipRt = (RectTransform)skipGo.transform;
            Place(skipRt, new Vector2(0f, -98f), new Vector2(220f, 48f));
            skipGo.GetComponent<Image>().color = TrackBg;
            var skipLbl = CreateTmp(skipRt, "Lbl", "ATLA / İPTAL", 16f, TextSub, FontStyles.Bold);
            skipLbl.rectTransform.anchorMin = Vector2.zero; skipLbl.rectTransform.anchorMax = Vector2.one;
            skipLbl.rectTransform.offsetMin = Vector2.zero; skipLbl.rectTransform.offsetMax = Vector2.zero;
            var skipBtn = skipGo.GetComponent<Button>();
            skipBtn.transition = Selectable.Transition.None;
            skipBtn.onClick.AddListener(() => onCancel?.Invoke());

            return canvasGo;
        }

        static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static RectTransform NewRect(Transform parent, string name, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = pivot;
            return rt;
        }

        static TextMeshProUGUI CreateTmp(Transform parent, string name, string text,
            float size, Color color, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = text;
            tmp.fontSize           = size;
            tmp.fontStyle          = style;
            tmp.color              = color;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.raycastTarget      = false;
            tmp.enableWordWrapping = false;
            return tmp;
        }
    }
}
