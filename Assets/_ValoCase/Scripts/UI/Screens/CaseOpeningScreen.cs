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
        bool _backBtnCreated;
        GameObject _runtimeBackBtn;

        // ── Visual-only neon pulse / burst (no gameplay logic) ───────────────
        Image      _spinOverlayBg;
        Coroutine  _spinPulseCoroutine;

        // ── Lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
            if (backButton != null) backButton.onClick.AddListener(OnBack);
            if (openButton != null) openButton.onClick.AddListener(OpenSelected);
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
            AlignOpenButtonAndPrice();
            BuildCaseList();
            RefreshWallet();
            ShowSpinOverlay(false);
            GameEvents.OnVpChanged += OnVpChanged;

            // Re-assert top sibling every time the screen is shown so no panel can cover the button.
            if (_runtimeBackBtn != null)
                _runtimeBackBtn.transform.SetAsLastSibling();
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

            // Center the button under the icon.
            btnRt.anchorMin = new Vector2(0.5f, 1f);
            btnRt.anchorMax = new Vector2(0.5f, 1f);
            btnRt.pivot     = new Vector2(0.5f, 1f);
            btnRt.anchoredPosition = new Vector2(0f, -370f);

            // Place the price label centered, just below the button.
            var priceRt = priceLabel.rectTransform;
            priceRt.anchorMin = new Vector2(0.5f, 1f);
            priceRt.anchorMax = new Vector2(0.5f, 1f);
            priceRt.pivot     = new Vector2(0.5f, 1f);
            var btnHeight = btnRt.sizeDelta.y;
            var gap = 8f;
            priceRt.anchoredPosition = new Vector2(0f, btnRt.anchoredPosition.y - btnHeight - gap);
            priceRt.sizeDelta = new Vector2(320f, 40f);
            priceLabel.alignment = TMPro.TextAlignmentOptions.Center;
            priceLabel.fontStyle = TMPro.FontStyles.Bold;
            priceLabel.color = new Color(0.7f, 1f, 0.7f);

            _openButtonAligned = true;
        }

        protected override void OnHidden()
        {
            // Halt any in-flight reveal so it doesn't fire on a hidden screen.
            StopAllCoroutines();
            flow?.Skip();
            HideSkipButton();
            if (caseDisplayPanel != null) caseDisplayPanel.transform.localScale = Vector3.one;
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
        }

        void SelectCase(CaseDefinitionSO caseDef)
        {
            _selected = caseDef;
            foreach (var item in _caseItems)
                item.SetSelected(item.Case == caseDef);

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
                selectedCaseLabel.text = caseDef.DisplayName.ToUpperInvariant();

            var price = ctx?.Shop?.GetDiscountedPrice(caseDef) ?? caseDef.VpPrice;
            if (priceLabel != null) priceLabel.text = $"{price:N0} VP";

            RefreshOpenButton();
            BuildDropList(caseDef);
        }

        void RefreshOpenButton()
        {
            if (openButton == null) return;
            openButton.interactable =
                _selected != null &&
                (flow == null || !flow.SessionActive) &&
                GameContext.Instance?.CaseOpening?.CanOpen(_selected) == true;
        }

        // ── Drop List ─────────────────────────────────────────────────────────

        void BuildDropList(CaseDefinitionSO caseDef)
        {
            foreach (var d in _dropItems)
                if (d != null) Destroy(d.gameObject);
            _dropItems.Clear();

            if (caseDef?.DropTable == null || dropListRoot == null || dropItemPrefab == null) return;

            var ctx      = GameContext.Instance;
            var visuals  = ctx?.RarityVisuals;
            var sellMult = ctx?.Config?.SellMultiplier ?? 1f;
            var table    = caseDef.DropTable;

            var drops = table.PossibleDrops
                .Where(d => d.skin != null)
                .OrderByDescending(d => (int)d.skin.Rarity)
                .ThenBy(d => d.skin.SkinName)
                .ToList();

            foreach (var drop in drops)
            {
                var chance = CalculateDropChance(table, drop);
                var item   = Instantiate(dropItemPrefab, dropListRoot);
                item.Bind(drop.skin, chance, visuals, sellMult);
                _dropItems.Add(item);
            }
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
            if (_selected == null || flow == null || flow.SessionActive) return;
            Debug.Log("[CASE] New open clicked — clearing previous result and starting fresh spin");

            // Önceki result state'ini temizle — panel'i tekrar kasa bilgisiyle doldur
            _showingResult = false;
            SelectCase(_selected);   // icon, label, fiyat ve drop list sıfırlanır

            if (openButton != null) openButton.interactable = false;
            HideSkipButton();
            ShowSpinOverlay(true);
            flow.StartOpening(_selected);
            StartCoroutine(SpinEndWatchdog());
        }

        // Polls every frame until session ends OR timeout.
        // Fallback: if OnCaseOpened event never fired, completes manually.
        IEnumerator SpinEndWatchdog()
        {
            const float extra = 10f;
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

        // Scale pop: 0.55 → 1.10 (ease-out) → 1.00 (settle).
        IEnumerator PanelPopAnimation()
        {
            if (caseDisplayPanel == null) yield break;
            var rt = caseDisplayPanel.transform as RectTransform;
            if (rt == null) yield break;

            const float startScale = 0.55f;
            const float peakScale  = 1.10f;
            const float riseDur    = 0.22f;
            const float settleDur  = 0.14f;

            rt.localScale = new Vector3(startScale, startScale, 1f);

            var t = 0f;
            while (t < riseDur)
            {
                t += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(t / riseDur);
                var eased = 1f - Mathf.Pow(1f - p, 3f);  // ease-out cubic
                var s = Mathf.Lerp(startScale, peakScale, eased);
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }

            t = 0f;
            while (t < settleDur)
            {
                t += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(t / settleDur);
                var s = Mathf.Lerp(peakScale, 1f, p);
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }

            rt.localScale = Vector3.one;
        }

        void ShowSpinOverlay(bool show)
        {
            if (spinOverlay != null) spinOverlay.SetActive(show);
            if (caseDisplayPanel != null) caseDisplayPanel.SetActive(!show);
            if (!show && openButton != null && !_showingResult)
                openButton.gameObject.SetActive(true);

            // Purely visual: pulse the spin overlay background during spin.
            if (show) StartSpinPulse();
            else      StopSpinPulse();
        }

        // ── Neon spin-phase pulse ────────────────────────────────────────────

        void StartSpinPulse()
        {
            _spinOverlayBg = spinOverlay != null ? spinOverlay.GetComponent<Image>() : null;
            if (_spinOverlayBg == null) return;
            if (_spinPulseCoroutine != null) StopCoroutine(_spinPulseCoroutine);
            _spinPulseCoroutine = StartCoroutine(SpinPulse());
        }

        void StopSpinPulse()
        {
            if (_spinPulseCoroutine != null)
            {
                StopCoroutine(_spinPulseCoroutine);
                _spinPulseCoroutine = null;
            }
            // Restore the overlay's base color exactly as set in the builder.
            if (_spinOverlayBg != null)
                _spinOverlayBg.color = new Color(0.04f, 0.07f, 0.1f, 0.97f);
        }

        // Breathes the spin overlay between near-black and a dim cyan tint.
        // Period ~1.4 s — subtle enough not to distract from the reel.
        IEnumerator SpinPulse()
        {
            var baseColor = new Color(0.04f, 0.07f, 0.10f, 0.97f);
            var peakColor = new Color(0.04f, 0.13f, 0.20f, 0.97f);  // slight cyan shift
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
    }
}
