using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Services.Backend;
using ValoCase.Systems;

namespace ValoCase.UI.Screens
{
    public sealed class MissionsScreen : MonoBehaviour
    {
        // ── Palette ───────────────────────────────────────────────────────────
        static readonly Color BgDeep     = new Color(0.031f, 0.043f, 0.078f, 1f);
        static readonly Color CardBg     = new Color(0.067f, 0.094f, 0.153f, 1f);
        static readonly Color NeonRed    = new Color(1f, 0.176f, 0.333f, 1f);
        static readonly Color BorderRed  = new Color(1f, 0.275f, 0.333f, 0.35f);
        static readonly Color RingTrackC = new Color(0.09f, 0.12f, 0.20f, 1f);
        static readonly Color TextBright = new Color(0.961f, 0.961f, 0.961f, 1f);
        static readonly Color TextDim    = new Color(0.541f, 0.569f, 0.651f, 1f);
        static readonly Color GoldColor  = new Color(0.902f, 0.816f, 0.435f, 1f);
        static readonly Color MutedBtn   = new Color(0.10f, 0.13f, 0.20f, 1f);
        static readonly Color DimBtn     = new Color(0.08f, 0.10f, 0.14f, 1f);
        static readonly Color GreenFull  = new Color(0.13f, 0.82f, 0.37f, 1f);
        static readonly Color GreenDim   = new Color(0.13f, 0.82f, 0.37f, 0.38f);

        // ── Circle sprite cache ───────────────────────────────────────────────
        static Sprite s_circle;

        // ── Card refs (per mission) ───────────────────────────────────────────
        struct CardRefs
        {
            public RectTransform    CardRt;
            public Image            RingFill;
            public TextMeshProUGUI  PctText;
            public TextMeshProUGUI  ProgressText;
            public TextMeshProUGUI  RemainingText;
            public Image            BtnImage;
            public Button           Btn;
            public TextMeshProUGUI  BtnText;
            public Outline          CardOutline;
            public TextMeshProUGUI  TitleText;
            public TextMeshProUGUI  RewardText;
        }

        MissionSystem _system;
        bool          _built;
        CardRefs[]    _cards;
        RectTransform _contentRt;
        float         _cellW;
        float         _cellH;

        // Centered "loading / empty" label shown inside the scroll area while there are
        // no cards (backend mode, before the first response or on an empty list). It is
        // the only thing shown when no backend data is available — never local missions.
        GameObject      _loadingGo;
        TextMeshProUGUI _loadingLbl;

        // Backend mode (runtime only). In local mode these stay empty and every card
        // accessor delegates to the local MissionSystem exactly as before.
        bool              _backend;
        MissionResponse[] _backendMissions;
        bool[]            _claimInFlight;

        // ── Public API ────────────────────────────────────────────────────────
        public void Init(MissionSystem system)
        {
            if (_system != null) _system.OnChanged -= Refresh;
            _system            = system;
            _system.OnChanged += Refresh;
        }

        void OnDestroy()
        {
            if (_system != null) _system.OnChanged -= Refresh;
        }

        public void Show()
        {
            gameObject.SetActive(true);

            // Backend mode: open INSTANTLY and stay fully backend-authoritative. Build
            // the screen frame immediately, then show the grid sized to backend data
            // only — the cached backend list if we have one from a previous open,
            // otherwise a loading state (NEVER local missions). The authoritative list
            // is fetched in parallel and the grid is resized to it on arrival.
            if (GameContext.Instance != null && GameContext.Instance.BackendEnabled)
            {
                _backend = true;
                BuildChrome();

                if (_backendMissions != null && _backendMissions.Length > 0)
                {
                    EnsureCards(_backendMissions.Length);   // cached backend missions
                    SetLoading(false);
                    SortCards();
                    Refresh();
                }
                else
                {
                    EnsureCards(0);                         // no cache yet
                    SetLoading(true, "LOADING…");           // frame + loading, no locals
                }

                StartCoroutine(ShowBackendRoutine());
                return;
            }

            // Local / offline mode — unchanged: the full local mission set.
            BuildChrome();
            EnsureCards(MissionSystem.MissionCount);
            SetLoading(false);
            SortCards();
            Refresh();
        }

        IEnumerator ShowBackendRoutine()
        {
            bool done = false, ok = false;
            GameContext.Instance.RefreshMissionsBackend(
                missions => { _backendMissions = missions; ok = true; done = true; },
                err => { if (!string.IsNullOrEmpty(err)) GameEvents.RaiseToast(err); done = true; });

            while (!done) yield return null;

            // On failure keep the screen open with whatever it is already showing
            // (cached backend list, or the loading state); the err callback surfaced the
            // message. Never fall back to local missions here.
            if (!ok || _backendMissions == null)
            {
                if (_cards == null || _cards.Length == 0)
                    SetLoading(true, "MISSIONS UNAVAILABLE");
                yield break;
            }

            // Authoritative data arrived — resize the grid to EXACTLY the backend count
            // (adds missing cards, removes surplus) and bind it.
            int count = _backendMissions.Length;
            EnsureCards(count);
            SetLoading(count == 0, "NO MISSIONS");
            SortCards();
            Refresh();
        }

        void EnsureClaimInFlight()
        {
            int len = _cards != null ? _cards.Length : 0;
            if (_claimInFlight == null || _claimInFlight.Length != len)
                _claimInFlight = new bool[len];
        }

        // ── Loading / empty state ─────────────────────────────────────────────
        void SetLoading(bool show, string text = null)
        {
            if (_loadingGo == null) return;
            if (show && _loadingLbl != null && !string.IsNullOrEmpty(text))
                _loadingLbl.text = text;
            _loadingGo.SetActive(show);
        }

        // Creates/destroys cards so the grid holds EXACTLY `count` cards, and resizes the
        // scroll content to match. The single source of truth for how many missions
        // render: in backend mode the caller always passes the backend list length.
        void EnsureCards(int count)
        {
            if (_contentRt == null) return;
            count = Mathf.Max(0, count);

            if (_cards == null) _cards = new CardRefs[0];
            int old = _cards.Length;

            if (count > old)
            {
                var arr = new CardRefs[count];
                System.Array.Copy(_cards, arr, old);
                _cards = arr;
                var circle = GetCircleSprite();
                for (int i = old; i < count; i++)
                {
                    int   col  = i % 3;
                    int   row  = i / 3;
                    float xPos = col * (_cellW + 8f);
                    float yPos = 8f + row * (_cellH + 16f);
                    BuildCard(i, _contentRt, xPos, yPos, _cellW, _cellH, circle);
                }
            }
            else if (count < old)
            {
                for (int i = count; i < old; i++)
                    if (_cards[i].CardRt != null) Destroy(_cards[i].CardRt.gameObject);
                var arr = new CardRefs[count];
                System.Array.Copy(_cards, arr, count);
                _cards = arr;
            }

            ResizeContent(count);
            EnsureClaimInFlight();
        }

        void ResizeContent(int count)
        {
            if (_contentRt == null) return;
            int rows = count <= 0 ? 0 : Mathf.CeilToInt(count / 3f);
            float totalH = count <= 0 ? 0f : 8f + rows * _cellH + (rows - 1) * 16f + 16f;
            _contentRt.sizeDelta = new Vector2(0f, totalH);
        }

        public void Hide() => gameObject.SetActive(false);

        // ── Card data accessors (backend list OR local MissionSystem) ───────────
        // In BACKEND mode every accessor reads only the authoritative backend list and
        // returns a neutral default for an invalid index — it never reads local mission
        // data, so ghost cards are impossible. In LOCAL mode it reads the local system
        // exactly as before. The grid is always sized to CardCount, so invalid indices
        // are not rendered in normal operation; the guards are pure safety.
        int CardCount => _backend
            ? (_backendMissions != null ? _backendMissions.Length : 0)
            : MissionSystem.MissionCount;

        bool BackendIndexValid(int i) => _backendMissions != null && i >= 0 && i < _backendMissions.Length && _backendMissions[i] != null;

        string CardTitle(int i)
        {
            if (_backend) return BackendIndexValid(i) ? (_backendMissions[i].title ?? "") : "";
            return _system.GetDef(i).Title;
        }

        int CardReward(int i)
        {
            if (_backend) return BackendIndexValid(i) ? _backendMissions[i].rewardVp : 0;
            return _system.GetDef(i).RewardVp;
        }

        int CardCurrent(int i)
        {
            if (_backend) return BackendIndexValid(i) ? _backendMissions[i].progress : 0;
            return _system.GetEntry(i).currentAmount;
        }

        int CardTarget(int i)
        {
            if (_backend) return BackendIndexValid(i) ? Mathf.Max(1, _backendMissions[i].targetCount) : 1;
            return _system.GetDef(i).TargetAmount;
        }

        bool CardClaimed(int i)
        {
            if (_backend) return BackendIndexValid(i) && _backendMissions[i].status == "CLAIMED";
            return _system.GetEntry(i).claimed;
        }

        int CardClaimOrder(int i)
        {
            if (_backend) return i;
            return _system.GetEntry(i).claimOrder;
        }

        bool CardClaimInFlight(int i)
            => _backend && _claimInFlight != null && i >= 0 && i < _claimInFlight.Length && _claimInFlight[i];

        // ── Build screen frame (once) ─────────────────────────────────────────
        // Builds the chrome only — background, header, back button, scroll view, the
        // (initially empty) content container, and the loading label. Cards are created
        // separately by EnsureCards so the grid can be sized to the backend list.
        void BuildChrome()
        {
            if (_built) return;
            _built = true;

            Canvas.ForceUpdateCanvases();
            var rt       = (RectTransform)transform;
            float panelW = rt.rect.width;
            if (panelW <= 0f)
            {
                var cRt = GetComponentInParent<Canvas>()?.GetComponent<RectTransform>();
                panelW = (cRt != null && cRt.rect.width > 0f) ? cRt.rect.width : 390f;
            }

            const float topPad  = 110f;
            const float botPad  = 110f;
            const float sidePad =  12f;
            const float colGap  =   8f;
            float usableW = panelW - 2f * sidePad;
            float cellW   = (usableW - 2f * colGap) / 3f;
            float cellH   = Mathf.Round(cellW * 1.75f);
            _cellW = cellW; _cellH = cellH;

            // Full background
            var bg = NewGo("Bg", rt, typeof(Image));
            Stretch(bg);
            bg.GetComponent<Image>().color = BgDeep;

            // ── Header ────────────────────────────────────────────────────────
            var hdrGo = NewGo("Header", rt, typeof(Image));
            var hRt   = (RectTransform)hdrGo.transform;
            hRt.anchorMin        = new Vector2(0f, 1f);
            hRt.anchorMax        = new Vector2(1f, 1f);
            hRt.pivot            = new Vector2(0.5f, 1f);
            hRt.anchoredPosition = Vector2.zero;
            hRt.sizeDelta        = new Vector2(0f, topPad);
            hdrGo.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.10f, 0.97f);

            var hLine = NewGo("HLine", hdrGo.transform, typeof(Image));
            var hlRt  = (RectTransform)hLine.transform;
            hlRt.anchorMin        = Vector2.zero;
            hlRt.anchorMax        = new Vector2(1f, 0f);
            hlRt.pivot            = new Vector2(0.5f, 0f);
            hlRt.anchoredPosition = Vector2.zero;
            hlRt.sizeDelta        = new Vector2(0f, 1.5f);
            hLine.GetComponent<Image>().color       = new Color(1f, 0.176f, 0.333f, 0.40f);
            hLine.GetComponent<Image>().raycastTarget = false;

            var titleTmp = MakeTmp(hdrGo.transform, "Title", "WEEKLY MISSIONS",
                17f, FontStyles.Bold, TextBright);
            titleTmp.characterSpacing = 4f;
            titleTmp.alignment        = TextAlignmentOptions.Center;
            titleTmp.raycastTarget    = false;
            var tRt = titleTmp.rectTransform;
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = new Vector2(0f, 22f); tRt.offsetMax = Vector2.zero;


            // ── Back button ───────────────────────────────────────────────────
            var backGo = NewGo("Back", rt, typeof(Image), typeof(Button), typeof(Outline));
            var bRt    = (RectTransform)backGo.transform;
            bRt.anchorMin        = new Vector2(0f, 1f);
            bRt.anchorMax        = new Vector2(0f, 1f);
            bRt.pivot            = new Vector2(0f, 1f);
            bRt.anchoredPosition = new Vector2(18f, -72f);
            bRt.sizeDelta        = new Vector2(80f, 32f);
            backGo.GetComponent<Image>().color = new Color(0.031f, 0.055f, 0.102f, 0.97f);
            var bol = backGo.GetComponent<Outline>();
            bol.effectColor    = new Color(1f, 0.122f, 0.224f, 0.80f);
            bol.effectDistance = new Vector2(1.5f, -1.5f);
            var backLbl = MakeTmp(backGo.transform, "Lbl", "BACK", 11f, FontStyles.Bold, Color.white);
            backLbl.alignment     = TextAlignmentOptions.Center;
            backLbl.raycastTarget = false;
            var blRt = backLbl.rectTransform;
            blRt.anchorMin = Vector2.zero; blRt.anchorMax = Vector2.one;
            blRt.offsetMin = blRt.offsetMax = Vector2.zero;
            var backBtn = backGo.GetComponent<Button>();
            backBtn.transition = Selectable.Transition.None;
            backBtn.onClick.AddListener(Hide);

            // ── ScrollRect + RectMask2D viewport ──────────────────────────────
            var scrollGo = NewGo("Scroll", rt, typeof(ScrollRect), typeof(Image));
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = Vector2.zero; scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(sidePad, botPad);
            scrollRt.offsetMax = new Vector2(-sidePad, -topPad);
            scrollGo.GetComponent<Image>().color = Color.clear;

            var viewportGo = NewGo("Viewport", scrollGo.transform, typeof(RectMask2D));
            Stretch(viewportGo);

            // Content starts empty (no cards, zero height). EnsureCards fills + sizes it.
            var contentGo = NewGo("Content", viewportGo.transform);
            _contentRt = (RectTransform)contentGo.transform;
            _contentRt.anchorMin        = new Vector2(0f, 1f);
            _contentRt.anchorMax        = new Vector2(1f, 1f);
            _contentRt.pivot            = new Vector2(0.5f, 1f);
            _contentRt.anchoredPosition = Vector2.zero;
            _contentRt.sizeDelta        = new Vector2(0f, 0f);

            var sr = scrollGo.GetComponent<ScrollRect>();
            sr.content           = _contentRt;
            sr.viewport          = (RectTransform)viewportGo.transform;
            sr.horizontal        = false;
            sr.vertical          = true;
            sr.scrollSensitivity = 30f;
            sr.movementType      = ScrollRect.MovementType.Elastic;

            _cards = new CardRefs[0];

            // ── Loading / empty label (centered in the scroll area, hidden by default) ──
            _loadingGo = NewGo("LoadingState", scrollGo.transform, typeof(Image));
            Stretch(_loadingGo);
            _loadingGo.GetComponent<Image>().color = Color.clear;
            _loadingGo.GetComponent<Image>().raycastTarget = false;
            _loadingLbl = MakeTmp(_loadingGo.transform, "Lbl", "LOADING…",
                13f, FontStyles.Bold, TextDim);
            _loadingLbl.alignment        = TextAlignmentOptions.Center;
            _loadingLbl.characterSpacing = 3f;
            _loadingLbl.raycastTarget    = false;
            var llRt = _loadingLbl.rectTransform;
            llRt.anchorMin = Vector2.zero; llRt.anchorMax = Vector2.one;
            llRt.offsetMin = llRt.offsetMax = Vector2.zero;
            _loadingGo.SetActive(false);

            backGo.transform.SetAsLastSibling();
        }

        void BuildCard(int index, RectTransform parent,
                       float xPos, float yPos, float cardW, float cardH, Sprite circle)
        {
            // Card root
            var card   = NewGo("Card_" + index, parent, typeof(Image), typeof(Outline));
            var cardRt = (RectTransform)card.transform;
            cardRt.anchorMin        = new Vector2(0f, 1f);
            cardRt.anchorMax        = new Vector2(0f, 1f);
            cardRt.pivot            = new Vector2(0f, 1f);
            cardRt.anchoredPosition = new Vector2(xPos, -yPos);
            cardRt.sizeDelta        = new Vector2(cardW, cardH);
            card.GetComponent<Image>().color = CardBg;
            var ol = card.GetComponent<Outline>();
            ol.effectColor    = BorderRed;
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            // Top accent bar
            var topBar = NewGo("TopBar", card.transform, typeof(Image));
            var tbRt   = (RectTransform)topBar.transform;
            tbRt.anchorMin        = new Vector2(0f, 1f);
            tbRt.anchorMax        = new Vector2(1f, 1f);
            tbRt.pivot            = new Vector2(0.5f, 1f);
            tbRt.anchoredPosition = Vector2.zero;
            tbRt.sizeDelta        = new Vector2(0f, 2f);
            topBar.GetComponent<Image>().color       = new Color(NeonRed.r, NeonRed.g, NeonRed.b, 0.6f);
            topBar.GetComponent<Image>().raycastTarget = false;

            // Mission title
            float titleH = Mathf.Round(cardH * 0.175f);
            var titleTmp = MakeTmp(card.transform, "Title", CardTitle(index),
                11f, FontStyles.Bold, TextBright);
            titleTmp.enableWordWrapping = true;
            titleTmp.alignment          = TextAlignmentOptions.Center;
            titleTmp.raycastTarget      = false;
            var titRt = titleTmp.rectTransform;
            titRt.anchorMin        = new Vector2(0f, 1f);
            titRt.anchorMax        = new Vector2(1f, 1f);
            titRt.pivot            = new Vector2(0.5f, 1f);
            titRt.anchoredPosition = new Vector2(0f, -5f);
            titRt.sizeDelta        = new Vector2(-8f, titleH);

            // ── Circular donut progress ring ──────────────────────────────────
            // Ring: pivot=(0.5,1) top-center. Hole: pivot=(0.5,0.5) centered on ring.
            float ringSize    = Mathf.Round(cardW * 0.67f);
            float ringTopY    = Mathf.Round(5f + titleH + cardH * 0.04f);
            float ringCenterY = ringTopY + ringSize * 0.5f;
            float holeSize    = Mathf.Round(ringSize * 0.54f);

            // Track: dark full circle (fill=1)
            var rTrack = MakeRingImg("RingTrack", card.transform, circle,
                RingTrackC, 1f, new Vector2(0f, -ringTopY), ringSize);
            rTrack.raycastTarget = false;

            // Fill: green arc (fill=progress%)
            var rFill = MakeRingImg("RingFill", card.transform, circle,
                GreenDim, 0f, new Vector2(0f, -ringTopY), ringSize);
            rFill.fillOrigin    = (int)Image.Origin360.Top;
            rFill.raycastTarget = false;

            // Hole: inner circle, same color as card bg — creates donut cutout
            var hole  = NewGo("RingHole", card.transform, typeof(Image));
            var hoRt  = (RectTransform)hole.transform;
            hoRt.anchorMin        = new Vector2(0.5f, 1f);
            hoRt.anchorMax        = new Vector2(0.5f, 1f);
            hoRt.pivot            = new Vector2(0.5f, 0.5f);
            hoRt.anchoredPosition = new Vector2(0f, -ringCenterY);
            hoRt.sizeDelta        = new Vector2(holeSize, holeSize);
            var hoImg = hole.GetComponent<Image>();
            if (circle != null) hoImg.sprite = circle;
            hoImg.color        = CardBg;
            hoImg.raycastTarget = false;

            // ── Texts centered inside the donut hole ──────────────────────────
            float textW = holeSize - 4f;

            var pctTmp = MakeTmp(card.transform, "Pct", "0%",
                13f, FontStyles.Bold, TextBright);
            pctTmp.alignment     = TextAlignmentOptions.Center;
            pctTmp.raycastTarget = false;
            SetCenterRT(pctTmp.rectTransform, 0f, -(ringCenterY - 9f), textW, 18f);

            var progTmp = MakeTmp(card.transform, "Prog", "0/1",
                7f, FontStyles.Normal, TextDim);
            progTmp.alignment     = TextAlignmentOptions.Center;
            progTmp.raycastTarget = false;
            SetCenterRT(progTmp.rectTransform, 0f, -(ringCenterY + 10f), textW, 13f);

            var remTmp = MakeTmp(card.transform, "Rem", "",
                6.5f, FontStyles.Normal, TextDim);
            remTmp.alignment     = TextAlignmentOptions.Center;
            remTmp.raycastTarget = false;
            SetCenterRT(remTmp.rectTransform, 0f, -(ringCenterY + 21f), textW, 13f);

            // ── Reward row ────────────────────────────────────────────────────
            float rewardY = ringTopY + ringSize + Mathf.Round(cardH * 0.05f);
            var rewTmp = MakeTmp(card.transform, "Reward", $"+{CardReward(index)} VP",
                10f, FontStyles.Bold, GoldColor);
            rewTmp.alignment     = TextAlignmentOptions.Center;
            rewTmp.raycastTarget = false;
            var rwRt = rewTmp.rectTransform;
            rwRt.anchorMin        = new Vector2(0f, 1f);
            rwRt.anchorMax        = new Vector2(1f, 1f);
            rwRt.pivot            = new Vector2(0.5f, 1f);
            rwRt.anchoredPosition = new Vector2(0f, -rewardY);
            rwRt.sizeDelta        = new Vector2(-8f, 20f);

            // ── Claim button ──────────────────────────────────────────────────
            float btnH = Mathf.Round(cardH * 0.155f);
            float btnY = rewardY + 20f + 6f;
            var btnGo = NewGo("ClaimBtn", card.transform, typeof(Image), typeof(Button));
            var btnRt = (RectTransform)btnGo.transform;
            btnRt.anchorMin        = new Vector2(0f, 1f);
            btnRt.anchorMax        = new Vector2(1f, 1f);
            btnRt.pivot            = new Vector2(0.5f, 1f);
            btnRt.anchoredPosition = new Vector2(0f, -btnY);
            btnRt.sizeDelta        = new Vector2(-8f, btnH);
            var btnImg = btnGo.GetComponent<Image>();
            var btn    = btnGo.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;

            var btnLbl = MakeTmp(btnGo.transform, "Lbl", "", 9f, FontStyles.Bold, Color.white);
            btnLbl.alignment     = TextAlignmentOptions.Center;
            btnLbl.raycastTarget = false;
            var bllRt = btnLbl.rectTransform;
            bllRt.anchorMin = Vector2.zero; bllRt.anchorMax = Vector2.one;
            bllRt.offsetMin = bllRt.offsetMax = Vector2.zero;

            int cap = index;
            btn.onClick.AddListener(() => OnClaimClicked(cap));

            _cards[index] = new CardRefs
            {
                CardRt        = cardRt,
                RingFill      = rFill,
                PctText       = pctTmp,
                ProgressText  = progTmp,
                RemainingText = remTmp,
                BtnImage      = btnImg,
                Btn           = btn,
                BtnText       = btnLbl,
                CardOutline   = ol,
                TitleText     = titleTmp,
                RewardText    = rewTmp,
            };
        }

        // ── Sort cards on each Show() — claimed sink to bottom by claimOrder ──
        void SortCards()
        {
            if (!_built || _cards == null) return;
            int count = _cards.Length;
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            System.Array.Sort(order, (a, b) =>
            {
                bool ca = CardClaimed(a);
                bool cb = CardClaimed(b);
                if (ca != cb) return ca ? 1 : -1;
                if (ca)       return CardClaimOrder(b).CompareTo(CardClaimOrder(a));
                return a.CompareTo(b);
            });
            const float colGap = 8f;
            const float rowGap = 16f;
            for (int slot = 0; slot < count; slot++)
            {
                int   mi   = order[slot];
                int   col  = slot % 3;
                int   row  = slot / 3;
                float xPos = col * (_cellW + colGap);
                float yPos = 8f + row * (_cellH + rowGap);
                _cards[mi].CardRt.anchoredPosition = new Vector2(xPos, -yPos);
            }
        }

        // ── Refresh (data → UI) ───────────────────────────────────────────────
        void Refresh()
        {
            if (!_built || _cards == null) return;
            for (int i = 0; i < _cards.Length; i++)
                RefreshCard(i);
        }

        void RefreshCard(int i)
        {
            int   current = CardCurrent(i);
            int   target  = CardTarget(i);
            bool  done    = current >= target;
            bool  claimed = CardClaimed(i);
            bool  inFlight = CardClaimInFlight(i);
            int   remain  = Mathf.Max(0, target - current);
            float pct     = target > 0
                ? Mathf.Clamp01((float)current / target)
                : 0f;

            ref var c = ref _cards[i];

            // Static fields are data-driven too, so when the authoritative list lands it
            // cleanly replaces the temporary placeholder (title + reward included).
            if (c.TitleText  != null) c.TitleText.text  = CardTitle(i);
            if (c.RewardText != null) c.RewardText.text = $"+{CardReward(i)} VP";

            // Ring fill: green-dim for partial, green-full for done
            c.RingFill.fillAmount = pct;
            c.RingFill.color      = done ? GreenFull : (pct > 0f ? GreenDim : GreenDim);

            c.PctText.text       = Mathf.RoundToInt(pct * 100f) + "%";
            c.ProgressText.text  = $"{current}/{target}";
            c.RemainingText.text = claimed ? "DONE"
                                 : remain > 0 ? $"{remain} left"
                                 : "READY!";

            // Outline: green when ready to claim, red dim otherwise
            c.CardOutline.effectColor = (done && !claimed)
                ? new Color(0.13f, 0.82f, 0.37f, 0.65f)
                : BorderRed;

            if (claimed)
            {
                c.BtnImage.color   = DimBtn;
                c.BtnText.text     = "CLAIMED";
                c.BtnText.color    = new Color(1f, 1f, 1f, 0.35f);
                c.Btn.interactable = false;
            }
            else if (done)
            {
                c.BtnImage.color   = NeonRed;
                c.BtnText.text     = inFlight ? "CLAIMING…" : "CLAIM REWARD";
                c.BtnText.color    = Color.white;
                c.Btn.interactable = !inFlight;   // disabled while a backend claim is pending
            }
            else
            {
                c.BtnImage.color   = MutedBtn;
                c.BtnText.text     = "IN PROGRESS";
                c.BtnText.color    = new Color(1f, 1f, 1f, 0.35f);
                c.Btn.interactable = false;
            }
        }

        // ── Claim ─────────────────────────────────────────────────────────────
        void OnClaimClicked(int index)
        {
            if (_backend) { OnClaimClickedBackend(index); return; }

            // ── Local mode (unchanged) ──
            var ctx = GameContext.Instance;
            if (ctx?.Economy == null)
            {
                Debug.LogError("[MissionsScreen] GameContext or Economy service unavailable.");
                return;
            }
            if (!_system.TryClaim(index, out int reward)) return;
            // Phase-4: route through the economy facade so the reward is counted in
            // totalVpEarned and persisted consistently with every other VP grant.
            ctx.Economy.GrantReward(reward, "mission");
            GameEvents.RaiseToast($"+{reward} VP reward claimed!");
        }

        // Backend claim: server validates + grants VP + returns the new balance. Unity
        // never grants VP locally; the button is disabled until the response lands.
        void OnClaimClickedBackend(int index)
        {
            if (!BackendIndexValid(index)) return;
            var mission = _backendMissions[index];
            if (mission.status == "CLAIMED") return;
            if (CardClaimInFlight(index)) return;

            var ctx = GameContext.Instance;
            if (ctx == null) return;

            _claimInFlight[index] = true;
            RefreshCard(index);   // disable + show CLAIMING…

            ctx.ClaimMissionBackend(mission.missionId,
                res =>
                {
                    // Guard: runs from the persistent GameContext; may fire after this
                    // screen was destroyed by navigation. (mission.status was already
                    // committed server-side; only the UI mutation is skipped.)
                    if (this == null) return;
                    _claimInFlight[index] = false;
                    // Wallet already applied by the helper. Mark this mission claimed
                    // from the authoritative status so it cannot be claimed again.
                    mission.status = res != null && !string.IsNullOrEmpty(res.status) ? res.status : "CLAIMED";
                    int reward = res != null ? res.rewardVp : mission.rewardVp;
                    GameEvents.RaiseToast($"+{reward} VP reward claimed!");
                    SortCards();
                    Refresh();
                },
                err =>
                {
                    if (this == null) return;
                    _claimInFlight[index] = false;
                    if (!string.IsNullOrEmpty(err)) GameEvents.RaiseToast(err);
                    RefreshCard(index);
                });
        }

        // ── Circle sprite: built-in Knob or procedural fallback ───────────────
        static Sprite GetCircleSprite()
        {
            if (s_circle != null) return s_circle;
            s_circle = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            if (s_circle != null) return s_circle;
            // Procedural anti-aliased white circle
            const int sz = 64;
            var tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float ctr = sz * 0.5f, rad = ctr - 1f;
            var px = new Color32[sz * sz];
            for (int y = 0; y < sz; y++)
            for (int x = 0; x < sz; x++)
            {
                float d = Mathf.Sqrt((x - ctr) * (x - ctr) + (y - ctr) * (y - ctr));
                byte  a = (byte)(Mathf.Clamp01(rad - d + 1f) * 255f);
                px[y * sz + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            s_circle = Sprite.Create(tex, new Rect(0, 0, sz, sz), new Vector2(0.5f, 0.5f));
            return s_circle;
        }

        // ── Ring image: pivot top-center, Radial360 fill ──────────────────────
        static Image MakeRingImg(string name, Transform parent, Sprite sprite, Color color,
                                 float fillAmount, Vector2 anchoredPos, float size)
        {
            var go = NewGo(name, parent, typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.anchorMin        = new Vector2(0.5f, 1f);
            rt.anchorMax        = new Vector2(0.5f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            if (sprite != null) img.sprite = sprite;
            img.color      = color;
            img.type       = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillAmount = fillAmount;
            return img;
        }

        // pivot=(0.5,0.5), anchor=(0.5,1) — center-point positioning from card top
        static void SetCenterRT(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin        = new Vector2(0.5f, 1f);
            rt.anchorMax        = new Vector2(0.5f, 1f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta        = new Vector2(w, h);
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
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
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
    }
}
