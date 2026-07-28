using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;

namespace ValoCase.UI
{
    /// <summary>
    /// Global connectivity / server-error popup. The shared API layer classifies every
    /// failed request and raises GameEvents.OnConnectivityLost (no reachability) or
    /// GameEvents.OnServerError (5xx, timeout, connection failure, unparseable response);
    /// the bootstrap below wires those straight to Show(), so this never depends on any
    /// specific screen. Built at runtime with zero prefab dependency, same pattern as
    /// SkinWinPopup. Only one popup is ever visible; a later failure can show it again.
    /// </summary>
    public sealed class ServerConnectionPopup : MonoBehaviour
    {
        public enum Kind { NoInternet, ServerError }

        static ServerConnectionPopup Instance;

        TextMeshProUGUI _title;
        TextMeshProUGUI _body;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            GameEvents.OnConnectivityLost += () => EnsureExists()?.Show(Kind.NoInternet);
            GameEvents.OnServerError     += () => EnsureExists()?.Show(Kind.ServerError);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        static ServerConnectionPopup EnsureExists() => Instance != null ? Instance : RuntimeBuild();

        bool IsShowing => gameObject.activeSelf;

        public void Show(Kind kind)
        {
            if (IsShowing) return;

            if (kind == Kind.NoInternet)
            {
                _title.text = "NO INTERNET";
                _body.text  = "Please check your internet connection and try again.";
            }
            else
            {
                _title.text = "SERVER ERROR";
                _body.text  = "The server is temporarily unavailable. Please try again later.";
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        void Hide() => gameObject.SetActive(false);

        static ServerConnectionPopup RuntimeBuild()
        {
            Transform parent = null;
            var safeArea = GameObject.Find("SafeArea");
            if (safeArea != null) parent = safeArea.transform;
            else
            {
                Canvas best = null;
                int bestOrder = int.MinValue;
                foreach (var c in Object.FindObjectsOfType<Canvas>())
                    if (c.isRootCanvas && c.sortingOrder > bestOrder) { bestOrder = c.sortingOrder; best = c; }
                if (best != null) parent = best.transform;
            }
            if (parent == null)
            {
                Debug.LogError("[ServerConnectionPopup] No Canvas in scene — cannot create popup");
                return null;
            }

            var accent   = new Color(1f, 0.275f, 0.333f, 1f);
            var textMain = new Color(0.961f, 0.961f, 0.961f, 1f);
            var cardBg   = new Color(0.051f, 0.067f, 0.090f, 1f);
            var darkText = new Color(0.043f, 0.055f, 0.082f, 1f);

            var rootGo = new GameObject("ServerConnectionPopup", typeof(RectTransform), typeof(Image));
            rootGo.transform.SetParent(parent, false);
            var rootRt = (RectTransform)rootGo.transform;
            rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero; rootRt.offsetMax = Vector2.zero;
            var rootImg = rootGo.GetComponent<Image>();
            rootImg.color         = new Color(0f, 0f, 0f, 0.82f);
            rootImg.raycastTarget = true;

            var cardGo = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Outline));
            cardGo.transform.SetParent(rootGo.transform, false);
            var cardRt = (RectTransform)cardGo.transform;
            cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot     = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(420f, 260f);
            cardGo.GetComponent<Image>().color = cardBg;
            var cardOl = cardGo.GetComponent<Outline>();
            cardOl.effectColor    = new Color(accent.r, accent.g, accent.b, 0.85f);
            cardOl.effectDistance = new Vector2(2f, -2f);

            var titleLbl = UIBuild.MakeTmp(cardGo.transform, "Title", "SERVER ERROR", 22f, FontStyles.Bold, accent);
            titleLbl.alignment = TextAlignmentOptions.Center;
            var tRt = titleLbl.rectTransform;
            tRt.anchorMin        = new Vector2(0f, 1f);
            tRt.anchorMax        = new Vector2(1f, 1f);
            tRt.pivot            = new Vector2(0.5f, 1f);
            tRt.anchoredPosition = new Vector2(0f, -32f);
            tRt.sizeDelta        = new Vector2(-32f, 32f);

            var bodyLbl = UIBuild.MakeTmp(cardGo.transform, "Body", string.Empty, 16f, FontStyles.Normal, textMain);
            bodyLbl.alignment          = TextAlignmentOptions.Center;
            bodyLbl.enableWordWrapping = true;
            var bRt = bodyLbl.rectTransform;
            bRt.anchorMin        = new Vector2(0f, 1f);
            bRt.anchorMax        = new Vector2(1f, 1f);
            bRt.pivot            = new Vector2(0.5f, 1f);
            bRt.anchoredPosition = new Vector2(0f, -78f);
            bRt.sizeDelta        = new Vector2(-48f, 56f);

            var btnGo = new GameObject("OkBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(cardGo.transform, false);
            var btnRt = (RectTransform)btnGo.transform;
            btnRt.anchorMin        = new Vector2(0.5f, 0f);
            btnRt.anchorMax        = new Vector2(0.5f, 0f);
            btnRt.pivot            = new Vector2(0.5f, 0f);
            btnRt.anchoredPosition = new Vector2(0f, 30f);
            btnRt.sizeDelta        = new Vector2(200f, 52f);
            var btnImg = btnGo.GetComponent<Image>();
            btnImg.color = accent;
            var btn = btnGo.GetComponent<Button>();
            btn.transition    = Selectable.Transition.None;
            btn.targetGraphic = btnImg;

            var btnLbl = UIBuild.MakeTmp(btnGo.transform, "Lbl", "OK", 17f, FontStyles.Bold, darkText);
            btnLbl.alignment = TextAlignmentOptions.Center;
            UIBuild.Stretch(btnLbl.rectTransform);

            var popup = rootGo.AddComponent<ServerConnectionPopup>();
            popup._title = titleLbl;
            popup._body  = bodyLbl;
            btn.onClick.AddListener(popup.Hide);
            UIBuild.WireButtonClick(btn);

            Instance = popup;
            rootGo.SetActive(false);
            return popup;
        }
    }
}
