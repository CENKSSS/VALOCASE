using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.CaseOpening;
using ValoCase.Core;
using ValoCase.Data;

namespace ValoCase.UI.Screens
{
    public sealed class CaseOpeningScreen : UIScreenBase
    {
        [Header("Navigation")]
        [SerializeField] UINavigator navigator;
        [SerializeField] Button backButton;
        [SerializeField] TextMeshProUGUI walletLabel;

        [Header("Case Selector")]
        [SerializeField] Transform caseListRoot;
        [SerializeField] CaseListItemView caseItemPrefab;

        [Header("Case Display Panel")]
        [SerializeField] GameObject caseDisplayPanel;
        [SerializeField] Image caseIconDisplay;
        [SerializeField] Image caseThemeBg;
        [SerializeField] TextMeshProUGUI selectedCaseLabel;
        [SerializeField] TextMeshProUGUI priceLabel;
        [SerializeField] Button openButton;

        [Header("Drop List")]
        [SerializeField] Transform dropListRoot;
        [SerializeField] DropItemView dropItemPrefab;

        [Header("Spin Overlay")]
        [SerializeField] GameObject spinOverlay;
        [SerializeField] CaseOpeningFlowController flow;
        [SerializeField] Button skipButton;

        // Set by ShopScreen before navigating here to pre-select a specific case.
        public static string PendingCaseId;

        readonly List<CaseListItemView> _caseItems = new();
        readonly List<DropItemView> _dropItems = new();
        CaseDefinitionSO _selected;
        bool _buttonReady;
        bool _showingResult;
        Coroutine _spinWatchdog;
        bool _backBtnCreated;
        GameObject _runtimeBackBtn;

        // ── Mobile UI redesign (revertible) ──────────────────────────────────
        bool _caseOpeningUiStyled;
        bool _heroMovedUp;
        GameObject _spinFocusFrame;

        // ── Visual-only neon pulse / burst (no gameplay logic) ───────────────
        Image      _spinOverlayBg;
        Coroutine  _spinPulseCoroutine;

        // ── Premium dark decoration protection list ──────────────────────────────
        readonly List<Image> _decorImages = new();
        bool _decorBuilt;

        // ── Lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
            Debug.Log("[OPEN_BTN_DEBUG] Awake");
            Debug.Log("[OPEN_BTN_DEBUG] openButton null=" + (openButton == null));
            Debug.Log("[OPEN_BTN_DEBUG] flow null=" + (flow == null));
            Debug.Log("[OPEN_BTN_DEBUG] navigator null=" + (navigator == null));

            if (backButton != null) backButton.onClick.AddListener(OnBack);
            if (openButton != null)
            {
                openButton.onClick.AddListener(OpenSelected);
                Debug.Log("[OPEN_BTN_DEBUG] listener added to openButton");
                // Separate RAW listener — proves whether the click reaches the button at all.
                openButton.onClick.AddListener(() => Debug.Log("[OPEN_BTN_DEBUG] RAW BUTTON CLICK RECEIVED"));
            }
            GameEvents.OnCaseOpened += OnCaseOpened;
            EnsureBackButton();
        }

        // Start runs after all Awakes — canvas hierarchy is fully live.
        void Start() => PrepareButtonOnce();

        void OnDestroy() => GameEvents.OnCaseOpened -= OnCaseOpened;

        protected override void OnShown()
        {
            _showingResult = false;
            HideSkipButton();
            // Reset panel scale in case a reveal animation was interrupted on previous visit.
            if (caseDisplayPanel != null) caseDisplayPanel.transform.localScale = Vector3.one;
            ApplyDarkBackground();
            BuildNeonDecorations();
            ApplyPremiumDarkTheme();
            AlignOpenButtonAndPrice();
            ApplyCaseOpeningMobileLayout();
            EnsureOpenButtonClickable();
            BuildCaseList();
            RefreshWallet();
            ShowSpinOverlay(false);
            GameEvents.OnVpChanged += OnVpChanged;

            // Hide the CaseTabs selector strip — case is already chosen from Shop.
            HideCaseTabsSelector();

            // Hierarchy dump + stray-image scan (debug only, no side-effects).
            DebugCaseOpeningHierarchy();
            HideTopStraySkinImages();

            // Re-assert top sibling every time the screen is shown so no panel can cover the button.
            if (_runtimeBackBtn != null)
                _runtimeBackBtn.transform.SetAsLastSibling();

            Debug.Log("[OPEN_BTN_DEBUG] OnShown");
            Debug.Log("[OPEN_BTN_DEBUG] selected=" + (_selected != null ? _selected.DisplayName : "NULL"));
            Debug.Log("[OPEN_BTN_DEBUG] openButton active=" + (openButton != null && openButton.gameObject.activeInHierarchy));
            Debug.Log("[OPEN_BTN_DEBUG] openButton interactable=" + (openButton != null && openButton.interactable));
            DebugOpenButtonRaycast();
        }

        // Centers the OPEN CASE button under the case icon and parks the price
        // label directly below it. The baked prefab had them side-by-side; this
        // realigns at runtime without touching the prefab.
        bool _openButtonAligned;
        void AlignOpenButtonAndPrice()
        {
            if (_openButtonAligned) return;
            if (openButton == null || priceLabel == null) return;

            var btnRt = openButton.GetComponent<RectTransform>();
            if (btnRt == null) return;

            // Mobile: a single large, thumb-friendly action button near the bottom.
            btnRt.anchorMin = new Vector2(0.5f, 1f);
            btnRt.anchorMax = new Vector2(0.5f, 1f);
            btnRt.pivot     = new Vector2(0.5f, 1f);
            btnRt.sizeDelta = new Vector2(360f, 58f);
            btnRt.anchoredPosition = new Vector2(0f, -390f);

            // Dark-red fill + red neon outline to match the rest of the theme.
            var btnImg = openButton.GetComponent<Image>();
            if (btnImg != null) btnImg.color = new Color(0.10f, 0.03f, 0.06f, 1f);
            if (openButton.GetComponent<Outline>() == null)
            {
                var btnOutline = openButton.gameObject.AddComponent<Outline>();
                btnOutline.effectColor    = new Color(1f, 0.275f, 0.333f, 0.9f);
                btnOutline.effectDistance = new Vector2(1.5f, -1.5f);
            }

            var btnLabel = openButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (btnLabel != null)
            {
                btnLabel.text     = "KASAYI AÇ";
                btnLabel.fontStyle = FontStyles.Bold;
                btnLabel.color     = new Color(0.961f, 0.961f, 0.961f, 1f);
            }

            // Price label sits fully ABOVE the button (no overlap) and must never
            // intercept clicks meant for the button.
            var priceRt = priceLabel.rectTransform;
            priceRt.anchorMin = new Vector2(0.5f, 1f);
            priceRt.anchorMax = new Vector2(0.5f, 1f);
            priceRt.pivot     = new Vector2(0.5f, 1f);
            priceRt.sizeDelta = new Vector2(320f, 40f);
            // Button top edge is at y=-390 (top pivot). Park the price a clear gap above it.
            priceRt.anchoredPosition = new Vector2(0f, -344f);
            priceLabel.alignment = TMPro.TextAlignmentOptions.Center;
            priceLabel.fontStyle = TMPro.FontStyles.Bold;
            priceLabel.color = new Color(0.7f, 1f, 0.7f);
            priceLabel.raycastTarget = false;

            _openButtonAligned = true;
        }

        protected override void OnHidden()
        {
            // Halt any in-flight reveal so it doesn't fire on a hidden screen.
            StopAllCoroutines();
            flow?.Skip();
            HideSkipButton();
            if (caseDisplayPanel != null) caseDisplayPanel.transform.localScale = Vector3.one;
            // Mobile UI: tear down focus frame + reset any fade left from the reveal.
            if (_spinFocusFrame != null) _spinFocusFrame.SetActive(false);
            if (caseDisplayPanel != null)
            {
                var cg = caseDisplayPanel.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 1f;
            }
            GameEvents.OnVpChanged -= OnVpChanged;
        }

        // ── Navigation ───────────────────────────────────────────────────────

        void OnBack()
        {
            if (flow != null && flow.SessionActive) return;
            Debug.Log("[CASE_OPENING] Back clicked -> Shop");
            navigator?.Navigate(ScreenType.Shop);
        }

        // ── TAMAM butonu ─────────────────────────────────────────────────────

        // Runs once in Start() — canvas hierarchy is live, reparenting is safe.
        void PrepareButtonOnce()
        {
            if (_buttonReady || skipButton == null) return;
            _buttonReady = true;

            // Pull button out of spinOverlay so spinOverlay.SetActive(false) never hides it.
            skipButton.transform.SetParent(transform, false);

            var rt = skipButton.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin        = new Vector2(0.5f, 0.5f);
                rt.anchorMax        = new Vector2(0.5f, 0.5f);
                rt.pivot            = new Vector2(0.5f, 0.5f);
                rt.sizeDelta        = new Vector2(400f, 80f);
                rt.anchoredPosition = new Vector2(0f, -250f);
            }

            skipButton.transform.localScale = Vector3.one;
            skipButton.gameObject.SetActive(false);
        }

        void HideSkipButton()
        {
            if (skipButton != null) skipButton.gameObject.SetActive(false);
        }

        // Always creates a fresh visible runtime back button on the screen root.
        // Uses _backBtnCreated so it only builds once per MonoBehaviour lifetime.
        // Does NOT check the Inspector backButton field — that may be invisible/misconfigured.
        void EnsureBackButton()
        {
            Debug.Log("[CASE_OPENING_BACK] EnsureBackButton called");

            if (_backBtnCreated) return;
            _backBtnCreated = true;

            // ── Container — child of THIS transform (screen root, NOT a scroll/panel child) ──
            var go = new GameObject("BackButton_Runtime",
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            _runtimeBackBtn = go;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(0f, 1f);
            rt.pivot            = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(24f, -185f);
            rt.sizeDelta        = new Vector2(96f, 36f);

            // Fully opaque background — alpha 1 so nothing makes it invisible
            var img = go.GetComponent<Image>();
            img.color = new Color(0.05f, 0.08f, 0.16f, 1f);

            // Red neon border via Outline
            var outline = go.AddComponent<Outline>();
            outline.effectColor    = new Color(1f, 0.122f, 0.224f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            // Button — wire to OnBack (respects flow.SessionActive guard inside OnBack)
            var btn = go.GetComponent<Button>();
            var bc  = btn.colors;
            bc.normalColor      = Color.white;
            bc.highlightedColor = new Color(1f, 0.85f, 0.85f, 1f);
            bc.pressedColor     = new Color(0.75f, 0.50f, 0.50f, 1f);
            btn.colors = bc;
            btn.onClick.AddListener(OnBack);
            backButton = btn;  // also update the serialized field so OnBack guard works

            // ── Label — MUST be a separate child (TMP + Image can't share a GameObject) ──
            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = Vector2.zero;
            lblRt.offsetMax = Vector2.zero;
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text               = "GERİ";
            lbl.fontSize           = 13f;
            lbl.fontStyle          = FontStyles.Bold;
            lbl.alignment          = TextAlignmentOptions.Center;
            lbl.color              = new Color(0.925f, 0.910f, 0.882f, 1f);
            lbl.enableWordWrapping = false;
            lbl.raycastTarget      = false;

            // Place on top of all other children so no panel covers it
            go.transform.SetAsLastSibling();

            Debug.Log("[CASE_OPENING_BACK] created active=" + go.activeInHierarchy);
            Debug.Log("[CASE_OPENING_BACK] final pos=" + rt.anchoredPosition + " size=" + rt.sizeDelta);
            Debug.Log("[CASE_OPENING_BACK] sibling=" + go.transform.GetSiblingIndex());
        }

        void ConvertToTamam()
        {
            if (skipButton == null) return;

            PrepareButtonOnce(); // idempotent safety net

            var label = skipButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) { label.text = "TAMAM"; label.enabled = true; }

            var img = skipButton.GetComponent<Image>();
            if (img != null) img.enabled = true;

            skipButton.transform.localScale = Vector3.one;

            var cg = skipButton.GetComponent<CanvasGroup>();
            if (cg != null) { cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true; }

            skipButton.interactable = true;

            skipButton.onClick.RemoveAllListeners();
            skipButton.onClick.AddListener(() =>
            {
                skipButton.gameObject.SetActive(false);
                navigator?.Navigate(ScreenType.Shop);
            });

            skipButton.gameObject.SetActive(true);
            skipButton.transform.SetAsLastSibling();
        }

        // ── VP ───────────────────────────────────────────────────────────────

        void OnVpChanged(int _, int __) => RefreshWallet();

        void RefreshWallet()
        {
            if (walletLabel == null) return;
            var ctx = GameContext.Instance;
            if (ctx?.Vp == null) return;
            walletLabel.text = $"{ctx.Vp.Balance:N0} VP";
        }

        // ── Case Selector ────────────────────────────────────────────────────

        void BuildCaseList()
        {
            foreach (var item in _caseItems)
                if (item != null) Destroy(item.gameObject);
            _caseItems.Clear();

            var ctx = GameContext.Instance;
            if (ctx?.Content == null || caseListRoot == null || caseItemPrefab == null) return;

            foreach (var caseDef in ctx.Content.Cases)
            {
                if (caseDef == null) continue;
                var view = Instantiate(caseItemPrefab, caseListRoot);
                var unlocked = ctx.CaseProgression?.IsCaseUnlocked(caseDef) ?? true;
                view.Bind(caseDef, unlocked, SelectCase);
                _caseItems.Add(view);
            }

            // Honour pending selection from Shop, then keep previous, then default first.
            CaseDefinitionSO first = null;
            var pending = PendingCaseId;
            PendingCaseId = null;
            if (!string.IsNullOrEmpty(pending))
                first = ctx.Content.Cases.FirstOrDefault(c => c.CaseId == pending);
            if (first == null) first = _selected ?? ctx.Content.Cases.FirstOrDefault();
            SelectCase(first);
            // After SelectCase sets _selected, refresh the selector layout
            // so it fits all non-selected cases in a single row.
            RefreshCaseSelectorLayout();
        }

        void SelectCase(CaseDefinitionSO caseDef)
        {
            _selected = caseDef;
            foreach (var item in _caseItems)
                item.SetSelected(item.Case == caseDef);

            // Show only non-selected cases in the top selector strip.
            RefreshCaseSelectorLayout();

            if (caseDef == null) return;

            var ctx = GameContext.Instance;

            if (caseIconDisplay != null)
            {
                caseIconDisplay.sprite  = caseDef.CaseIcon;
                caseIconDisplay.enabled = true;
                caseIconDisplay.color   = caseDef.CaseIcon != null ? Color.white : caseDef.ThemeColor;
            }

            if (caseThemeBg != null)
            {
                var c = caseDef.ThemeColor;
                c.a = 0.15f;
                caseThemeBg.color = c;
            }

            if (selectedCaseLabel != null)
            {
                var dn = !string.IsNullOrEmpty(caseDef.DisplayName) ? caseDef.DisplayName : caseDef.CaseId ?? "";
                selectedCaseLabel.text = dn.ToUpperInvariant();
            }

            var price = caseDef.VpPrice;
            if (priceLabel != null) priceLabel.text = $"{price:N0} VP";

            RefreshOpenButton();
            BuildDropList(caseDef);
        }

        void RefreshOpenButton()
        {
            if (openButton == null) return;

            bool sessionBusy = flow != null && flow.SessionActive;
            bool canOpen      = GameContext.Instance?.CaseOpening?.CanOpen(_selected) == true;
            bool insufficient = _selected != null && !sessionBusy && !canOpen;

            openButton.interactable = _selected != null && !sessionBusy && canOpen;

            var btnLabel = openButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (btnLabel != null)
                btnLabel.text = insufficient ? "YETERSİZ VP" : "KASAYI AÇ";
        }

        // ── Drop List ─────────────────────────────────────────────────────────

        void BuildDropList(CaseDefinitionSO caseDef)
        {
            // Clear previously instantiated skin items.
            foreach (var d in _dropItems)
                if (d != null) Destroy(d.gameObject);
            _dropItems.Clear();

            if (caseDef == null)           { Debug.LogWarning("[DROP_GRID] caseDef NULL"); return; }
            if (caseDef.DropTable == null) { Debug.LogWarning("[DROP_GRID] DropTable NULL for " + caseDef.DisplayName); return; }
            if (dropListRoot == null)      { Debug.LogWarning("[DROP_GRID] dropListRoot NULL"); return; }

            // Disable root layout groups so they don't fight with the table container.
            var rootGo = dropListRoot.gameObject;
            var vlg = rootGo.GetComponent<VerticalLayoutGroup>();   if (vlg != null) vlg.enabled = false;
            var hlg = rootGo.GetComponent<HorizontalLayoutGroup>(); if (hlg != null) hlg.enabled = false;
            var glg = rootGo.GetComponent<GridLayoutGroup>();       if (glg != null) glg.enabled = false;
            var csf = rootGo.GetComponent<ContentSizeFitter>();     if (csf != null) csf.enabled = false;

            HideTopStraySkinImages();
            BuildRateTable(caseDef);
        }

        // ── CaseTabs selector hide ───────────────────────────────────────────
        // The case is already selected from the Shop screen, so the top
        // CaseTabs strip (Viewport → Content → case items with Icon PNGs) must
        // not be visible inside CaseOpeningScreen. We walk up from caseListRoot
        // to find the "CaseTabs" ancestor and disable it; if not found we fall
        // back to hiding caseListRoot itself. Safe to call on every OnShown.
        void HideCaseTabsSelector()
        {
            Debug.Log("[CASE_TABS] caseListRoot=" + (caseListRoot != null ? caseListRoot.name : "NULL"));

            if (caseListRoot == null)
            {
                Debug.LogWarning("[CASE_TABS] caseListRoot is null — nothing to hide");
                return;
            }

            // Walk up the parent chain looking for an ancestor named "CaseTabs".
            Transform found  = null;
            var       cursor = caseListRoot.parent;
            while (cursor != null)
            {
                if (cursor.name == "CaseTabs")
                {
                    found = cursor;
                    break;
                }
                cursor = cursor.parent;
            }

            if (found != null)
            {
                Debug.Log("[CASE_TABS] found CaseTabs=" + found.name);
                if (found.gameObject.activeSelf) found.gameObject.SetActive(false);
                Debug.Log("[CASE_TABS] CaseTabs hidden");
            }
            else
            {
                Debug.Log("[CASE_TABS] CaseTabs not found in parent chain — fallback: caseListRoot hidden");
                if (caseListRoot.gameObject.activeSelf) caseListRoot.gameObject.SetActive(false);
            }
        }

        // ── Runtime hierarchy dump ───────────────────────────────────────────
        // Call once in OnShown — reveals every object so we can track stray images.
        void DebugCaseOpeningHierarchy()
        {
            Debug.Log("[CASE_DEBUG] caseListRoot="    + (caseListRoot    != null ? caseListRoot.name    : "NULL"));
            Debug.Log("[CASE_DEBUG] dropListRoot="    + (dropListRoot    != null ? dropListRoot.name    : "NULL"));
            Debug.Log("[CASE_DEBUG] caseDisplayPanel="+ (caseDisplayPanel!= null ? caseDisplayPanel.name: "NULL"));
            Debug.Log("[CASE_DEBUG] spinOverlay="     + (spinOverlay     != null ? spinOverlay.name     : "NULL"));

            DumpTransform(transform, "CaseOpening");
        }

        static void DumpTransform(Transform t, string path)
        {
            if (t == null) return;

            var rt  = t as RectTransform;
            var img = t.GetComponent<Image>();
            var tmp = t.GetComponent<TextMeshProUGUI>();

            var sb = new System.Text.StringBuilder();
            sb.Append("[CASE_HIERARCHY] path=").Append(path);
            sb.Append(" active=").Append(t.gameObject.activeSelf);
            sb.Append(" activeH=").Append(t.gameObject.activeInHierarchy);
            sb.Append(" sibling=").Append(t.GetSiblingIndex());
            if (rt != null)
            {
                sb.Append(" pos=(").Append(rt.anchoredPosition.x.ToString("F0")).Append(",").Append(rt.anchoredPosition.y.ToString("F0")).Append(")");
                sb.Append(" size=(").Append(rt.sizeDelta.x.ToString("F0")).Append(",").Append(rt.sizeDelta.y.ToString("F0")).Append(")");
                sb.Append(" ancMin=(").Append(rt.anchorMin.x.ToString("F2")).Append(",").Append(rt.anchorMin.y.ToString("F2")).Append(")");
                sb.Append(" ancMax=(").Append(rt.anchorMax.x.ToString("F2")).Append(",").Append(rt.anchorMax.y.ToString("F2")).Append(")");
            }
            if (img != null)
            {
                sb.Append(" image=true");
                sb.Append(" sprite=").Append(img.sprite != null ? img.sprite.name : "null");
                sb.Append(" imgEnabled=").Append(img.enabled);
                sb.Append(" imgColor=(").Append(img.color.r.ToString("F2")).Append(",").Append(img.color.g.ToString("F2")).Append(",").Append(img.color.b.ToString("F2")).Append(",").Append(img.color.a.ToString("F2")).Append(")");
                sb.Append(" raycast=").Append(img.raycastTarget);
            }
            if (tmp != null)
            {
                sb.Append(" tmp=true text=").Append(tmp.text.Length > 30 ? tmp.text.Substring(0, 30) : tmp.text);
            }
            Debug.Log(sb.ToString());

            for (int i = 0; i < t.childCount; i++)
                DumpTransform(t.GetChild(i), path + "/" + t.GetChild(i).name);
        }

        // ── Stray skin-preview hide ──────────────────────────────────────────
        // Logs every Image candidate that might be a stray skin preview,
        // then hides the confirmed ones. Safe to call repeatedly.
        void HideTopStraySkinImages()
        {
            int hidden = 0;

            // Protected roots — Images inside these are intentional.
            bool IsProtected(Transform t)
            {
                if (caseIconDisplay != null      && (t == caseIconDisplay.transform      || t.IsChildOf(caseIconDisplay.transform)))      return true;
                if (caseThemeBg     != null      && (t == caseThemeBg.transform          || t.IsChildOf(caseThemeBg.transform)))           return true;
                if (openButton      != null      && t.IsChildOf(openButton.transform))                                                      return true;
                if (skipButton      != null      && t.IsChildOf(skipButton.transform))                                                      return true;
                if (_runtimeBackBtn != null      && t.IsChildOf(_runtimeBackBtn.transform))                                                 return true;
                if (_spinFocusFrame != null      && t.IsChildOf(_spinFocusFrame.transform))                                                 return true;
                if (spinOverlay     != null      && t.IsChildOf(spinOverlay.transform))                                                     return true;
                // Case list item icons are intentional (they show the case image).
                if (t.GetComponent<CaseListItemView>() != null) return true;
                if (t.GetComponentInParent<CaseListItemView>() != null) return true;
                // SkinWinPopup
                var swp = GetComponentInChildren<SkinWinPopup>(true);
                if (swp != null && t.IsChildOf(swp.transform)) return true;
                return false;
            }

            foreach (var img in GetComponentsInChildren<Image>(true))
            {
                if (img == null) continue;
                if (IsProtected(img.transform)) continue;

                var imgTf = img.transform;
                var rt = imgTf as RectTransform ?? imgTf.GetComponent<RectTransform>();
                var pos  = rt != null ? rt.anchoredPosition : Vector2.zero;
                var size = rt != null ? rt.sizeDelta        : Vector2.zero;

                // Log every Image with a sprite as a candidate.
                if (img.sprite != null)
                {
                    Debug.Log("[CASE_PREVIEW_CANDIDATE] path=" + GetPath(imgTf) +
                              " sprite=" + img.sprite.name +
                              " pos=(" + pos.x.ToString("F0") + "," + pos.y.ToString("F0") + ")" +
                              " size=(" + size.x.ToString("F0") + "," + size.y.ToString("F0") + ")" +
                              " active=" + img.gameObject.activeInHierarchy);
                }

                // Auto-hide confirmed stray skin cards: DropItemView or ReelItemView parent.
                bool isDropItem = imgTf.GetComponentInParent<DropItemView>()  != null;
                bool isReelItem = imgTf.GetComponentInParent<ReelItemView>()  != null;
                if ((isDropItem || isReelItem) && img.gameObject.activeSelf)
                {
                    var root = isDropItem
                        ? (Transform)imgTf.GetComponentInParent<DropItemView>().transform
                        : (Transform)imgTf.GetComponentInParent<ReelItemView>().transform;
                    root.gameObject.SetActive(false);
                    Debug.Log("[CASE_PREVIEW_HIDE] hidden path=" + GetPath(root) +
                              " sprite=" + (img.sprite != null ? img.sprite.name : "null"));
                    hidden++;
                }
            }

            if (hidden > 0) Debug.Log("[CASE_UI_FIX] top stray previews hidden count=" + hidden);
            else             Debug.Log("[CASE_PREVIEW] no confirmed stray skin previews found (count=0). Check CANDIDATE logs above.");
        }

        static string GetPath(Transform t)
        {
            if (t == null) return "null";
            var parts = new System.Collections.Generic.List<string>();
            var cur = t;
            while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ── KASA İÇERİR rarity table ────────────────────────────────────────
        // Built directly inside caseDisplayPanel (NOT inside DropScroll) so the
        // table is never clipped by the ScrollRect viewport.
        // Destroys and recreates all children on every call — guarantees exactly
        // 5 rarity rows regardless of which case was shown before.
        void BuildRateTable(CaseDefinitionSO caseDef)
        {
            if (caseDef?.DropTable == null) return;

            // ── Read rates ────────────────────────────────────────────────────
            float selectRate    = GetRarityRate(caseDef, SkinRarity.Select);
            float deluxeRate    = GetRarityRate(caseDef, SkinRarity.Deluxe);
            float premiumRate   = GetRarityRate(caseDef, SkinRarity.Premium);
            float exclusiveRate = GetRarityRate(caseDef, SkinRarity.Exclusive);
            float ultraRate     = GetRarityRate(caseDef, SkinRarity.Ultra);

            Debug.Log("[CASE_RARITY_TABLE] rebuilding for=" + caseDef.DisplayName);
            Debug.Log("[CASE_RARITY_TABLE] SELECT="    + selectRate);
            Debug.Log("[CASE_RARITY_TABLE] DELUXE="    + deluxeRate);
            Debug.Log("[CASE_RARITY_TABLE] PREMIUM="   + premiumRate);
            Debug.Log("[CASE_RARITY_TABLE] EXCLUSIVE=" + exclusiveRate);
            Debug.Log("[CASE_RARITY_TABLE] ULTRA="     + ultraRate);

            // ── Choose parent — caseDisplayPanel avoids DropScroll clipping ──
            var tableParent = caseDisplayPanel != null
                ? caseDisplayPanel.transform
                : dropListRoot;
            if (tableParent == null) { Debug.LogWarning("[CASE_RARITY_TABLE] no valid parent"); return; }

            // Hide the prefab-baked header and DropScroll — our table replaces them.
            var nativeHeader = tableParent.Find("ContainsHeader");
            if (nativeHeader != null && nativeHeader.gameObject.activeSelf)
                nativeHeader.gameObject.SetActive(false);
            var nativeDropScroll = tableParent.Find("DropScroll");
            if (nativeDropScroll != null && nativeDropScroll.gameObject.activeSelf)
                nativeDropScroll.gameObject.SetActive(false);

            // Destroy any stale RateTable that was previously built inside dropListRoot.
            if (dropListRoot != null)
            {
                var oldInRoot = dropListRoot.Find("RateTable");
                if (oldInRoot != null) Destroy(oldInRoot.gameObject);
            }

            // ── Find or create the table container ────────────────────────────
            const string containerName = "RateTable";
            var containerTf = tableParent.Find(containerName);
            int oldChildCount = containerTf != null ? containerTf.childCount : 0;
            Debug.Log("[CASE_RARITY_TABLE] rows before=" + oldChildCount);

            if (containerTf == null)
            {
                var cGo = new GameObject(containerName, typeof(RectTransform), typeof(Image));
                cGo.transform.SetParent(tableParent, false);
                var cRt = (RectTransform)cGo.transform;
                // Anchored to top edge of caseDisplayPanel; positioned below the open button.
                cRt.anchorMin        = new Vector2(0.04f, 1f);
                cRt.anchorMax        = new Vector2(0.96f, 1f);
                cRt.pivot            = new Vector2(0.5f, 1f);
                cRt.anchoredPosition = new Vector2(0f, -458f);
                // title 26 + 5 rows×22 + padding(10+10) + spacing(4×5) = 176
                cRt.sizeDelta        = new Vector2(0f, 176f);
                var bg = cGo.GetComponent<Image>();
                bg.color         = PanelDark;
                bg.raycastTarget = false;
                var tblOutline = cGo.AddComponent<Outline>();
                tblOutline.effectColor    = new Color(1f, 0.275f, 0.333f, 0.5f);
                tblOutline.effectDistance = new Vector2(1f, -1f);
                var vlg = cGo.AddComponent<VerticalLayoutGroup>();
                vlg.childControlWidth      = true;
                vlg.childForceExpandWidth  = true;
                vlg.childControlHeight     = false;
                vlg.childForceExpandHeight = false;
                vlg.spacing = 4f;
                vlg.padding = new RectOffset(10, 10, 10, 10);
                containerTf = cGo.transform;
            }
            else
            {
                // Destroy all stale rows so we always rebuild exactly 5.
                for (int k = containerTf.childCount - 1; k >= 0; k--)
                {
                    var ch = containerTf.GetChild(k);
                    if (ch != null) Destroy(ch.gameObject);
                }
            }

            // ── Title ─────────────────────────────────────────────────────────
            MakeRateTableRow(containerTf, "Title", "KASA İÇERİR", "",
                             Color.white, Color.white, isTitle: true, height: 26f);

            // ── 5 Rarity rows (always created, never skipped) ─────────────────
            float[] rates = { selectRate, deluxeRate, premiumRate, exclusiveRate, ultraRate };
            for (int i = 0; i < k_RarityOrder.Length; i++)
                MakeRateTableRow(containerTf, "Row_" + i,
                                 k_RarityNames[i], "%" + rates[i].ToString("F0"),
                                 k_RarityColors[i], new Color(0.82f, 0.86f, 0.92f, 1f),
                                 isTitle: false, height: 22f);

            int newChildCount = containerTf.childCount;
            Debug.Log("[CASE_RARITY_TABLE] rows after=" + newChildCount);
            Debug.Log("[CASE_RARITY_TABLE] selected case=" + caseDef.DisplayName);
            Debug.Log("[CASE_UI_FIX] rarity table fixed with 5 rows");
        }

        // Returns the weight% for a rarity from the drop table (0 when missing).
        static float GetRarityRate(CaseDefinitionSO caseDef, SkinRarity rarity) =>
            caseDef.DropTable?.RarityWeights?
                .FirstOrDefault(w => w.rarity == rarity)?.weightPercent ?? 0f;

        // Creates one row inside the rate table container.
        // isTitle = true  → single centred label spanning full width.
        // isTitle = false → left rarity-name label + right pct label side by side.
        static void MakeRateTableRow(Transform parent, string rowName,
                                     string leftText, string rightText,
                                     Color leftColor, Color rightColor,
                                     bool isTitle, float height)
        {
            var row = new GameObject(rowName, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            ((RectTransform)row.transform).sizeDelta = new Vector2(0f, height);
            var rle = row.AddComponent<LayoutElement>();
            rle.preferredHeight = height;
            rle.minHeight       = height;

            if (isTitle)
            {
                var lbl = new GameObject("Label", typeof(RectTransform));
                lbl.transform.SetParent(row.transform, false);
                var lrt = (RectTransform)lbl.transform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;
                var t = lbl.AddComponent<TextMeshProUGUI>();
                t.text              = leftText;
                t.fontSize          = 13f;
                t.fontStyle         = FontStyles.Bold;
                t.color             = leftColor;
                t.alignment         = TextAlignmentOptions.Center;
                t.raycastTarget     = false;
                t.enableWordWrapping = false;
            }
            else
            {
                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.childControlWidth      = true;
                hlg.childForceExpandWidth  = true;
                hlg.childControlHeight     = false;
                hlg.padding = new RectOffset(2, 2, 0, 0);

                // Left — rarity name in its rarity colour
                var nGo = new GameObject("Name", typeof(RectTransform));
                nGo.transform.SetParent(row.transform, false);
                ((RectTransform)nGo.transform).sizeDelta = new Vector2(0f, height);
                nGo.AddComponent<LayoutElement>().flexibleWidth = 2f;
                var nTmp = nGo.AddComponent<TextMeshProUGUI>();
                nTmp.text              = leftText;
                nTmp.fontSize          = 11f;
                nTmp.color             = leftColor;
                nTmp.alignment         = TextAlignmentOptions.Left;
                nTmp.raycastTarget     = false;
                nTmp.enableWordWrapping = false;

                // Right — percentage in light/neutral colour, bold
                var pGo = new GameObject("Pct", typeof(RectTransform));
                pGo.transform.SetParent(row.transform, false);
                ((RectTransform)pGo.transform).sizeDelta = new Vector2(0f, height);
                pGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var pTmp = pGo.AddComponent<TextMeshProUGUI>();
                pTmp.text              = rightText;
                pTmp.fontSize          = 11f;
                pTmp.fontStyle         = FontStyles.Bold;
                pTmp.color             = rightColor;
                pTmp.alignment         = TextAlignmentOptions.Right;
                pTmp.raycastTarget     = false;
                pTmp.enableWordWrapping = false;
            }
        }

        // Hides the currently selected case in the top strip (it's shown in the
        // hero panel) and makes the remaining cases fill the strip evenly.
        void RefreshCaseSelectorLayout()
        {
            if (caseListRoot == null || _caseItems.Count == 0) return;

            // Show all non-selected cases; hide the selected one.
            int visibleCount = 0;
            foreach (var item in _caseItems)
            {
                if (item == null) continue;
                bool isSelected = item.Case == _selected;
                item.gameObject.SetActive(!isSelected);
                if (!isSelected) visibleCount++;
            }
            Debug.Log("[CASE_SELECTOR] visible other cases=" + visibleCount);
            if (visibleCount == 0) return;

            // Ensure a single HorizontalLayoutGroup that distributes items equally.
            var rootGo = caseListRoot.gameObject;
            var existVlg = rootGo.GetComponent<VerticalLayoutGroup>(); if (existVlg != null) existVlg.enabled = false;
            var existGlg = rootGo.GetComponent<GridLayoutGroup>();     if (existGlg != null) existGlg.enabled = false;
            var selectorHlg = rootGo.GetComponent<HorizontalLayoutGroup>();
            if (selectorHlg == null) selectorHlg = rootGo.AddComponent<HorizontalLayoutGroup>();
            selectorHlg.childControlWidth     = true;
            selectorHlg.childForceExpandWidth  = true;
            selectorHlg.childControlHeight    = false;
            selectorHlg.childForceExpandHeight = false;
            selectorHlg.spacing = 3f;
            selectorHlg.padding = new RectOffset(4, 4, 2, 2);

            // Shrink labels so they fit in narrow columns.
            foreach (var item in _caseItems)
            {
                if (item == null || !item.gameObject.activeSelf) continue;
                foreach (var tmp in item.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    tmp.enableAutoSizing   = false;
                    tmp.fontSize           = 9f;
                    tmp.enableWordWrapping = true;
                    tmp.overflowMode       = TextOverflowModes.Truncate;
                }
            }

            Canvas.ForceUpdateCanvases();
            var rootRt = caseListRoot as RectTransform;
            float containerWidth = (rootRt != null && rootRt.rect.width > 0f) ? rootRt.rect.width : Screen.width;
            float itemWidth      = (containerWidth - selectorHlg.padding.horizontal - selectorHlg.spacing * (visibleCount - 1)) / visibleCount;
            Debug.Log("[CASE_SELECTOR] item width=" + itemWidth);
        }

        // Builds "Select %65 | Deluxe %15 | ..." from the drop table's rarity weights.
        // Reads live data — no hardcoded values, so every case shows its own rates.
        static string BuildRateText(CaseDropTableSO table)
        {
            if (table?.RarityWeights == null || table.RarityWeights.Count == 0) return "—";

            var order = new[] { SkinRarity.Select, SkinRarity.Deluxe, SkinRarity.Premium, SkinRarity.Exclusive, SkinRarity.Ultra };
            var parts = new System.Text.StringBuilder();
            bool first = true;
            foreach (var rarity in order)
            {
                var entry = table.RarityWeights.FirstOrDefault(r => r.rarity == rarity);
                if (entry == null || entry.weightPercent <= 0f) continue;
                if (!first) parts.Append(" | ");
                first = false;
                var name = rarity == SkinRarity.Select    ? "Select"
                         : rarity == SkinRarity.Deluxe    ? "Deluxe"
                         : rarity == SkinRarity.Premium   ? "Premium"
                         : rarity == SkinRarity.Exclusive ? "Exclusive"
                         : rarity == SkinRarity.Ultra     ? "Ultra"
                         : rarity.ToString();
                parts.Append(name).Append(" %").Append(entry.weightPercent.ToString("F0"));
            }
            return parts.Length > 0 ? parts.ToString() : "—";
        }

        static float CalculateDropChance(CaseDropTableSO table, SkinDropEntry entry)
        {
            var skin = entry.skin;
            var rarityEntry = table.RarityWeights.FirstOrDefault(r => r.rarity == skin.Rarity);
            if (rarityEntry == null) return 0f;

            var skinsOfRarity = table.PossibleDrops
                .Where(d => d.skin != null && d.skin.Rarity == skin.Rarity)
                .ToList();
            if (skinsOfRarity.Count == 0) return 0f;

            var totalWeight = skinsOfRarity.Sum(d => d.skinWeightOverride > 0 ? d.skinWeightOverride : 1f);
            var skinWeight  = entry.skinWeightOverride > 0 ? entry.skinWeightOverride : 1f;
            return rarityEntry.weightPercent * (skinWeight / totalWeight);
        }

        // ── Opening ──────────────────────────────────────────────────────────

        void OpenSelected()
        {
            Debug.Log("[CASE_OPEN_CLICK] OpenSelected CALLED");
            Debug.Log("[CASE_OPEN_CLICK] selected=" + (_selected != null ? _selected.DisplayName : "NULL"));
            Debug.Log("[CASE_OPEN_CLICK] flow null=" + (flow == null));
            Debug.Log("[CASE_OPEN_CLICK] flow active=" + (flow != null && flow.SessionActive));
            Debug.Log("[CASE_OPEN_CLICK] canOpen=" + GameContext.Instance?.CaseOpening?.CanOpen(_selected));

            // Safe guards — never proceed (or crash) on a bad state.
            if (_selected == null) return;
            if (flow == null) return;
            if (flow.SessionActive) return;
            if (GameContext.Instance?.CaseOpening?.CanOpen(_selected) != true) { RefreshOpenButton(); return; }

            Debug.Log("[CASE_OPEN_CLICK] starting spin");

            // The case is already selected and its drop list is already built —
            // do NOT rebuild here (that re-ran EnsureDropGridLayout and crashed).
            // Just kick off the spin.
            _showingResult = false;
            if (openButton != null) openButton.interactable = false;
            HideSkipButton();

            // ── Backend mode: wait for the server BEFORE showing the spin. ──────
            // The button is already disabled (in-flight state); the back button is
            // blocked by flow.SessionActive. The spin overlay + watchdog start only
            // once the server result arrives. On failure nothing spins.
            if (GameContext.Instance?.BackendEnabled == true)
            {
                Debug.Log("[CASE_OPEN_CLICK] backend mode — requesting server open");
                // The disabled button is the in-flight state. We deliberately do NOT
                // retitle it here — the shared reveal/confirm path re-enables the button
                // without resetting its label, so a transient title would get stuck.
                flow.StartOpeningBackend(_selected,
                    onSpinStarting: () =>
                    {
                        // Guard: invoked from the flow controller's coroutine; skip if the
                        // screen was destroyed by navigation before the server replied.
                        if (this == null) return;
                        ShowSpinOverlay(true);
                        _spinWatchdog = StartCoroutine(SpinEndWatchdog());
                    },
                    onFailed: msg =>
                    {
                        if (this == null) return;
                        // Stop the watchdog so the in-flight warmup never resolves into
                        // a bogus reveal — the open was rejected, nothing was granted.
                        if (_spinWatchdog != null) { StopCoroutine(_spinWatchdog); _spinWatchdog = null; }
                        ShowSpinOverlay(false);
                        EnsureInteractive();
                        RefreshOpenButton();
                        RefreshWallet();
                        if (!string.IsNullOrEmpty(msg)) GameEvents.RaiseToast(msg);
                    });
                return;
            }

            // ── Local mode (unchanged) ─────────────────────────────────────────
            Debug.Log("[CASE_OPEN_CLICK] starting spin");
            ShowSpinOverlay(true);
            flow.StartOpening(_selected);
            StartCoroutine(SpinEndWatchdog());
        }

        // Polls every frame until session ends OR timeout.
        // Fallback: if OnCaseOpened event never fired, completes manually.
        IEnumerator SpinEndWatchdog()
        {
            // Backend mode adds an optimistic warmup that can legitimately run for the
            // network round-trip (up to the request timeout) before the reel lands, so
            // the safety-net limit must clear that window or it would fire mid-spin.
            var extra   = GameContext.Instance?.BackendEnabled == true ? 25f : 10f;
            var limit   = GameConstants.CaseSpinDurationSeconds + extra;
            var elapsed = 0f;

            while (flow != null && flow.SessionActive && elapsed < limit)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            // Reveal already running — event chain worked.
            if (_showingResult) yield break;

            // Event chain broke — pull the locked roll, force save, then reveal.
            var skin   = flow?.RolledSkin;
            var forced = flow?.TryForceComplete();
            if (skin == null) skin = forced;

            // Never force a reveal with no resolved skin. In backend mode the open may
            // still be pending/timed-out/failed (RolledSkin null while SessionActive),
            // and revealing null produces a blank panel that blocks the real reveal.
            // The flow's own failure path surfaces the mapped message; here we just make
            // sure the screen is not left stuck, and we never invent a result.
            if (skin == null)
            {
                Debug.LogWarning("[CASE] Watchdog fired with no resolved skin — skipping reveal, restoring UI.");
                ShowSpinOverlay(false);
                EnsureInteractive();
                RefreshOpenButton();
                RefreshWallet();
                yield break;
            }

            StartRevealSequence(skin);
        }

        void OnCaseOpened(CaseDefinitionSO _, SkinDefinitionSO skin)
        {
            // Guard against double-fire (watchdog/Update may have started already).
            if (_showingResult) return;
            if (spinOverlay != null && !spinOverlay.activeSelf) return;
            StartRevealSequence(skin);
        }

        // ── Reveal sequence (single source of truth for result display) ─────

        // The ONLY entry point for showing a result. All paths (event,
        // watchdog, Update fast-fail) route through here. `_showingResult`
        // is set immediately to prevent re-entry.
        void StartRevealSequence(SkinDefinitionSO skin)
        {
            if (_showingResult) return;

            // Defense-in-depth: never show a blank result panel. A null skin means the
            // backend open is not actually ready (pending/failed) — restore safe UI and
            // wait for the real result/failure path instead of locking into a blank reveal.
            if (skin == null)
            {
                Debug.LogWarning("[CASE] StartRevealSequence called with null skin — ignoring (no blank reveal).");
                ShowSpinOverlay(false);
                EnsureInteractive();
                RefreshOpenButton();
                return;
            }

            _showingResult = true;
            StartCoroutine(RevealCoroutine(skin));
        }

        // Valorant-style reveal: brief pause so the player registers the
        // reel result, then transition to the result panel, populate, pop
        // it in with a scale animation, then show the OK (TAMAM) button.
        IEnumerator RevealCoroutine(SkinDefinitionSO skin)
        {
            // Phase 1 — short delay so the spin's freeze frame lingers
            yield return new WaitForSecondsRealtime(0.45f);

            // Phase 2 — transition spin → result panel
            ShowSpinOverlay(false);
            EnsureInteractive();
            RefreshOpenButton();
            RefreshWallet();

            // Phase 3 — populate panel with the locked reward (no RNG here)
            PopulateResultPanel(skin);
            StartCoroutine(GlowBurst(skin));   // visual-only: rarity flash → white

            // Phase 4 — pop-in animation (runs in parallel with GlowBurst)
            yield return PanelPopAnimation();

            // Phase 5 — brief beat, then show the win popup overlay.
            // Falls back to the TAMAM button if SkinWinPopup is not in the scene.
            yield return new WaitForSecondsRealtime(0.25f);
            Debug.Log("[DEBUG][CASE] Reward reached — skin=" + (skin != null ? skin.SkinName : "NULL"));
            var popup = ValoCase.UI.SkinWinPopup.EnsureExists();
            Debug.Log("[DEBUG][CASE] EnsureExists() returned: " + popup);
            if (popup != null)
                popup.Show(skin, () =>
                {
                    Debug.Log("[CASE] Confirm clicked — staying on result, ready for next open");
                    _showingResult = false;
                    if (openButton != null)
                    {
                        openButton.gameObject.SetActive(true);
                        openButton.interactable = true;
                    }
                    EnsureInteractive();
                });
            else
                ConvertToTamam();
        }

        // Repurposes existing panel fields. No RNG, no recalculation — uses
        // ONLY the skin passed in from the locked roll.
        void PopulateResultPanel(SkinDefinitionSO skin)
        {
            if (skin == null) return;

            if (caseIconDisplay != null)
            {
                caseIconDisplay.sprite  = skin.Icon != null ? skin.Icon : caseIconDisplay.sprite;
                caseIconDisplay.enabled = true;
                caseIconDisplay.color   = Color.white;
            }

            if (caseThemeBg != null)
            {
                var ctx = GameContext.Instance;
                var col = Color.white;
                if (ctx?.RarityVisuals != null &&
                    ctx.RarityVisuals.TryGet(skin.Rarity, out var entry))
                    col = entry.primaryColor;
                col.a = 0.35f;
                caseThemeBg.color = col;
            }

            if (selectedCaseLabel != null)
                selectedCaseLabel.text = skin.SkinName.ToUpperInvariant();

            if (priceLabel != null)
                priceLabel.text = "BU SKİNİ KAZANDIN!";

            if (openButton != null) openButton.gameObject.SetActive(false);
        }

        // Mobile reveal: gentle fade + subtle scale (0.94 → 1.00). No overshoot.
        IEnumerator PanelPopAnimation()
        {
            if (caseDisplayPanel == null) yield break;
            var rt = caseDisplayPanel.transform as RectTransform;
            if (rt == null) yield break;

            var cg = caseDisplayPanel.GetComponent<CanvasGroup>();
            if (cg == null) cg = caseDisplayPanel.AddComponent<CanvasGroup>();

            const float startScale = 0.94f;
            const float dur        = 0.22f;

            rt.localScale = new Vector3(startScale, startScale, 1f);
            cg.alpha = 0f;

            var t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(t / dur);
                var eased = 1f - Mathf.Pow(1f - p, 3f);  // ease-out cubic
                var s = Mathf.Lerp(startScale, 1f, eased);
                rt.localScale = new Vector3(s, s, 1f);
                cg.alpha = p;
                yield return null;
            }

            rt.localScale = Vector3.one;
            cg.alpha = 1f;
        }

        void ShowSpinOverlay(bool show)
        {
            if (spinOverlay != null) spinOverlay.SetActive(show);
            if (caseDisplayPanel != null) caseDisplayPanel.SetActive(!show);
            if (!show && openButton != null && !_showingResult)
                openButton.gameObject.SetActive(true);

            if (show)
            {
                EnsureSpinFocusFrame();
                if (_spinFocusFrame != null)
                {
                    _spinFocusFrame.SetActive(true);
                    _spinFocusFrame.transform.SetAsLastSibling();
                }
                HideOldSpinIndicators();
                StartSpinPulse();
            }
            else
            {
                if (_spinFocusFrame != null) _spinFocusFrame.SetActive(false);
                StopSpinPulse();
            }
        }

        // ── Neon spin-phase pulse ────────────────────────────────────────────

        void StartSpinPulse()
        {
            // Pulse/glow disabled — pin the spin overlay to the static background color.
            _spinOverlayBg = spinOverlay != null ? spinOverlay.GetComponent<Image>() : null;
            if (_spinOverlayBg != null) _spinOverlayBg.color = BgStatic;
            // _spinPulseCoroutine intentionally not started.
        }

        void StopSpinPulse()
        {
            // Stop any coroutine that might still be running from a previous session.
            if (_spinPulseCoroutine != null) { StopCoroutine(_spinPulseCoroutine); _spinPulseCoroutine = null; }
            // Always restore to the exact static color — no animated remnants.
            if (_spinOverlayBg != null) _spinOverlayBg.color = BgStatic;
        }

        // Breathes the spin overlay between near-black and a dim cyan tint.
        // Period ~1.4 s — subtle enough not to distract from the reel.
        IEnumerator SpinPulse()
        {
            var baseColor = new Color(0.05f, 0.02f, 0.03f, 0.97f);   // dark red-black
            var peakColor = new Color(0.16f, 0.03f, 0.05f, 0.97f);   // deep red glow
            const float halfPeriod = 0.70f;
            var t = 0f;
            while (true)
            {
                t += Time.unscaledDeltaTime;
                var p = Mathf.PingPong(t / halfPeriod, 1f);
                _spinOverlayBg.color = Color.Lerp(baseColor, peakColor, p);
                yield return null;
            }
        }

        // ── Neon reveal glow burst ───────────────────────────────────────────

        // Flashes the case icon with the skin's rarity color then fades to white.
        // Runs in parallel with PanelPopAnimation — no sequencing impact.
        IEnumerator GlowBurst(SkinDefinitionSO skin)
        {
            if (caseIconDisplay == null || skin == null) yield break;

            var rarityColor = Color.white;
            var ctx = GameContext.Instance;
            if (ctx?.RarityVisuals != null &&
                ctx.RarityVisuals.TryGet(skin.Rarity, out var entry))
                rarityColor = entry.primaryColor;

            caseIconDisplay.color = rarityColor;
            var t = 0f;
            const float dur = 0.4f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                caseIconDisplay.color = Color.Lerp(rarityColor, Color.white, t / dur);
                yield return null;
            }
            caseIconDisplay.color = Color.white;
        }

        // Fast-fail safety net: if the session ended but no reveal has
        // started (event missed, exception swallowed), kick it off here
        // without waiting for the watchdog's full timeout.
        void Update()
        {
            if (!IsVisible || flow == null || _showingResult) return;

            if (!flow.SessionActive && spinOverlay != null && spinOverlay.activeSelf)
            {
                var skin   = flow.RolledSkin;
                var forced = flow.TryForceComplete();
                if (skin == null) skin = forced;
                StartRevealSequence(skin);
                return;
            }

            if (!flow.SessionActive)
                RefreshOpenButton();
        }

        // ── OPEN button click diagnostics (debug only) ──────────────────────
        void DebugOpenButtonRaycast()
        {
            if (openButton == null) { Debug.Log("[OPEN_BTN_DEBUG] openButton NULL in raycast debug"); return; }

            var rt = openButton.GetComponent<RectTransform>();
            if (rt != null)
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Debug.Log("[OPEN_BTN_DEBUG] world corners BL=" + corners[0] + " TL=" + corners[1] +
                          " TR=" + corners[2] + " BR=" + corners[3]);
            }

            var img = openButton.GetComponent<Image>();
            if (img != null) Debug.Log("[OPEN_BTN_DEBUG] image raycastTarget=" + img.raycastTarget);

            foreach (var tmp in openButton.GetComponentsInChildren<TextMeshProUGUI>(true))
                Debug.Log("[OPEN_BTN_DEBUG] child tmp=" + tmp.name + " raycastTarget=" + tmp.raycastTarget);

            // Walk the parent chain logging every CanvasGroup that could gate input.
            var t = openButton.transform;
            while (t != null)
            {
                var cg = t.GetComponent<CanvasGroup>();
                if (cg != null)
                    Debug.Log("[OPEN_BTN_DEBUG] parent canvasGroup=" + cg.name +
                              " interactable=" + cg.interactable +
                              " blocksRaycasts=" + cg.blocksRaycasts +
                              " alpha=" + cg.alpha);
                t = t.parent;
            }

            var raycaster = GetComponentInParent<GraphicRaycaster>();
            Debug.Log("[OPEN_BTN_DEBUG] graphicRaycaster=" + (raycaster != null ? raycaster.name : "NULL"));

            // Dump every Graphic under this screen so we can spot a raycastTarget=true
            // panel/image sitting on top of the button.
            foreach (var g in GetComponentsInChildren<Graphic>(true))
                Debug.Log("[OPEN_BTN_RAYCAST] graphic=" + g.name +
                          " raycast=" + g.raycastTarget +
                          " active=" + g.gameObject.activeInHierarchy);
        }

        // ── Mobile UI redesign helpers (revertible block) ────────────────────

        // Guarantees the OPEN button receives clicks: rebinds the listener,
        // turns on its own raycast target, turns OFF raycast on every child
        // text/label so nothing on top of the button swallows the tap, and
        // lifts the button to the top of its parent.
        void EnsureOpenButtonClickable()
        {
            if (openButton == null) return;

            openButton.onClick.RemoveListener(OpenSelected);
            openButton.onClick.AddListener(OpenSelected);

            var img = openButton.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;

            foreach (var t in openButton.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (t != null) t.raycastTarget = false;
            if (priceLabel != null) priceLabel.raycastTarget = false;

            var cg = openButton.GetComponent<CanvasGroup>();
            if (cg != null) { cg.interactable = true; cg.blocksRaycasts = true; }

            openButton.transform.SetAsLastSibling();

            Debug.Log("[CASE_OPEN_BTN] openButton interactable=" + openButton.interactable);
            Debug.Log("[CASE_OPEN_BTN] listener fixed");
            Debug.Log("[CASE_OPEN_BTN] raycast blockers disabled");
        }

        // Pins every background surface in CaseOpening to the same flat color as
        // the CASES/Shop screen. No gradients, no pulse — just static navy/black.
        // Runs every OnShown so Unity can never reset it.
        static readonly Color BgStatic  = new Color(0.031f, 0.055f, 0.102f, 1f); // #080E1A
        static readonly Color BgPremium = new Color(0.031f, 0.043f, 0.078f, 1f); // #080B14
        static readonly Color PanelDark = new Color(0.067f, 0.094f, 0.153f, 1f); // #111827
        static readonly Color NeonRed   = new Color(1.000f, 0.275f, 0.333f, 1f); // #FF4655

        // Rarity display data for the rate table (Select → Ultra)
        static readonly SkinRarity[] k_RarityOrder  = { SkinRarity.Select, SkinRarity.Deluxe, SkinRarity.Premium, SkinRarity.Exclusive, SkinRarity.Ultra };
        static readonly string[]     k_RarityNames  = { "SELECT", "DELUXE", "PREMIUM", "EXCLUSIVE", "ULTRA" };
        static readonly Color[]      k_RarityColors =
        {
            new Color(0.56f, 0.63f, 0.78f, 1f), // Select   — blue-gray
            new Color(0.07f, 0.58f, 1.00f, 1f), // Deluxe   — blue
            new Color(0.65f, 0.13f, 0.98f, 1f), // Premium  — purple
            new Color(0.86f, 0.16f, 0.26f, 1f), // Exclusive— red
            new Color(1.00f, 0.63f, 0.00f, 1f), // Ultra    — gold
        };

        void ApplyDarkBackground()
        {
            // Root full-screen background.
            var rootImg = GetComponent<Image>();
            if (rootImg != null)
            {
                rootImg.color         = BgStatic;
                rootImg.raycastTarget = false;
                Debug.Log("[CASE_BG] Static background applied: 0.031, 0.055, 0.102");
            }

            // Hero / case-display panel.
            if (caseDisplayPanel != null)
            {
                var pImg = caseDisplayPanel.GetComponent<Image>();
                if (pImg != null) { pImg.color = BgStatic; pImg.raycastTarget = false; Debug.Log("[CASE_BG] changed image=" + pImg.name); }
            }

            // SpinOverlay background — must stay flat even during a spin.
            if (spinOverlay != null)
            {
                var soImg = spinOverlay.GetComponent<Image>();
                if (soImg != null) { soImg.color = BgStatic; soImg.raycastTarget = false; Debug.Log("[CASE_BG] changed image=" + soImg.name); }
            }

            // Broad scan: any other light/opaque Image that is not a protected element.
            foreach (var img in GetComponentsInChildren<Image>(true))
            {
                if (img == null) continue;
                if (img == caseIconDisplay) continue;                     // case/skin icon
                if (img == caseThemeBg) continue;                        // rarity wash overlay
                if (openButton      != null && img.transform.IsChildOf(openButton.transform))      continue;
                if (_runtimeBackBtn != null && img.transform.IsChildOf(_runtimeBackBtn.transform)) continue;
                if (_spinFocusFrame != null && img.transform.IsChildOf(_spinFocusFrame.transform)) continue;
                if (skipButton      != null && img.transform.IsChildOf(skipButton.transform))      continue;
                if (_decorImages.Contains(img)) continue;

                // Recolour any image that is still noticeably light (avg > 0.35, alpha > 0.5).
                var c = img.color;
                if ((c.r + c.g + c.b) / 3f <= 0.35f || c.a <= 0.5f) continue;

                img.color         = BgStatic;
                img.raycastTarget = false;
                Debug.Log("[CASE_BG] changed image=" + img.gameObject.name);
            }

            Debug.Log("[CASE_BG] Disabled background pulse/glow animations");
        }

        void BuildNeonDecorations()
        {
            if (_decorBuilt) return;
            _decorBuilt = true;

            var parent = caseDisplayPanel != null ? caseDisplayPanel.transform : transform;

            var topLine = CreateDecorImage("NeonLine_TopBorder", parent);
            var topRt   = topLine.GetComponent<RectTransform>();
            topRt.anchorMin        = new Vector2(0f, 1f);
            topRt.anchorMax        = new Vector2(1f, 1f);
            topRt.pivot            = new Vector2(0.5f, 1f);
            topRt.anchoredPosition = Vector2.zero;
            topRt.sizeDelta        = new Vector2(0f, 2f);
            topLine.color          = new Color(NeonRed.r, NeonRed.g, NeonRed.b, 0.85f);

            var btmLine = CreateDecorImage("NeonLine_BottomBorder", parent);
            var btmRt   = btmLine.GetComponent<RectTransform>();
            btmRt.anchorMin        = new Vector2(0f, 0f);
            btmRt.anchorMax        = new Vector2(1f, 0f);
            btmRt.pivot            = new Vector2(0.5f, 0f);
            btmRt.anchoredPosition = Vector2.zero;
            btmRt.sizeDelta        = new Vector2(0f, 2f);
            btmLine.color          = new Color(NeonRed.r, NeonRed.g, NeonRed.b, 0.85f);

            var leftBand = CreateDecorImage("GlowBand_Left", parent);
            var leftRt   = leftBand.GetComponent<RectTransform>();
            leftRt.anchorMin        = new Vector2(0f, 0f);
            leftRt.anchorMax        = new Vector2(0f, 1f);
            leftRt.pivot            = new Vector2(0f, 0.5f);
            leftRt.anchoredPosition = Vector2.zero;
            leftRt.sizeDelta        = new Vector2(3f, 0f);
            leftBand.color          = new Color(NeonRed.r, NeonRed.g, NeonRed.b, 0.50f);

            var rightBand = CreateDecorImage("GlowBand_Right", parent);
            var rightRt   = rightBand.GetComponent<RectTransform>();
            rightRt.anchorMin        = new Vector2(1f, 0f);
            rightRt.anchorMax        = new Vector2(1f, 1f);
            rightRt.pivot            = new Vector2(1f, 0.5f);
            rightRt.anchoredPosition = Vector2.zero;
            rightRt.sizeDelta        = new Vector2(3f, 0f);
            rightBand.color          = new Color(NeonRed.r, NeonRed.g, NeonRed.b, 0.50f);
        }

        Image CreateDecorImage(string goName, Transform parent)
        {
            var go = new GameObject(goName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            _decorImages.Add(img);
            return img;
        }

        void ApplyPremiumDarkTheme()
        {
            var rootImg = GetComponent<Image>();
            if (rootImg != null) rootImg.color = BgPremium;

            if (caseDisplayPanel != null)
            {
                var pImg = caseDisplayPanel.GetComponent<Image>();
                if (pImg != null) pImg.color = PanelDark;
            }

            if (selectedCaseLabel != null)
            {
                selectedCaseLabel.color     = new Color(0.961f, 0.961f, 0.961f, 1f);
                selectedCaseLabel.fontStyle = FontStyles.Bold;
            }

            if (walletLabel != null)
                walletLabel.color = new Color(0.541f, 0.569f, 0.651f, 1f);
        }

        // One-time style pass for the mobile layout. Idempotent.
        void ApplyCaseOpeningMobileLayout()
        {
            if (_caseOpeningUiStyled) return;
            _caseOpeningUiStyled = true;
            AlignOpenButtonAndPrice();   // sets priceLabel y=-344, openButton y=-390
            ResizeCaseIcon();            // scales + nudges icon
            MoveHeroSectionUp();         // shifts everything up ~50 px to fill gap
            EnsureSpinFocusFrame();
        }

        // Shifts the entire hero block (icon, name, price, button) upward so the
        // screen does not leave empty space where the old top preview strip was.
        void MoveHeroSectionUp()
        {
            if (_heroMovedUp) return;
            _heroMovedUp = true;

            const float shift = 80f;

            if (caseIconDisplay != null)
            {
                var rt = caseIconDisplay.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition += new Vector2(0f, shift);
            }

            if (selectedCaseLabel != null)
                selectedCaseLabel.rectTransform.anchoredPosition += new Vector2(0f, shift);

            if (priceLabel != null)
                priceLabel.rectTransform.anchoredPosition += new Vector2(0f, shift);

            if (openButton != null)
            {
                var rt = openButton.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition += new Vector2(0f, shift);
            }

            // Also shift the RateTable container if it already exists.
            if (caseDisplayPanel != null)
            {
                var rateTf = caseDisplayPanel.transform.Find("RateTable");
                if (rateTf != null)
                {
                    var rt = rateTf as RectTransform;
                    if (rt != null) rt.anchoredPosition += new Vector2(0f, shift);
                }
            }

            Debug.Log("[CASE_HERO_LAYOUT] moved hero block upward by " + shift + " px");
            Debug.Log("[CASE_UI_FIX] hero layout moved up");
        }

        // Scale the case hero icon 1.8× from its prefab size and nudge it downward.
        bool _iconResized;
        void ResizeCaseIcon()
        {
            if (_iconResized || caseIconDisplay == null) return;
            _iconResized = true;

            var iconRt = caseIconDisplay.GetComponent<RectTransform>();
            if (iconRt == null) return;

            var cur = iconRt.sizeDelta;
            if (cur.x < 10f) cur = new Vector2(120f, 100f); // prefab fallback
            iconRt.sizeDelta        = new Vector2(cur.x * 1.8f, cur.y * 1.8f);
            var pos = iconRt.anchoredPosition;
            iconRt.anchoredPosition = new Vector2(pos.x, pos.y - 20f);

            Debug.Log("[CASE_HERO] case icon resized to " + iconRt.sizeDelta);
        }

        // Converts the drop list container into a 2-column grid for mobile.
        // Fully null-safe: never throws, never crashes the open flow.
        void EnsureDropGridLayout()
        {
            Debug.Log("[DROP_GRID] EnsureDropGridLayout called");

            if (dropListRoot == null) { Debug.LogWarning("[DROP_GRID] dropListRoot NULL — skip"); return; }

            var rootGo = dropListRoot.gameObject;
            var rt     = dropListRoot as RectTransform;
            if (rootGo == null) { Debug.LogWarning("[DROP_GRID] root GameObject NULL — skip"); return; }
            if (rt == null)     { Debug.LogWarning("[DROP_GRID] root is not a RectTransform — skip"); return; }

            Debug.Log("[DROP_GRID] root=" + dropListRoot.name);

            // Already converted — nothing to do.
            if (rootGo.GetComponent<GridLayoutGroup>() != null)
            {
                Debug.Log("[DROP_GRID] grid already present");
                return;
            }

            // Disable (do NOT destroy) any conflicting layout groups.
            var vlg = rootGo.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) vlg.enabled = false;
            var hlg = rootGo.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) hlg.enabled = false;

            // AddComponent can return null if another LayoutGroup still blocks it
            // (Unity disallows two LayoutGroups). Guard against that.
            var grid = rootGo.AddComponent<GridLayoutGroup>();
            if (grid == null)
            {
                Debug.LogWarning("[DROP_GRID] could not add GridLayoutGroup (another LayoutGroup blocks it) — skip");
                return;
            }

            grid.cellSize        = new Vector2(150f, 64f);
            grid.spacing         = new Vector2(8f, 8f);
            grid.padding         = new RectOffset(8, 8, 8, 8);
            grid.childAlignment  = TextAnchor.UpperCenter;
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;

            if (rootGo.GetComponent<ContentSizeFitter>() == null)
            {
                var fitter = rootGo.AddComponent<ContentSizeFitter>();
                if (fitter != null)
                    fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            Debug.Log("[DROP_GRID] Layout applied successfully");
        }

        // Red neon focus frame that flanks the center reel item during a spin.
        void EnsureSpinFocusFrame()
        {
            if (_spinFocusFrame != null) return;
            if (spinOverlay == null) return;

            var go = new GameObject("SpinFocusFrame", typeof(RectTransform));
            go.transform.SetParent(spinOverlay.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(190f, 250f);

            BuildFocusLine(rt, -95f);
            BuildFocusLine(rt,  95f);

            _spinFocusFrame = go;
            go.SetActive(false);
        }

        void BuildFocusLine(RectTransform parent, float x)
        {
            var go = new GameObject("FocusLine", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta        = new Vector2(4f, 250f);
            var img = go.GetComponent<Image>();
            img.color         = new Color(1f, 0.122f, 0.224f, 0.9f);
            img.raycastTarget = false;
        }

        // Hides legacy center-line/marker graphics so only the new frame shows.
        void HideOldSpinIndicators()
        {
            if (spinOverlay == null) return;
            foreach (var img in spinOverlay.GetComponentsInChildren<Image>(true))
            {
                if (img == null) continue;
                if (_spinFocusFrame != null && img.transform.IsChildOf(_spinFocusFrame.transform)) continue;

                var n = img.gameObject.name.ToLowerInvariant();

                // Never touch skin/icon/rarity images — only needle/pointer-style indicators.
                bool isSkinImage = n.Contains("icon")   || n.Contains("skin")   ||
                                   n.Contains("weapon")  || n.Contains("rarity") ||
                                   n.Contains("frame")   || n.Contains("glow")   ||
                                   n.Contains("reel")    || n.Contains("item");
                if (isSkinImage) continue;

                // Only disable legacy needle/pointer/center-line elements.
                bool isIndicator = n.Contains("centerline") || n.Contains("center_line") ||
                                   n.Contains("marker")     || n.Contains("indicator")   ||
                                   n.Contains("needle")     || n.Contains("pointer");
                if (isIndicator) img.enabled = false;
            }
        }
    }
}
