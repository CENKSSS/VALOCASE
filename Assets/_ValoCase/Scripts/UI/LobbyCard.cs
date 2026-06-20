using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI
{
    /// <summary>
    /// Premium bot-lobby card.
    /// </summary>
    public sealed class LobbyCard : MonoBehaviour
    {
        public const float Height = 180f;

        const float Pad          = 54f;
        const float AccentW      = 4f;
        const float ThumbSz      = 64f;
        const float TextLeft     = AccentW + Pad + ThumbSz + 12f;
        const float TextWidthD   = -(TextLeft + Pad);
        const float FooterH      = 58f;
        const float JoinW        = 104f;
        const float JoinH        = 42f;
        const float ThumbTopInset = (Height - FooterH - ThumbSz) * 0.5f;

        static readonly Color RefCard       = HexColor("#07090F");
        static readonly Color RefSecondary  = HexColor("#181922");
        static readonly Color RefBorder     = HexColor("#7A1020");
        static readonly Color RefPrimary    = HexColor("#FF003C");
        static readonly Color RefForeground = HexColor("#FAFAFA");
        static readonly Color RefMuted      = HexColor("#999999");
        static readonly Color RefGold       = HexColor("#FFAA88");
        static readonly Color RefWhite      = HexColor("#FFFFFF");

        BattleLobbyData         _data;
        Action<BattleLobbyData> _onJoin;
        AngledCutImage          _joinBg;
        bool                    _locked;
        Sprite                  _hostAvatar;

        public static LobbyCard Create(Transform parent, BattleLobbyData data,
            Sprite caseIcon, Action<BattleLobbyData> onJoin, bool locked, Sprite hostAvatar = null)
        {
            var go   = NewGo("LobbyCard_" + data.LobbyId, parent);
            var card = go.AddComponent<LobbyCard>();

            var le = go.AddComponent<LayoutElement>();
            le.minHeight       = Height;
            le.preferredHeight = Height;

            card.Build(data, caseIcon, onJoin, locked, hostAvatar);
            return card;
        }

        void Build(BattleLobbyData data, Sprite caseIcon, Action<BattleLobbyData> onJoin, bool locked, Sprite hostAvatar)
        {
            _data       = data;
            _onJoin     = onJoin;
            _locked     = locked;
            _hostAvatar = hostAvatar;

            var bg = MakeImage("Bg", transform, RefCard, raycast: true);
            Stretch(bg.rectTransform);

            var border = bg.gameObject.AddComponent<Outline>();
            border.effectColor    = RefBorder;
            border.effectDistance = new Vector2(1f, -1f);

            var glow = bg.gameObject.AddComponent<Shadow>();
            glow.effectColor = WithAlpha(RefPrimary, 0.60f);
            glow.effectDistance = new Vector2(0f, -4f);

            var cardBtn = bg.gameObject.AddComponent<Button>();
            cardBtn.transition = Selectable.Transition.None;
            cardBtn.onClick.AddListener(OnJoinPressed);

            var accent = MakeImage("Accent", transform, RefPrimary);
            var aRt = accent.rectTransform;
            aRt.anchorMin        = new Vector2(0f, 0f);
            aRt.anchorMax        = new Vector2(0f, 1f);
            aRt.pivot            = new Vector2(0f, 0.5f);
            aRt.sizeDelta        = new Vector2(AccentW, 0f);
            aRt.anchoredPosition = Vector2.zero;

            BuildThumb(caseIcon);
            BuildTextContent(data);
            BuildDivider();
            BuildMetaRow(data);
            BuildJoinButton();
        }

        void BuildThumb(Sprite caseIcon)
        {
            float x = AccentW + Pad;

            var tile = MakeImage("ThumbBg", transform, RefSecondary);
            SetRect(tile.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, -ThumbTopInset), new Vector2(ThumbSz, ThumbSz));

            var tb = tile.gameObject.AddComponent<Outline>();
            tb.effectColor    = WithAlpha(RefPrimary, 0.5f);
            tb.effectDistance = new Vector2(1f, -1f);

            if (caseIcon != null)
            {
                var icon = MakeImage("Thumb", tile.transform, Color.white);
                icon.sprite         = caseIcon;
                icon.preserveAspect = true;

                var iRt = icon.rectTransform;
                iRt.anchorMin = new Vector2(0.5f, 0.5f);
                iRt.anchorMax = new Vector2(0.5f, 0.5f);
                iRt.pivot     = new Vector2(0.5f, 0.5f);
                iRt.sizeDelta = new Vector2(ThumbSz - 10f, ThumbSz - 10f);
            }

            // Host avatar badge (online lobbies only): a small framed portrait at the
            // thumb's bottom-right showing the real creator's backend avatar.
            if (_hostAvatar != null)
            {
                const float badge = 26f;
                var frame = MakeImage("HostAvatarFrame", tile.transform, RefCard);
                SetRect(frame.rectTransform,
                    new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(4f, -4f), new Vector2(badge, badge));
                var fb = frame.gameObject.AddComponent<Outline>();
                fb.effectColor    = WithAlpha(RefPrimary, 0.8f);
                fb.effectDistance = new Vector2(1f, -1f);

                var photo = MakeImage("HostAvatar", frame.transform, Color.white);
                photo.sprite         = _hostAvatar;
                photo.preserveAspect = true;
                var pr = photo.rectTransform;
                pr.anchorMin = new Vector2(0f, 0f);
                pr.anchorMax = new Vector2(1f, 1f);
                pr.offsetMin = new Vector2(1.5f, 1.5f);
                pr.offsetMax = new Vector2(-1.5f, -1.5f);
            }
        }

        void BuildTextContent(BattleLobbyData data)
        {
            var title = MakeTmp(transform, "Title", LobbyTitle(data),
                15f, FontStyles.Bold, RefForeground);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            SetRect(title.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(TextLeft, -16f), new Vector2(TextWidthD, 22f));

            var botBadge = MakeAngled("StatusBadge", transform, RefPrimary, 3f);
            SetRect(botBadge.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(TextLeft, -44f), new Vector2(76f, 18f));

            var botLbl = MakeTmp(botBadge.transform, "Lbl", StatusText(data),
                8f, FontStyles.Bold, RefWhite);
            botLbl.alignment        = TextAlignmentOptions.Center;
            botLbl.characterSpacing = 1f;
            Stretch(botLbl.rectTransform);

            var typeBadge = MakeImage("TypeBadge", transform, RefSecondary);
            SetRect(typeBadge.rectTransform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(TextLeft + 82f, -44f), new Vector2(50f, 18f));

            var typeBorder = typeBadge.gameObject.AddComponent<Outline>();
            typeBorder.effectColor    = WithAlpha(RefPrimary, 0.6f);
            typeBorder.effectDistance = new Vector2(1f, -1f);

            var typeLbl = MakeTmp(typeBadge.transform, "Lbl", TypeText(data),
                8f, FontStyles.Bold, RefPrimary);
            typeLbl.alignment        = TextAlignmentOptions.Center;
            typeLbl.characterSpacing = 1f;
            Stretch(typeLbl.rectTransform);

            var caseLbl = MakeTmp(transform, "CaseName", CaseLine(data),
                11f, FontStyles.Normal, RefMuted);
            caseLbl.alignment = TextAlignmentOptions.MidlineLeft;
            caseLbl.enableWordWrapping = false;
            caseLbl.overflowMode = TextOverflowModes.Ellipsis;
            SetRect(caseLbl.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(TextLeft, -68f), new Vector2(TextWidthD, 18f));
        }

        void BuildDivider()
        {
            var div = MakeImage("Divider", transform, WithAlpha(RefBorder, 0.7f));
            SetRect(div.rectTransform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, FooterH), new Vector2(-(AccentW + Pad * 2f), 1f));
        }

        void BuildMetaRow(BattleLobbyData data)
        {
            var row = NewGo("MetaRow", transform, typeof(HorizontalLayoutGroup));
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth      = false;
            hlg.childControlHeight     = false;
            hlg.childAlignment         = TextAnchor.MiddleLeft;

            var rRt = (RectTransform)row.transform;
            rRt.anchorMin = new Vector2(0f, 0f);
            rRt.anchorMax = new Vector2(1f, 0f);
            rRt.pivot     = new Vector2(0f, 0f);
            rRt.offsetMin = new Vector2(AccentW + Pad, 17f);
            rRt.offsetMax = new Vector2(-(JoinW + Pad + 8f), 41f);

            string roundTxt = data.Rounds + (data.Rounds == 1 ? " ROUND" : " ROUNDS");
            // Show the real entry cost (= case price × rounds), which is exactly what
            // WaitingRoomScreen charges on join (EntryCost => WagerVP). Previously this
            // displayed the prize pot (WagerVP × players), e.g. 1000 for a 1-round
            // 500 VP 1V1, which did not match the amount actually charged.
            int entryCost = data.WagerVP;

            AddMeta(row.transform, RefPrimary,
                $"{data.CurrentPlayers}/{data.MaxPlayers}", RefForeground, 40f);

            AddMeta(row.transform, WithAlpha(RefMuted, 0.9f),
                roundTxt, RefMuted, 72f);

            AddMeta(row.transform, RefGold,
                entryCost.ToString("N0") + " VP", RefGold, 84f);
        }

        static void AddMeta(Transform parent, Color dotColor, string text, Color textColor, float labelW)
        {
            var dot = MakeAngled("Dot", parent, dotColor, 2f);
            ((RectTransform)dot.transform).sizeDelta = new Vector2(8f, 8f);
            dot.gameObject.AddComponent<LayoutElement>().minWidth = 8f;

            var lbl = MakeTmp(parent, "Lbl", text, 11f, FontStyles.Bold, textColor);
            lbl.alignment               = TextAlignmentOptions.MidlineLeft;
            lbl.rectTransform.sizeDelta = new Vector2(labelW, 20f);
            lbl.gameObject.AddComponent<LayoutElement>().minWidth = labelW;
        }

        void BuildJoinButton()
        {
            _joinBg = MakeAngled("JoinBtn", transform, _locked ? RefSecondary : RefPrimary, 6f, raycast: true);

            if (_locked)
            {
                var lockBorder = _joinBg.gameObject.AddComponent<Outline>();
                lockBorder.effectColor    = WithAlpha(RefMuted, 0.6f);
                lockBorder.effectDistance = new Vector2(1f, -1f);
            }
            else
            {
                var joinGlow = _joinBg.gameObject.AddComponent<Shadow>();
                joinGlow.effectColor = WithAlpha(RefPrimary, 0.75f);
                joinGlow.effectDistance = new Vector2(0f, -4f);
            }

            SetRect(_joinBg.rectTransform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-Pad, (FooterH - JoinH) * 0.5f), new Vector2(JoinW, JoinH));

            var joinBtn = _joinBg.gameObject.AddComponent<Button>();
            joinBtn.transition = Selectable.Transition.None;
            joinBtn.onClick.AddListener(OnJoinPressed);

            var lbl = MakeTmp(_joinBg.transform, "Lbl", _locked ? "Yetersiz VP" : "VIEW",
                _locked ? 9f : 14f, FontStyles.Bold, _locked ? RefMuted : RefWhite);
            lbl.alignment          = TextAlignmentOptions.Center;
            lbl.characterSpacing   = _locked ? 0f : 2f;
            lbl.enableWordWrapping  = false;
            lbl.overflowMode        = TextOverflowModes.Ellipsis;
            Stretch(lbl.rectTransform);
        }

        void OnJoinPressed()
        {
            if (!_locked) StartCoroutine(Flash());
            _onJoin?.Invoke(_data);
        }

        IEnumerator Flash()
        {
            if (_joinBg == null) yield break;
            _joinBg.color = WithAlpha(RefPrimary, 0.8f);
            yield return new WaitForSecondsRealtime(0.09f);
            if (_joinBg != null) _joinBg.color = RefPrimary;
        }

        void OnDisable() => StopAllCoroutines();

        // Online lobbies show the creator's display name; the bot fallback keeps its
        // original labels (HostName is "BOT" for those, so they fall through).
        static string LobbyTitle(BattleLobbyData d)
        {
            if (!string.IsNullOrEmpty(d.HostName) &&
                !string.Equals(d.HostName, "BOT", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(d.HostName, "YOU", StringComparison.OrdinalIgnoreCase))
                return d.HostName;

            return d.MaxPlayers switch
            {
                2 => "Basic Vandal Duel",
                3 => "Basic Vandal Triple Battle",
                _ => d.CaseName + " Battle",
            };
        }

        // Joined "Name xN" per selected case; falls back to the single-case summary.
        static string CaseLine(BattleLobbyData d)
        {
            string qtyHex = Hex(RefGold);
            if (d.CaseSelections != null && d.CaseSelections.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < d.CaseSelections.Count; i++)
                {
                    var s = d.CaseSelections[i];
                    if (i > 0) sb.Append("   ");
                    sb.Append(s.CaseName).Append("  <color=#").Append(qtyHex).Append("><b>x")
                      .Append(s.Quantity).Append("</b></color>");
                }
                return sb.ToString();
            }
            return $"{d.CaseName}  <color=#{qtyHex}><b>x{d.Rounds}</b></color>";
        }

        static string StatusText(BattleLobbyData d) => d.Status switch
        {
            LobbyStatus.Full => "FULL",
            LobbyStatus.Live => "LIVE",
            _                => "WAITING",
        };

        static string TypeText(BattleLobbyData d) => d.MaxPlayers switch
        {
            2 => "1V1",
            3 => "1V1V1",
            _ => d.MaxPlayers + "P",
        };

        static Color HexColor(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);
            return color;
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        static string Hex(Color c)
        {
            Color32 c32 = c;
            return c32.r.ToString("X2") + c32.g.ToString("X2") + c32.b.ToString("X2");
        }
    }
}