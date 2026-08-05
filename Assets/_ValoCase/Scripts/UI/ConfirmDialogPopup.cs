using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI
{
    /// <summary>
    /// A minimal yes/no confirmation, built at runtime with no prefab dependency like the
    /// other popups in this folder. Exactly one of the callbacks fires, exactly once:
    /// confirm on the accent button, cancel on the grey button or the backdrop. The
    /// dialog destroys itself before invoking either, so a callback that opens another
    /// popup never finds this one still on top.
    /// </summary>
    public sealed class ConfirmDialogPopup : MonoBehaviour
    {
        static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.85f);
        static readonly Color CardBg   = new Color(0.051f, 0.067f, 0.090f, 1f);
        static readonly Color BtnIdle  = new Color(0.180f, 0.204f, 0.243f, 1f);
        static readonly Color Accent   = new Color(1f, 0.275f, 0.333f, 1f);
        static readonly Color TextMain = new Color(0.961f, 0.961f, 0.961f, 1f);
        static readonly Color TextDim  = new Color(0.541f, 0.569f, 0.651f, 1f);
        static readonly Color DarkText = new Color(0.043f, 0.055f, 0.082f, 1f);

        static ConfirmDialogPopup _instance;

        string _message;
        string _confirmLabel;
        string _cancelLabel;
        Action _onConfirm;
        Action _onCancel;

        public static void Show(Transform parent, string message,
                                string confirmLabel, string cancelLabel,
                                Action onConfirm, Action onCancel)
        {
            if (_instance != null || parent == null) return;

            var go = new GameObject("ConfirmDialogPopup");
            _instance = go.AddComponent<ConfirmDialogPopup>();
            _instance._message      = message;
            _instance._confirmLabel = confirmLabel;
            _instance._cancelLabel  = cancelLabel;
            _instance._onConfirm    = onConfirm;
            _instance._onCancel     = onCancel;
            go.transform.SetParent(parent, false);
            _instance.BuildUi();
            go.transform.SetAsLastSibling();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // Null both callbacks before invoking, so a click racing Destroy cannot fire the
        // other one as well.
        void Finish(Action chosen)
        {
            _onConfirm = null;
            _onCancel  = null;
            Destroy(gameObject);
            chosen?.Invoke();
        }

        void BuildUi()
        {
            var rootRt = gameObject.GetComponent<RectTransform>();
            if (rootRt == null) rootRt = gameObject.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;

            var dim = gameObject.AddComponent<Image>();
            dim.color         = Backdrop;
            dim.raycastTarget = true;
            var dimBtn = gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(() => Finish(_onCancel));

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Outline));
            card.transform.SetParent(transform, false);
            var cardRt = (RectTransform)card.transform;
            cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(620f, 340f);
            card.GetComponent<Image>().color = CardBg;
            var cardOl = card.GetComponent<Outline>();
            cardOl.effectColor    = new Color(Accent.r, Accent.g, Accent.b, 0.85f);
            cardOl.effectDistance = new Vector2(2f, -2f);

            var body = UIBuild.MakeTmp(card.transform, "Message", _message, 24f, FontStyles.Bold, TextMain);
            body.alignment          = TextAlignmentOptions.Center;
            body.enableWordWrapping = true;
            var bRt = body.rectTransform;
            bRt.anchorMin        = new Vector2(0f, 1f);
            bRt.anchorMax        = new Vector2(1f, 1f);
            bRt.pivot            = new Vector2(0.5f, 1f);
            bRt.anchoredPosition = new Vector2(0f, -44f);
            bRt.sizeDelta        = new Vector2(-72f, 150f);

            BuildButton(card.transform, "ConfirmBtn", _confirmLabel, Accent, DarkText,
                        new Vector2(-150f, 44f), () => Finish(_onConfirm));
            BuildButton(card.transform, "CancelBtn", _cancelLabel, BtnIdle, TextDim,
                        new Vector2(150f, 44f), () => Finish(_onCancel));
        }

        void BuildButton(Transform parent, string name, string label, Color bg, Color fg,
                         Vector2 anchoredPos, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin        = new Vector2(0.5f, 0f);
            rt.anchorMax        = new Vector2(0.5f, 0f);
            rt.pivot            = new Vector2(0.5f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = new Vector2(260f, 80f);
            go.GetComponent<Image>().color = bg;

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            UIBuild.WireButtonClick(btn);
            btn.onClick.AddListener(() => onClick());

            var lbl = UIBuild.MakeTmp(go.transform, "Lbl", label, 24f, FontStyles.Bold, fg);
            lbl.alignment        = TextAlignmentOptions.Center;
            lbl.characterSpacing = 2f;
            UIBuild.Stretch(lbl.rectTransform);
        }
    }
}
