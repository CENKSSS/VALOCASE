using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValoCase.CaseOpening;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Pooling;
using ValoCase.Progression;
using ValoCase.Services;
using ValoCase.Services.Backend;

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
        CaseDetailResponse _detail;
        string _detailCaseId;
        int    _detailToken;
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

        // ── Redesigned open button + VP badge (runtime, revertible) ──────────────
        TextMeshProUGUI _openMainLabel;
        TextMeshProUGUI _openSubLabel;
        Image _vpBadgeBg;

        // ── Open quantity (1..5) + simultaneous multi-open ──────────────────────
        int _quantity = 1;
        TextMeshProUGUI _qtyLabel;
        RectTransform _qtySelector;
        bool _multiOpenActive;
        const int MaxQuantity = 5;

        sealed class ReviewCard
        {
            public SkinDefinitionSO skin;
            public bool sold;
            public bool sellInFlight;
            public TextMeshProUGUI nameLabel;
            public Button sellButton;
            public Button cardButton;
            public GameObject soldOverlay;
        }

        readonly List<ReviewCard> _reviewCards = new();
        Transform _reviewRoot;
        Button _sellAllButton;
        TextMeshProUGUI _reviewTotalLabel;

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
            _multiOpenActive = false;
            _quantity = 1;
            if (_qtyLabel != null) _qtyLabel.text = "1";
            if (_multiReelPanel != null) _multiReelPanel.SetActive(false);
            ClearReview();
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
            if (walletLabel != null) walletLabel.gameObject.SetActive(false);
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

            // ── Green VP pill badge ───────────────────────────────────────────
            var badgeGo = new GameObject("VpBadge", typeof(RectTransform), typeof(Image));
            badgeGo.transform.SetParent(priceLabel.transform.parent, false);
            _vpBadgeBg = badgeGo.GetComponent<Image>();
            _vpBadgeBg.sprite        = RoundedSprite();
            _vpBadgeBg.type          = Image.Type.Sliced;
            _vpBadgeBg.color         = new Color(0.094f, 0.62f, 0.31f, 1f);
            _vpBadgeBg.raycastTarget = false;
            var badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin        = new Vector2(0.5f, 1f);
            badgeRt.anchorMax        = new Vector2(0.5f, 1f);
            badgeRt.pivot            = new Vector2(0.5f, 1f);
            badgeRt.sizeDelta        = new Vector2(150f, 40f);
            if (badgeGo.GetComponent<Outline>() == null)
            {
                var badgeOutline = badgeGo.AddComponent<Outline>();
                badgeOutline.effectColor    = new Color(0.34f, 1f, 0.55f, 0.5f);
                badgeOutline.effectDistance = new Vector2(1f, -1f);
            }

            var priceRt = priceLabel.rectTransform;
            priceRt.SetParent(badgeGo.transform, false);
            priceRt.anchorMin        = Vector2.zero;
            priceRt.anchorMax        = Vector2.one;
            priceRt.offsetMin        = Vector2.zero;
            priceRt.offsetMax        = Vector2.zero;
            priceLabel.alignment     = TextAlignmentOptions.Center;
            priceLabel.fontStyle     = FontStyles.Bold;
            priceLabel.enableAutoSizing = false;
            priceLabel.fontSize      = 18f;
            priceLabel.color         = Color.white;
            priceLabel.raycastTarget = false;

            // ── Large premium red open button ─────────────────────────────────
            btnRt.anchorMin = new Vector2(0.5f, 1f);
            btnRt.anchorMax = new Vector2(0.5f, 1f);
            btnRt.pivot     = new Vector2(0.5f, 1f);
            btnRt.sizeDelta = new Vector2(300f, 72f);

            var btnImg = openButton.GetComponent<Image>();
            if (btnImg != null)
            {
                btnImg.sprite = RoundedSprite();
                btnImg.type   = Image.Type.Sliced;
                btnImg.color  = new Color(0.78f, 0.13f, 0.20f, 1f);
            }
            if (openButton.GetComponent<Outline>() == null)
            {
                var btnOutline = openButton.gameObject.AddComponent<Outline>();
                btnOutline.effectColor    = new Color(1f, 0.275f, 0.333f, 1f);
                btnOutline.effectDistance = new Vector2(1.5f, -1.5f);
            }

            _openMainLabel = openButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (_openMainLabel != null)
            {
                var mainRt = _openMainLabel.rectTransform;
                mainRt.anchorMin = new Vector2(0f, 0.42f);
                mainRt.anchorMax = new Vector2(1f, 1f);
                mainRt.offsetMin = Vector2.zero;
                mainRt.offsetMax = Vector2.zero;
                _openMainLabel.text          = "OPEN CASE";
                _openMainLabel.fontStyle     = FontStyles.Bold;
                _openMainLabel.alignment     = TextAlignmentOptions.Bottom;
                _openMainLabel.enableAutoSizing = false;
                _openMainLabel.fontSize      = 22f;
                _openMainLabel.color         = new Color(0.97f, 0.97f, 0.97f, 1f);
                _openMainLabel.raycastTarget = false;
            }

            var subGo = new GameObject("OpenSubLabel", typeof(RectTransform));
            subGo.transform.SetParent(openButton.transform, false);
            var subRt = subGo.GetComponent<RectTransform>();
            subRt.anchorMin = new Vector2(0f, 0f);
            subRt.anchorMax = new Vector2(1f, 0.42f);
            subRt.offsetMin = Vector2.zero;
            subRt.offsetMax = Vector2.zero;
            _openSubLabel = subGo.AddComponent<TextMeshProUGUI>();
            _openSubLabel.alignment         = TextAlignmentOptions.Top;
            _openSubLabel.enableAutoSizing   = false;
            _openSubLabel.fontSize           = 12f;
            _openSubLabel.fontStyle          = FontStyles.Bold;
            _openSubLabel.color              = new Color(1f, 0.86f, 0.88f, 0.9f);
            _openSubLabel.raycastTarget      = false;
            _openSubLabel.enableWordWrapping = false;

            AddPressScale(openButton);
            BuildQuantitySelector(openButton.transform.parent);

            _openButtonAligned = true;
        }

        // Compact [−] qty [+] stepper placed beside the open button (1..5).
        void BuildQuantitySelector(Transform parent)
        {
            var go = new GameObject("QuantitySelector", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _qtySelector = go.GetComponent<RectTransform>();
            _qtySelector.anchorMin = new Vector2(0.5f, 1f);
            _qtySelector.anchorMax = new Vector2(0.5f, 1f);
            _qtySelector.pivot     = new Vector2(0.5f, 1f);
            _qtySelector.sizeDelta = new Vector2(108f, 72f);

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childControlWidth      = false;
            hlg.childControlHeight     = false;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleCenter;

            MakeQtyButton(go.transform, "−", -1);

            var lblGo = new GameObject("QtyValue", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            ((RectTransform)lblGo.transform).sizeDelta = new Vector2(36f, 56f);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 36f;
            _qtyLabel = lblGo.AddComponent<TextMeshProUGUI>();
            _qtyLabel.text          = _quantity.ToString();
            _qtyLabel.fontSize      = 24f;
            _qtyLabel.fontStyle     = FontStyles.Bold;
            _qtyLabel.alignment     = TextAlignmentOptions.Center;
            _qtyLabel.color         = new Color(0.96f, 0.96f, 0.96f, 1f);
            _qtyLabel.raycastTarget = false;

            MakeQtyButton(go.transform, "+", 1);
        }

        void MakeQtyButton(Transform parent, string glyph, int delta)
        {
            var go = new GameObject("Qty" + (delta < 0 ? "Minus" : "Plus"),
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(44f, 44f);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth  = 44f;
            le.preferredHeight = 44f;

            var img = go.GetComponent<Image>();
            img.color = new Color(0.10f, 0.03f, 0.06f, 1f);
            var outline = go.AddComponent<Outline>();
            outline.effectColor    = new Color(1f, 0.275f, 0.333f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);

            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => ChangeQuantity(delta));

            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lrt = (RectTransform)lblGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text          = glyph;
            lbl.fontSize      = 26f;
            lbl.fontStyle     = FontStyles.Bold;
            lbl.alignment     = TextAlignmentOptions.Center;
            lbl.color         = new Color(0.96f, 0.96f, 0.96f, 1f);
            lbl.raycastTarget = false;
        }

        void ChangeQuantity(int delta)
        {
            if (_multiOpenActive) return;
            _quantity = Mathf.Clamp(_quantity + delta, 1, MaxQuantity);
            if (_qtyLabel != null) _qtyLabel.text = _quantity.ToString();
            UpdateOpenButtonTexts();
            RefreshOpenButton();
        }

        // Built-in EventTrigger press feedback — never interferes with onClick.
        void AddPressScale(Button btn)
        {
            if (btn == null) return;
            var trigger = btn.GetComponent<EventTrigger>();
            if (trigger == null) trigger = btn.gameObject.AddComponent<EventTrigger>();

            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => { if (btn != null) btn.transform.localScale = new Vector3(0.96f, 0.96f, 1f); });
            trigger.triggers.Add(down);

            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            up.callback.AddListener(_ => { if (btn != null) btn.transform.localScale = Vector3.one; });
            trigger.triggers.Add(up);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => { if (btn != null) btn.transform.localScale = Vector3.one; });
            trigger.triggers.Add(exit);
        }

        void UpdateOpenButtonTexts()
        {
            if (_openSubLabel == null) return;
            var total = SelectedUnitPrice() * _quantity;
            _openSubLabel.text = $"{total:N0} VP HARCANACAK";
        }

        protected override void OnHidden()
        {
            AutoClaimReview();
            _multiOpenActive = false;
            SetNavLock(false);
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
            if (_multiOpenActive) return;
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

            // Keep a single back control: restyle the existing top button into a clean
            // arrow. Only build a runtime one if the serialized button is missing.
            if (backButton != null)
            {
                StyleAsBackArrow(backButton);
                return;
            }

            var go = new GameObject("BackButton_Runtime",
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            _runtimeBackBtn = go;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(0f, 1f);
            rt.pivot            = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, -56f);
            rt.sizeDelta        = new Vector2(52f, 52f);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.05f, 0.08f, 0.16f, 1f);

            var outline = go.AddComponent<Outline>();
            outline.effectColor    = new Color(1f, 0.122f, 0.224f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var btn = go.GetComponent<Button>();
            var bc  = btn.colors;
            bc.normalColor      = Color.white;
            bc.highlightedColor = new Color(1f, 0.85f, 0.85f, 1f);
            bc.pressedColor     = new Color(0.75f, 0.50f, 0.50f, 1f);
            btn.colors = bc;
            btn.onClick.AddListener(OnBack);
            backButton = btn;

            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = Vector2.zero;
            lblRt.offsetMax = Vector2.zero;
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text               = "←";
            lbl.fontSize           = 30f;
            lbl.fontStyle          = FontStyles.Bold;
            lbl.alignment          = TextAlignmentOptions.Center;
            lbl.color              = new Color(0.925f, 0.910f, 0.882f, 1f);
            lbl.enableWordWrapping = false;
            lbl.raycastTarget      = false;

            go.transform.SetAsLastSibling();
        }

        void StyleAsBackArrow(Button btn)
        {
            var rt = (RectTransform)btn.transform;
            rt.sizeDelta = new Vector2(52f, rt.sizeDelta.y);

            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (lbl != null)
            {
                lbl.text = "←";
                lbl.fontSize = 30f;
                lbl.alignment = TextAlignmentOptions.Center;
                lbl.characterSpacing = 0f;
            }
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

        void OnVpChanged(int _, int __)
        {
            RefreshWallet();
            RefreshOpenButton();
        }

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
            _detail = null;
            _detailCaseId = null;
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

            // Page background stays the navbar dark navy — never tinted by case rarity.
            if (caseThemeBg != null) caseThemeBg.color = NavBarNavy;

            if (selectedCaseLabel != null)
            {
                var dn = !string.IsNullOrEmpty(caseDef.DisplayName) ? caseDef.DisplayName : caseDef.CaseId ?? "";
                selectedCaseLabel.text = dn.ToUpperInvariant();
            }

            var price = caseDef.VpPrice;
            if (priceLabel != null) priceLabel.text = $"{price:N0} VP";

            RefreshOpenButton();
            UpdateOpenButtonTexts();
            BuildDropList(caseDef);
            FetchDetailForSelected();
        }

        bool HasDetailForSelected => _detail != null && _selected != null && _detailCaseId == _selected.CaseId;

        int SelectedUnitPrice() => HasDetailForSelected && _detail.priceVp > 0
            ? _detail.priceVp
            : (_selected != null ? _selected.VpPrice : 0);

        void FetchDetailForSelected()
        {
            var ctx = GameContext.Instance;
            if (_selected == null || ctx == null || !ctx.BackendEnabled) return;

            int token = ++_detailToken;
            string id = _selected.CaseId;
            ctx.FetchCaseDetailBackend(id,
                d =>
                {
                    if (this == null || token != _detailToken) return;
                    _detail = d;
                    _detailCaseId = id;
                    ApplyDetail();
                },
                _ => { });
        }

        void ApplyDetail()
        {
            if (!HasDetailForSelected) return;
            if (priceLabel != null) priceLabel.text = $"{SelectedUnitPrice():N0} VP";
            BuildRateTable(_selected);
            UpdateOpenButtonTexts();
            RefreshOpenButton();
        }

        void InvalidateSelectedDetail()
        {
            _detailToken++;
            _detail = null;
            _detailCaseId = null;
        }

        bool DetailLocksSelected(out int requiredLevel, out string reason)
        {
            requiredLevel = 0;
            reason = null;
            if (!HasDetailForSelected || _detail.canOpen) return false;

            requiredLevel = _detail.requiredLevel;
            reason = _detail.lockedReason;
            if (reason == "CASE_INACTIVE") return true;

            int current = _detail.currentLevel > 0 ? _detail.currentLevel : PlayerProgression.Level;
            if (requiredLevel > 0 && requiredLevel > current) return true;

            if (string.IsNullOrEmpty(reason)) return false;
            return reason.IndexOf("LEVEL", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("LOCK", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool CanOpenSelectedCase()
        {
            if (_selected == null) return false;
            if (DetailLocksSelected(out _, out _)) return false;
            if (GameContext.Instance?.BackendEnabled == true)
                return PlayerProgression.IsCaseUnlocked(_selected.CaseId);
            return GameContext.Instance?.CaseProgression?.IsCaseUnlocked(_selected) ?? true;
        }

        void RefreshOpenButton()
        {
            if (openButton == null) return;

            bool sessionBusy = (flow != null && flow.SessionActive) || _multiOpenActive;
            bool lockedByDetail = DetailLocksSelected(out var requiredLevel, out var lockReason);
            bool canOpenCase = CanOpenSelectedCase();
            int  balance     = GameContext.Instance?.Vp?.Balance ?? 0;
            int  total       = SelectedUnitPrice() * _quantity;
            bool affordable  = balance >= total;

            bool openable = _selected != null && !sessionBusy && canOpenCase && affordable;
            openButton.interactable = openable;

            var btnLabel = _openMainLabel != null
                ? _openMainLabel
                : openButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (btnLabel != null)
            {
                if (lockedByDetail || (_selected != null && !canOpenCase))
                    btnLabel.text = lockReason == "CASE_INACTIVE" ? "KULLANILAMIYOR"
                                  : requiredLevel > 0 ? $"SEVİYE {requiredLevel}" : "KİLİTLİ";
                else if (_selected != null && !sessionBusy && !affordable)
                    btnLabel.text = "YETERSİZ VP";
                else
                    btnLabel.text = "OPEN CASE";
            }
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
            bool hasBackend = _detail != null && caseDef != null &&
                              _detailCaseId == caseDef.CaseId && _detail.drops != null;
            if (caseDef == null || (caseDef.DropTable == null && !hasBackend)) return;

            float[] rates = RarityRatesFor(caseDef);

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
                cRt.anchorMin        = new Vector2(0.06f, 1f);
                cRt.anchorMax        = new Vector2(0.94f, 1f);
                cRt.pivot            = new Vector2(0.5f, 1f);
                cRt.anchoredPosition = new Vector2(0f, -648f);
                // title 30 + 5 rows×46 + padding(14+14) + spacing(8×5) = 328
                cRt.sizeDelta        = new Vector2(0f, 328f);
                var bg = cGo.GetComponent<Image>();
                bg.sprite        = RoundedSprite();
                bg.type          = Image.Type.Sliced;
                bg.color         = PanelDark;
                bg.raycastTarget = false;
                var tblOutline = cGo.AddComponent<Outline>();
                tblOutline.effectColor    = new Color(NeonCyan.r, NeonCyan.g, NeonCyan.b, 0.35f);
                tblOutline.effectDistance = new Vector2(1f, -1f);
                var vlg = cGo.AddComponent<VerticalLayoutGroup>();
                vlg.childControlWidth      = true;
                vlg.childForceExpandWidth  = true;
                vlg.childControlHeight     = false;
                vlg.childForceExpandHeight = false;
                vlg.spacing = 8f;
                vlg.padding = new RectOffset(14, 14, 14, 14);
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
            MakeRateTableRow(containerTf, "Title", "KASA ORANLARI", "",
                             Color.white, Color.white, isTitle: true, height: 30f);

            // ── 5 Rarity rows (always created, never skipped) ─────────────────
            for (int i = 0; i < k_RarityOrder.Length; i++)
                MakeRarityOddsRow(containerTf, "Row_" + i, k_RarityOrder[i],
                                  k_RarityNames[i], k_RarityColors[i], rates[i], height: 46f);

            int newChildCount = containerTf.childCount;
            Debug.Log("[CASE_RARITY_TABLE] rows after=" + newChildCount);
            Debug.Log("[CASE_RARITY_TABLE] selected case=" + caseDef.DisplayName);
            Debug.Log("[CASE_UI_FIX] rarity table fixed with 5 rows");
        }

        // Returns the weight% for a rarity from the drop table (0 when missing).
        static float GetRarityRate(CaseDefinitionSO caseDef, SkinRarity rarity) =>
            caseDef.DropTable?.RarityWeights?
                .FirstOrDefault(w => w.rarity == rarity)?.weightPercent ?? 0f;

        // Backend drops are authoritative when present (already exclude inactive/zero-weight);
        // their per-skin dropChance is summed per rarity. Local weights are the fallback.
        float[] RarityRatesFor(CaseDefinitionSO caseDef)
        {
            if (_detail != null && caseDef != null && _detailCaseId == caseDef.CaseId && _detail.drops != null)
            {
                var rates = new float[k_RarityOrder.Length];
                foreach (var d in _detail.drops)
                {
                    if (d == null || string.IsNullOrEmpty(d.rarity)) continue;
                    if (!Enum.TryParse<SkinRarity>(d.rarity, true, out var parsed)) continue;
                    int idx = Array.IndexOf(k_RarityOrder, parsed);
                    if (idx >= 0) rates[idx] += d.dropChance;   // backend dropChance is a 0–1 fraction
                }
                for (int i = 0; i < rates.Length; i++) rates[i] *= 100f;
                return rates;
            }

            var local = new float[k_RarityOrder.Length];
            for (int i = 0; i < k_RarityOrder.Length; i++)
                local[i] = GetRarityRate(caseDef, k_RarityOrder[i]);
            return local;
        }

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

        // Premium odds row: rarity icon, name, progress bar (track + colored fill),
        // and percentage. Fill width is the live rate; no hardcoded values.
        void MakeRarityOddsRow(Transform parent, string rowName, SkinRarity rarity,
                               string displayName, Color color, float percent, float height)
        {
            var row = new GameObject(rowName, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            ((RectTransform)row.transform).sizeDelta = new Vector2(0f, height);
            var rle = row.AddComponent<LayoutElement>();
            rle.preferredHeight = height;
            rle.minHeight       = height;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(row.transform, false);
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.anchorMin        = new Vector2(0f, 0.5f);
            iconRt.anchorMax        = new Vector2(0f, 0.5f);
            iconRt.pivot            = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(2f, 0f);
            iconRt.sizeDelta        = new Vector2(34f, 34f);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.raycastTarget  = false;
            iconImg.preserveAspect = true;
            if (RaritySymbolLoader.TryGet(rarity, out var symbol) && symbol != null)
            {
                iconImg.sprite = symbol;
                iconImg.color  = Color.white;
            }
            else
            {
                iconImg.enabled = false;
            }

            var nGo = new GameObject("Name", typeof(RectTransform));
            nGo.transform.SetParent(row.transform, false);
            var nRt = (RectTransform)nGo.transform;
            nRt.anchorMin        = new Vector2(0f, 0f);
            nRt.anchorMax        = new Vector2(0f, 1f);
            nRt.pivot            = new Vector2(0f, 0.5f);
            nRt.anchoredPosition = new Vector2(44f, 0f);
            nRt.sizeDelta        = new Vector2(120f, 0f);
            var nTmp = nGo.AddComponent<TextMeshProUGUI>();
            nTmp.text               = displayName;
            nTmp.fontSize           = 13f;
            nTmp.fontStyle          = FontStyles.Bold;
            nTmp.color              = color;
            nTmp.alignment          = TextAlignmentOptions.Left;
            nTmp.raycastTarget      = false;
            nTmp.enableWordWrapping = false;

            var track = new GameObject("BarTrack", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(row.transform, false);
            var trackRt = (RectTransform)track.transform;
            trackRt.anchorMin = new Vector2(0f, 0.5f);
            trackRt.anchorMax = new Vector2(1f, 0.5f);
            trackRt.pivot     = new Vector2(0.5f, 0.5f);
            trackRt.offsetMin = new Vector2(170f, -6f);
            trackRt.offsetMax = new Vector2(-64f, 6f);
            var trackImg = track.GetComponent<Image>();
            trackImg.sprite        = RoundedSprite();
            trackImg.type          = Image.Type.Sliced;
            trackImg.color         = new Color(0.07f, 0.09f, 0.13f, 1f);
            trackImg.raycastTarget = false;

            var fill = new GameObject("BarFill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            var fillRt = (RectTransform)fill.transform;
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(Mathf.Clamp01(percent / 100f), 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fillImg = fill.GetComponent<Image>();
            fillImg.sprite        = RoundedSprite();
            fillImg.type          = Image.Type.Sliced;
            fillImg.color         = color;
            fillImg.raycastTarget = false;
            var fillGlow = fill.AddComponent<Outline>();
            fillGlow.effectColor    = new Color(color.r, color.g, color.b, 0.5f);
            fillGlow.effectDistance = new Vector2(1.5f, -1.5f);

            var pGo = new GameObject("Pct", typeof(RectTransform));
            pGo.transform.SetParent(row.transform, false);
            var pRt = (RectTransform)pGo.transform;
            pRt.anchorMin        = new Vector2(1f, 0f);
            pRt.anchorMax        = new Vector2(1f, 1f);
            pRt.pivot            = new Vector2(1f, 0.5f);
            pRt.anchoredPosition = new Vector2(-2f, 0f);
            pRt.sizeDelta        = new Vector2(58f, 0f);
            var pTmp = pGo.AddComponent<TextMeshProUGUI>();
            pTmp.text               = "%" + percent.ToString("F0");
            pTmp.fontSize           = 13f;
            pTmp.fontStyle          = FontStyles.Bold;
            pTmp.color              = new Color(0.92f, 0.94f, 0.98f, 1f);
            pTmp.alignment          = TextAlignmentOptions.Right;
            pTmp.raycastTarget      = false;
            pTmp.enableWordWrapping = false;
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

            // Client-side lock guard (backend stays authoritative; this just avoids
            // starting an open the server would reject with 403). Show the unlock level.
            if (!PlayerProgression.IsCaseUnlocked(_selected.CaseId))
            {
                int req = PlayerProgression.RequiredLevelForCaseId(_selected.CaseId);
                GameEvents.RaiseToast($"Seviye {req}'te açılır");
                return;
            }

            var ctx = GameContext.Instance;
            int total = SelectedUnitPrice() * _quantity;
            int balance = ctx?.Vp?.Balance ?? 0;
            if (!CanOpenSelectedCase() || balance < total) { RefreshOpenButton(); return; }
            if (ctx?.BackendEnabled != true && ctx?.CaseOpening?.CanOpen(_selected) != true) { RefreshOpenButton(); return; }

            // All quantities (including 1) spin inside the rates-panel area.
            StartMultiOpen(_quantity);
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

        // ── Simultaneous multi-open (quantity > 1) ──────────────────────────────
        // Results are acquired up front (authoritative, via the same service/backend
        // calls a single open uses), then N reel rows spin in sync and reveal together.
        // The single CaseSpinController path (quantity == 1) is never touched.

        sealed class MultiRow
        {
            public RectTransform root;
            public RectTransform content;
            public readonly List<ReelItemView> items = new();
        }

        readonly List<MultiRow> _multiRows = new();
        GameObject _multiReelPanel;
        Transform _multiRowsRoot;
        int _multiWinnerIndex;
        // One random landing offset SHARED by every row, re-rolled each open: all rows
        // stay perfectly in sync (same spot under their triangle), but that spot shifts
        // across the winning card from one open to the next.
        float _multiLandingJitter;
        const float MultiItemWidth = 132f;
        const float MultiRowHeight = 176f;
        // Keeps the shared random landing within the card (±40% of the card width) so
        // the winner is always unambiguously the card under the triangle.
        const float MultiMarkerJitterFraction = 0.8f;

        void StartMultiOpen(int qty)
        {
            if (_multiOpenActive) return;
            AutoClaimReview();
            _multiOpenActive = true;
            SetNavLock(true);
            if (openButton != null) openButton.interactable = false;
            StartCoroutine(MultiOpenRoutine(qty));
        }

        GameObject _inputShield;

        void SetNavLock(bool locked)
        {
            if (navigator != null) navigator.NavigationLocked = locked;
            EnsureInputShield();
            if (_inputShield != null)
            {
                _inputShield.SetActive(locked);
                if (locked) _inputShield.transform.SetAsLastSibling();
            }
        }

        // Full-screen raycast blocker over the whole canvas (incl. bottom navbar) so no
        // navigation/menu button can be clicked, highlighted, or queued while opening.
        void EnsureInputShield()
        {
            if (_inputShield != null) return;
            var canvas = GetComponentInParent<Canvas>();
            var parent = canvas != null ? canvas.transform : transform;
            var go = new GameObject("OpeningInputShield", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;
            go.SetActive(false);
            _inputShield = go;
        }

        // Leaving with rewards still on screen never loses them: unsold rewards were
        // already granted to inventory at open; sold rewards stay sold.
        void AutoClaimReview()
        {
            if (_reviewRoot == null) return;
            ClearReview();
            RestoreRateTable();
        }

        IEnumerator MultiOpenRoutine(int qty)
        {
            var winners = new List<SkinDefinitionSO>();
            var spents  = new List<int>();
            bool backend = GameContext.Instance?.BackendEnabled == true;

            if (backend) yield return AcquireBackendWinners(qty, winners, spents);
            else         AcquireLocalWinners(qty, winners, spents);

            int n = winners.Count;
            if (n == 0) { EndMultiOpen(0); yield break; }

            HideRateTable();
            BuildMultiRows(winners);
            PositionReelPanelToRates(n);
            ShowMultiReelPanel(true);
            yield return AnimateMultiRows();

            // Unsold rewards are granted via the existing inventory flow up front; the
            // review's SELL then removes a unit via the existing economy. A sold reward
            // therefore ends up sold (VP), never also kept — backend stays authoritative.
            GrantMultiWinners(backend, winners, spents);

            yield return new WaitForSecondsRealtime(0.5f);

            ClearMultiRows();
            RefreshWallet();

            if (n == 1)
            {
                ShowMultiReelPanel(false);
                RestoreRateTable();
                _multiOpenActive = false;
                SetNavLock(false);
                EnsureInteractive();
                RefreshOpenButton();
                ShowSingleResultPopup(winners[0]);
            }
            else
            {
                BuildRewardReview(winners);
                // Review stays open but the screen is free again — opening another case
                // is allowed; unsold rewards are already safely in inventory.
                _multiOpenActive = false;
                SetNavLock(false);
                EnsureInteractive();
                RefreshOpenButton();
            }
        }

        void ShowSingleResultPopup(SkinDefinitionSO skin)
        {
            var popup = ValoCase.UI.SkinWinPopup.EnsureExists();
            if (popup != null)
                popup.Show(skin, OnResultPopupClosed);
        }

        void OnResultPopupClosed()
        {
            _showingResult = false;
            _multiOpenActive = false;
            SetNavLock(false);
            EnsureInteractive();
            if (openButton != null) openButton.gameObject.SetActive(true);
            RefreshWallet();
            InvalidateSelectedDetail();
            UpdateOpenButtonTexts();
            RefreshOpenButton();
            FetchDetailForSelected();
        }

        Transform _rateTable;
        void HideRateTable()
        {
            if (caseDisplayPanel == null) return;
            _rateTable = caseDisplayPanel.transform.Find("RateTable");
            if (_rateTable != null) _rateTable.gameObject.SetActive(false);
        }

        void RestoreRateTable()
        {
            if (_rateTable != null) _rateTable.gameObject.SetActive(true);
        }

        // Places the reel panel in the rates panel's slot: same top anchor and width;
        // height grows downward to fit the stacked rows.
        void PositionReelPanelToRates(int rows)
        {
            EnsureMultiReelPanel();
            var rt = (RectTransform)_multiReelPanel.transform;
            rt.anchorMin = new Vector2(0.06f, 1f);
            rt.anchorMax = new Vector2(0.94f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -648f);
            float height = rows * MultiRowHeight + Mathf.Max(0, rows - 1) * 12f + 16f;
            rt.sizeDelta = new Vector2(0f, height);
        }

        void AcquireLocalWinners(int qty, List<SkinDefinitionSO> winners, List<int> spents)
        {
            var svc = GameContext.Instance?.CaseOpening;
            if (svc == null) return;
            for (int i = 0; i < qty; i++)
            {
                if (!svc.CanOpen(_selected)) break;
                if (svc.TryBeginOpen(_selected, out var skin, out var spent) && skin != null)
                {
                    winners.Add(skin);
                    spents.Add(spent);
                }
                else break;
            }
        }

        // Mirrors the single backend open (request → map → authoritative wallet),
        // gathering each result before the synchronized reveal. Grant happens later.
        IEnumerator AcquireBackendWinners(int qty, List<SkinDefinitionSO> winners, List<int> spents)
        {
            var ctx = GameContext.Instance;
            if (ctx?.Backend == null || !ctx.BackendReady) yield break;

            for (int i = 0; i < qty; i++)
            {
                if (BackendErrorMapper.IsOffline) break;

                OpenCaseResultResponse response = null;
                BackendError error = null;
                yield return ctx.Backend.OpenCase(_selected.CaseId, r => response = r, e => error = e);

                if (error != null || response == null)
                {
                    if (error == null || error.HttpStatus == 0) ctx.RequestBackendResync();
                    // A 403 locked-category error carries the exact unlock level — show it
                    // instead of the generic forbidden message, and do not spend/animate.
                    var msg = (error != null && error.IsLockedCategory)
                        ? $"Seviye {error.RequiredLevel}'te açılır"
                        : BackendErrorMapper.Map(error);
                    if (!string.IsNullOrEmpty(msg)) GameEvents.RaiseToast(msg);
                    break;
                }

                var mapped = BackendResultMapper.ToCaseOpeningResult(response);
                var skin   = ctx.Content != null ? ctx.Content.GetSkin(mapped?.RolledSkinId) : null;
                ctx.ApplyBackendWallet(response.newVpBalance);
                // Backend confirmed the open — mirror its level/XP into the UI cache.
                // Per-open "+5 XP" toast only for a single open; level-up always shows.
                ProgressionSync.ApplyFromOpen(response, showXpToast: qty == 1);
                if (skin == null) { ctx.RequestBackendResync(); break; }

                winners.Add(skin);
                spents.Add(_selected.VpPrice);
            }
        }

        void GrantMultiWinners(bool backend, List<SkinDefinitionSO> winners, List<int> spents)
        {
            var ctx = GameContext.Instance;
            if (ctx?.CaseOpening == null) return;
            for (int i = 0; i < winners.Count; i++)
            {
                if (backend) ctx.CaseOpening.CompleteOpenFromBackend(_selected, winners[i], spents[i]);
                else         ctx.CaseOpening.CompleteOpen(_selected, winners[i], spents[i]);
            }
            ctx.Statistics?.RecalculateInventoryStats(ctx.Inventory, ctx.Content);
            ctx.Save?.Save();
        }

        void EndMultiOpen(int opened)
        {
            _multiOpenActive = false;
            SetNavLock(false);
            ClearMultiRows();
            ClearReview();
            ShowMultiReelPanel(false);
            RestoreRateTable();
            EnsureInteractive();
            RefreshWallet();
            RefreshOpenButton();
        }

        // ── Multi-reel rows (N simultaneous strips) ──────────────────────────────

        void EnsureMultiReelPanel()
        {
            if (_multiReelPanel != null) return;

            var parent = caseDisplayPanel != null ? caseDisplayPanel.transform : transform;
            var go = new GameObject("MultiReelPanel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = PanelDark;

            var rowsGo = new GameObject("Rows", typeof(RectTransform));
            rowsGo.transform.SetParent(go.transform, false);
            var rrt = (RectTransform)rowsGo.transform;
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot     = new Vector2(0.5f, 1f);
            rrt.offsetMin = new Vector2(8f, rrt.offsetMin.y);
            rrt.offsetMax = new Vector2(-8f, rrt.offsetMax.y);
            rrt.anchoredPosition = new Vector2(0f, -8f);
            var vlg = rowsGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth      = true;
            vlg.childForceExpandWidth   = true;
            vlg.childControlHeight     = false;
            vlg.childForceExpandHeight  = false;
            var csf = rowsGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _multiRowsRoot = rowsGo.transform;

            go.SetActive(false);
            _multiReelPanel = go;
        }

        void BuildMultiRows(List<SkinDefinitionSO> winners)
        {
            EnsureMultiReelPanel();
            ClearMultiRows();
            if (PoolManager.Instance == null) return;

            int total = GameConstants.ReelPaddingItems + GameConstants.ReelVisibleItemCount;
            _multiWinnerIndex = total - Mathf.CeilToInt(GameConstants.ReelVisibleItemCount / 2f) - 2;
            // Roll ONE shared landing offset for every row this open (synced rows, varies per open).
            _multiLandingJitter = UnityEngine.Random.Range(-0.5f, 0.5f) * MultiItemWidth * MultiMarkerJitterFraction;
            var visuals = GameContext.Instance?.RarityVisuals;

            foreach (var winner in winners)
            {
                var rowGo = new GameObject("ReelRow", typeof(RectTransform));
                rowGo.transform.SetParent(_multiRowsRoot, false);
                ((RectTransform)rowGo.transform).sizeDelta = new Vector2(0f, MultiRowHeight);
                rowGo.AddComponent<LayoutElement>().preferredHeight = MultiRowHeight;

                var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
                vpGo.transform.SetParent(rowGo.transform, false);
                var vrt = (RectTransform)vpGo.transform;
                vrt.anchorMin = Vector2.zero;
                vrt.anchorMax = Vector2.one;
                vrt.offsetMin = Vector2.zero;
                vrt.offsetMax = Vector2.zero;

                var contentGo = new GameObject("Content", typeof(RectTransform));
                contentGo.transform.SetParent(vpGo.transform, false);
                var crt = (RectTransform)contentGo.transform;
                crt.anchorMin = new Vector2(0.5f, 0.5f);
                crt.anchorMax = new Vector2(0.5f, 0.5f);
                crt.pivot     = new Vector2(0f, 0.5f);
                crt.anchoredPosition = Vector2.zero;

                var row = new MultiRow { root = (RectTransform)rowGo.transform, content = crt };
                var strip = CaseReelBuilder.BuildReelStrip(_selected, winner, total, _multiWinnerIndex);
                for (int i = 0; i < strip.Count; i++)
                {
                    var view = PoolManager.Instance.GetReelItem();
                    view.transform.SetParent(crt, false);
                    view.Bind(strip[i], visuals);
                    view.RectTransform.anchoredPosition = new Vector2(i * MultiItemWidth, 0f);
                    row.items.Add(view);
                }
                _multiRows.Add(row);

                BuildRowMarker(rowGo.transform);
            }
        }

        // Per-row marker matching the single-case triangle: top-center of its row,
        // apex on the card's top edge, pointing down. Sits above the masked viewport.
        void BuildRowMarker(Transform rowParent)
        {
            var go = new GameObject("RowTriangle", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(rowParent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(20f, 16f);
            rt.anchoredPosition = new Vector2(0f, MultiRowHeight * 0.5f - 4f);
            var img = go.GetComponent<Image>();
            img.sprite        = TriangleSprite();
            img.color         = NeonRed;
            img.raycastTarget = false;
        }

        IEnumerator AnimateMultiRows()
        {
            float dur    = GameConstants.CaseSpinDurationSeconds;
            float target = -(_multiWinnerIndex * MultiItemWidth);
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / dur);
                float eased = 1f - Mathf.Pow(1f - p, 5f);
                // Every row shares the same offset this frame, so all triangles stay in
                // perfect sync — but the shared landing spot is random each open.
                float x = Mathf.Lerp(0f, target + _multiLandingJitter, eased);
                foreach (var row in _multiRows)
                    if (row.content != null)
                        row.content.anchoredPosition = new Vector2(x, 0f);
                yield return null;
            }
            float finalX = target + _multiLandingJitter;
            foreach (var row in _multiRows)
                if (row.content != null)
                    row.content.anchoredPosition = new Vector2(finalX, 0f);
        }

        void ClearMultiRows()
        {
            var pm = PoolManager.Instance;
            var poolRoot = pm != null ? pm.transform : null;
            foreach (var row in _multiRows)
            {
                if (row?.items != null)
                    foreach (var item in row.items)
                    {
                        if (item == null) continue;
                        pm?.ReleaseReelItem(item);
                        // Detach from the row before it is destroyed so pooled items survive.
                        if (poolRoot != null) item.transform.SetParent(poolRoot, false);
                    }
                if (row?.root != null) Destroy(row.root.gameObject);
            }
            _multiRows.Clear();
        }

        void ShowMultiReelPanel(bool show)
        {
            EnsureMultiReelPanel();
            _multiReelPanel.SetActive(show);
            if (show) _multiReelPanel.transform.SetAsLastSibling();
        }

        // ── Reward review (replaces the reel in the same panel area) ─────────────

        static readonly Color SoldDim = new Color(0f, 0f, 0f, 0.62f);

        void BuildRewardReview(List<SkinDefinitionSO> winners)
        {
            EnsureMultiReelPanel();
            ClearReview();

            int n    = winners.Count;
            int cols = n <= 4 ? 2 : 3;
            int rows = Mathf.CeilToInt(n / (float)cols);
            const float cellH = 196f, spacing = 10f, pad = 12f, bulkH = 64f;

            var prt = (RectTransform)_multiReelPanel.transform;
            prt.anchorMin = new Vector2(0.06f, 1f);
            prt.anchorMax = new Vector2(0.94f, 1f);
            prt.pivot     = new Vector2(0.5f, 1f);
            prt.anchoredPosition = new Vector2(0f, -648f);
            float gridH = rows * cellH + Mathf.Max(0, rows - 1) * spacing;
            prt.sizeDelta = new Vector2(0f, pad + gridH + 12f + bulkH + pad);

            var rootGo = new GameObject("Review", typeof(RectTransform));
            rootGo.transform.SetParent(_multiReelPanel.transform, false);
            var rrt = (RectTransform)rootGo.transform;
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = new Vector2(pad, pad);
            rrt.offsetMax = new Vector2(-pad, -pad);
            _reviewRoot = rootGo.transform;

            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(rootGo.transform, false);
            var grt = (RectTransform)gridGo.transform;
            grt.anchorMin = new Vector2(0f, 1f);
            grt.anchorMax = new Vector2(1f, 1f);
            grt.pivot     = new Vector2(0.5f, 1f);
            grt.offsetMin = new Vector2(0f, -gridH);
            grt.offsetMax = Vector2.zero;
            grt.anchoredPosition = Vector2.zero;
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = cols;
            grid.spacing         = new Vector2(spacing, spacing);
            grid.childAlignment  = TextAnchor.UpperCenter;

            foreach (var w in winners) BuildReviewCard(gridGo.transform, w);

            BuildReviewBulkBar(rootGo.transform, bulkH);

            Canvas.ForceUpdateCanvases();
            float w2 = prt.rect.width - pad * 2f;
            float cellW = (w2 - spacing * (cols - 1)) / cols;
            grid.cellSize = new Vector2(Mathf.Max(1f, cellW), cellH);

            ShowMultiReelPanel(true);
        }

        void BuildReviewCard(Transform parent, SkinDefinitionSO skin)
        {
            var accent = Color.white;
            var ctx = GameContext.Instance;
            if (ctx?.RarityVisuals != null && ctx.RarityVisuals.TryGet(skin.Rarity, out var entry))
                accent = entry.primaryColor;

            var card = new GameObject("RewardCard", typeof(RectTransform), typeof(Image), typeof(Button));
            card.transform.SetParent(parent, false);
            card.GetComponent<Image>().color = PanelDark;
            var cardBtn = card.GetComponent<Button>();
            cardBtn.transition = Selectable.Transition.None;
            var cardOutline = card.AddComponent<Outline>();
            cardOutline.effectColor    = new Color(accent.r, accent.g, accent.b, 0.5f);
            cardOutline.effectDistance = new Vector2(1.5f, -1.5f);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(card.transform, false);
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.anchorMin = new Vector2(0f, 0.46f);
            iconRt.anchorMax = new Vector2(1f, 1f);
            iconRt.offsetMin = new Vector2(8f, 4f);
            iconRt.offsetMax = new Vector2(-8f, -6f);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite        = skin.Icon;
            iconImg.enabled       = skin.Icon != null;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            var nameLabel = MakeCardText(card.transform, skin.SkinName, 16f, FontStyles.Bold,
                new Color(0.94f, 0.94f, 0.96f, 1f), 0.36f, 0.46f);
            var rarityLabel = MakeCardText(card.transform, skin.Rarity.ToString(), 13f, FontStyles.Normal,
                accent, 0.28f, 0.36f);
            MakeCardText(card.transform, $"{skin.VpValue:N0} VP", 14f, FontStyles.Bold,
                new Color(0.7f, 1f, 0.7f, 1f), 0.20f, 0.28f);

            var sellGo = new GameObject("Sell", typeof(RectTransform), typeof(Image), typeof(Button));
            sellGo.transform.SetParent(card.transform, false);
            var srt = (RectTransform)sellGo.transform;
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(1f, 0.18f);
            srt.offsetMin = new Vector2(8f, 6f);
            srt.offsetMax = new Vector2(-8f, 0f);
            sellGo.GetComponent<Image>().color = new Color(0.78f, 0.13f, 0.20f, 1f);
            var sellLbl = MakeStretchText(sellGo.transform, "SAT", 15f, FontStyles.Bold, Color.white);

            var review = new ReviewCard { skin = skin, nameLabel = nameLabel, cardButton = cardBtn };
            var sellButton = sellGo.GetComponent<Button>();
            review.sellButton = sellButton;
            sellButton.onClick.AddListener(() => SellReviewCard(review));
            cardBtn.onClick.AddListener(() => SellReviewCard(review));

            var overlay = new GameObject("SoldOverlay", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(card.transform, false);
            var ort = (RectTransform)overlay.transform;
            ort.anchorMin = Vector2.zero;
            ort.anchorMax = Vector2.one;
            ort.offsetMin = Vector2.zero;
            ort.offsetMax = Vector2.zero;
            overlay.GetComponent<Image>().color = SoldDim;
            MakeStretchText(overlay.transform, "SATILDI", 20f, FontStyles.Bold,
                new Color(1f, 0.275f, 0.333f, 1f));
            overlay.SetActive(false);
            review.soldOverlay = overlay;

            _reviewCards.Add(review);
        }

        TextMeshProUGUI MakeCardText(Transform card, string text, float size, FontStyles style,
            Color color, float yMin, float yMax)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(card, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, yMin);
            rt.anchorMax = new Vector2(1f, yMax);
            rt.offsetMin = new Vector2(6f, 0f);
            rt.offsetMax = new Vector2(-6f, 0f);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text          = text;
            t.fontSize      = size;
            t.fontStyle     = style;
            t.alignment     = TextAlignmentOptions.Center;
            t.color         = color;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode  = TextOverflowModes.Ellipsis;
            return t;
        }

        TextMeshProUGUI MakeStretchText(Transform parent, string text, float size, FontStyles style, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text          = text;
            t.fontSize      = size;
            t.fontStyle     = style;
            t.alignment     = TextAlignmentOptions.Center;
            t.color         = color;
            t.raycastTarget = false;
            return t;
        }

        void BuildReviewBulkBar(Transform parent, float height)
        {
            var bar = new GameObject("BulkBar", typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            var brt = (RectTransform)bar.transform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot     = new Vector2(0.5f, 0f);
            brt.offsetMin = new Vector2(0f, 0f);
            brt.offsetMax = new Vector2(0f, height);

            _sellAllButton = MakeBulkButton(bar.transform, "SELL ALL", new Color(0.78f, 0.13f, 0.20f, 1f),
                false, SellAllReview);
            MakeBulkButton(bar.transform, "CLAIM ALL", new Color(0.094f, 0.55f, 0.26f, 1f),
                true, CompleteReview);

            // Lift the SELL ALL label to the top half of the button, then add a
            // parenthetical running total of the unsold rewards' VP directly beneath it.
            var sellLbl = _sellAllButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (sellLbl != null)
            {
                var lrt = sellLbl.rectTransform;
                lrt.anchorMin = new Vector2(0f, 0.44f);
                lrt.anchorMax = new Vector2(1f, 1f);
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;
                sellLbl.alignment = TextAlignmentOptions.Bottom;
            }

            _reviewTotalLabel = MakeStretchText(_sellAllButton.transform, "", 13f, FontStyles.Bold,
                new Color(1f, 0.92f, 0.72f, 1f));
            var crt = _reviewTotalLabel.rectTransform;
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(1f, 0.44f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            _reviewTotalLabel.alignment         = TextAlignmentOptions.Top;
            _reviewTotalLabel.enableWordWrapping = false;
            UpdateReviewTotal();
        }

        // Sums the VP of every reward that is still unsold and shows it inside the SELL
        // ALL button (in parentheses, beneath the label). Called on build and whenever a
        // card flips sold/unsold so it drops as skins sell and restores if a sell fails.
        void UpdateReviewTotal()
        {
            if (_reviewTotalLabel == null) return;
            int total = 0;
            foreach (var card in _reviewCards)
                if (card != null && !card.sold && card.skin != null)
                    total += card.skin.VpValue;
            _reviewTotalLabel.text = $"({total:N0} VP)";
        }

        Button MakeBulkButton(Transform parent, string label, Color bg, bool rightSide, Action onClick)
        {
            var go = new GameObject("BulkButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(rightSide ? 0.5f : 0f, 0f);
            rt.anchorMax = new Vector2(rightSide ? 1f : 0.5f, 1f);
            rt.offsetMin = new Vector2(rightSide ? 5f : 0f, 0f);
            rt.offsetMax = new Vector2(rightSide ? 0f : -5f, 0f);
            go.GetComponent<Image>().color = bg;
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick());
            MakeStretchText(go.transform, label, 18f, FontStyles.Bold, Color.white);
            return btn;
        }

        void SellReviewCard(ReviewCard card)
        {
            if (card == null || card.sold || card.sellInFlight) return;
            StartCoroutine(SellReviewCardRoutine(card));
        }

        IEnumerator SellReviewCardRoutine(ReviewCard card)
        {
            if (card == null || card.sold || card.sellInFlight) yield break;
            var ctx = GameContext.Instance;
            if (ctx == null) yield break;

            if (ctx.BackendEnabled)
            {
                card.sellInFlight = true;
                SetCardInteractable(card, false);
                bool done = false, ok = false;
                ctx.SellOneBackend(card.skin.SkinId,
                    onSold:   _   => { ok = true;  done = true; },
                    onFailed: msg => { ok = false; done = true; if (!string.IsNullOrEmpty(msg)) GameEvents.RaiseToast(msg); });
                while (!done) { if (this == null) yield break; yield return null; }
                card.sellInFlight = false;
                if (ok) { MarkCardSold(card); RefreshWallet(); }
                else SetCardInteractable(card, true);
                yield break;
            }

            if (ctx.Economy != null && ctx.Economy.SellOne(card.skin.SkinId, out _))
            {
                MarkCardSold(card);
                RefreshWallet();
            }
        }

        static void SetCardInteractable(ReviewCard card, bool value)
        {
            if (card.sellButton != null) card.sellButton.interactable = value;
            if (card.cardButton != null) card.cardButton.interactable = value;
        }

        void MarkCardSold(ReviewCard card)
        {
            card.sold = true;
            SetCardInteractable(card, false);
            if (card.soldOverlay != null) card.soldOverlay.SetActive(true);
            if (card.nameLabel != null) card.nameLabel.fontStyle |= FontStyles.Strikethrough;
            UpdateReviewTotal();
        }

        void RevertCardSold(ReviewCard card)
        {
            card.sold = false;
            if (card.soldOverlay != null) card.soldOverlay.SetActive(false);
            if (card.nameLabel != null) card.nameLabel.fontStyle &= ~FontStyles.Strikethrough;
            SetCardInteractable(card, true);
            if (_sellAllButton != null) _sellAllButton.interactable = true;
            UpdateReviewTotal();
        }

        // One atomic action: collect unsold cards, flip them all to SOLD this frame,
        // then settle the economy (local: once; backend: per request, cards already SOLD).
        void SellAllReview()
        {
            var ctx = GameContext.Instance;
            if (ctx == null) return;

            var pending = new List<ReviewCard>();
            foreach (var card in _reviewCards)
                if (card != null && !card.sold && !card.sellInFlight) pending.Add(card);
            if (pending.Count == 0) return;

            foreach (var card in pending) MarkCardSold(card);
            if (_sellAllButton != null) _sellAllButton.interactable = false;

            if (ctx.BackendEnabled)
            {
                // Backend sells MUST be serialized. Firing one /inventory/sell request
                // per card in the same frame races on the server-side inventory, so only
                // a random subset commits and the rest fail and revert — this is the
                // "only 3 of 5 sold" bug. Mark every card in-flight up front (blocks a
                // re-entrant SELL ALL) and sell them one at a time instead.
                foreach (var card in pending) card.sellInFlight = true;
                StartCoroutine(SellReviewSequentially(pending));
                return;
            }

            foreach (var card in pending)
                if (!(ctx.Economy != null && ctx.Economy.SellOne(card.skin.SkinId, out _)))
                    RevertCardSold(card);
            RefreshWallet();
        }

        // Sells the review cards one at a time over the backend. Each SellOneBackend
        // fully settles (POST sell + inventory resync) before the next starts, so every
        // card sells deterministically — no concurrent-request race. Cards are already
        // marked SOLD and in-flight by SellAllReview; a failure reverts just that card
        // (which re-enables SELL ALL so the player can retry the leftovers).
        IEnumerator SellReviewSequentially(List<ReviewCard> pending)
        {
            var ctx = GameContext.Instance;
            foreach (var card in pending)
            {
                if (this == null) yield break;
                if (card == null) continue;
                if (ctx == null) { card.sellInFlight = false; RevertCardSold(card); continue; }

                bool done = false, ok = false;
                string failMsg = null;
                ctx.SellOneBackend(card.skin.SkinId,
                    onSold:   _   => { ok = true; done = true; },
                    onFailed: msg => { failMsg = msg; done = true; });
                while (!done) { if (this == null) yield break; yield return null; }

                card.sellInFlight = false;
                if (ok) RefreshWallet();
                else
                {
                    RevertCardSold(card);
                    if (!string.IsNullOrEmpty(failMsg)) GameEvents.RaiseToast(failMsg);
                }
            }
        }

        void CompleteReview()
        {
            ClearReview();
            ShowMultiReelPanel(false);
            RestoreRateTable();
            _multiOpenActive = false;
            EnsureInteractive();
            RefreshWallet();
            RefreshOpenButton();
        }

        void ClearReview()
        {
            _reviewCards.Clear();
            _sellAllButton = null;
            _reviewTotalLabel = null;
            if (_reviewRoot != null) { Destroy(_reviewRoot.gameObject); _reviewRoot = null; }
        }

        // ── Reveal sequence (single source of truth for result display) ─────

        // The ONLY entry point for showing a result. All paths (event,
        // watchdog, Update fast-fail) route through here. `_showingResult`
        // is set immediately to prevent re-entry.
        void StartRevealSequence(SkinDefinitionSO skin)
        {
            if (_multiOpenActive) return;
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
                    if (openButton != null) openButton.gameObject.SetActive(true);
                    OnResultPopupClosed();
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
                    // On top of the reel cards so the triangle selector is visible.
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
            if (!IsVisible || flow == null || _showingResult || _multiOpenActive) return;

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
        // Single flat navy for the whole screen — matches the rarity odds panel.
        static readonly Color BgStatic  = new Color(0.043f, 0.063f, 0.106f, 1f); // #0B101B
        static readonly Color BgPremium = new Color(0.043f, 0.063f, 0.106f, 1f); // #0B101B
        static readonly Color PanelDark = new Color(0.043f, 0.063f, 0.106f, 1f); // #0B101B
        static readonly Color NavBarNavy = new Color(0.031f, 0.055f, 0.102f, 1f); // #080E1A — matches BottomNavBar
        static readonly Color NeonRed   = new Color(1.000f, 0.275f, 0.333f, 1f); // #FF4655
        static readonly Color NeonCyan  = new Color(0.196f, 0.804f, 0.969f, 1f); // #32CDF7

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

        // ── Hero layout anchors (top-anchored, y grows downward) ──────────────
        const float HeroIconCenterY = -250f;
        static readonly Vector2 HeroIconSize = new Vector2(420f, 340f);

        // Hero glow / light rays / geometric lines intentionally removed — the
        // background is a single flat navy and the case stays transparent.
        void BuildNeonDecorations()
        {
            if (_decorBuilt) return;
            _decorBuilt = true;
        }

        // The built-in "UI/Skin/UISprite.psd" is not present in this Unity install and
        // logged an error on every call. Return null (Image then draws a plain quad,
        // matching the current look) — keeps these panels sharp and the console clean.
        Sprite RoundedSprite() => null;

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
        // Authoritative vertical stack (top-anchored, y downward):
        // back button → case image → case name → VP badge → open button → odds card.
        void MoveHeroSectionUp()
        {
            if (_heroMovedUp) return;
            _heroMovedUp = true;

            if (selectedCaseLabel != null)
            {
                var rt = selectedCaseLabel.rectTransform;
                rt.anchorMin        = new Vector2(0.5f, 1f);
                rt.anchorMax        = new Vector2(0.5f, 1f);
                rt.pivot            = new Vector2(0.5f, 1f);
                rt.sizeDelta        = new Vector2(620f, 52f);
                rt.anchoredPosition = new Vector2(0f, -432f);
                selectedCaseLabel.alignment       = TextAlignmentOptions.Center;
                selectedCaseLabel.enableAutoSizing = false;
                selectedCaseLabel.fontSize        = 34f;
                selectedCaseLabel.characterSpacing = 6f;
            }

            if (_vpBadgeBg != null)
                ((RectTransform)_vpBadgeBg.transform).anchoredPosition = new Vector2(0f, -494f);

            // Button (300 wide) + quantity stepper (108 wide) sit on one row, centered
            // as a group, so the selector is directly beside the open button.
            if (openButton != null)
            {
                var rt = openButton.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition = new Vector2(-60f, -548f);
            }
            if (_qtySelector != null)
                _qtySelector.anchoredPosition = new Vector2(156f, -548f);

            if (caseDisplayPanel != null)
            {
                var rateTf = caseDisplayPanel.transform.Find("RateTable");
                if (rateTf is RectTransform rateRt)
                    rateRt.anchoredPosition = new Vector2(0f, -648f);
            }

            Debug.Log("[CASE_UI_FIX] hero layout stacked");
        }

        // Scale the case hero icon 1.8× from its prefab size and nudge it downward.
        bool _iconResized;
        void ResizeCaseIcon()
        {
            if (_iconResized || caseIconDisplay == null) return;
            _iconResized = true;

            var iconRt = caseIconDisplay.GetComponent<RectTransform>();
            if (iconRt == null) return;

            iconRt.anchorMin        = new Vector2(0.5f, 1f);
            iconRt.anchorMax        = new Vector2(0.5f, 1f);
            iconRt.pivot            = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta        = HeroIconSize;
            iconRt.anchoredPosition = new Vector2(0f, HeroIconCenterY);
            caseIconDisplay.preserveAspect = true;

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
            // Match ReelViewport's vertical center (+60) so the marker tracks the card band.
            rt.anchoredPosition = new Vector2(0f, 60f);
            rt.sizeDelta        = new Vector2(60f, 200f);

            BuildTriangleMarker(rt);

            _spinFocusFrame = go;
            go.SetActive(false);
        }

        // One small downward red triangle at top-center, fixed, pointing at the
        // card that lands under the reel's center marker.
        void BuildTriangleMarker(RectTransform parent)
        {
            var go = new GameObject("TriangleSelector", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            // Apex on the card's top edge (card half-height ≈ 84), body above it.
            rt.anchoredPosition = new Vector2(0f, 92f);
            rt.sizeDelta        = new Vector2(20f, 16f);
            var img = go.GetComponent<Image>();
            img.sprite        = TriangleSprite();
            img.color         = NeonRed;
            img.raycastTarget = false;
        }

        // Procedural downward-triangle alpha mask (white, tinted via Image.color).
        // Generated at runtime — no project asset and no font-glyph dependency.
        Sprite _triangleSprite;
        Sprite TriangleSprite()
        {
            if (_triangleSprite != null) return _triangleSprite;

            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            {
                float t = (float)y / (s - 1);            // 1 at top row, 0 at bottom apex
                float halfWidth = t * (s * 0.5f);
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs(x - s * 0.5f);
                    byte a = dx <= halfWidth ? (byte)255 : (byte)0;
                    px[y * s + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _triangleSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
            return _triangleSprite;
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
