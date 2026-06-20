using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Services.Backend;
using ValoCase.Systems;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Tools hub screen — links to VP Kazan, Çark, and Görevler.
    ///
    /// VP KAZAN row navigates to the existing EarnVpScreen.
    /// ÇARK and GÖREVLER show a Coming Soon overlay (dismissible).
    /// All UI built procedurally in BuildOnce() — no Inspector setup needed.
    /// </summary>
    public sealed class ToolsScreen : UIScreenBase
    {
        [SerializeField] UINavigator navigator;
        [SerializeField] Button backButton;

        // ── Palette (Valorant dark navy + neon red, matches BottomNavBar) ─────
        static readonly Color CardBg    = new Color(0.040f, 0.065f, 0.125f, 1f);
        static readonly Color ActiveRed = new Color(1f, 0.122f, 0.224f, 1f);
        static readonly Color AccentVP  = new Color(0.902f, 0.816f, 0.435f, 1f);   // gold
        static readonly Color AccentWhl = new Color(0.68f,  0.08f,  1.00f, 1f);   // purple
        static readonly Color AccentMsn = new Color(0.00f,  0.90f,  1.00f, 1f);   // cyan
        static readonly Color TextBright= new Color(0.925f, 0.910f, 0.882f, 1f);
        static readonly Color TextDim   = new Color(1f, 1f, 1f, 0.38f);

        bool            _built;
        GameObject      _comingSoonPanel;
        TextMeshProUGUI _comingSoonTitle;
        MissionSystem   _missionSystem;
        MissionsScreen  _missionsPanel;

        const int DailyRewardVp = 2000;

        Button              _dailyClaimBtn;
        Image               _dailyClaimImg;
        TextMeshProUGUI     _dailyClaimLbl;
        TextMeshProUGUI     _dailyStatusLbl;
        bool                _dailyBackend;
        bool                _dailyStatusLoaded;
        bool                _dailyClaimInFlight;
        bool                _dailyRefetching;
        DailyStatusResponse _dailyStatus;
        double              _dailyNextClaimRealtime;
        float               _dailyTick;

        public void Inject(MissionSystem system)
        {
            _missionSystem = system;
            if (_built) EnsureMissionsPanel();
        }

        // Builds the MissionsScreen panel on demand. No longer depends on CompositionRoot
        // having injected the MissionSystem first: if injection was missed or late (the
        // Android symptom), it resolves a MissionSystem itself so the panel is always
        // available. Returns true when _missionsPanel is ready to Show().
        bool EnsureMissionsPanel()
        {
            if (_missionsPanel != null) return true;

            var system = ResolveMissionSystem();
            if (system == null)
            {
                Debug.LogWarning("[ToolsScreen] MissionSystem unavailable — cannot build MissionsScreen yet.");
                return false;
            }

            try
            {
                var rt    = (RectTransform)transform;
                var msnGo = new GameObject("MissionsPanel", typeof(RectTransform));
                msnGo.transform.SetParent(rt, false);
                var mRt   = (RectTransform)msnGo.transform;
                mRt.anchorMin = Vector2.zero; mRt.anchorMax = Vector2.one;
                mRt.offsetMin = mRt.offsetMax = Vector2.zero;
                _missionsPanel = msnGo.AddComponent<MissionsScreen>();
                _missionsPanel.Init(system);
                msnGo.SetActive(false);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[ToolsScreen] Failed to create MissionsScreen: " + e);
                _missionsPanel = null;
                return false;
            }
        }

        // Prefers the injected MissionSystem; if it was never injected (Android build
        // showed Coming Soon because of this), lazily constructs one from GameContext so
        // the screen is never blocked by a missed/late injection. Mission backend logic
        // is untouched — this only obtains the same system CompositionRoot would have.
        MissionSystem ResolveMissionSystem()
        {
            if (_missionSystem != null) return _missionSystem;

            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Save == null)
            {
                Debug.LogWarning("[ToolsScreen] GameContext/Save not ready — cannot resolve MissionSystem.");
                return null;
            }

            try
            {
                var ms = new MissionSystem(ctx.Save);
                ms.Initialize();
                _missionSystem = ms;
                Debug.Log("[ToolsScreen] MissionSystem resolved lazily (injection was missing/late).");
                return _missionSystem;
            }
            catch (Exception e)
            {
                Debug.LogError("[ToolsScreen] Failed to construct MissionSystem: " + e);
                return null;
            }
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            if (backButton != null)
                backButton.gameObject.SetActive(false);
        }

        protected override void OnShown()
        {
            BuildOnce();
            // Make sure overlays are hidden when re-entering
            if (_comingSoonPanel != null) _comingSoonPanel.SetActive(false);
            _missionsPanel?.Hide();
            RefreshDaily();
        }

        void Update()
        {
            if (!IsVisible || _dailyClaimBtn == null) return;
            _dailyTick += Time.unscaledDeltaTime;
            if (_dailyTick < 1f) return;
            _dailyTick = 0f;

            // Backend cooldown elapsed while the screen stays open — re-pull so the
            // CLAIM visual restores to active without re-entering Tools.
            if (_dailyBackend && _dailyStatusLoaded && _dailyStatus != null &&
                !_dailyStatus.claimable && !_dailyClaimInFlight && !_dailyRefetching &&
                _dailyNextClaimRealtime - Time.unscaledTimeAsDouble <= 0)
            {
                _dailyRefetching = true;
                FetchDailyStatus();
                return;
            }

            UpdateDailyUi();
        }

        // ── Build (runs once on first show) ───────────────────────────────────
        void BuildOnce()
        {
            if (_built) return;
            _built = true;

            var rt = (RectTransform)transform;

            // Shared section background (cover image, aspect preserved)
            FullscreenBackground.AttachShared(gameObject);

            // Navbar space is reserved by the shared Screens host (ScreenContentFitter);
            // these are just content-edge margins.
            const float topPad  = 24f;
            const float botPad  = 24f;
            const float sidePad =  24f;

            var content = NewGo("Content", rt, typeof(VerticalLayoutGroup));
            var cRt = (RectTransform)content.transform;
            cRt.anchorMin = Vector2.zero;
            cRt.anchorMax = Vector2.one;
            cRt.offsetMin = new Vector2(sidePad, botPad);
            cRt.offsetMax = new Vector2(-sidePad, -topPad);

            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment        = TextAnchor.UpperCenter;
            vlg.spacing               = 14f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(0, 0, 10, 8);

            // Section header
            var hdr = MakeTmp(content.transform, "Header", "TOOLS",
                20f, FontStyles.Bold, TextBright);
            hdr.characterSpacing = 5f;
            hdr.alignment = TextAlignmentOptions.Left;
            hdr.gameObject.AddComponent<LayoutElement>().minHeight = 34f;

            // Thin divider under header
            var div = NewGo("Div", content.transform, typeof(Image), typeof(LayoutElement));
            div.GetComponent<Image>().color = new Color(1f, 0.122f, 0.224f, 0.20f);
            div.GetComponent<LayoutElement>().minHeight = 1f;

            // Small spacer
            var sp0 = NewGo("Sp0", content.transform, typeof(LayoutElement));
            sp0.GetComponent<LayoutElement>().minHeight = 10f;

            BuildDailyRewardCard(content.transform);

            // ── Three rows ────────────────────────────────────────────────────
            BuildRow(content.transform, "VP KAZAN", AccentVP, () =>
            {
                navigator?.Navigate(ScreenType.EarnVp);
            });

            BuildRow(content.transform, "CARK", AccentWhl, () =>
            {
                ShowComingSoon("CARK");
            });

            BuildRow(content.transform, "GOREVLER", AccentMsn, () =>
            {
                // Build the panel on demand (no longer depends on prior injection), then
                // open it. In backend mode we never fall back to Coming Soon for Görevler.
                if (EnsureMissionsPanel() && _missionsPanel != null)
                {
                    _missionsPanel.Show();
                    return;
                }

                bool backend = GameContext.Instance != null && GameContext.Instance.BackendEnabled;
                if (backend)
                    Debug.LogError("[ToolsScreen] Görevler could not open: MissionsScreen unavailable in backend mode.");
                else
                    ShowComingSoon("GOREVLER");
            });

            // Coming Soon overlay (hidden until needed)
            BuildComingSoonPanel(rt);

            // Missions overlay panel (full-screen, shown on GOREVLER tap)
            EnsureMissionsPanel();
        }

        // ── Tappable row card ──────────────────────────────────────────────────
        void BuildRow(Transform parent, string label, Color accent, System.Action onClick)
        {
            var rowGo = NewGo("Row_" + label, parent,
                typeof(Image), typeof(Button), typeof(Outline), typeof(LayoutElement),
                typeof(EventTrigger));
            rowGo.GetComponent<LayoutElement>().minHeight = 80f;

            var rowImg = rowGo.GetComponent<Image>();
            rowImg.color = CardBg;

            var ol = rowGo.GetComponent<Outline>();
            ol.effectColor    = new Color(accent.r, accent.g, accent.b, 0.28f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            var btn = rowGo.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());

            // Left accent bar
            var barGo = NewGo("Bar", rowGo.transform, typeof(Image));
            var bRt = (RectTransform)barGo.transform;
            bRt.anchorMin        = new Vector2(0f, 0f);
            bRt.anchorMax        = new Vector2(0f, 1f);
            bRt.pivot            = new Vector2(0f, 0.5f);
            bRt.anchoredPosition = Vector2.zero;
            bRt.sizeDelta        = new Vector2(3.5f, 0f);
            barGo.GetComponent<Image>().color        = accent;
            barGo.GetComponent<Image>().raycastTarget = false;

            // Label
            var lbl = MakeTmp(rowGo.transform, "Lbl", label, 17f, FontStyles.Bold, TextBright);
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            var lRt = lbl.rectTransform;
            lRt.anchorMin        = new Vector2(0f, 0f);
            lRt.anchorMax        = new Vector2(0.8f, 1f);
            lRt.offsetMin        = new Vector2(20f, 0f);
            lRt.offsetMax        = Vector2.zero;
            lbl.raycastTarget    = false;

            // Chevron arrow
            var arrow = MakeTmp(rowGo.transform, "Arrow", ">", 20f, FontStyles.Bold,
                new Color(accent.r, accent.g, accent.b, 0.55f));
            arrow.alignment = TextAlignmentOptions.MidlineRight;
            var aRt = arrow.rectTransform;
            aRt.anchorMin        = new Vector2(1f, 0f);
            aRt.anchorMax        = new Vector2(1f, 1f);
            aRt.pivot            = new Vector2(1f, 0.5f);
            aRt.anchoredPosition = new Vector2(-16f, 0f);
            aRt.sizeDelta        = new Vector2(30f, 0f);
            arrow.raycastTarget  = false;

            // Hover tint via EventTrigger
            var et  = rowGo.GetComponent<EventTrigger>();
            var img = rowImg;
            AddPE(et, EventTriggerType.PointerEnter,
                _ => img.color = new Color(accent.r * 0.18f, accent.g * 0.18f, accent.b * 0.18f, 1f));
            AddPE(et, EventTriggerType.PointerExit,
                _ => img.color = CardBg);
        }

        // ── Full-screen Coming Soon overlay ───────────────────────────────────
        void BuildComingSoonPanel(RectTransform screenRt)
        {
            _comingSoonPanel = NewGo("ComingSoon", screenRt, typeof(Image), typeof(Button));
            Stretch(_comingSoonPanel);
            _comingSoonPanel.GetComponent<Image>().color = new Color(0.016f, 0.025f, 0.055f, 0.96f);

            // Tap anywhere to dismiss
            var dismissBtn = _comingSoonPanel.GetComponent<Button>();
            dismissBtn.transition = Selectable.Transition.None;
            dismissBtn.onClick.AddListener(() => _comingSoonPanel.SetActive(false));

            // Title (set dynamically)
            _comingSoonTitle = MakeTmp(_comingSoonPanel.transform, "CSTitle", "",
                30f, FontStyles.Bold, ActiveRed);
            _comingSoonTitle.alignment       = TextAlignmentOptions.Center;
            _comingSoonTitle.characterSpacing = 5f;
            _comingSoonTitle.raycastTarget   = false;
            var tRt = _comingSoonTitle.rectTransform;
            tRt.anchorMin        = new Vector2(0f, 0.5f);
            tRt.anchorMax        = new Vector2(1f, 0.5f);
            tRt.pivot            = new Vector2(0.5f, 0.5f);
            tRt.anchoredPosition = new Vector2(0f, 32f);
            tRt.sizeDelta        = new Vector2(0f, 44f);

            // Sub label
            var sub = MakeTmp(_comingSoonPanel.transform, "CSSub", "Coming Soon",
                18f, FontStyles.Normal, TextDim);
            sub.alignment     = TextAlignmentOptions.Center;
            sub.raycastTarget = false;
            var sRt = sub.rectTransform;
            sRt.anchorMin        = new Vector2(0f, 0.5f);
            sRt.anchorMax        = new Vector2(1f, 0.5f);
            sRt.pivot            = new Vector2(0.5f, 0.5f);
            sRt.anchoredPosition = new Vector2(0f, -24f);
            sRt.sizeDelta        = new Vector2(0f, 32f);

            // Dismiss hint
            var hint = MakeTmp(_comingSoonPanel.transform, "Hint", "[ tap to close ]",
                10f, FontStyles.Normal, new Color(1f, 1f, 1f, 0.20f));
            hint.alignment     = TextAlignmentOptions.Center;
            hint.raycastTarget = false;
            var hRt = hint.rectTransform;
            hRt.anchorMin        = new Vector2(0f, 0f);
            hRt.anchorMax        = new Vector2(1f, 0f);
            hRt.pivot            = new Vector2(0.5f, 0f);
            hRt.anchoredPosition = new Vector2(0f, 120f);
            hRt.sizeDelta        = new Vector2(0f, 24f);

            _comingSoonPanel.SetActive(false);
        }

        void ShowComingSoon(string title)
        {
            if (_comingSoonPanel == null) return;
            _comingSoonTitle.text = title;
            _comingSoonPanel.SetActive(true);
        }

        // ── Daily Reward card — unconditional 2000 VP every 24h ───────────────
        void BuildDailyRewardCard(Transform parent)
        {
            var cardGo = NewGo("DailyRewardCard", parent,
                typeof(Image), typeof(Outline), typeof(LayoutElement));
            cardGo.GetComponent<LayoutElement>().minHeight = 96f;
            cardGo.GetComponent<Image>().color = CardBg;

            var ol = cardGo.GetComponent<Outline>();
            ol.effectColor    = new Color(AccentVP.r, AccentVP.g, AccentVP.b, 0.35f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            var bar = NewGo("Bar", cardGo.transform, typeof(Image));
            var barRt = (RectTransform)bar.transform;
            barRt.anchorMin        = new Vector2(0f, 0f);
            barRt.anchorMax        = new Vector2(0f, 1f);
            barRt.pivot            = new Vector2(0f, 0.5f);
            barRt.anchoredPosition = Vector2.zero;
            barRt.sizeDelta        = new Vector2(3.5f, 0f);
            bar.GetComponent<Image>().color        = AccentVP;
            bar.GetComponent<Image>().raycastTarget = false;

            var title = MakeTmp(cardGo.transform, "Title", "DAILY REWARD", 15f, FontStyles.Bold, TextBright);
            title.alignment = TextAlignmentOptions.TopLeft;
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(0.6f, 1f);
            titleRt.pivot     = new Vector2(0f, 1f);
            titleRt.anchoredPosition = new Vector2(20f, -14f);
            titleRt.sizeDelta = new Vector2(0f, 22f);
            title.raycastTarget = false;

            var reward = MakeTmp(cardGo.transform, "Reward", $"+{DailyRewardVp:N0} VP", 22f, FontStyles.Bold, AccentVP);
            reward.alignment = TextAlignmentOptions.TopLeft;
            var rewardRt = reward.rectTransform;
            rewardRt.anchorMin = new Vector2(0f, 1f);
            rewardRt.anchorMax = new Vector2(0.6f, 1f);
            rewardRt.pivot     = new Vector2(0f, 1f);
            rewardRt.anchoredPosition = new Vector2(20f, -38f);
            rewardRt.sizeDelta = new Vector2(0f, 28f);
            reward.raycastTarget = false;

            _dailyStatusLbl = MakeTmp(cardGo.transform, "Status", "", 11f, FontStyles.Normal, TextDim);
            _dailyStatusLbl.alignment = TextAlignmentOptions.TopLeft;
            var statusRt = _dailyStatusLbl.rectTransform;
            statusRt.anchorMin = new Vector2(0f, 0f);
            statusRt.anchorMax = new Vector2(0.6f, 0f);
            statusRt.pivot     = new Vector2(0f, 0f);
            statusRt.anchoredPosition = new Vector2(20f, 12f);
            statusRt.sizeDelta = new Vector2(0f, 16f);
            _dailyStatusLbl.raycastTarget = false;

            var btnGo = NewGo("ClaimBtn", cardGo.transform, typeof(Image), typeof(Button));
            _dailyClaimImg = btnGo.GetComponent<Image>();
            _dailyClaimImg.color = AccentVP;
            var btnRt = (RectTransform)btnGo.transform;
            btnRt.anchorMin        = new Vector2(1f, 0.5f);
            btnRt.anchorMax        = new Vector2(1f, 0.5f);
            btnRt.pivot            = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-16f, 0f);
            btnRt.sizeDelta        = new Vector2(128f, 56f);

            _dailyClaimBtn = btnGo.GetComponent<Button>();
            _dailyClaimBtn.transition = Selectable.Transition.None;
            _dailyClaimBtn.onClick.AddListener(OnDailyClaim);

            _dailyClaimLbl = MakeTmp(btnGo.transform, "Lbl", "CLAIM", 15f, FontStyles.Bold, new Color(0.05f, 0.06f, 0.09f, 1f));
            _dailyClaimLbl.alignment     = TextAlignmentOptions.Center;
            _dailyClaimLbl.raycastTarget = false;
            var lblRt = _dailyClaimLbl.rectTransform;
            lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = Vector2.zero; lblRt.offsetMax = Vector2.zero;
        }

        void RefreshDaily()
        {
            if (_dailyClaimBtn == null) return;
            var ctx = GameContext.Instance;
            _dailyBackend = ctx != null && ctx.BackendEnabled;
            if (_dailyBackend)
            {
                _dailyStatusLoaded  = false;
                _dailyClaimInFlight = false;
                _dailyRefetching    = false;
                FetchDailyStatus();
            }
            UpdateDailyUi();
        }

        void FetchDailyStatus()
        {
            var ctx = GameContext.Instance;
            if (ctx == null) return;
            ctx.RefreshDailyBackend(
                res =>
                {
                    if (this == null) return;
                    _dailyRefetching = false;
                    _dailyStatus = res;
                    _dailyStatusLoaded = true;
                    _dailyNextClaimRealtime = Time.unscaledTimeAsDouble +
                        Math.Max(0L, res != null ? res.secondsUntilNextClaim : 0L);
                    UpdateDailyUi();
                },
                err =>
                {
                    if (this == null) return;
                    _dailyRefetching = false;
                    if (!string.IsNullOrEmpty(err)) GameEvents.RaiseToast(err);
                });
        }

        void UpdateDailyUi()
        {
            if (_dailyClaimBtn == null) return;

            if (_dailyBackend)
            {
                bool claimable = _dailyStatusLoaded && _dailyStatus != null && _dailyStatus.claimable && !_dailyClaimInFlight;
                _dailyClaimBtn.interactable = claimable;
                SetClaimVisual(claimable);
                if (_dailyStatusLbl != null)
                {
                    if (!_dailyStatusLoaded || _dailyStatus == null)
                        _dailyStatusLbl.text = "Yükleniyor…";
                    else if (_dailyStatus.claimable)
                        _dailyStatusLbl.text = "Şimdi alınabilir";
                    else
                        _dailyStatusLbl.text = $"Sonraki: {CooldownText(_dailyNextClaimRealtime - Time.unscaledTimeAsDouble)}";
                }
                return;
            }

            var daily = GameContext.Instance?.DailyRewards;
            bool can = daily != null && daily.CanClaimToday;
            _dailyClaimBtn.interactable = can;
            SetClaimVisual(can);
            if (_dailyStatusLbl != null)
                _dailyStatusLbl.text = daily == null ? "" :
                    (can ? "Şimdi alınabilir" : $"Sonraki: {daily.TimeUntilNextClaim:hh\\:mm\\:ss}");
        }

        static readonly Color DailyBtnActive   = new Color(0.902f, 0.816f, 0.435f, 1f);   // AccentVP gold
        static readonly Color DailyBtnClaimed  = new Color(0.902f, 0.816f, 0.435f, 0.18f);
        static readonly Color DailyLblActive   = new Color(0.05f, 0.06f, 0.09f, 1f);
        static readonly Color DailyLblClaimed  = new Color(0.925f, 0.910f, 0.882f, 0.30f);

        void SetClaimVisual(bool claimable)
        {
            if (_dailyClaimImg != null) _dailyClaimImg.color = claimable ? DailyBtnActive : DailyBtnClaimed;
            if (_dailyClaimLbl != null) _dailyClaimLbl.color = claimable ? DailyLblActive : DailyLblClaimed;
        }

        static string CooldownText(double seconds)
        {
            if (seconds < 0) seconds = 0;
            return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
        }

        void OnDailyClaim()
        {
            var ctx = GameContext.Instance;
            if (ctx == null) return;

            if (_dailyBackend)
            {
                if (_dailyClaimInFlight) return;
                if (!_dailyStatusLoaded || _dailyStatus == null || !_dailyStatus.claimable) return;

                _dailyClaimInFlight = true;
                _dailyClaimBtn.interactable = false;
                ctx.ClaimDailyBackend(
                    res =>
                    {
                        if (this == null) return;
                        _dailyClaimInFlight = false;
                        if (_dailyStatus != null)
                        {
                            _dailyStatus.claimable = false;
                            if (res != null) _dailyStatus.currentStreak = res.currentStreak;
                        }
                        int reward = res != null ? res.rewardVp : 0;
                        GameEvents.RaiseToast($"Günlük ödül: +{reward:N0} VP");
                        FetchDailyStatus();
                        UpdateDailyUi();
                    },
                    err =>
                    {
                        if (this == null) return;
                        _dailyClaimInFlight = false;
                        if (!string.IsNullOrEmpty(err)) GameEvents.RaiseToast(err);
                        UpdateDailyUi();
                    });
                return;
            }

            if (ctx.DailyRewards != null && ctx.DailyRewards.TryClaim(out var local))
            {
                ctx.Statistics?.RecordVpEarned(local);
                ctx.Save?.Save();
                GameEvents.RaiseToast($"Günlük ödül: +{local:N0} VP");
            }
            UpdateDailyUi();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        static GameObject NewGo(string name, Transform parent, params System.Type[] comps)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            foreach (var c in comps) go.AddComponent(c);
            return go;
        }

        static void Stretch(GameObject go)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static TextMeshProUGUI MakeTmp(Transform parent, string name, string text,
            float size, FontStyles style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = text;
            tmp.fontSize           = size;
            tmp.fontStyle          = style;
            tmp.color              = color;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Ellipsis;
            return tmp;
        }

        static void AddPE(EventTrigger et, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> action)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(action);
            et.triggers.Add(e);
        }
    }
}
