using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI
{
    /// <summary>
    /// Premium lobby card — redesigned to match reference layout.
    ///
    /// Layout:
    ///   [3px rarity accent] | [Content zone — 3 rows] | [JOIN column — full height]
    ///
    ///   Row 1:  [Mode badge]  [#ID]   ───spacer───  [Wager $$$]
    ///   Row 2:  [CaseChip][CaseChip] ×N  ─spacer─  [P1][P2][○][○]
    ///   Row 3:  [● LIVE / WAITING indicator]  [X/X OPS]
    /// </summary>
    public sealed class LobbyCard : MonoBehaviour
    {
        // ── Sizing constants ─────────────────────────────────────────────────
        public const float Height    = 108f;
        const float JoinColumnW      = 80f;
        const float AccentW          = 3f;
        const float PadH             = 12f;   // horizontal inner padding
        const float PadVTop          = 11f;   // top padding inside content zone
        const float RowGap           = 6f;    // gap between rows
        const float Row1H            = 22f;
        const float Row2H            = 28f;
        const float Row3H            = 16f;
        const float ChipSize         = 28f;
        const float CircleSize       = 24f;
        const float BadgeH           = 22f;

        static readonly Color32[] PlayerColors =
        {
            new Color32(220,  45,  70, 255),   // P1 – red
            new Color32( 45, 110, 220, 255),   // P2 – blue
            new Color32( 45, 200,  95, 255),   // P3 – green
            new Color32(220, 145,  45, 255),   // P4 – amber
        };

        BattleLobbyData         _data;
        Action<BattleLobbyData> _onJoin;

        AngledCutImage  _joinBg;
        TextMeshProUGUI _joinLabel;
        Image           _liveDot;
        Button          _joinButton;
        Outline         _joinBorder;
        Coroutine       _livePulse;

        // ── Factory ───────────────────────────────────────────────────────────
        public static LobbyCard Create(Transform parent, BattleLobbyData data,
            Action<BattleLobbyData> onJoin)
        {
            var go   = NewGo("LobbyCard_" + data.LobbyId, parent);
            var card = go.AddComponent<LobbyCard>();
            var le   = go.AddComponent<LayoutElement>();
            le.minHeight       = Height;
            le.preferredHeight = Height;
            card.Build(data, onJoin);
            return card;
        }

        // ── Build ─────────────────────────────────────────────────────────────
        void Build(BattleLobbyData data, Action<BattleLobbyData> onJoin)
        {
            _data   = data;
            _onJoin = onJoin;

            // Card background — slightly elevated surface
            var bg = MakeImage("Bg", transform, ColorPalette.CardBg, raycast: true);
            Stretch(bg.rectTransform);
            // Subtle outer border
            var border = bg.gameObject.AddComponent<Outline>();
            border.effectColor    = ColorPalette.Border;
            border.effectDistance = new Vector2(1f, -1f);

            // Rarity top-edge tint — 1px strip across top in rarity colour
            var topTint = MakeImage("TopTint", transform, ColorPalette.WithAlpha(ColorPalette.ForRarity(data.Rarity), 0.35f), raycast: false);
            var ttRt = topTint.rectTransform;
            ttRt.anchorMin        = new Vector2(0f, 1f);
            ttRt.anchorMax        = new Vector2(1f, 1f);
            ttRt.pivot            = new Vector2(0.5f, 1f);
            ttRt.anchoredPosition = Vector2.zero;
            ttRt.sizeDelta        = new Vector2(0f, 2f);

            // 3px left rarity accent strip
            var accent = MakeImage("RarityAccent", transform, ColorPalette.ForRarity(data.Rarity), raycast: false);
            var aRt = accent.rectTransform;
            aRt.anchorMin        = new Vector2(0f, 0f);
            aRt.anchorMax        = new Vector2(0f, 1f);
            aRt.pivot            = new Vector2(0f, 0.5f);
            aRt.anchoredPosition = Vector2.zero;
            aRt.sizeDelta        = new Vector2(AccentW, 0f);

            BuildJoinColumn();
            BuildContentZone(data);
            ApplyStatusVisuals();
        }

        // ── JOIN column — full card height, 80dp wide ─────────────────────────
        void BuildJoinColumn()
        {
            var col = NewGo("JoinColumn", transform);
            var colRt = (RectTransform)col.transform;
            colRt.anchorMin        = new Vector2(1f, 0f);
            colRt.anchorMax        = new Vector2(1f, 1f);
            colRt.pivot            = new Vector2(1f, 0.5f);
            colRt.anchoredPosition = Vector2.zero;
            colRt.sizeDelta        = new Vector2(JoinColumnW, 0f);

            // Left divider
            var div = MakeImage("Divider", col.transform, ColorPalette.Border);
            div.raycastTarget = false;
            SetRect(div.rectTransform,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(1f, 0f));

            // JOIN button — nearly full height, 10dp inner vertical padding
            _joinBg = MakeAngled("JoinBtn", col.transform, ColorPalette.ActiveRed, 8f, raycast: true);
            var joinRt = _joinBg.rectTransform;
            joinRt.anchorMin = Vector2.zero;
            joinRt.anchorMax = Vector2.one;
            joinRt.offsetMin = new Vector2(10f, 10f);
            joinRt.offsetMax = new Vector2(-10f, -10f);

            _joinButton = _joinBg.gameObject.AddComponent<Button>();
            _joinButton.transition = Selectable.Transition.None;
            _joinButton.onClick.AddListener(OnJoinPressed);

            // Live pulse dot — sits above the JOIN label
            _liveDot = MakeImage("LiveDot", _joinBg.transform, ColorPalette.ActiveRed);
            _liveDot.raycastTarget = false;
            SetRect(_liveDot.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -7f), new Vector2(7f, 7f));

            _joinLabel = MakeTmp(_joinBg.transform, "JoinLbl", "JOIN", 14f, FontStyles.Bold, Color.white);
            _joinLabel.alignment = TextAlignmentOptions.Center;
            Stretch(_joinLabel.rectTransform);
        }

        // ── Content zone — 3 rows inside [accent..join divider] ──────────────
        void BuildContentZone(BattleLobbyData data)
        {
            var zone = NewGo("ContentZone", transform);
            var zRt  = (RectTransform)zone.transform;
            zRt.anchorMin = Vector2.zero;
            zRt.anchorMax = Vector2.one;
            zRt.offsetMin = new Vector2(AccentW + 2f, 0f);
            zRt.offsetMax = new Vector2(-JoinColumnW, 0f);

            BuildRow1(zone.transform, data);
            BuildRow2(zone.transform, data);
            BuildRow3(zone.transform, data);
        }

        // Row 1: [Mode badge]  [#ID]  ──spacer──  [Wager]
        void BuildRow1(Transform zone, BattleLobbyData data)
        {
            var row = MakeHRow("Row1", zone, PadH, 6f, TextAnchor.MiddleLeft);
            SetRect((RectTransform)row.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -PadVTop), new Vector2(0f, Row1H));

            // Mode badge — angled cut, rarity-tinted background
            var mbGo = NewGo("ModeBadge", row.transform, typeof(AngledCutImage), typeof(LayoutElement));
            var mbImg = mbGo.GetComponent<AngledCutImage>();
            mbImg.color        = ColorPalette.ActiveRed;
            mbImg.CutSize      = 5f;
            mbImg.raycastTarget = false;
            var mbLe = mbGo.GetComponent<LayoutElement>();
            mbLe.minWidth  = 48f;
            mbLe.minHeight = BadgeH;
            ((RectTransform)mbGo.transform).sizeDelta = new Vector2(48f, BadgeH);
            var mbLbl = MakeTmp(mbGo.transform, "ModeLbl", ModeText(data), 10f, FontStyles.Bold, Color.white);
            mbLbl.characterSpacing = 1f;
            mbLbl.alignment        = TextAlignmentOptions.Center;
            Stretch(mbLbl.rectTransform);

            // Lobby ID
            var idLbl = MakeTmp(row.transform, "LobbyId", "#" + data.LobbyId,
                11f, FontStyles.Normal, ColorPalette.TextDim);
            idLbl.alignment = TextAlignmentOptions.MidlineLeft;
            var idRt = idLbl.rectTransform;
            idRt.sizeDelta = new Vector2(48f, Row1H);
            idLbl.gameObject.AddComponent<LayoutElement>().minWidth = 48f;

            // Spacer
            AddSpacer(row.transform);

            // Wager — dominant right element, large bold
            var wagerLbl = MakeTmp(row.transform, "Wager",
                "$" + data.WagerVP.ToString("N0"),
                24f, FontStyles.Bold, Color.white);
            wagerLbl.alignment = TextAlignmentOptions.MidlineRight;
            wagerLbl.rectTransform.sizeDelta = new Vector2(120f, Row1H);
            wagerLbl.gameObject.AddComponent<LayoutElement>().minWidth = 90f;
        }

        // Row 2: [Chip][Chip] ×N  ──spacer──  [P1][P2][○][○]
        void BuildRow2(Transform zone, BattleLobbyData data)
        {
            float top = PadVTop + Row1H + RowGap;
            var row = MakeHRow("Row2", zone, PadH, 4f, TextAnchor.MiddleLeft);
            SetRect((RectTransform)row.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -top), new Vector2(0f, Row2H));

            // Case chips — up to 3, rarity-coloured background + border
            Color chipBg     = ColorPalette.WithAlpha(ColorPalette.ForRarity(data.Rarity), 0.25f);
            Color chipBorder = ColorPalette.ForRarity(data.Rarity);
            string abbrev    = CaseAbbrev(data.CaseName);
            int    chips     = Mathf.Clamp(data.Rounds, 1, 3);
            for (int i = 0; i < chips; i++)
            {
                var chipGo  = NewGo("Chip_" + i, row.transform, typeof(Image), typeof(LayoutElement));
                var chipImg = chipGo.GetComponent<Image>();
                chipImg.color = chipBg; chipImg.raycastTarget = false;
                var cb = chipGo.AddComponent<Outline>();
                cb.effectColor    = chipBorder;
                cb.effectDistance = new Vector2(1f, -1f);
                ((RectTransform)chipGo.transform).sizeDelta = new Vector2(ChipSize, ChipSize);
                chipGo.GetComponent<LayoutElement>().minWidth  = ChipSize;
                chipGo.GetComponent<LayoutElement>().minHeight = ChipSize;
                var chipLbl = MakeTmp(chipGo.transform, "Lbl", abbrev, 8f, FontStyles.Bold, Color.white);
                chipLbl.alignment = TextAlignmentOptions.Center;
                Stretch(chipLbl.rectTransform);
            }

            // ×N quantity
            var qtyLbl = MakeTmp(row.transform, "Qty", "×" + data.Rounds,
                11f, FontStyles.Bold, ColorPalette.TextDim);
            qtyLbl.alignment = TextAlignmentOptions.MidlineLeft;
            qtyLbl.rectTransform.sizeDelta = new Vector2(28f, Row2H);
            qtyLbl.gameObject.AddComponent<LayoutElement>().minWidth = 28f;

            // Spacer
            AddSpacer(row.transform);

            // Player circles — 24×24, clearly labeled and gapped
            for (int i = 0; i < data.MaxPlayers; i++)
            {
                bool filled = i < data.CurrentPlayers;
                var circGo  = NewGo("P_" + i, row.transform, typeof(Image), typeof(LayoutElement));
                var circImg = circGo.GetComponent<Image>();
                circImg.raycastTarget = false;
                var circLe = circGo.GetComponent<LayoutElement>();
                circLe.minWidth = circLe.minHeight = CircleSize;
                ((RectTransform)circGo.transform).sizeDelta = new Vector2(CircleSize, CircleSize);

                if (filled)
                {
                    Color32 pc = PlayerColors[Mathf.Clamp(i, 0, PlayerColors.Length - 1)];
                    circImg.color = pc;
                    var pLbl = MakeTmp(circGo.transform, "P", "P" + (i + 1),
                        9f, FontStyles.Bold, Color.white);
                    pLbl.alignment = TextAlignmentOptions.Center;
                    Stretch(pLbl.rectTransform);
                }
                else
                {
                    circImg.color = ColorPalette.WithAlpha(ColorPalette.Surface, 0.4f);
                    var eb = circGo.AddComponent<Outline>();
                    eb.effectColor    = ColorPalette.WithAlpha(ColorPalette.Border, 0.7f);
                    eb.effectDistance = new Vector2(1f, -1f);
                    // "+" inside empty slot
                    var plusLbl = MakeTmp(circGo.transform, "Plus", "+",
                        11f, FontStyles.Normal, ColorPalette.WithAlpha(ColorPalette.TextDim, 0.5f));
                    plusLbl.alignment = TextAlignmentOptions.Center;
                    Stretch(plusLbl.rectTransform);
                }

                // Small gap between circles (not after last)
                if (i < data.MaxPlayers - 1)
                {
                    var gap = NewGo("Gap_" + i, row.transform, typeof(LayoutElement));
                    gap.GetComponent<LayoutElement>().minWidth = 4f;
                }
            }
        }

        // Row 3: status dot + "X/X OPS · WAITING"
        void BuildRow3(Transform zone, BattleLobbyData data)
        {
            float top = PadVTop + Row1H + RowGap + Row2H + RowGap;
            var row = MakeHRow("Row3", zone, PadH, 5f, TextAnchor.MiddleLeft);
            SetRect((RectTransform)row.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -top), new Vector2(0f, Row3H));

            int cur    = data.CurrentPlayers;
            int max    = data.MaxPlayers;
            var status = data.ComputeStatus();

            // Small coloured status dot
            var dotGo  = NewGo("StatusDot", row.transform, typeof(Image), typeof(LayoutElement));
            var dotImg = dotGo.GetComponent<Image>();
            dotImg.raycastTarget = false;
            dotImg.color = status == LobbyStatus.Live ? ColorPalette.ActiveRed
                         : status == LobbyStatus.Full ? ColorPalette.TextDim
                         : new Color(0.3f, 0.75f, 0.4f, 1f);   // green = waiting/open
            var dotLe = dotGo.GetComponent<LayoutElement>();
            dotLe.minWidth = dotLe.minHeight = 7f;
            ((RectTransform)dotGo.transform).sizeDelta = new Vector2(7f, 7f);

            string statusStr = status == LobbyStatus.Live    ? "LIVE"
                             : status == LobbyStatus.Full    ? "FULL"
                             : "WAITING";

            var statusLbl = MakeTmp(row.transform, "Status",
                cur + "/" + max + " OPS  ·  " + statusStr,
                10f, FontStyles.Normal,
                status == LobbyStatus.Live ? ColorPalette.ActiveRed : ColorPalette.TextDim);
            statusLbl.alignment = TextAlignmentOptions.MidlineLeft;
            statusLbl.rectTransform.sizeDelta = new Vector2(200f, Row3H);
            statusLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        // ── Status visuals on JOIN column ─────────────────────────────────────
        void ApplyStatusVisuals()
        {
            var status = _data.ComputeStatus();
            switch (status)
            {
                case LobbyStatus.Full:
                    _joinBg.color             = ColorPalette.Surface;
                    _joinLabel.text           = "FULL";
                    _joinLabel.fontSize        = 11f;
                    _joinLabel.color           = ColorPalette.TextDim;
                    _joinButton.interactable   = false;
                    _liveDot.gameObject.SetActive(false);
                    EnsureJoinBorder(ColorPalette.Border);
                    break;

                case LobbyStatus.Live:
                    _joinBg.color             = Color.clear;
                    _joinLabel.text           = "LIVE";
                    _joinLabel.fontSize        = 12f;
                    _joinLabel.color           = ColorPalette.ActiveRed;
                    _joinButton.interactable   = true;
                    _liveDot.gameObject.SetActive(true);
                    EnsureJoinBorder(ColorPalette.ActiveRed);
                    break;

                default:  // Waiting — primary CTA, full red
                    _joinBg.color             = ColorPalette.ActiveRed;
                    _joinLabel.text           = "JOIN";
                    _joinLabel.fontSize        = 15f;
                    _joinLabel.color           = Color.white;
                    _joinButton.interactable   = true;
                    _liveDot.gameObject.SetActive(false);
                    RemoveJoinBorder();
                    break;
            }
        }

        void EnsureJoinBorder(Color c)
        {
            if (_joinBorder == null) _joinBorder = _joinBg.gameObject.AddComponent<Outline>();
            _joinBorder.effectColor    = c;
            _joinBorder.effectDistance = new Vector2(1f, -1f);
        }

        void RemoveJoinBorder()
        {
            if (_joinBorder != null) _joinBorder.effectColor = Color.clear;
        }

        // ── Unity events ──────────────────────────────────────────────────────
        void OnEnable()
        {
            if (_data == null) return;
            if (_data.ComputeStatus() == LobbyStatus.Live && _liveDot != null)
                _livePulse = StartCoroutine(UIAnimator.PulseOpacity(_liveDot, 0.3f, 1f, 1f));
        }

        void OnDisable()
        {
            StopAllCoroutines();
            _livePulse = null;
        }

        void OnJoinPressed()
        {
            if (_data.ComputeStatus() == LobbyStatus.Full) return;
            StartCoroutine(UIAnimator.ScalePress(transform, 1.02f, 0.12f));
            _onJoin?.Invoke(_data);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        // Creates a row container with a HorizontalLayoutGroup.
        static GameObject MakeHRow(string name, Transform parent, float padH, float spacing, TextAnchor align)
        {
            var go  = NewGo(name, parent, typeof(HorizontalLayoutGroup));
            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            hlg.padding               = new RectOffset((int)padH, (int)padH, 0, 0);
            hlg.spacing               = spacing;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth      = false;
            hlg.childControlHeight     = false;
            hlg.childAlignment         = align;
            return go;
        }

        static void AddSpacer(Transform parent)
        {
            var sp = NewGo("Spacer", parent, typeof(LayoutElement));
            sp.GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        static string ModeText(BattleLobbyData d)
        {
            return d.MaxPlayers switch
            {
                2 => "1V1",
                3 => "1V1V1",
                4 => d.Mode == BattleMode.Crazy ? "1V1V1V1" : "2V2",
                _ => d.MaxPlayers + "P",
            };
        }

        static string CaseAbbrev(string name)
        {
            if (string.IsNullOrEmpty(name)) return "??-";
            var parts = name.Split(' ');
            return parts.Length >= 2
                ? (parts[0][..Mathf.Min(2, parts[0].Length)] + parts[1][0]).ToUpper() + "-"
                : name[..Mathf.Min(3, name.Length)].ToUpper() + "-";
        }
    }
}
