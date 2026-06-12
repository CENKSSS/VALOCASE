using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI
{
    /// <summary>
    /// Lightweight runtime UGUI factory shared by the inventory selling popups.
    /// Builds plain, sharp-cornered panels in the ValoCase dark / red palette so no
    /// prefab wiring is required. Kept internal to the selling flow on purpose.
    /// </summary>
    static class SellUIFactory
    {
        public static readonly Color Dim    = new(0f, 0f, 0f, 0.82f);
        public static readonly Color Card   = new(0.082f, 0.090f, 0.110f, 1f); // #15171C
        public static readonly Color Red    = new(1f, 0.274f, 0.333f, 1f);     // #FF4655
        public static readonly Color Grey   = new(0.157f, 0.172f, 0.204f, 1f);
        public static readonly Color Field  = new(0.047f, 0.051f, 0.063f, 1f);
        public static readonly Color Light  = new(0.925f, 0.906f, 0.863f, 1f); // #ECE8DC
        public static readonly Color Muted  = new(0.560f, 0.588f, 0.643f, 1f);

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        // Stretches horizontally, pins to the card's top edge spanning [top, top+height] downward.
        public static void PinTop(RectTransform rt, float top, float height, float margin)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(margin, -(top + height));
            rt.offsetMax = new Vector2(-margin, -top);
        }

        public static Image Image(string name, Transform parent, Color color)
        {
            var rt  = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size,
            Color color, FontStyles style = FontStyles.Normal,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var rt = Rect(name, parent);
            var t  = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text         = text;
            t.fontSize     = size;
            t.color        = color;
            t.fontStyle    = style;
            t.alignment    = align;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            return t;
        }

        public static Button Button(Transform parent, string name, string label, Color bg, Color textColor,
            float fontSize, out TextMeshProUGUI labelText)
        {
            var img = Image(name, parent, bg);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor     = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor    = Color.white;
            colors.disabledColor    = new Color(0.5f, 0.5f, 0.5f, 1f);
            colors.fadeDuration     = 0.08f;
            btn.colors = colors;

            labelText = Text(name + "Label", img.transform, label, fontSize, textColor, FontStyles.Bold);
            Stretch(labelText.rectTransform, 16, 16, 0, 0);
            return btn;
        }
    }

    /// <summary>
    /// Owns the inventory selling popup chain, built entirely at runtime:
    ///   SELL → SELL OPTIONS → (SELL ALL → confirm) | (SELL BELOW VALUE → input → confirm)
    /// The host screen supplies the two sell actions; this class only handles UX.
    /// </summary>
    public sealed class SellFlow
    {
        readonly Action      _onSellAll;
        readonly Action<int> _onSellBelow;

        GameObject _overlay;
        GameObject _optionsCard;
        GameObject _confirmCard;
        GameObject _inputCard;

        TextMeshProUGUI _confirmMessage;
        TMP_InputField  _input;
        Action          _confirmAction;

        public SellFlow(Transform canvas, Action onSellAll, Action<int> onSellBelow)
        {
            _onSellAll   = onSellAll;
            _onSellBelow = onSellBelow;
            Build(canvas);
        }

        // ── Public entry points ────────────────────────────────────────────────
        public void OpenOptions()
        {
            _overlay.transform.SetAsLastSibling();
            _overlay.SetActive(true);
            _optionsCard.SetActive(true);
            _confirmCard.SetActive(false);
            _inputCard.SetActive(false);
        }

        public void CloseAll()
        {
            if (_overlay != null) _overlay.SetActive(false);
        }

        // ── Construction ───────────────────────────────────────────────────────
        void Build(Transform canvas)
        {
            _overlay = SellUIFactory.Image("SellOverlay", canvas, SellUIFactory.Dim).gameObject;
            SellUIFactory.Stretch((RectTransform)_overlay.transform);
            var dimBtn = _overlay.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(CloseAll);

            BuildOptionsCard();
            BuildConfirmCard();
            BuildInputCard();

            _overlay.SetActive(false);
        }

        RectTransform NewCard(string name, float width, float height)
        {
            var img = SellUIFactory.Image(name, _overlay.transform, SellUIFactory.Card);
            var rt  = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = Vector2.zero;

            // Top accent strip — Valorant-style sharp red edge.
            var accent = SellUIFactory.Image("Accent", rt, SellUIFactory.Red);
            SellUIFactory.PinTop(accent.rectTransform, 0, 6, 0);
            return rt;
        }

        // Two bottom-row buttons split at the card centre (left = secondary, right = primary).
        Button RowButton(RectTransform card, string name, string label, Color bg, Color textColor,
            bool rightSide, float bottom, float height, float margin, float gap, out TextMeshProUGUI lbl)
        {
            var btn = SellUIFactory.Button(card, name, label, bg, textColor, 38, out lbl);
            var rt  = (RectTransform)btn.transform;
            rt.anchorMin = new Vector2(rightSide ? 0.5f : 0f, 0f);
            rt.anchorMax = new Vector2(rightSide ? 1f : 0.5f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            float lm = rightSide ? gap * 0.5f : margin;
            float rm = rightSide ? margin : gap * 0.5f;
            rt.offsetMin = new Vector2(lm, bottom);
            rt.offsetMax = new Vector2(-rm, bottom + height);
            return rt.GetComponent<Button>();
        }

        void BuildOptionsCard()
        {
            // Compact, centered card. Closes by tapping the dimmed background (no Cancel).
            var card = NewCard("SellOptionsCard", 720f, 360f);
            _optionsCard = card.gameObject;

            var title = SellUIFactory.Text("Title", card, "SELL OPTIONS", 42, SellUIFactory.Red, FontStyles.Bold);
            SellUIFactory.PinTop(title.rectTransform, 34, 54, 48);

            var sub = SellUIFactory.Text("Subtitle", card, "Choose how you'd like to sell your skins.",
                26, SellUIFactory.Muted);
            SellUIFactory.PinTop(sub.rectTransform, 96, 38, 48);

            // SELL ALL (left, red) and SELL BELOW VALUE (right, dark) share one row, equal width.
            var sellAllBtn = RowButton(card, "SellAllButton", "SELL ALL",
                SellUIFactory.Red, Color.white, false, 50, 150, 48, 28, out var allLbl);
            allLbl.fontSize = 34;
            sellAllBtn.onClick.AddListener(() =>
                ShowConfirm("Are you sure you want to sell ALL skins in your inventory?", _onSellAll));

            var belowBtn = RowButton(card, "SellBelowButton", "SELL BELOW VALUE",
                SellUIFactory.Grey, SellUIFactory.Light, true, 50, 150, 48, 28, out var belowLbl);
            belowLbl.enableAutoSizing = true;
            belowLbl.fontSizeMin = 22;
            belowLbl.fontSizeMax = 30;
            belowBtn.onClick.AddListener(ShowInput);
        }

        void BuildConfirmCard()
        {
            var card = NewCard("SellConfirmCard", 840f, 480f);
            _confirmCard = card.gameObject;

            var title = SellUIFactory.Text("Title", card, "CONFIRM", 50, SellUIFactory.Red, FontStyles.Bold);
            SellUIFactory.PinTop(title.rectTransform, 44, 64, 60);

            _confirmMessage = SellUIFactory.Text("Message", card, "", 38, SellUIFactory.Light);
            SellUIFactory.PinTop(_confirmMessage.rectTransform, 130, 160, 70);

            var cancel = RowButton(card, "ConfirmCancel", "CANCEL", SellUIFactory.Grey, SellUIFactory.Light,
                false, 50, 120, 60, 30, out _);
            cancel.onClick.AddListener(CloseAll);

            var confirm = RowButton(card, "ConfirmYes", "CONFIRM", SellUIFactory.Red, Color.white,
                true, 50, 120, 60, 30, out _);
            confirm.onClick.AddListener(() =>
            {
                var action = _confirmAction;
                CloseAll();
                action?.Invoke();
            });
        }

        void BuildInputCard()
        {
            var card = NewCard("SellBelowCard", 840f, 580f);
            _inputCard = card.gameObject;

            var title = SellUIFactory.Text("Title", card, "SELL BELOW VALUE", 48, SellUIFactory.Red, FontStyles.Bold);
            SellUIFactory.PinTop(title.rectTransform, 44, 64, 60);

            var label = SellUIFactory.Text("FieldLabel", card, "Sell every skin valued at or below:",
                32, SellUIFactory.Muted);
            SellUIFactory.PinTop(label.rectTransform, 140, 44, 60);

            _input = BuildInputField(card);
            SellUIFactory.PinTop((RectTransform)_input.transform, 200, 130, 60);

            var cancel = RowButton(card, "BelowCancel", "CANCEL", SellUIFactory.Grey, SellUIFactory.Light,
                false, 50, 120, 60, 30, out _);
            cancel.onClick.AddListener(CloseAll);

            var sell = RowButton(card, "BelowSell", "SELL", SellUIFactory.Red, Color.white,
                true, 50, 120, 60, 30, out _);
            sell.onClick.AddListener(OnBelowSellPressed);
        }

        TMP_InputField BuildInputField(Transform parent)
        {
            var img   = SellUIFactory.Image("ThresholdField", parent, SellUIFactory.Field);
            var input = img.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = img;

            var viewport = SellUIFactory.Rect("TextArea", img.transform);
            SellUIFactory.Stretch(viewport, 28, 28, 14, 14);
            viewport.gameObject.AddComponent<RectMask2D>();

            var placeholder = SellUIFactory.Text("Placeholder", viewport, "e.g. 2000", 44,
                SellUIFactory.Muted, FontStyles.Italic, TextAlignmentOptions.Left);
            placeholder.enableWordWrapping = false;
            SellUIFactory.Stretch(placeholder.rectTransform);

            var text = SellUIFactory.Text("Text", viewport, "", 44,
                SellUIFactory.Light, FontStyles.Bold, TextAlignmentOptions.Left);
            text.enableWordWrapping = false;
            SellUIFactory.Stretch(text.rectTransform);

            input.textViewport   = viewport;
            input.textComponent  = text;
            input.placeholder    = placeholder;
            input.contentType    = TMP_InputField.ContentType.IntegerNumber;
            input.characterLimit = 9;
            input.lineType       = TMP_InputField.LineType.SingleLine;
            return input;
        }

        // ── State transitions ──────────────────────────────────────────────────
        void ShowInput()
        {
            if (_input != null) _input.text = string.Empty;
            _optionsCard.SetActive(false);
            _confirmCard.SetActive(false);
            _inputCard.SetActive(true);
        }

        void OnBelowSellPressed()
        {
            if (_input == null) return;
            if (!int.TryParse(_input.text, out var threshold) || threshold <= 0) return;

            ShowConfirm($"Sell every skin valued at {threshold:N0} VP or below?",
                () => _onSellBelow?.Invoke(threshold));
        }

        void ShowConfirm(string message, Action onConfirm)
        {
            _confirmAction = onConfirm;
            if (_confirmMessage != null) _confirmMessage.text = message;
            _optionsCard.SetActive(false);
            _inputCard.SetActive(false);
            _confirmCard.SetActive(true);
        }
    }
}
