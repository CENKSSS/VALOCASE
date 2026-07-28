using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Services.Backend;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI
{
    // Floating toast shown once per joinable FREE LOBBY EVENT lobby identity.
    // State comes only from the real lobby list — never fabricated locally.
    // It appears directly when triggered, auto-hides after a timeout, and a tap
    // opens the Battle lobby list. Once an event key is shown it stays marked, so
    // polling never respawns the same event.
    public sealed class FreeLobbyEventBanner : MonoBehaviour, IPointerClickHandler
    {
        public static FreeLobbyEventBanner Instance { get; private set; }

        const float PollInterval = 20f;   // global heartbeat; suppressed while the lobby list feeds us
        const float AutoHide     = 5f;

        UINavigator   _navigator;
        RectTransform _rt;
        CanvasGroup   _cg;
        GameObject    _box;
        Coroutine     _hideCo;
        Vector2       _restPos;
        string        _shownKey;
        float         _lastFeedAt;

        public static void Ensure(Transform parent, UINavigator navigator)
        {
            if (Instance != null) { Instance._navigator = navigator; return; }
            if (parent == null) return;
            var go = NewGo("FreeLobbyEventBanner", parent);
            go.AddComponent<FreeLobbyEventBanner>()._navigator = navigator;
        }

        // eventKey = battleId of a joinable event lobby, or null when none is active.
        public static void NotifyEventActive(string eventKey)
        {
            if (Instance == null) return;
            Instance._lastFeedAt = Time.unscaledTime;
            Instance.Apply(eventKey);
        }

        public static bool IsJoinableEvent(LobbyResponse r)
        {
            if (r == null) return false;
            if (!string.Equals(r.status, "WAITING", StringComparison.OrdinalIgnoreCase)) return false;
            int max = r.maxSlots > 0 ? r.maxSlots : 2;
            if (r.filledSlots >= max) return false;
            return r.isEventLobby || !string.IsNullOrEmpty(r.eventType) || r.entryCost <= 0;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            SetVisible(false);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnEnable() => StartCoroutine(PollLoop());

        void Apply(string eventKey)
        {
            if (string.IsNullOrEmpty(eventKey)) return;
            if (eventKey == _shownKey) return;
            _shownKey = eventKey;
            ShowToast();
        }

        static readonly Color CardGold = new Color(0.914f, 0.816f, 0.529f, 1f);   // #E9D087 champagne gold
        static readonly Color CardInk  = new Color(0.149f, 0.125f, 0.063f, 1f);   // #261F10 warm near-black
        static readonly Color CardCream= new Color(0.973f, 0.945f, 0.851f, 1f);   // #F8F1D9 warm ivory glyph
        static readonly Color GoldEdge = new Color(0.722f, 0.565f, 0.231f, 1f);   // #B8903B deep gold edge

        static Sprite _roundedSprite;
        static Sprite RoundedCard()
        {
            if (_roundedSprite == null)
                _roundedSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            return _roundedSprite;
        }

        void Build()
        {
            _rt = (RectTransform)transform;
            _rt.anchorMin = new Vector2(0.5f, 1f);
            _rt.anchorMax = new Vector2(0.5f, 1f);
            _rt.pivot     = new Vector2(0.5f, 1f);
            _rt.sizeDelta = new Vector2(340f, 72f);
            _restPos      = new Vector2(0f, -(TopProfileBar.Height + 10f));
            _rt.anchoredPosition = _restPos;

            _cg = gameObject.AddComponent<CanvasGroup>();

            var card = MakeImage("Card", _rt, CardGold, raycast: true);
            card.sprite                  = RoundedCard();
            card.type                    = Image.Type.Sliced;
            card.pixelsPerUnitMultiplier = 0.5f;
            Stretch(card.rectTransform);
            _box = card.gameObject;

            var shadowSoft = card.gameObject.AddComponent<Shadow>();
            shadowSoft.effectColor    = new Color(0f, 0f, 0f, 0.16f);
            shadowSoft.effectDistance = new Vector2(0f, -7f);

            var shadowTight = card.gameObject.AddComponent<Shadow>();
            shadowTight.effectColor    = new Color(0f, 0f, 0f, 0.28f);
            shadowTight.effectDistance = new Vector2(0f, -3f);

            var edge = card.gameObject.AddComponent<Outline>();
            edge.effectColor    = ColorPalette.WithAlpha(GoldEdge, 0.55f);
            edge.effectDistance = new Vector2(1f, -1f);

            var badge = MakeImage("IconBadge", card.transform, CardInk);
            badge.sprite         = CircleSprite();
            badge.preserveAspect = true;
            SetRect(badge.rectTransform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(16f, 0f), new Vector2(34f, 34f));

            var glyph = MakeTmp(badge.transform, "Glyph", "!", 22f, FontStyles.Bold, CardCream);
            glyph.alignment = TextAlignmentOptions.Center;
            Stretch(glyph.rectTransform);

            var label = MakeTmp(card.transform, "Label", "TAP TO JOIN\nFREE EVENT", 19f, FontStyles.Bold, CardInk);
            label.alignment        = TextAlignmentOptions.MidlineLeft;
            label.characterSpacing = 1.2f;
            label.lineSpacing      = -6f;
            label.overflowMode     = TextOverflowModes.Overflow;
            SetRect(label.rectTransform,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(62f, 0f), new Vector2(-78f, 50f));

            transform.SetAsLastSibling();
        }

        void ShowToast()
        {
            SetVisible(true);
            transform.SetAsLastSibling();
            _rt.anchoredPosition = _restPos;
            if (_cg != null) _cg.alpha = 1f;
            if (_hideCo != null) StopCoroutine(_hideCo);
            _hideCo = StartCoroutine(HideAfter());
        }

        IEnumerator HideAfter()
        {
            yield return new WaitForSecondsRealtime(AutoHide);
            Hide();
            _hideCo = null;
        }

        public void OnPointerClick(PointerEventData e) => OnTap();

        void OnTap()
        {
            if (_navigator == null) _navigator = FindFirstObjectByType<UINavigator>();
            _navigator?.Navigate(ScreenType.CaseBattleLobby);
            Hide();
        }

        // Hides the toast. The shown key is intentionally kept so polling treats this
        // event as already handled and never respawns it.
        void Hide()
        {
            if (_hideCo != null) { StopCoroutine(_hideCo); _hideCo = null; }
            SetVisible(false);
            _rt.anchoredPosition = _restPos;
            if (_cg != null) _cg.alpha = 1f;
        }

        void SetVisible(bool active)
        {
            if (_box != null && _box.activeSelf != active) _box.SetActive(active);
        }

        IEnumerator PollLoop()
        {
            var wait = new WaitForSecondsRealtime(2f);
            while (true)
            {
                if (Time.unscaledTime - _lastFeedAt >= PollInterval)
                    yield return Poll();
                yield return wait;
            }
        }

        IEnumerator Poll()
        {
            var ctx = GameContext.Instance;
            if (ctx == null || !ctx.BackendReady || ctx.Backend == null) yield break;

            LobbyListResponse resp = null;
            yield return ctx.Backend.GetBattleLobbies(r => resp = r, _ => { });
            if (resp == null) yield break;

            _lastFeedAt = Time.unscaledTime;

            string key = null;
            if (resp.lobbies != null)
                foreach (var r in resp.lobbies)
                    if (IsJoinableEvent(r)) { key = r.battleId; break; }
            Apply(key);
        }
    }
}
