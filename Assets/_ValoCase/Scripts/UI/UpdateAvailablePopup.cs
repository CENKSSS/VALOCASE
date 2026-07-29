using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI
{
    /// <summary>
    /// "New update available" notice. Same runtime-built, prefab-free shape as
    /// <see cref="ServerConnectionPopup"/> so both notices read as one family.
    ///
    /// The version to compare against comes from the server (see BackendApiClient's
    /// WalletResponse.latestVersion). While the server omits it nothing is ever shown,
    /// so shipping this ahead of the backend field is harmless.
    ///
    /// UPDATE opens the store page for this exact build's package id; LATER dismisses,
    /// because a player without a connection strong enough to update must still be able
    /// to keep playing. Making the update mandatory is a one-line change: drop the LATER
    /// button and the Hide() wiring.
    /// </summary>
    public sealed class UpdateAvailablePopup : MonoBehaviour
    {
        static UpdateAvailablePopup Instance;
        static bool _shownThisSession;

        TextMeshProUGUI _body;
        string _storeUrl;

        void OnDestroy() { if (Instance == this) Instance = null; }

        bool IsShowing => gameObject.activeSelf;

        /// <summary>
        /// Shows the notice when <paramref name="latestVersion"/> is newer than the running
        /// build. Safe to call on every boot: it is a no-op when the server sent nothing,
        /// when the strings do not parse, or when this build is already current.
        /// Shown at most once per session so it cannot interrupt play repeatedly.
        /// </summary>
        public static void TryShow(string latestVersion)
        {
            if (_shownThisSession) return;
            if (string.IsNullOrWhiteSpace(latestVersion)) return;
            if (!IsNewer(latestVersion, Application.version)) return;

            var popup = Instance != null ? Instance : RuntimeBuild();
            if (popup == null) return;

            _shownThisSession = true;
            popup.Show(latestVersion);
        }

        void Show(string latestVersion)
        {
            if (IsShowing) return;

            _body.text = $"Version {latestVersion} is available.\nYou are on {Application.version}.";
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Debug.Log($"[Update] Notice shown — installed={Application.version} latest={latestVersion}");
        }

        void Hide() => gameObject.SetActive(false);

        void OpenStore()
        {
            Debug.Log("[Update] Opening store page — " + _storeUrl);
            Application.OpenURL(_storeUrl);
        }

        // ── Version comparison ────────────────────────────────────────────────

        /// <summary>
        /// True when <paramref name="candidate"/> is a strictly higher version than
        /// <paramref name="current"/>. Compares dot-separated numbers component by
        /// component, so 1.0.15 correctly beats 1.0.5 — a plain string compare does not.
        /// Returns false if either side has no parsable numbers, which keeps a malformed
        /// server value from nagging every player.
        /// </summary>
        public static bool IsNewer(string candidate, string current)
        {
            if (!TryParse(candidate, out var a) || !TryParse(current, out var b)) return false;

            int len = Mathf.Max(a.Length, b.Length);
            for (int i = 0; i < len; i++)
            {
                int ai = i < a.Length ? a[i] : 0;   // "1.1" is treated as "1.1.0"
                int bi = i < b.Length ? b[i] : 0;
                if (ai != bi) return ai > bi;
            }
            return false;   // identical
        }

        static bool TryParse(string version, out int[] parts)
        {
            parts = null;
            if (string.IsNullOrWhiteSpace(version)) return false;

            var chunks = version.Trim().Split('.');
            var result = new int[chunks.Length];
            for (int i = 0; i < chunks.Length; i++)
            {
                // Tolerates suffixes like "1.0.15b" by reading the leading digits only.
                int digits = 0;
                while (digits < chunks[i].Length && char.IsDigit(chunks[i][digits])) digits++;
                if (digits == 0) return false;
                if (!int.TryParse(chunks[i].Substring(0, digits), out result[i])) return false;
            }

            parts = result;
            return true;
        }

        // ── Runtime construction (mirrors ServerConnectionPopup) ──────────────

        static UpdateAvailablePopup RuntimeBuild()
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
                Debug.LogError("[UpdateAvailablePopup] No Canvas in scene — cannot create popup");
                return null;
            }

            var accent   = new Color(1f, 0.275f, 0.333f, 1f);
            var textMain = new Color(0.961f, 0.961f, 0.961f, 1f);
            var textDim  = new Color(0.541f, 0.569f, 0.651f, 1f);
            var cardBg   = new Color(0.051f, 0.067f, 0.090f, 1f);
            var darkText = new Color(0.043f, 0.055f, 0.082f, 1f);

            var rootGo = new GameObject("UpdateAvailablePopup", typeof(RectTransform), typeof(Image));
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
            cardRt.sizeDelta = new Vector2(420f, 290f);
            cardGo.GetComponent<Image>().color = cardBg;
            var cardOl = cardGo.GetComponent<Outline>();
            cardOl.effectColor    = new Color(accent.r, accent.g, accent.b, 0.85f);
            cardOl.effectDistance = new Vector2(2f, -2f);

            var titleLbl = UIBuild.MakeTmp(cardGo.transform, "Title", "NEW UPDATE AVAILABLE",
                                           22f, FontStyles.Bold, accent);
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
            bRt.sizeDelta        = new Vector2(-48f, 70f);

            var updateGo = new GameObject("UpdateBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            updateGo.transform.SetParent(cardGo.transform, false);
            var uRt = (RectTransform)updateGo.transform;
            uRt.anchorMin        = new Vector2(0.5f, 0f);
            uRt.anchorMax        = new Vector2(0.5f, 0f);
            uRt.pivot            = new Vector2(0.5f, 0f);
            uRt.anchoredPosition = new Vector2(0f, 74f);
            uRt.sizeDelta        = new Vector2(240f, 52f);
            var uImg = updateGo.GetComponent<Image>();
            uImg.color = accent;
            var uBtn = updateGo.GetComponent<Button>();
            uBtn.transition    = Selectable.Transition.None;
            uBtn.targetGraphic = uImg;

            var uLbl = UIBuild.MakeTmp(updateGo.transform, "Lbl", "UPDATE", 17f, FontStyles.Bold, darkText);
            uLbl.alignment = TextAlignmentOptions.Center;
            UIBuild.Stretch(uLbl.rectTransform);

            var laterGo = new GameObject("LaterBtn", typeof(RectTransform), typeof(Image), typeof(Button));
            laterGo.transform.SetParent(cardGo.transform, false);
            var lRt = (RectTransform)laterGo.transform;
            lRt.anchorMin        = new Vector2(0.5f, 0f);
            lRt.anchorMax        = new Vector2(0.5f, 0f);
            lRt.pivot            = new Vector2(0.5f, 0f);
            lRt.anchoredPosition = new Vector2(0f, 26f);
            lRt.sizeDelta        = new Vector2(240f, 40f);
            var lImg = laterGo.GetComponent<Image>();
            lImg.color = new Color(0f, 0f, 0f, 0f);   // text-only, no filled slab
            var lBtn = laterGo.GetComponent<Button>();
            lBtn.transition    = Selectable.Transition.None;
            lBtn.targetGraphic = lImg;

            var lLbl = UIBuild.MakeTmp(laterGo.transform, "Lbl", "LATER", 15f, FontStyles.Normal, textDim);
            lLbl.alignment = TextAlignmentOptions.Center;
            UIBuild.Stretch(lLbl.rectTransform);

            var popup = rootGo.AddComponent<UpdateAvailablePopup>();
            popup._body = bodyLbl;
            // Built from the running app's own id, so a renamed package cannot send
            // players to someone else's store listing.
            popup._storeUrl = "https://play.google.com/store/apps/details?id=" + Application.identifier;

            uBtn.onClick.AddListener(popup.OpenStore);
            lBtn.onClick.AddListener(popup.Hide);
            UIBuild.WireButtonClick(uBtn);
            UIBuild.WireButtonClick(lBtn);

            Instance = popup;
            rootGo.SetActive(false);
            return popup;
        }
    }
}
