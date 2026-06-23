using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Services.Ads;
using ValoCase.Services.Backend;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Market screen — Coming Soon placeholder.
    /// UI built procedurally in BuildOnce(); no Inspector setup needed.
    /// </summary>
    public sealed class MarketScreen : UIScreenBase
    {
        static readonly Color ActiveRed = new Color(1f, 0.122f, 0.224f, 1f);
        static readonly Color TextBright= new Color(0.925f, 0.910f, 0.882f, 1f);
        static readonly Color TextDim   = new Color(1f, 1f, 1f, 0.38f);
        static readonly Color CardDark  = new Color(0.051f, 0.067f, 0.090f, 1f);

        const int RewardVpDisplay = 2500;

        bool _built;
        Button          _adButton;
        TextMeshProUGUI _adButtonLabel;
        Image           _adIcon;
        bool            _adInFlight;

        bool      _cooldownActive;
        float     _cooldownEndRealtime;
        Coroutine _cooldownTicker;

        protected override void OnShown()
        {
            BuildOnce();
            bool backend = GameContext.Instance?.BackendEnabled ?? false;
            if (_adButton != null) _adButton.gameObject.SetActive(backend);
            if (!backend) return;
            ResetAdButton();
            RefreshMarketStatus();
        }

        protected override void OnHidden()
        {
            StopCooldownTicker();
            _cooldownActive = false;
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

            BuildAdButton(rt);
        }

        void BuildAdButton(RectTransform rt)
        {
            var go = new GameObject("AdRewardButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(rt, false);
            var brt = (RectTransform)go.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(0f, -120f);
            brt.sizeDelta        = new Vector2(360f, 84f);

            var img = go.GetComponent<Image>();
            img.color = CardDark;
            var ol = go.AddComponent<Outline>();
            ol.effectColor    = new Color(ActiveRed.r, ActiveRed.g, ActiveRed.b, 0.85f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            _adButton = go.GetComponent<Button>();
            _adButton.transition = Selectable.Transition.None;
            _adButton.onClick.AddListener(OnAdButtonClicked);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(22f, 0f);
            irt.sizeDelta        = new Vector2(56f, 56f);
            _adIcon = iconGo.GetComponent<Image>();
            _adIcon.raycastTarget  = false;
            _adIcon.preserveAspect = true;

            var iconSprite = Resources.Load<Sprite>(ProjectPaths.ArayuzRoot + "/MarketAdIcon");
            if (iconSprite != null) { _adIcon.sprite = iconSprite; _adIcon.color = Color.white; }
            else
            {
                _adIcon.color = new Color(ActiveRed.r, ActiveRed.g, ActiveRed.b, 0.18f);
                var glyphGo = new GameObject("Glyph", typeof(RectTransform));
                glyphGo.transform.SetParent(iconGo.transform, false);
                var grt = (RectTransform)glyphGo.transform;
                grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
                grt.offsetMin = Vector2.zero; grt.offsetMax = Vector2.zero;
                var gt = glyphGo.AddComponent<TextMeshProUGUI>();
                gt.text          = "▶";
                gt.fontSize      = 28f;
                gt.fontStyle     = FontStyles.Bold;
                gt.alignment     = TextAlignmentOptions.Center;
                gt.color         = ActiveRed;
                gt.raycastTarget = false;
            }

            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lrt = (RectTransform)lblGo.transform;
            lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 1f);
            lrt.offsetMin = new Vector2(90f, 0f); lrt.offsetMax = new Vector2(-16f, 0f);
            _adButtonLabel = lblGo.AddComponent<TextMeshProUGUI>();
            _adButtonLabel.fontSize           = 19f;
            _adButtonLabel.fontStyle          = FontStyles.Bold;
            _adButtonLabel.alignment          = TextAlignmentOptions.Left;
            _adButtonLabel.color              = TextBright;
            _adButtonLabel.raycastTarget      = false;
            _adButtonLabel.enableWordWrapping = false;
        }

        void ResetAdButton()
        {
            _adInFlight = false;
            _cooldownActive = false;
            StopCooldownTicker();
            if (_adButton != null) _adButton.interactable = true;
            if (_adButtonLabel != null) _adButtonLabel.text = $"REKLAM İZLE  +{RewardVpDisplay:N0} VP";
        }

        void OnAdButtonClicked()
        {
            if (_adInFlight || _cooldownActive) return;
            var ctx = GameContext.Instance;
            if (ctx == null || !ctx.BackendEnabled) { GameEvents.RaiseToast("Şu anda kullanılamıyor."); return; }

            _adInFlight = true;
            if (_adButton != null) _adButton.interactable = false;
            if (_adButtonLabel != null) _adButtonLabel.text = "REKLAM İZLENİYOR...";

            ctx.WatchMarketVp2500Ad(
                onClaimed: res =>
                {
                    if (this == null) return;
                    if (res != null && res.marketCooldownActive && res.marketCooldownRemainingSeconds > 0L)
                    {
                        if (res.grantedVp > 0L) GameEvents.RaiseToast($"+{RewardVpDisplay:N0} VP");
                        StartCooldown(res.marketCooldownRemainingSeconds);
                        return;
                    }
                    GameEvents.RaiseToast($"+{RewardVpDisplay:N0} VP");
                    ResetAdButton();
                },
                onFailed: msg =>
                {
                    if (this == null) return;
                    GameEvents.RaiseToast(MapAdFailure(msg));
                    ResetAdButton();
                },
                onCancelled: () =>
                {
                    if (this == null) return;
                    ResetAdButton();
                });
        }

        void RefreshMarketStatus()
        {
            var ctx = GameContext.Instance;
            if (ctx == null || !ctx.BackendEnabled) return;
            ctx.RefreshMarketAdStatus(
                res =>
                {
                    if (this == null || !isActiveAndEnabled) return;
                    var s = res?.Find(AdRewardTypes.MarketVp2500);
                    if (s != null && s.cooldownRemainingSeconds > 0L) StartCooldown(s.cooldownRemainingSeconds);
                    else ResetAdButton();
                },
                _ => { if (this != null) ResetAdButton(); });
        }

        void StartCooldown(long remainingSeconds)
        {
            _adInFlight = false;
            _cooldownActive = true;
            _cooldownEndRealtime = Time.unscaledTime + remainingSeconds;
            if (_adButton != null) _adButton.interactable = false;
            StopCooldownTicker();
            _cooldownTicker = StartCoroutine(CooldownTicker());
        }

        IEnumerator CooldownTicker()
        {
            var wait = new WaitForSecondsRealtime(1f);
            while (_cooldownActive)
            {
                int remaining = Mathf.CeilToInt(_cooldownEndRealtime - Time.unscaledTime);
                if (remaining <= 0)
                {
                    _cooldownActive = false;
                    RefreshMarketStatus();
                    yield break;
                }
                if (_adButtonLabel != null) _adButtonLabel.text = $"BEKLE  {FormatCooldown(remaining)}";
                yield return wait;
            }
        }

        void StopCooldownTicker()
        {
            if (_cooldownTicker != null) { StopCoroutine(_cooldownTicker); _cooldownTicker = null; }
        }

        static string FormatCooldown(int seconds)
            => $"{seconds / 60:00}:{seconds % 60:00}";

        static string MapAdFailure(string msg)
            => string.IsNullOrEmpty(msg) ? "Şu anda kullanılamıyor."
             : msg == "AUTH_PENDING"    ? AdRewardMessages.MapUnavailable(msg)
             : msg;
    }
}
