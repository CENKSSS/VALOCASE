using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;
using ValoCase.UI;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Waiting Room — shown after JOIN (bot lobby) or CREATE (host lobby).
    ///
    /// Static frame (header + CTA) is built once. Lobby-specific content
    /// (summary card + player slots) is rebuilt on every Show so the same
    /// screen instance can display different lobbies without stale data.
    ///
    /// Bot lobbies: all slots fill immediately (bots are always READY).
    /// Host lobbies: player slot 0 = YOU (HOST), remaining = BOT.
    /// START BATTLE is available immediately in both paths.
    /// </summary>
    public sealed class WaitingRoomScreen : MonoBehaviour
    {
        public event Action OnLeave;
        public event Action<BattleLobbyData> OnStartBattle;

        const float SidePad    = 16f;
        const float HeaderH    = 60f;
        const float CtaH       = 54f;
        const float NavReserve = 98f;

        bool            _built;
        BattleLobbyData _lobby;
        bool            _isHost;

        Transform      _dynamicRoot;
        AngledCutImage _startBg;

        // ── Lifecycle ────────────────────────────────────────────────────────────
        public void Show(BattleLobbyData lobby, bool isHost)
        {
            _lobby  = lobby;
            _isHost = isHost;
            gameObject.SetActive(true);
            BuildOnce();
            RebuildDynamic();
            StartCoroutine(UIAnimator.SlideFromBottom((RectTransform)transform, 0.24f));
        }

        public void Hide()
        {
            StopAllCoroutines();
            gameObject.SetActive(false);
        }

        void OnDisable() => StopAllCoroutines();

        // ── Static frame (built once) ──────────────────────────────────────────
        void BuildOnce()
        {
            if (_built) return;
            _built = true;

            var rt = (RectTransform)transform;

            var bg = MakeImage("Bg", rt, ColorPalette.BgDeep, raycast: true);
            Stretch(bg.rectTransform);

            // Dynamic root sits between bg and the static header/CTA so lobby
            // content is always rendered beneath the chrome.
            _dynamicRoot = NewGo("DynamicRoot", rt).transform;
            Stretch((RectTransform)_dynamicRoot);

            BuildHeader(rt);
            BuildCta(rt);
        }

        void BuildHeader(RectTransform rt)
        {
            var hdr = MakeImage("Header", rt, ColorPalette.CardBg, raycast: true);
            TopStrip(hdr.rectTransform, HeaderH);

            var accent = MakeImage("TopAccent", hdr.transform, ColorPalette.ActiveRed);
            accent.raycastTarget = false;
            TopStrip(accent.rectTransform, 2f);

            var divider = MakeImage("BottomBorder", hdr.transform, ColorPalette.Border);
            divider.raycastTarget = false;
            BottomStrip(divider.rectTransform, 1f);

            // LEAVE button (left).
            var leaveGo = NewGo("LeaveBtn", hdr.transform, typeof(Image), typeof(Button));
            leaveGo.GetComponent<Image>().color = Color.clear;
            SetRect((RectTransform)leaveGo.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(SidePad, 0f), new Vector2(80f, 44f));
            var leaveLbl = MakeTmp(leaveGo.transform, "Lbl", "< LEAVE", 11f, FontStyles.Bold, ColorPalette.ActiveRed);
            leaveLbl.alignment = TextAlignmentOptions.Center;
            leaveLbl.characterSpacing = 1f;
            Stretch(leaveLbl.rectTransform);
            var leaveBtn = leaveGo.GetComponent<Button>();
            leaveBtn.transition = Selectable.Transition.None;
            leaveBtn.onClick.AddListener(() => OnLeave?.Invoke());

            // Title (center).
            var title = MakeTmp(hdr.transform, "Title", "WAITING ROOM", 16f, FontStyles.Bold, ColorPalette.TextBright);
            title.characterSpacing = 2f;
            title.alignment = TextAlignmentOptions.Center;
            SetRect(title.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -4f), new Vector2(-160f, 0f));

            // BOT LOBBY chip (right).
            var chip = MakeAngled("BotChip", hdr.transform,
                ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.18f), 3f);
            SetRect(chip.rectTransform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-SidePad, 0f), new Vector2(76f, 28f));
            var chipBorder = chip.gameObject.AddComponent<Outline>();
            chipBorder.effectColor    = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.6f);
            chipBorder.effectDistance = new Vector2(1f, -1f);
            var chipLbl = MakeTmp(chip.transform, "Lbl", "BOT LOBBY", 8f, FontStyles.Bold, ColorPalette.ActiveRed);
            chipLbl.alignment = TextAlignmentOptions.Center;
            chipLbl.characterSpacing = 1.5f;
            Stretch(chipLbl.rectTransform);
        }

        void BuildCta(RectTransform rt)
        {
            _startBg = MakeAngled("StartBtn", rt, ColorPalette.ActiveRed, 10f, raycast: true);
            var cRt = _startBg.rectTransform;
            cRt.anchorMin = new Vector2(0f, 0f);
            cRt.anchorMax = new Vector2(1f, 0f);
            cRt.pivot     = new Vector2(0.5f, 0f);
            cRt.offsetMin = new Vector2(SidePad, NavReserve);
            cRt.offsetMax = new Vector2(-SidePad, NavReserve + CtaH);

            var btn = _startBg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnStartPressed);

            var lbl = MakeTmp(_startBg.transform, "Lbl", "START BATTLE", 15f, FontStyles.Bold, Color.white);
            lbl.characterSpacing = 2f;
            lbl.alignment = TextAlignmentOptions.Center;
            Stretch(lbl.rectTransform);
        }

        // ── Dynamic lobby content (rebuilt per Show) ──────────────────────────
        void RebuildDynamic()
        {
            if (_dynamicRoot == null) return;
            for (int i = _dynamicRoot.childCount - 1; i >= 0; i--)
                Destroy(_dynamicRoot.GetChild(i).gameObject);
            var rt = (RectTransform)_dynamicRoot;
            BuildLobbySummary(rt);
            BuildPlayersSection(rt);
        }

        void BuildLobbySummary(RectTransform rt)
        {
            const float topOffset = HeaderH + 16f;   // 76
            const float height    = 92f;

            var card = MakeImage("SummaryCard", rt, ColorPalette.CardBg);
            TopStrip(card.rectTransform, height, -topOffset);
            card.rectTransform.offsetMin = new Vector2(SidePad, card.rectTransform.offsetMin.y);
            card.rectTransform.offsetMax = new Vector2(-SidePad, card.rectTransform.offsetMax.y);
            var cardBorder = card.gameObject.AddComponent<Outline>();
            cardBorder.effectColor    = ColorPalette.Border;
            cardBorder.effectDistance = new Vector2(1f, -1f);

            // Left red accent bar.
            var bar = MakeImage("AccentBar", card.transform, ColorPalette.ActiveRed);
            var bRt = bar.rectTransform;
            bRt.anchorMin = new Vector2(0f, 0f);
            bRt.anchorMax = new Vector2(0f, 1f);
            bRt.pivot     = new Vector2(0f, 0.5f);
            bRt.sizeDelta = new Vector2(3f, 0f);

            const float il = 14f;

            // Case name.
            var nameLbl = MakeTmp(card.transform, "CaseName",
                _lobby?.CaseName ?? "Basic Vandal Case",
                15f, FontStyles.Bold, ColorPalette.TextBright);
            nameLbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(nameLbl.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(il, -14f), new Vector2(-8f, 22f));

            // Mode badge (1V1 / 1V1V1).
            string modeStr = (_lobby?.MaxPlayers ?? 2) == 3 ? "1V1V1" : "1V1";
            float  modeW   = modeStr == "1V1V1" ? 52f : 40f;
            var modeBadge = MakeAngled("ModeBadge", card.transform, ColorPalette.Surface, 3f);
            SetRect(modeBadge.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(il, -42f), new Vector2(modeW, 18f));
            var mb = modeBadge.gameObject.AddComponent<Outline>();
            mb.effectColor    = ColorPalette.WithAlpha(ColorPalette.ActiveRed, 0.55f);
            mb.effectDistance = new Vector2(1f, -1f);
            var modeLbl = MakeTmp(modeBadge.transform, "Lbl", modeStr, 9f, FontStyles.Bold, ColorPalette.ActiveRed);
            modeLbl.alignment = TextAlignmentOptions.Center;
            modeLbl.characterSpacing = 1f;
            Stretch(modeLbl.rectTransform);

            // Rounds badge.
            int    rounds   = _lobby?.Rounds ?? 1;
            string roundStr = rounds + (rounds == 1 ? " ROUND" : " ROUNDS");
            var roundBadge = MakeImage("RoundBadge", card.transform, ColorPalette.Surface);
            SetRect(roundBadge.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(il + modeW + 6f, -42f), new Vector2(72f, 18f));
            var rb = roundBadge.gameObject.AddComponent<Outline>();
            rb.effectColor    = ColorPalette.Border;
            rb.effectDistance = new Vector2(1f, -1f);
            var roundLbl = MakeTmp(roundBadge.transform, "Lbl", roundStr, 9f, FontStyles.Bold, ColorPalette.TextDim);
            roundLbl.alignment = TextAlignmentOptions.Center;
            roundLbl.characterSpacing = 1f;
            Stretch(roundLbl.rectTransform);

            // Total pot (WagerVP is per-player entry; pot = entry × player count).
            int    wager  = _lobby?.WagerVP ?? 500;
            int    maxP   = _lobby?.MaxPlayers ?? 2;
            string potStr = (wager * maxP).ToString("N0") + " VP";
            string line   = $"TOTAL POT  <color=#{Hex(ColorPalette.GoldAccent)}><b>{potStr}</b></color>";
            var wagerLbl = MakeTmp(card.transform, "Wager", line, 11f, FontStyles.Normal, ColorPalette.TextDim);
            wagerLbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(wagerLbl.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                new Vector2(il, 14f), new Vector2(-8f, 18f));
        }

        void BuildPlayersSection(RectTransform rt)
        {
            const float summaryH   = 92f;
            const float sectionTop = HeaderH + 16f + summaryH + 12f;   // 180
            const float sectionH   = 36f;
            const float slotsTop   = sectionTop + sectionH + 8f;       // 224
            const float slotH      = 70f;
            const float slotGap    = 10f;

            int maxPlayers = _lobby?.MaxPlayers ?? 2;

            // "PLAYERS" section header.
            var secGo = NewGo("PlayersHeader", rt);
            TopStrip((RectTransform)secGo.transform, sectionH, -sectionTop);

            var tick = MakeImage("Tick", secGo.transform, ColorPalette.ActiveRed);
            SetRect(tick.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(SidePad, -4f), new Vector2(3f, 14f));

            var secTitle = MakeTmp(secGo.transform, "Title",
                $"PLAYERS  <color=#{Hex(ColorPalette.TextDim)}>{maxPlayers}/{maxPlayers}</color>",
                13f, FontStyles.Bold, ColorPalette.TextBright);
            secTitle.characterSpacing = 2f;
            secTitle.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(secTitle.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(SidePad + 10f, -2f), new Vector2(-110f, 18f));

            // ALL READY chip (right).
            var chip = MakeAngled("AllReadyChip", secGo.transform,
                ColorPalette.WithAlpha(ColorPalette.GoldAccent, 0.12f), 3f);
            SetRect(chip.rectTransform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-SidePad, 0f), new Vector2(76f, 22f));
            var cb = chip.gameObject.AddComponent<Outline>();
            cb.effectColor    = ColorPalette.WithAlpha(ColorPalette.GoldAccent, 0.45f);
            cb.effectDistance = new Vector2(1f, -1f);
            var chipLbl = MakeTmp(chip.transform, "Lbl", "ALL READY", 8f, FontStyles.Bold, ColorPalette.GoldAccent);
            chipLbl.alignment = TextAlignmentOptions.Center;
            chipLbl.characterSpacing = 1f;
            Stretch(chipLbl.rectTransform);

            // Player slot cards.
            for (int i = 0; i < maxPlayers; i++)
            {
                bool   isUser = i == 0;
                string name   = isUser ? "YOU" : "BOT " + i;
                float  top    = slotsTop + i * (slotH + slotGap);
                BuildPlayerSlot(rt, name, isUser, top);
            }
        }

        void BuildPlayerSlot(RectTransform rt, string playerName, bool isUser, float topOffset)
        {
            const float h = 70f;

            var slot = MakeImage("Slot_" + playerName, rt, ColorPalette.CardBg);
            TopStrip(slot.rectTransform, h, -topOffset);
            slot.rectTransform.offsetMin = new Vector2(SidePad, slot.rectTransform.offsetMin.y);
            slot.rectTransform.offsetMax = new Vector2(-SidePad, slot.rectTransform.offsetMax.y);
            var slotBorder = slot.gameObject.AddComponent<Outline>();
            slotBorder.effectColor    = isUser
                ? ColorPalette.WithAlpha(ColorPalette.GoldAccent, 0.4f)
                : ColorPalette.Border;
            slotBorder.effectDistance = new Vector2(1f, -1f);

            // Avatar (gold for user, red for bot).
            Color avatarColor = isUser ? ColorPalette.GoldAccent : ColorPalette.ActiveRed;
            var avatar = MakeAngled("Avatar", slot.transform, avatarColor, 5f);
            SetRect(avatar.rectTransform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(14f, 0f), new Vector2(40f, 40f));
            var avatarLbl = MakeTmp(avatar.transform, "Initial",
                isUser ? "Y" : "B", 18f, FontStyles.Bold,
                isUser ? ColorPalette.BgDeep : Color.white);
            avatarLbl.alignment = TextAlignmentOptions.Center;
            Stretch(avatarLbl.rectTransform);

            // Player name.
            var nameLbl = MakeTmp(slot.transform, "Name", playerName,
                14f, FontStyles.Bold, ColorPalette.TextBright);
            nameLbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(nameLbl.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(64f, -10f), new Vector2(-90f, 20f));

            // Role sub-label.
            string subText  = isUser ? (_isHost ? "HOST" : "PLAYER") : "BOT PLAYER";
            Color  subColor = isUser ? ColorPalette.GoldAccent : ColorPalette.TextDim;
            var subLbl = MakeTmp(slot.transform, "Sub", subText, 9f, FontStyles.Bold, subColor);
            subLbl.characterSpacing = 1.5f;
            subLbl.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(subLbl.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                new Vector2(64f, 12f), new Vector2(-90f, 16f));

            // READY badge (right, gold).
            var readyBg = MakeAngled("ReadyBadge", slot.transform,
                ColorPalette.WithAlpha(ColorPalette.GoldAccent, 0.12f), 3f);
            SetRect(readyBg.rectTransform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-14f, 0f), new Vector2(62f, 26f));
            var ro = readyBg.gameObject.AddComponent<Outline>();
            ro.effectColor    = ColorPalette.WithAlpha(ColorPalette.GoldAccent, 0.5f);
            ro.effectDistance = new Vector2(1f, -1f);
            var readyLbl = MakeTmp(readyBg.transform, "Lbl", "READY", 9f, FontStyles.Bold, ColorPalette.GoldAccent);
            readyLbl.alignment = TextAlignmentOptions.Center;
            readyLbl.characterSpacing = 2f;
            Stretch(readyLbl.rectTransform);
        }

        // ── CTA ───────────────────────────────────────────────────────────────
        void OnStartPressed()
        {
            if (_startBg != null)
                StartCoroutine(UIAnimator.ScalePress(_startBg.transform, 0.97f, 0.12f));
            OnStartBattle?.Invoke(_lobby);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        static string Hex(Color c)
        {
            Color32 c32 = c;
            return c32.r.ToString("X2") + c32.g.ToString("X2") + c32.b.ToString("X2");
        }
    }
}
