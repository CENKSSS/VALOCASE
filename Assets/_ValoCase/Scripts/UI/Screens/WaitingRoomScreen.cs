using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;
using ValoCase.UI;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Screen 3 — Waiting Room. Hosts player slots (filled / empty-waiting / add-bot / bot),
    /// a bottom Add-Bot confirmation sheet, and a START BATTLE CTA. Host-only controls are
    /// gated by the lobby's IsHost flag (always true here since the local player creates/joins).
    /// Emits OnLeave on cancel/leave and OnStartBattle when the host starts the battle.
    /// </summary>
    public sealed class WaitingRoomScreen : MonoBehaviour
    {
        const float HeaderH  = 72f;
        const float StripH   = 80f;
        const float FooterH  = 72f;
        const float SidePad  = 14f;
        const float SheetH   = 220f;
        const float PulseDur = 1.5f;

        static readonly Color32[] PlayerColors =
        {
            new Color32(200,  37,  60, 255),  // P1 red
            new Color32( 37,  99, 200, 255),  // P2 blue
            new Color32( 37, 200,  85, 255),  // P3 green
            new Color32(200, 134,  37, 255),  // P4 orange
        };

        public event Action OnLeave;
        public event Action<BattleLobbyData> OnStartBattle;

        bool _built;
        bool _isHost = true;

        BattleLobbyData    _lobby;
        BattlePlayerData[] _players;
        int _pendingBotSlot = -1;

        // Header refs.
        TextMeshProUGUI _headerTitle;
        TextMeshProUGUI _battleId;
        Coroutine       _titlePulse;

        // Strip refs.
        TextMeshProUGUI _caseName;
        TextMeshProUGUI _roundsBadge;
        TextMeshProUGUI _wager;
        Outline         _thumbBorder;

        // Slots container.
        RectTransform _slotsContainer;
        readonly List<GameObject> _slotGos = new();

        // Footer refs.
        TextMeshProUGUI _countIndicator;
        AngledCutImage  _startBg;
        TextMeshProUGUI _startLabel;
        Button          _startButton;
        TextMeshProUGUI _waitingForHost;
        Coroutine       _startGlow;
        Coroutine       _dotsRoutine;

        // Bot sheet.
        GameObject    _sheetOverlay;
        RectTransform _sheetRt;
        Coroutine     _sheetRoutine;

        public void Show(BattleLobbyData lobby, bool isHost)
        {
            _lobby  = lobby;
            _isHost = isHost;
            gameObject.SetActive(true);
            BuildOnce();
            InitPlayers();
            RebuildSlots();
            RefreshHeader();
            RefreshStrip();
            RefreshFooter();
        }

        public void Hide()
        {
            StopAllCoroutines();
            _titlePulse = _startGlow = _dotsRoutine = _sheetRoutine = null;
            gameObject.SetActive(false);
        }

        void InitPlayers()
        {
            int max = _lobby.MaxPlayers;
            _players = new BattlePlayerData[max];
            _players[0] = new BattlePlayerData
            {
                Username = _isHost ? "YOU" : _lobby.HostName,
                IsHost = true, IsLocalPlayer = _isHost, SlotType = PlayerSlotType.Filled
            };
            int filled = Mathf.Clamp(_lobby.CurrentPlayers, 1, max);
            for (int i = 1; i < max; i++)
            {
                if (i < filled)
                    _players[i] = new BattlePlayerData
                    {
                        Username = "Player" + (i + 1), SlotType = PlayerSlotType.Filled
                    };
                else
                    _players[i] = BattlePlayerData.Empty(_isHost);
            }
        }

        int CurrentPlayerCount()
        {
            int n = 0;
            foreach (var p in _players)
                if (p != null && (p.SlotType == PlayerSlotType.Filled || p.SlotType == PlayerSlotType.Bot)) n++;
            return n;
        }

        // ── Build (once) ──────────────────────────────────────────────────────
        void BuildOnce()
        {
            if (_built) return;
            _built = true;

            var rt = (RectTransform)transform;
            var bg = MakeImage("Bg", rt, ColorPalette.BgDeep, raycast: true);
            Stretch(bg.rectTransform);

            BuildHeader(rt);
            BuildStrip(rt);
            BuildSlotsContainer(rt);
            BuildFooter(rt);
            BuildBotSheet(rt);
        }

        void BuildHeader(RectTransform rt)
        {
            var hdr = MakeImage("Header", rt, ColorPalette.CardBg, raycast: false);
            TopStrip(hdr.rectTransform, HeaderH);

            // Bottom border
            var hdrBorder = MakeImage("BottomBorder", hdr.transform, ColorPalette.Border);
            hdrBorder.raycastTarget = false;
            SetRect(hdrBorder.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, 1f));

            // Leave / Cancel button
            var leave = NewGo("Leave", hdr.transform, typeof(Image), typeof(Button));
            leave.GetComponent<Image>().color = ColorPalette.Surface;
            leave.GetComponent<Image>().raycastTarget = true;
            var leaveBorder = leave.AddComponent<Outline>();
            leaveBorder.effectColor = ColorPalette.Border; leaveBorder.effectDistance = new Vector2(1f, -1f);
            var leaveRt = (RectTransform)leave.transform;
            leaveRt.anchorMin        = new Vector2(0f, 0.5f);
            leaveRt.anchorMax        = new Vector2(0f, 0.5f);
            leaveRt.pivot            = new Vector2(0f, 0.5f);
            leaveRt.anchoredPosition = new Vector2(16f, 0f);
            leaveRt.sizeDelta        = new Vector2(72f, 36f);
            var leaveLbl = MakeTmp(leave.transform, "Lbl", _isHost ? "CANCEL" : "LEAVE", 10f, FontStyles.Bold,
                _isHost ? ColorPalette.ActiveRed : ColorPalette.TextDim);
            leaveLbl.alignment = TextAlignmentOptions.Center;
            Stretch(leaveLbl.rectTransform);
            var leaveBtn = leave.GetComponent<Button>();
            leaveBtn.transition = Selectable.Transition.None;
            leaveBtn.onClick.AddListener(() => OnLeave?.Invoke());

            // Title
            _headerTitle = MakeTmp(hdr.transform, "Title", "WAITING FOR PLAYERS", 14f, FontStyles.Bold, ColorPalette.TextBright);
            _headerTitle.characterSpacing = 2f;
            _headerTitle.alignment = TextAlignmentOptions.Center;
            var titleRt = _headerTitle.rectTransform;
            titleRt.anchorMin        = new Vector2(0f, 0.5f);
            titleRt.anchorMax        = new Vector2(1f, 0.5f);
            titleRt.pivot            = new Vector2(0.5f, 0.5f);
            titleRt.anchoredPosition = new Vector2(0f, 8f);
            titleRt.sizeDelta        = new Vector2(0f, 22f);
            titleRt.offsetMin        = new Vector2(104f, titleRt.offsetMin.y);
            titleRt.offsetMax        = new Vector2(-104f, titleRt.offsetMax.y);

            // Battle ID
            _battleId = MakeTmp(hdr.transform, "BattleId", "#----", 10f, FontStyles.Normal, ColorPalette.TextDim);
            _battleId.alignment = TextAlignmentOptions.Center;
            var bidRt = _battleId.rectTransform;
            bidRt.anchorMin        = new Vector2(0f, 0.5f);
            bidRt.anchorMax        = new Vector2(1f, 0.5f);
            bidRt.pivot            = new Vector2(0.5f, 0.5f);
            bidRt.anchoredPosition = new Vector2(0f, -14f);
            bidRt.sizeDelta        = new Vector2(0f, 14f);
        }

        void BuildStrip(RectTransform rt)
        {
            var strip = MakeImage("HeroStrip", rt, ColorPalette.CardBg, raycast: true);
            TopStrip(strip.rectTransform, StripH, -HeaderH);

            // Top border — RED accent, height 2
            var topBorderImg = MakeImage("TopBorder", strip.transform, ColorPalette.ActiveRed);
            topBorderImg.raycastTarget = false;
            TopStrip(topBorderImg.rectTransform, 2f);

            // Bottom border — grey, height 1
            var botBorderImg = MakeImage("BottomBorder", strip.transform, ColorPalette.Border);
            botBorderImg.raycastTarget = false;
            BottomStrip(botBorderImg.rectTransform, 1f);

            // Case thumb — rarity-colored outline
            var thumb = MakeImage("CaseThumb", strip.transform, ColorPalette.Surface);
            thumb.raycastTarget = false;
            _thumbBorder = thumb.gameObject.AddComponent<Outline>();
            _thumbBorder.effectColor    = ColorPalette.Border;
            _thumbBorder.effectDistance = new Vector2(1f, -1f);
            SetRect(thumb.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(16f, 0f), new Vector2(52f, 52f));

            // Case name
            _caseName = MakeTmp(strip.transform, "CaseName", "", 14f, FontStyles.Bold, ColorPalette.TextBright);
            _caseName.alignment = TextAlignmentOptions.MidlineLeft;
            var cnRt = _caseName.rectTransform;
            cnRt.anchorMin        = new Vector2(0f, 0.5f);
            cnRt.anchorMax        = new Vector2(0.55f, 0.5f);
            cnRt.pivot            = new Vector2(0f, 0.5f);
            cnRt.anchoredPosition = new Vector2(80f, 10f);
            cnRt.sizeDelta        = new Vector2(0f, 20f);

            // Rounds badge
            _roundsBadge = MakeTmp(strip.transform, "Rounds", "", 10f, FontStyles.Normal, ColorPalette.TextDim);
            _roundsBadge.alignment = TextAlignmentOptions.MidlineLeft;
            var rbRt = _roundsBadge.rectTransform;
            rbRt.anchorMin        = new Vector2(0f, 0.5f);
            rbRt.anchorMax        = new Vector2(0.55f, 0.5f);
            rbRt.pivot            = new Vector2(0f, 0.5f);
            rbRt.anchoredPosition = new Vector2(80f, -12f);
            rbRt.sizeDelta        = new Vector2(0f, 16f);

            // Wager value
            _wager = MakeTmp(strip.transform, "Wager", "", 18f, FontStyles.Bold, ColorPalette.GoldAccent);
            _wager.alignment = TextAlignmentOptions.MidlineRight;
            var wRt = _wager.rectTransform;
            wRt.anchorMin        = new Vector2(0.55f, 0.5f);
            wRt.anchorMax        = new Vector2(1f, 0.5f);
            wRt.pivot            = new Vector2(1f, 0.5f);
            wRt.anchoredPosition = new Vector2(-16f, 8f);
            wRt.sizeDelta        = new Vector2(0f, 24f);

            // Wager sub-label
            var wagerSub = MakeTmp(strip.transform, "WagerSub", "TOTAL POT", 9f, FontStyles.Normal, ColorPalette.TextDim);
            wagerSub.alignment = TextAlignmentOptions.MidlineRight;
            var wsRt = wagerSub.rectTransform;
            wsRt.anchorMin        = new Vector2(0.55f, 0.5f);
            wsRt.anchorMax        = new Vector2(1f, 0.5f);
            wsRt.pivot            = new Vector2(1f, 0.5f);
            wsRt.anchoredPosition = new Vector2(-16f, -14f);
            wsRt.sizeDelta        = new Vector2(0f, 14f);
        }

        void BuildSlotsContainer(RectTransform rt)
        {
            var go = NewGo("SlotsContainer", rt);
            _slotsContainer = (RectTransform)go.transform;
            _slotsContainer.anchorMin = Vector2.zero;
            _slotsContainer.anchorMax = Vector2.one;
            _slotsContainer.offsetMin = new Vector2(14f, FooterH + 12f);
            _slotsContainer.offsetMax = new Vector2(-14f, -(HeaderH + StripH + 12f));
        }

        void BuildFooter(RectTransform rt)
        {
            var footer = MakeImage("Footer", rt, ColorPalette.CardBg, raycast: true);
            BottomStrip(footer.rectTransform, FooterH);

            // Top border
            var topBorder = MakeImage("TopBorder", footer.transform, ColorPalette.Border);
            topBorder.raycastTarget = false;
            TopStrip(topBorder.rectTransform, 1f);

            // Player count indicator — 12pt, top of footer
            _countIndicator = MakeTmp(footer.transform, "Count", "0 / 0 PLAYERS", 12f, FontStyles.Bold, ColorPalette.TextDim);
            _countIndicator.characterSpacing = 2f;
            _countIndicator.alignment = TextAlignmentOptions.Center;
            var countRt = _countIndicator.rectTransform;
            countRt.anchorMin        = new Vector2(0f, 1f);
            countRt.anchorMax        = new Vector2(1f, 1f);
            countRt.pivot            = new Vector2(0.5f, 1f);
            countRt.anchoredPosition = new Vector2(0f, -4f);
            countRt.sizeDelta        = new Vector2(0f, 14f);

            // START BATTLE button — 48dp tall
            _startBg = MakeAngled("Start", footer.transform, ColorPalette.Border, 10f, raycast: true);
            var startRt = _startBg.rectTransform;
            startRt.anchorMin        = new Vector2(0f, 0f);
            startRt.anchorMax        = new Vector2(1f, 0f);
            startRt.pivot            = new Vector2(0.5f, 0f);
            startRt.offsetMin        = new Vector2(14f, 8f);
            startRt.offsetMax        = new Vector2(-14f, 56f);
            _startButton = _startBg.gameObject.AddComponent<Button>();
            _startButton.transition = Selectable.Transition.None;
            _startButton.onClick.AddListener(OnStartPressed);

            _startLabel = MakeTmp(_startBg.transform, "Lbl", "START BATTLE", 15f, FontStyles.Bold, ColorPalette.TextDim);
            _startLabel.characterSpacing = 2f;
            _startLabel.alignment = TextAlignmentOptions.Center;
            Stretch(_startLabel.rectTransform);

            // Guest waiting label
            _waitingForHost = MakeTmp(footer.transform, "WaitHost", "WAITING FOR HOST TO START", 12f, FontStyles.Normal, ColorPalette.TextDim);
            _waitingForHost.alignment = TextAlignmentOptions.Center;
            var wfhRt = _waitingForHost.rectTransform;
            wfhRt.anchorMin        = new Vector2(0.5f, 0f);
            wfhRt.anchorMax        = new Vector2(0.5f, 0f);
            wfhRt.pivot            = new Vector2(0.5f, 0f);
            wfhRt.anchoredPosition = new Vector2(0f, 38f);
            wfhRt.sizeDelta        = new Vector2(340f, 24f);
            _waitingForHost.gameObject.SetActive(false);
        }

        // ── Slots ─────────────────────────────────────────────────────────────
        void RebuildSlots()
        {
            foreach (var go in _slotGos) Destroy(go);
            _slotGos.Clear();

            // Remove any VerticalLayoutGroup left from a previous stacked build
            var existingVlg = _slotsContainer.GetComponent<VerticalLayoutGroup>();
            if (existingVlg != null) Destroy(existingVlg);

            bool gridMode = _lobby.MaxPlayers >= 3;
            if (gridMode) BuildGridSlots();
            else          BuildStackedSlots();
        }

        void BuildStackedSlots()
        {
            const float slotH  = 96f;
            const float vsH    = 24f;
            const float vsGap  = 6f;
            const float totalH = slotH + vsGap + vsH + vsGap + slotH; // 228

            // VLG on container — childAlignment MiddleCenter vertically centers the 228dp group
            var vlg = _slotsContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing               = 0f;
            vlg.padding               = new RectOffset(0, 0, 0, 0);
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            vlg.childAlignment         = TextAnchor.MiddleCenter;

            // Slot 0 — fixed 96dp
            if (_players.Length > 0)
            {
                var slot0 = BuildSlot(0, slotH);
                var le0   = slot0.AddComponent<LayoutElement>();
                le0.minHeight      = slotH;
                le0.flexibleHeight = 0f;
            }

            // Gap above VS divider
            var gapAbove = NewGo("GapAbove", _slotsContainer);
            _slotGos.Add(gapAbove);
            gapAbove.AddComponent<LayoutElement>().minHeight = vsGap;

            // VS divider — 24dp
            var vsGo = NewGo("VSDivider", _slotsContainer);
            _slotGos.Add(vsGo);
            var vsLe = vsGo.AddComponent<LayoutElement>();
            vsLe.minHeight      = vsH;
            vsLe.flexibleHeight = 0f;
            var vsHlg = vsGo.AddComponent<HorizontalLayoutGroup>();
            vsHlg.spacing              = 8f;
            vsHlg.childForceExpandWidth  = false;
            vsHlg.childForceExpandHeight = false;
            vsHlg.childControlWidth      = false;
            vsHlg.childControlHeight     = false;
            vsHlg.childAlignment         = TextAnchor.MiddleCenter;

            var leftLine = MakeImage("LeftLine", vsGo.transform, ColorPalette.Border);
            leftLine.raycastTarget = false;
            leftLine.rectTransform.sizeDelta = new Vector2(80f, 1f);
            var llLe = leftLine.gameObject.AddComponent<LayoutElement>();
            llLe.minWidth = 80f; llLe.minHeight = 1f;

            var vsText = MakeTmp(vsGo.transform, "VS", "VS", 16f, FontStyles.Bold, ColorPalette.ActiveRed);
            vsText.alignment = TextAlignmentOptions.Center;
            vsText.gameObject.AddComponent<LayoutElement>().minWidth = 28f;

            var rightLine = MakeImage("RightLine", vsGo.transform, ColorPalette.Border);
            rightLine.raycastTarget = false;
            rightLine.rectTransform.sizeDelta = new Vector2(80f, 1f);
            var rlLe = rightLine.gameObject.AddComponent<LayoutElement>();
            rlLe.minWidth = 80f; rlLe.minHeight = 1f;

            // Gap below VS divider
            var gapBelow = NewGo("GapBelow", _slotsContainer);
            _slotGos.Add(gapBelow);
            gapBelow.AddComponent<LayoutElement>().minHeight = vsGap;

            // Slot 1 — fixed 96dp
            if (_players.Length > 1)
            {
                var slot1 = BuildSlot(1, slotH);
                var le1   = slot1.AddComponent<LayoutElement>();
                le1.minHeight      = slotH;
                le1.flexibleHeight = 0f;
            }
        }

        void BuildGridSlots()
        {
            const float cellH = 88f;
            const float gapX  = 4f;
            const float gapY  = 4f;

            for (int i = 0; i < _players.Length; i++)
            {
                int   col  = i % 2;
                int   row  = i / 2;
                float top  = -(row * (cellH + gapY));
                float bot  = top - cellH;

                var slot = BuildSlot(i, cellH);
                var srt  = (RectTransform)slot.transform;

                if (col == 0)
                {
                    // Left half: anchor spans 0–50%, inset 0 left, gapX/2 right
                    srt.anchorMin = new Vector2(0f, 1f);
                    srt.anchorMax = new Vector2(0.5f, 1f);
                    srt.pivot     = new Vector2(0f, 1f);
                    srt.offsetMin = new Vector2(0f, bot);
                    srt.offsetMax = new Vector2(-gapX / 2f, top);
                }
                else
                {
                    // Right half: anchor spans 50–100%, inset gapX/2 left, 0 right
                    srt.anchorMin = new Vector2(0.5f, 1f);
                    srt.anchorMax = new Vector2(1f, 1f);
                    srt.pivot     = new Vector2(0f, 1f);
                    srt.offsetMin = new Vector2(gapX / 2f, bot);
                    srt.offsetMax = new Vector2(0f, top);
                }
            }
        }

        GameObject BuildSlot(int index, float height)
        {
            var p      = _players[index];
            var slotGo = NewGo("Slot_" + index, _slotsContainer);
            _slotGos.Add(slotGo);

            bool gridMode = _lobby.MaxPlayers >= 3;

            switch (p.SlotType)
            {
                case PlayerSlotType.Filled:      BuildFilledSlot(slotGo, index, p, gridMode);   break;
                case PlayerSlotType.Bot:          BuildBotSlot(slotGo, index, p);                break;
                case PlayerSlotType.EmptyAddBot:  BuildAddBotSlot(slotGo, index);                break;
                default:                          BuildWaitingSlot(slotGo);                      break;
            }
            return slotGo;
        }

        void BuildFilledSlot(GameObject slotGo, int index, BattlePlayerData p, bool compact)
        {
            // Standard Image background (NOT AngledCutImage)
            var bg = MakeImage("Bg", slotGo.transform, ColorPalette.CardBg, raycast: true);
            Stretch(bg.rectTransform);
            var border = bg.gameObject.AddComponent<Outline>();
            bool own = p.IsLocalPlayer;
            border.effectColor    = own ? ColorPalette.ActiveRed : ColorPalette.Border;
            border.effectDistance = own ? new Vector2(2f, -2f) : new Vector2(1f, -1f);

            // Player color top edge strip — 2dp, full width
            Color32 edgeColor = PlayerColors[Mathf.Clamp(index, 0, PlayerColors.Length - 1)];
            var topEdge = MakeImage("TopEdge", slotGo.transform, edgeColor, raycast: false);
            SetRect(topEdge.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 2f));

            if (own)
            {
                var glow = MakeImage("InnerGlow", slotGo.transform, ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.06f));
                glow.raycastTarget = false;
                Stretch(glow.rectTransform);
            }

            // Avatar
            Color32 pc = PlayerColors[Mathf.Clamp(index, 0, PlayerColors.Length - 1)];
            var avatar = MakeImage("Avatar", slotGo.transform, ColorPalette.WithAlpha(pc, 0.12f));
            avatar.raycastTarget = false;
            var avatarBorder = avatar.gameObject.AddComponent<Outline>();
            avatarBorder.effectColor    = pc;
            avatarBorder.effectDistance = new Vector2(1f, -1f);
            const float avatarSize = 42f;
            const float avatarX    = 12f;
            SetRect(avatar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(avatarX, 0f), new Vector2(avatarSize, avatarSize));

            // Crown (host only)
            if (p.IsHost)
            {
                var crown = MakeTmp(slotGo.transform, "Crown", "★", 12f, FontStyles.Bold, ColorPalette.GoldAccent);
                crown.alignment = TextAlignmentOptions.Center;
                SetRect(crown.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(42f, 28f), new Vector2(20f, 20f));
            }

            // Username — 12pt, right of 42dp avatar + 12dp left + 8dp gap = 62dp
            var name = MakeTmp(slotGo.transform, "Name", p.Username, 12f, FontStyles.Bold, ColorPalette.TextBright);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(62f, 7f), new Vector2(-100f, 18f));

            // Role badge — 9pt
            string roleText  = p.IsHost ? "HOST" : (p.IsLocalPlayer ? "YOU" : "READY");
            Color  roleColor = p.IsHost ? ColorPalette.GoldAccent
                             : (p.IsLocalPlayer ? ColorPalette.ActiveRed
                             : new Color(0.145f, 0.784f, 0.333f));
            var badge = MakeTmp(slotGo.transform, "Role", roleText, 9f, FontStyles.Bold, roleColor);
            badge.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(62f, -10f), new Vector2(-100f, 14f));
        }

        void BuildBotSlot(GameObject slotGo, int index, BattlePlayerData p)
        {
            var bg = MakeImage("Bg", slotGo.transform, ColorPalette.CardBg, raycast: true);
            Stretch(bg.rectTransform);
            var border = bg.gameObject.AddComponent<Outline>();
            border.effectColor    = ColorPalette.WithAlpha(ColorPalette.TextDim, 0.4f);
            border.effectDistance = new Vector2(1f, -1f);

            var avatar = MakeImage("Avatar", slotGo.transform, ColorPalette.Surface);
            avatar.raycastTarget = false;
            SetRect(avatar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(42f, 42f));

            var avatarIcon = MakeTmp(avatar.transform, "Icon", "BOT", 9f, FontStyles.Bold, new Color(0.267f, 0.294f, 0.353f));
            avatarIcon.alignment = TextAlignmentOptions.Center;
            Stretch(avatarIcon.rectTransform);

            var name = MakeTmp(slotGo.transform, "Name", "BOT", 12f, FontStyles.Bold, new Color(0.267f, 0.294f, 0.353f));
            name.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(62f, 0f), new Vector2(-100f, 18f));

            var badge = MakeTmp(slotGo.transform, "Badge", "BOT", 9f, FontStyles.Normal, new Color(0.267f, 0.294f, 0.353f));
            badge.alignment = TextAlignmentOptions.MidlineRight;
            SetRect(badge.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-8f, -8f), new Vector2(40f, 16f));

            if (_isHost)
            {
                var btn = bg.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                int cap = index;
                btn.onClick.AddListener(() => RemoveBot(cap));

                var remove = MakeTmp(slotGo.transform, "Remove", "Remove Bot", 10f, FontStyles.Bold, ColorPalette.ActiveRed);
                remove.alignment = TextAlignmentOptions.MidlineRight;
                SetRect(remove.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-8f, 6f), new Vector2(-90f, 16f));
            }
        }

        void BuildWaitingSlot(GameObject slotGo)
        {
            // Standard Image background (NOT AngledCutImage)
            var bg = MakeImage("Bg", slotGo.transform, ColorPalette.WithAlpha(ColorPalette.CardBg, 0.5f));
            Stretch(bg.rectTransform);
            var border = bg.gameObject.AddComponent<Outline>();
            border.effectColor    = ColorPalette.Border;
            border.effectDistance = new Vector2(1f, -1f);

            // Empty icon — dashed-border rect placeholder
            var icon = MakeImage("EmptyIcon", slotGo.transform, Color.clear);
            icon.raycastTarget = false;
            var iconBorder = icon.gameObject.AddComponent<Outline>();
            iconBorder.effectColor    = ColorPalette.Border;
            iconBorder.effectDistance = new Vector2(2f, -2f);
            SetRect(icon.rectTransform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(40f, 0f), new Vector2(28f, 28f));

            var lbl = MakeTmp(slotGo.transform, "Lbl", "OPEN SLOT", 11f, FontStyles.Normal, new Color(0.267f, 0.294f, 0.353f));
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(lbl.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(72f, 0f), new Vector2(-90f, 20f));

            StartCoroutine(UIAnimator.PulseOpacity(bg, 0.3f, 0.7f, PulseDur));
            _dotsRoutine = StartCoroutine(UIAnimator.DottedSuffix(lbl, "OPEN SLOT", 0.4f));
        }

        void BuildAddBotSlot(GameObject slotGo, int index)
        {
            // Standard Image background (NOT AngledCutImage)
            var bg = MakeImage("Bg", slotGo.transform, ColorPalette.CardBg, raycast: true);
            Stretch(bg.rectTransform);
            var border = bg.gameObject.AddComponent<Outline>();
            border.effectColor    = ColorPalette.Border;
            border.effectDistance = new Vector2(1f, -1f);

            // Plus icon — 18pt centered
            var icon = MakeTmp(slotGo.transform, "Icon", "＋", 18f, FontStyles.Bold, ColorPalette.TextDim);
            icon.alignment = TextAlignmentOptions.Center;
            SetRect(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(30f, 0f), new Vector2(26f, 26f));

            var lbl = MakeTmp(slotGo.transform, "Lbl", "ADD BOT", 10f, FontStyles.Bold, new Color(0.667f, 0.667f, 0.667f));
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(lbl.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(62f, 0f), new Vector2(-80f, 18f));

            // ADD BOT chip — 56×24
            var addBtnGo = NewGo("AddBotBtn", slotGo.transform, typeof(AngledCutImage), typeof(Button));
            var addBtnImg = addBtnGo.GetComponent<AngledCutImage>();
            addBtnImg.color = ColorPalette.ActiveRed; addBtnImg.CutSize = 4f; addBtnImg.raycastTarget = true;
            var addBtnRt = (RectTransform)addBtnGo.transform;
            addBtnRt.anchorMin        = new Vector2(1f, 0.5f);
            addBtnRt.anchorMax        = new Vector2(1f, 0.5f);
            addBtnRt.pivot            = new Vector2(1f, 0.5f);
            addBtnRt.anchoredPosition = new Vector2(-10f, 0f);
            addBtnRt.sizeDelta        = new Vector2(56f, 24f);
            var addBtnLbl = MakeTmp(addBtnGo.transform, "Lbl", "ADD BOT", 10f, FontStyles.Bold, Color.white);
            addBtnLbl.alignment = TextAlignmentOptions.Center;
            Stretch(addBtnLbl.rectTransform);
            var addBtn = addBtnGo.GetComponent<Button>();
            addBtn.transition = Selectable.Transition.None;
            int cap = index;
            addBtn.onClick.AddListener(() => OpenBotSheet(cap, null, border));
        }

        // ── Bot add/remove ────────────────────────────────────────────────────
        void OpenBotSheet(int slot, AngledCutImage bgRef, Outline border)
        {
            _pendingBotSlot = slot;
            if (border != null) border.effectColor = ColorPalette.ActiveRed;
            ShowBotSheet(true);
        }

        void RemoveBot(int slot)
        {
            if (slot < 0 || slot >= _players.Length) return;
            _players[slot] = BattlePlayerData.Empty(_isHost);
            RebuildSlots();
            RefreshFooter();
        }

        void ConfirmAddBot()
        {
            if (_pendingBotSlot >= 0 && _pendingBotSlot < _players.Length)
            {
                _players[_pendingBotSlot] = BattlePlayerData.MakeBot("BOT");
                _pendingBotSlot = -1;
                RebuildSlots();
                RefreshFooter();
            }
            ShowBotSheet(false);
        }

        // ── Bot confirm bottom sheet ──────────────────────────────────────────
        void BuildBotSheet(RectTransform rt)
        {
            _sheetOverlay = NewGo("BotSheetOverlay", rt, typeof(Image), typeof(Button));
            _sheetOverlay.GetComponent<Image>().color = ColorPalette.WithAlpha(ColorPalette.BgDeep, 0.6f);
            Stretch(_sheetOverlay);
            var dismiss = _sheetOverlay.GetComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(() => ShowBotSheet(false));

            var sheet = NewGo("Sheet", _sheetOverlay.transform, typeof(Image));
            _sheetRt = (RectTransform)sheet.transform;
            BottomStrip(_sheetRt, SheetH);
            sheet.GetComponent<Image>().color = ColorPalette.CardBg;   // #141519
            sheet.GetComponent<Image>().raycastTarget = true;

            var topBorder = MakeImage("TopBorder", sheet.transform, ColorPalette.ActiveRed);
            topBorder.raycastTarget = false;
            TopStrip(topBorder.rectTransform, 2f);

            var handle = MakeImage("Handle", sheet.transform, ColorPalette.WithAlpha(ColorPalette.TextDim, 0.5f));
            handle.raycastTarget = false;
            SetRect(handle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(32f, 4f));

            var title = MakeTmp(sheet.transform, "Title", "ADD BOT TO SLOT?", 15f, FontStyles.Bold, ColorPalette.TextBright);
            title.alignment = TextAlignmentOptions.Center;
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -40f), new Vector2(0f, 24f));

            var sub = MakeTmp(sheet.transform, "Sub", "Bots open cases automatically each round.", 12f, FontStyles.Normal, ColorPalette.TextDim);
            sub.alignment = TextAlignmentOptions.Center;
            sub.enableWordWrapping = true;
            SetRect(sub.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -72f), new Vector2(-48f, 36f));

            var cancel = NewGo("Cancel", sheet.transform, typeof(Image), typeof(Button));
            cancel.GetComponent<Image>().color = Color.clear;
            var cancelBorder = cancel.AddComponent<Outline>();
            cancelBorder.effectColor = ColorPalette.Border; cancelBorder.effectDistance = new Vector2(1f, -1f);
            ((RectTransform)cancel.transform).anchorMin = new Vector2(0f, 0f);
            ((RectTransform)cancel.transform).anchorMax = new Vector2(0.5f, 0f);
            ((RectTransform)cancel.transform).pivot     = new Vector2(0.5f, 0f);
            ((RectTransform)cancel.transform).offsetMin = new Vector2(SidePad, 20f);
            ((RectTransform)cancel.transform).offsetMax = new Vector2(-6f, 72f);
            var cancelLbl = MakeTmp(cancel.transform, "Lbl", "CANCEL", 14f, FontStyles.Bold, ColorPalette.TextBright);
            cancelLbl.alignment = TextAlignmentOptions.Center;
            Stretch(cancelLbl.rectTransform);
            var cancelBtn = cancel.GetComponent<Button>();
            cancelBtn.transition = Selectable.Transition.None;
            cancelBtn.onClick.AddListener(() => ShowBotSheet(false));

            var confirm = NewGo("Confirm", sheet.transform, typeof(AngledCutImage), typeof(Button));
            confirm.GetComponent<AngledCutImage>().color = ColorPalette.ActiveRed;
            confirm.GetComponent<AngledCutImage>().CutSize = 8f;
            confirm.GetComponent<AngledCutImage>().raycastTarget = true;
            ((RectTransform)confirm.transform).anchorMin = new Vector2(0.5f, 0f);
            ((RectTransform)confirm.transform).anchorMax = new Vector2(1f, 0f);
            ((RectTransform)confirm.transform).pivot     = new Vector2(0.5f, 0f);
            ((RectTransform)confirm.transform).offsetMin = new Vector2(6f, 20f);
            ((RectTransform)confirm.transform).offsetMax = new Vector2(-SidePad, 72f);
            var confirmLbl = MakeTmp(confirm.transform, "Lbl", "ADD BOT", 14f, FontStyles.Bold, Color.white);
            confirmLbl.alignment = TextAlignmentOptions.Center;
            Stretch(confirmLbl.rectTransform);
            var confirmBtn = confirm.GetComponent<Button>();
            confirmBtn.transition = Selectable.Transition.None;
            confirmBtn.onClick.AddListener(ConfirmAddBot);

            _sheetOverlay.SetActive(false);
        }

        void ShowBotSheet(bool show)
        {
            if (_sheetRoutine != null) { StopCoroutine(_sheetRoutine); _sheetRoutine = null; }
            if (show)
            {
                _sheetOverlay.SetActive(true);
                _sheetRoutine = StartCoroutine(UIAnimator.SlideFromBottom(_sheetRt, 0.2f));
            }
            else
            {
                _sheetRoutine = StartCoroutine(CloseSheet());
            }
        }

        IEnumerator CloseSheet()
        {
            Vector2 shown  = _sheetRt.anchoredPosition;
            Vector2 hidden = shown - new Vector2(0f, SheetH);
            float t = 0f;
            while (t < 0.15f)
            {
                t += Time.unscaledDeltaTime;
                _sheetRt.anchoredPosition = Vector2.Lerp(shown, hidden, t / 0.15f);
                yield return null;
            }
            _sheetRt.anchoredPosition = shown;
            _sheetOverlay.SetActive(false);
        }

        // ── Refresh ───────────────────────────────────────────────────────────
        void RefreshHeader()
        {
            _battleId.text = "#" + _lobby.LobbyId;
            bool full = CurrentPlayerCount() >= _lobby.MaxPlayers;
            if (_titlePulse != null) { StopCoroutine(_titlePulse); _titlePulse = null; }
            if (full)
            {
                _headerTitle.text  = "READY TO BATTLE!";
                _headerTitle.color = ColorPalette.ActiveRed;
                SetAlpha(_headerTitle, 1f);
            }
            else
            {
                _headerTitle.text  = "WAITING FOR PLAYERS";
                _headerTitle.color = ColorPalette.TextBright;
                _titlePulse = StartCoroutine(UIAnimator.PulseOpacity(_headerTitle, 0.5f, 1f, PulseDur));
            }
        }

        void RefreshStrip()
        {
            _caseName.text    = _lobby.CaseName;
            _roundsBadge.text = "×" + _lobby.Rounds + " ROUNDS";
            _wager.text       = (_lobby.WagerVP * Mathf.Max(1, CurrentPlayerCount())).ToString("N0") + " VP";
            if (_thumbBorder != null)
            {
                _thumbBorder.effectColor    = ColorPalette.ForRarity(_lobby.Rarity);
                _thumbBorder.effectDistance = new Vector2(2f, -2f);
            }
        }

        void RefreshFooter()
        {
            int cur  = CurrentPlayerCount();
            int max  = _lobby.MaxPlayers;
            bool full     = cur >= max;
            bool canStart = cur >= 2;

            _countIndicator.text  = $"{cur} / {max} PLAYERS";
            if (full)
                _countIndicator.color = ColorPalette.ActiveRed;
            else if (cur == max - 1)
                _countIndicator.color = ColorPalette.GoldAccent;
            else
                _countIndicator.color = ColorPalette.TextDim;

            RefreshHeader();
            RefreshStrip();

            if (!_isHost)
            {
                _startBg.gameObject.SetActive(false);
                _waitingForHost.gameObject.SetActive(true);
                if (_dotsRoutine != null) StopCoroutine(_dotsRoutine);
                _dotsRoutine = StartCoroutine(UIAnimator.DottedSuffix(_waitingForHost, "WAITING FOR HOST TO START", 0.4f));
                return;
            }

            _startBg.gameObject.SetActive(true);
            _waitingForHost.gameObject.SetActive(false);

            if (_startGlow != null) { StopCoroutine(_startGlow); _startGlow = null; }

            if (canStart)
            {
                _startBg.color            = ColorPalette.ActiveRed;
                _startLabel.text          = "START BATTLE";
                _startLabel.color         = ColorPalette.TextBright;
                _startButton.interactable = true;
                _startGlow = StartCoroutine(UIAnimator.PulseGlow(_startBg, ColorPalette.ActiveRed, 1.8f));
            }
            else
            {
                int needed = 2 - cur;
                _startBg.color            = ColorPalette.Border;
                _startLabel.text          = "NEED " + needed + " MORE PLAYER" + (needed == 1 ? "" : "S");
                _startLabel.color         = new Color(0.267f, 0.294f, 0.353f);
                _startButton.interactable = false;
                var sh = _startBg.GetComponent<Shadow>();
                if (sh != null) sh.effectColor = Color.clear;
            }
        }

        void OnStartPressed()
        {
            if (CurrentPlayerCount() < 2) return;
            _lobby.CurrentPlayers = CurrentPlayerCount();
            _lobby.Status = LobbyStatus.Live;
            StartCoroutine(StartPressAnim());
        }

        IEnumerator StartPressAnim()
        {
            yield return UIAnimator.ScalePress(_startBg.transform, 0.97f, 0.15f);
            OnStartBattle?.Invoke(_lobby);
        }

        static void SetAlpha(Graphic g, float a)
        {
            var c = g.color; c.a = a; g.color = c;
        }

        void OnDisable()
        {
            StopAllCoroutines();
            _titlePulse = _startGlow = _dotsRoutine = _sheetRoutine = null;
        }
    }
}
