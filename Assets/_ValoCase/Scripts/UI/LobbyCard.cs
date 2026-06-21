using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;
using static ValoCase.UI.UIBuild;

namespace ValoCase.UI
{
    public sealed class LobbyCard : MonoBehaviour
    {
        public const float Height = 142f;
        public const float CasesA = 0f;
        public const float CostA = 0.645f;
        public const float PlayersA = 0.765f;
        public const float ActionA = 0.85f;
        public const float EndA = 0.985f;
        public const float CostCenterA = (CostA + PlayersA) * 0.5f;
        public const float PlayersCenterA = PlayersA + (ActionA - PlayersA) * 0.5f;

        public const int MaxCaseSlots = 5;
        const float CasesLeftPad = 22f;

        static readonly Color RefCard       = HexColor("#171823");
        static readonly Color RefAlt        = HexColor("#11131E");
        static readonly Color RefLine       = HexColor("#2C2038");
        static readonly Color RefPrimary    = HexColor("#C8253C");
        static readonly Color RefJoin       = HexColor("#267BC8");
        static readonly Color RefJoinDark   = HexColor("#16436F");
        static readonly Color RefForeground = HexColor("#FAFAFA");
        static readonly Color RefMuted      = HexColor("#A4A8B5");
        static readonly Color RefGold       = HexColor("#E6CF6F");
        static readonly Color RefGreen      = HexColor("#45DB74");

        BattleLobbyData         _data;
        Action<BattleLobbyData> _onJoin;
        AngledCutImage          _joinBg;
        bool                    _locked;

        public static LobbyCard Create(Transform parent, BattleLobbyData data,
            IReadOnlyList<Sprite> caseIcons, Action<BattleLobbyData> onJoin, bool locked)
        {
            var go   = NewGo("LobbyRow_" + data.LobbyId, parent);
            var card = go.AddComponent<LobbyCard>();

            var le = go.AddComponent<LayoutElement>();
            le.minHeight       = Height;
            le.preferredHeight = Height;

            card.Build(data, caseIcons, onJoin, locked);
            return card;
        }

        void Build(BattleLobbyData data, IReadOnlyList<Sprite> caseIcons,
            Action<BattleLobbyData> onJoin, bool locked)
        {
            _data       = data;
            _onJoin     = onJoin;
            _locked     = locked;

            var bg = MakeImage("Bg", transform, RefCard, raycast: true);
            Stretch(bg.rectTransform);

            var border = bg.gameObject.AddComponent<Outline>();
            border.effectColor    = RefLine;
            border.effectDistance = new Vector2(1f, -1f);

            var cardBtn = bg.gameObject.AddComponent<Button>();
            cardBtn.transition = Selectable.Transition.None;
            cardBtn.onClick.AddListener(OnJoinPressed);

            var casesCell = MakeCell("CasesCell", CasesA, CostA, Color.clear);
            var costCell = MakeCell("CostCell", CostA, PlayersA, Color.clear);
            var playersCell = MakeCell("PlayersCell", PlayersA, ActionA, Color.clear);
            var actionCell = MakeCell("ActionCell", ActionA, EndA, Color.clear);

            AddDivider(CostA);
            AddDivider(PlayersA);
            AddDivider(ActionA);

            BuildCasesCell(casesCell, data, caseIcons);
            BuildCostCell(costCell, data);
            BuildPlayersCell(playersCell, data);
            BuildJoinButton(actionCell);
        }

        RectTransform MakeCell(string name, float minX, float maxX, Color color)
        {
            var img = MakeImage(name, transform, color);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(minX, 0f);
            rt.anchorMax = new Vector2(maxX, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        void AddDivider(float x)
        {
            var line = MakeImage("Divider", transform, RefLine);
            var rt = line.rectTransform;
            rt.anchorMin = new Vector2(x, 0f);
            rt.anchorMax = new Vector2(x, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(1f, 0f);
        }

        void BuildCasesCell(RectTransform cell, BattleLobbyData data, IReadOnlyList<Sprite> caseIcons)
        {
            var content = NewGo("CasesContent", cell);
            var contentRt = (RectTransform)content.transform;
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = new Vector2(CasesLeftPad, 0f);
            contentRt.offsetMax = Vector2.zero;

            int count = Mathf.Clamp(CaseCount(data), 1, MaxCaseSlots);
            for (int i = 0; i < count; i++)
            {
                float min = i / (float)MaxCaseSlots;
                float max = (i + 1) / (float)MaxCaseSlots;
                var slot = MakeImage("CaseSlot_" + i, content.transform, Color.clear);
                var sRt = slot.rectTransform;
                sRt.anchorMin = new Vector2(min, 0f);
                sRt.anchorMax = new Vector2(max, 1f);
                sRt.offsetMin = new Vector2(5f, 10f);
                sRt.offsetMax = new Vector2(-5f, -10f);

                var box = MakeImage("CaseIconBg", slot.transform, HexColor("#202333"));
                StretchInset(box.rectTransform, 0f);

                var outline = box.gameObject.AddComponent<Outline>();
                outline.effectColor    = ColorPalette.WithAlpha(RefGreen, 0.65f);
                outline.effectDistance = new Vector2(0f, -2f);

                Sprite icon = caseIcons != null && i < caseIcons.Count ? caseIcons[i] : null;
                if (icon != null)
                {
                    var img = MakeImage("CaseIcon", box.transform, Color.white);
                    img.sprite = icon;
                    img.preserveAspect = true;
                    StretchInset(img.rectTransform, 5f);
                }

                var qtyBg = MakeAngled("QtyBg", box.transform, HexColor("#090B13"), 4f);
                SetRect(qtyBg.rectTransform,
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-4f, -4f), new Vector2(34f, 21f));

                var qty = MakeTmp(qtyBg.transform, "Qty", "x" + CaseQuantity(data, i),
                    14f, FontStyles.Bold, RefGold);
                qty.alignment = TextAlignmentOptions.Center;
                Stretch(qty.rectTransform);
            }
        }

        void BuildCostCell(RectTransform cell, BattleLobbyData data)
        {
            var center = MakeCenterBox(cell, "CostCenter", 0.5f, 76f, 48f);

            var cost = MakeTmp(center, "Cost", data.WagerVP.ToString("N0"),
                15f, FontStyles.Bold, RefForeground);
            cost.alignment = TextAlignmentOptions.Center;
            SetRect(cost.rectTransform,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(0f, 28f));
        }

        void BuildPlayersCell(RectTransform cell, BattleLobbyData data)
        {
            var center = MakeCenterBox(cell, "PlayersCenter", 0.5f, 54f, 48f);
            var lbl = MakeTmp(center, "Players", $"{data.CurrentPlayers}/{data.MaxPlayers}",
                19f, FontStyles.Bold, RefForeground);
            lbl.alignment = TextAlignmentOptions.Center;
            Stretch(lbl.rectTransform);
        }

        void BuildJoinButton(RectTransform cell)
        {
            bool full = _data.ComputeStatus() != LobbyStatus.Waiting;
            Color bg = _locked ? HexColor("#151620") : (full ? RefPrimary : RefJoin);
            _joinBg = MakeAngled("JoinBtn", cell, bg, 12f, raycast: true);
            _joinBg.rectTransform.anchorMin = new Vector2(0.10f, 0.5f);
            _joinBg.rectTransform.anchorMax = new Vector2(0.82f, 0.5f);
            _joinBg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _joinBg.rectTransform.anchoredPosition = Vector2.zero;
            _joinBg.rectTransform.sizeDelta = new Vector2(0f, 54f);

            var outline = _joinBg.gameObject.AddComponent<Outline>();
            outline.effectColor    = _locked ? ColorPalette.WithAlpha(RefMuted, 0.45f) : ColorPalette.WithAlpha(bg, 0.85f);
            outline.effectDistance = new Vector2(1f, -1f);

            if (!_locked)
            {
                var shadow = _joinBg.gameObject.AddComponent<Shadow>();
                shadow.effectColor = ColorPalette.WithAlpha(full ? RefPrimary : RefJoinDark, 0.8f);
                shadow.effectDistance = new Vector2(0f, -3f);
            }

            var btn = _joinBg.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnJoinPressed);

            string text = _locked ? "LOCKED" : (full ? "VIEW" : "JOIN");
            var lbl = MakeTmp(_joinBg.transform, "Lbl", text,
                17f, FontStyles.Bold, _locked ? RefMuted : RefForeground);
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.characterSpacing = 1f;
            Stretch(lbl.rectTransform);
        }

        Transform MakeCenterBox(RectTransform parent, string name, float centerX, float w, float h)
        {
            var go = NewGo(name, parent);
            var rt = (RectTransform)go.transform;
            SetRect(rt,
                new Vector2(centerX, 0.5f), new Vector2(centerX, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(w, h));
            return go.transform;
        }

        void OnJoinPressed()
        {
            if (!_locked) StartCoroutine(Flash());
            _onJoin?.Invoke(_data);
        }

        IEnumerator Flash()
        {
            if (_joinBg == null) yield break;
            Color c = _joinBg.color;
            _joinBg.color = ColorPalette.WithAlpha(c, 0.75f);
            yield return new WaitForSecondsRealtime(0.09f);
            if (_joinBg != null) _joinBg.color = c;
        }

        void OnDisable() => StopAllCoroutines();

        static int CaseCount(BattleLobbyData d)
        {
            return d.CaseSelections != null && d.CaseSelections.Count > 0
                ? d.CaseSelections.Count
                : 1;
        }

        static int CaseQuantity(BattleLobbyData d, int index)
        {
            if (d.CaseSelections != null && index < d.CaseSelections.Count)
                return Mathf.Max(1, d.CaseSelections[index].Quantity);
            return Mathf.Max(1, d.Rounds);
        }

        static void StretchInset(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        static Color HexColor(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);
            return color;
        }
    }
}
