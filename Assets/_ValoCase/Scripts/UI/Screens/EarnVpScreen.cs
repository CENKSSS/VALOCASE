using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValoCase.Core;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// "EARN VP" screen — premium dark-casino style VP farming minigame.
    ///
    /// ── ECONOMY ──────────────────────────────────────────────────────────────
    /// No hard cap. Soft balance via three stacked systems:
    ///   1. Duration multiplier  – resets on release (rewards active play)
    ///   2. Combo multiplier     – decays 3 s after last reward
    ///   3. AFK check            – reduces rate if mouse unmoved for > 15 s
    ///
    /// ── INPUTS ───────────────────────────────────────────────────────────────
    /// Hold zone uses EventTrigger (PointerDown / PointerUp) so mobile and
    /// desktop both work.  Every release resets the duration multiplier —
    /// skilled players learn to tap-release for optimal gain.
    ///
    /// ── UI BUILDS ITSELF ─────────────────────────────────────────────────────
    /// The builder only lays down the chrome (back button, navigator ref).
    /// All gameplay UI is constructed in BuildUiOnce() via code.
    /// </summary>
    public sealed class EarnVpScreen : UIScreenBase
    {
        // ── Inspector refs (wired by builder) ─────────────────────────────────
        [SerializeField] UINavigator          navigator;
        [SerializeField] Button               backButton;
        [SerializeField] TextMeshProUGUI      walletLabel;  // top-bar VP (compat)

        // ── Palette ───────────────────────────────────────────────────────────
        static readonly Color BgDeep      = new Color(0.016f, 0.010f, 0.045f, 1.00f);
        static readonly Color BgPanel     = new Color(0.030f, 0.020f, 0.070f, 0.96f);
        static readonly Color BgCard      = new Color(0.055f, 0.040f, 0.110f, 1.00f);
        static readonly Color NeonPink    = new Color(1.00f, 0.18f, 0.55f, 1.00f);
        static readonly Color NeonPinkSoft= new Color(1.00f, 0.18f, 0.55f, 0.22f);
        static readonly Color NeonPinkDim = new Color(1.00f, 0.18f, 0.55f, 0.08f);
        static readonly Color NeonPurple  = new Color(0.68f, 0.08f, 1.00f, 1.00f);
        static readonly Color NeonCyan    = new Color(0.00f, 0.90f, 1.00f, 1.00f);
        static readonly Color NeonGold    = new Color(1.00f, 0.82f, 0.12f, 1.00f);
        static readonly Color NeonGreen   = new Color(0.22f, 1.00f, 0.08f, 1.00f);
        static readonly Color TextWhite   = new Color(0.97f, 0.97f, 1.00f, 1.00f);
        static readonly Color TextSub     = new Color(0.76f, 0.72f, 0.90f, 1.00f);
        static readonly Color TextDim     = new Color(0.42f, 0.38f, 0.58f, 1.00f);

        // ── Economy constants ─────────────────────────────────────────────────
        static readonly int[]   BaseRewards     = { 10, 25, 50 };
        const float             MinInterval     = 0.25f;
        const float             MaxInterval     = 0.60f;
        const int               MaxCombo        = 10;
        const float             ComboDecaySec   = 3.0f;   // grace after release
        const float             AfkThresholdSec = 15.0f;  // idle mouse → 0.5× AFK penalty

        // Duration multipliers  [breakpoints in seconds]
        static readonly (float secs, float mult)[] DurationSteps =
        {
            (10f, 1.00f),
            (30f, 0.80f),
            (float.MaxValue, 0.50f),
        };

        // ── Runtime state ─────────────────────────────────────────────────────
        bool        _builtUi;
        ScreenType  _prevScreen;   // captured on OnShown — used by back button
        bool      _holding;
        float     _holdStartTime;
        int       _comboCount;
        float     _lastRewardTime;
        int       _sessionEarned;
        Vector2   _lastMousePos;
        float     _lastMouseMoveTime;
        Coroutine _holdCo;
        Coroutine _pulseCo;

        // ── UI refs ───────────────────────────────────────────────────────────
        TextMeshProUGUI  _balanceLabel;
        TextMeshProUGUI  _multiplierLabel;   // "1.0×"
        TextMeshProUGUI  _comboLabel;        // "COMBO ×5"
        TextMeshProUGUI  _sessionLabel;      // "+2,450 VP this session"
        TextMeshProUGUI  _hintLabel;
        Image            _holdZoneImg;       // center circle image
        RectTransform    _holdZoneRt;
        Image            _holdRingOuter;     // outer glow ring
        Image            _holdRingInner;     // bright inner ring
        Image            _durationBarFill;   // left-to-right bar
        Transform        _floatRoot;         // parent for floating texts
        Transform        _shakeTarget;       // shaken on reward
        readonly List<Image> _comboDots = new List<Image>();

        // ── Combo colour ramp ─────────────────────────────────────────────────
        static readonly Color[] ComboColors =
        {
            new Color(1.00f, 0.18f, 0.55f, 1f), // x1   neon pink
            new Color(1.00f, 0.18f, 0.55f, 1f), // x2
            new Color(1.00f, 0.45f, 0.15f, 1f), // x3   orange
            new Color(1.00f, 0.55f, 0.05f, 1f), // x4
            new Color(1.00f, 0.82f, 0.12f, 1f), // x5   gold
            new Color(1.00f, 0.82f, 0.12f, 1f), // x6
            new Color(0.22f, 1.00f, 0.08f, 1f), // x7   green
            new Color(0.00f, 0.90f, 1.00f, 1f), // x8   cyan
            new Color(0.68f, 0.08f, 1.00f, 1f), // x9   purple
            new Color(1.00f, 1.00f, 1.00f, 1f), // x10  white (max)
        };

        // ═════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═════════════════════════════════════════════════════════════════════

        void Awake()
        {
            // Legacy builder back button → reuse with correct navigation
            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);
        }

        void OnBackClicked()
        {
            // Return to whichever screen opened VP Kazan.
            // Exclude self (EarnVp) and Settings from valid return targets.
            var target = (_prevScreen != ScreenType.EarnVp &&
                          _prevScreen != ScreenType.Settings)
                ? _prevScreen
                : ScreenType.Tools;
            Debug.Log("[VP_KAZAN] Back clicked, returning to: " + target);
            navigator?.Navigate(target);
        }

        protected override void OnShown()
        {
            // Capture previous screen before UINavigator updates it
            _prevScreen = navigator?.PreviousScreen ?? ScreenType.Tools;
            BuildUiOnce();
            _lastMousePos       = ReadMousePosition();
            _lastMouseMoveTime  = Time.time;
            RefreshBalance();
            GameEvents.OnVpChanged += HandleVpChanged;
        }

        protected override void OnHidden()
        {
            GameEvents.OnVpChanged -= HandleVpChanged;
            ForceStopHolding();
        }

        void Update()
        {
            if (!IsVisible) return;

            // Track mouse movement for AFK detection
            var curPos = ReadMousePosition();
            if (curPos != _lastMousePos)
            {
                _lastMousePos      = curPos;
                _lastMouseMoveTime = Time.time;
            }

            // Combo decay
            if (!_holding && _comboCount > 0 &&
                Time.time - _lastRewardTime > ComboDecaySec)
            {
                _comboCount = 0;
                UpdateComboVisual();
            }

            // Duration bar fill (while holding)
            if (_holding)
            {
                float held = Time.time - _holdStartTime;
                float fill = Mathf.Clamp01(held / 30f);   // 0→1 over 30 s
                if (_durationBarFill != null)
                    _durationBarFill.fillAmount = fill;

                // Update multiplier label live
                UpdateMultiplierLabel(held);
            }
        }

        void HandleVpChanged(int _, int __) => RefreshBalance();

        void RefreshBalance()
        {
            if (_balanceLabel == null) return;
            int bal = GameContext.Instance?.Vp?.Balance ?? 0;
            _balanceLabel.text = $"{bal:N0} VP";
        }

        // ═════════════════════════════════════════════════════════════════════
        // HOLD MECHANICS
        // ═════════════════════════════════════════════════════════════════════

        void StartHolding()
        {
            if (_holding) return;
            _holding       = true;
            _holdStartTime = Time.time;

            if (_holdZoneImg  != null) _holdZoneImg.color  = BgCard;
            if (_holdRingInner != null) _holdRingInner.color =
                new Color(NeonPink.r, NeonPink.g, NeonPink.b, 0.55f);

            if (_hintLabel != null) _hintLabel.text = "EARNING…  RELEASE TO RESET MULTIPLIER";

            if (_holdCo != null) StopCoroutine(_holdCo);
            _holdCo = StartCoroutine(HoldRewardLoop());

            if (_pulseCo != null) StopCoroutine(_pulseCo);
            _pulseCo = StartCoroutine(PulseLoop());
        }

        void StopHolding()
        {
            if (!_holding) return;
            _holding = false;

            if (_holdCo  != null) { StopCoroutine(_holdCo);  _holdCo  = null; }
            if (_pulseCo != null) { StopCoroutine(_pulseCo); _pulseCo = null; }

            // Reset bar to 0 over 0.3 s
            if (_durationBarFill != null) StartCoroutine(AnimateBarReset());
            UpdateMultiplierLabel(0f);

            if (_holdZoneRt   != null) _holdZoneRt.localScale   = Vector3.one;
            if (_holdZoneImg  != null) _holdZoneImg.color        = BgPanel;
            if (_holdRingInner != null) _holdRingInner.color =
                new Color(NeonPink.r, NeonPink.g, NeonPink.b, 0.12f);

            if (_hintLabel != null) _hintLabel.text = "HOLD TO EARN VP • RELEASE RESETS MULTIPLIER";
        }

        void ForceStopHolding()
        {
            _holding = false;
            if (_holdCo  != null) { StopCoroutine(_holdCo);  _holdCo  = null; }
            if (_pulseCo != null) { StopCoroutine(_pulseCo); _pulseCo = null; }
        }

        IEnumerator HoldRewardLoop()
        {
            while (_holding)
            {
                float interval = Random.Range(MinInterval, MaxInterval);
                yield return new WaitForSeconds(interval);
                if (_holding) GrantReward();
            }
        }

        void GrantReward()
        {
            // ── Economy calculation ───────────────────────────────────────────
            float held      = Time.time - _holdStartTime;
            float durMult   = GetDurationMultiplier(held);
            float comboMult = GetComboMultiplier();
            bool  isAfk     = (Time.time - _lastMouseMoveTime) > AfkThresholdSec;
            float afkMult   = isAfk ? 0.5f : 1.0f;

            int baseReward  = BaseRewards[Random.Range(0, BaseRewards.Length)];
            int finalReward = Mathf.RoundToInt(baseReward * durMult * comboMult * afkMult);
            finalReward     = Mathf.Max(5, (finalReward / 5) * 5);   // round to nearest 5, min 5

            // ── Apply ─────────────────────────────────────────────────────────
            GameContext.Instance?.Vp?.Add(finalReward);
            GameContext.Instance?.Statistics?.RecordVpEarned(finalReward);

            _sessionEarned  += finalReward;
            _comboCount      = Mathf.Min(_comboCount + 1, MaxCombo);
            _lastRewardTime  = Time.time;

            // ── Visuals ───────────────────────────────────────────────────────
            Color accentColor = ComboColors[Mathf.Clamp(_comboCount - 1, 0, MaxCombo - 1)];
            SpawnFloatingText($"+{finalReward} VP", accentColor);
            StartCoroutine(ShakeTarget(_shakeTarget, 3f, 0.10f));

            UpdateComboVisual();
            UpdateSessionLabel();
            RefreshBalance();

            // Ring color follows combo
            if (_holdRingOuter != null)
                _holdRingOuter.color = new Color(
                    accentColor.r, accentColor.g, accentColor.b, 0.20f);
        }

        // ── Economy helpers ───────────────────────────────────────────────────

        static float GetDurationMultiplier(float heldSeconds)
        {
            float prev = 0f;
            foreach (var (secs, mult) in DurationSteps)
            {
                if (heldSeconds <= secs) return mult;
                prev = mult;
            }
            return prev;
        }

        float GetComboMultiplier()
        {
            // Smooth ramp: x1.0 at combo-0 → x3.0 at combo-10
            return 1f + (_comboCount / (float)MaxCombo) * 2f;
        }

        // ═════════════════════════════════════════════════════════════════════
        // VISUAL HELPERS
        // ═════════════════════════════════════════════════════════════════════

        void UpdateComboVisual()
        {
            Color accent = _comboCount > 0
                ? ComboColors[Mathf.Clamp(_comboCount - 1, 0, MaxCombo - 1)]
                : TextDim;

            if (_comboLabel != null)
                _comboLabel.text = _comboCount > 0
                    ? $"COMBO  ×{_comboCount}"
                    : "COMBO";

            // Dot indicators
            for (int i = 0; i < _comboDots.Count; i++)
            {
                bool filled = i < _comboCount;
                _comboDots[i].color = filled ? accent : new Color(0.3f, 0.25f, 0.45f, 0.4f);
                var ol = _comboDots[i].GetComponent<Outline>();
                if (ol != null) ol.effectColor = filled
                    ? new Color(accent.r, accent.g, accent.b, 0.55f)
                    : Color.clear;
            }

            if (_comboLabel != null) _comboLabel.color = accent;
        }

        void UpdateMultiplierLabel(float heldSeconds)
        {
            if (_multiplierLabel == null) return;
            float m = GetDurationMultiplier(heldSeconds);
            _multiplierLabel.text  = $"{m:F1}×";
            _multiplierLabel.color = m >= 1f ? NeonGreen : (m >= 0.8f ? NeonGold : NeonPink);
        }

        void UpdateSessionLabel()
        {
            if (_sessionLabel != null)
                _sessionLabel.text = $"SESSION  +{_sessionEarned:N0} VP";
        }

        // ── Floating reward text ──────────────────────────────────────────────

        void SpawnFloatingText(string text, Color color)
        {
            if (_floatRoot == null) return;

            var go  = new GameObject("Float", typeof(RectTransform));
            go.transform.SetParent(_floatRoot, false);
            var rt  = (RectTransform)go.transform;
            rt.sizeDelta        = new Vector2(220f, 56f);
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(Random.Range(-50f, 50f), 20f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = text;
            tmp.fontSize           = 32f;
            tmp.fontStyle          = FontStyles.Bold;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.color              = color;
            tmp.raycastTarget      = false;
            tmp.enableWordWrapping = false;

            var ol = go.AddComponent<Outline>();
            ol.effectColor    = new Color(color.r, color.g, color.b, 0.55f);
            ol.effectDistance = new Vector2(2f, -2f);

            StartCoroutine(FloatTextAnim(go, rt, tmp));
        }

        IEnumerator FloatTextAnim(GameObject go, RectTransform rt, TextMeshProUGUI tmp)
        {
            const float dur    = 1.30f;
            Vector2     start  = rt.anchoredPosition;
            Color       col    = tmp.color;
            float       t      = 0f;

            rt.localScale = Vector3.one * 0.4f;

            while (t < dur)
            {
                t += Time.deltaTime;
                float p = t / dur;

                // Float up
                rt.anchoredPosition = start + Vector2.up * (p * 100f);

                // Pop scale
                float s = p < 0.12f ? Mathf.Lerp(0.4f, 1.25f, p / 0.12f)
                        : p < 0.22f ? Mathf.Lerp(1.25f, 1.00f, (p - 0.12f) / 0.10f)
                        : 1.00f;
                rt.localScale = Vector3.one * s;

                // Fade out second half
                float a = p < 0.45f ? 1f : Mathf.Lerp(1f, 0f, (p - 0.45f) / 0.55f);
                tmp.color = new Color(col.r, col.g, col.b, a);

                yield return null;
            }

            if (go != null) Destroy(go);
        }

        // ── Hold-zone pulse animation ─────────────────────────────────────────

        IEnumerator PulseLoop()
        {
            while (_holding)
            {
                Color accent = ComboColors[Mathf.Clamp(_comboCount - 1, 0, MaxCombo - 1)];
                if (_holdRingInner != null)
                    _holdRingInner.color = new Color(
                        accent.r, accent.g, accent.b, 0.55f);

                float dur = 0.40f;
                float t   = 0f;
                while (t < dur && _holding)
                {
                    t += Time.unscaledDeltaTime;
                    float s = 1f + Mathf.Sin(t / dur * Mathf.PI) * 0.035f;
                    if (_holdZoneRt != null)
                        _holdZoneRt.localScale = new Vector3(s, s, 1f);
                    yield return null;
                }
            }
        }

        // ── Screen / element shake ────────────────────────────────────────────

        IEnumerator ShakeTarget(Transform target, float amplitude, float duration)
        {
            if (target == null) yield break;
            Vector3 origin = target.localPosition;
            float   t      = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float decay = 1f - (t / duration);
                target.localPosition = origin + (Vector3)(
                    Random.insideUnitCircle * amplitude * decay);
                yield return null;
            }
            target.localPosition = origin;
        }

        // ── Duration bar reset ────────────────────────────────────────────────

        IEnumerator AnimateBarReset()
        {
            if (_durationBarFill == null) yield break;
            float dur   = 0.30f;
            float start = _durationBarFill.fillAmount;
            float t     = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                _durationBarFill.fillAmount = Mathf.Lerp(start, 0f, t / dur);
                yield return null;
            }
            _durationBarFill.fillAmount = 0f;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ONE-SHOT UI CONSTRUCTION
        // ═════════════════════════════════════════════════════════════════════

        void BuildUiOnce()
        {
            if (_builtUi) return;
            _builtUi = true;

            var rt = (RectTransform)transform;

            // ── Deep background ───────────────────────────────────────────────
            var bg = NewRect(rt, "Bg", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            bg.offsetMin = bg.offsetMax = Vector2.zero;
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = BgDeep; bgImg.raycastTarget = false;
            bg.SetSiblingIndex(0);

            // Radial center glow
            var glow = NewRect(rt, "CenterGlow",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            glow.sizeDelta = new Vector2(700f, 700f);
            glow.gameObject.AddComponent<Image>().color =
                new Color(0.68f, 0.08f, 1.00f, 0.055f);
            glow.GetComponent<Image>().raycastTarget = false;

            // ── Center: hold zone + stats ─────────────────────────────────────
            _shakeTarget = NewRect(rt, "Center",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _shakeTarget.GetComponent<RectTransform>().sizeDelta = new Vector2(560f, 620f);
            (_shakeTarget as RectTransform).anchoredPosition    = new Vector2(0f, -20f);

            // VP balance label (large, center-top)
            _balanceLabel = CreateTmp(_shakeTarget, "Balance", "0 VP",
                36, FontStyles.Bold, TextAlignmentOptions.Center, NeonGold);
            _balanceLabel.rectTransform.anchorMin        = new Vector2(0f, 1f);
            _balanceLabel.rectTransform.anchorMax        = new Vector2(1f, 1f);
            _balanceLabel.rectTransform.pivot            = new Vector2(0.5f, 1f);
            _balanceLabel.rectTransform.anchoredPosition = new Vector2(0f, -24f);
            _balanceLabel.rectTransform.sizeDelta        = new Vector2(0f, 48f);
            var oldOutline = _balanceLabel.GetComponent<Outline>();
            if (oldOutline != null)
                Destroy(oldOutline);
            var balOl = _balanceLabel.gameObject.AddComponent<Outline>();
            balOl.effectColor    = new Color(NeonGold.r, NeonGold.g, NeonGold.b, 0.45f);
            balOl.effectDistance = new Vector2(2f, -2f);

            // ── HOLD button ───────────────────────────────────────────────────
            BuildHoldZone(_shakeTarget);

            // ── Duration multiplier bar ───────────────────────────────────────
            BuildDurationBar(_shakeTarget);

            // ── Combo section ─────────────────────────────────────────────────
            BuildComboSection(_shakeTarget);

            // ── Stats row ─────────────────────────────────────────────────────
            BuildStatsRow(_shakeTarget);

            // ── Hint label ────────────────────────────────────────────────────
            _hintLabel = CreateTmp(_shakeTarget, "Hint",
                "HOLD TO EARN VP  •  RELEASE RESETS MULTIPLIER",
                10, FontStyles.Normal, TextAlignmentOptions.Center, TextDim);
            _hintLabel.characterSpacing = 1.5f;
            _hintLabel.rectTransform.anchorMin        = new Vector2(0f, 0f);
            _hintLabel.rectTransform.anchorMax        = new Vector2(1f, 0f);
            _hintLabel.rectTransform.pivot            = new Vector2(0.5f, 0f);
            _hintLabel.rectTransform.anchoredPosition = new Vector2(0f, 18f);
            _hintLabel.rectTransform.sizeDelta        = new Vector2(0f, 20f);

            // ── Float text root (above hold button) ───────────────────────────
            var fr = NewRect(_shakeTarget, "FloatRoot",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f));
            fr.anchoredPosition = new Vector2(0f, 60f);
            fr.sizeDelta        = new Vector2(340f, 200f);
            _floatRoot          = fr;

            UpdateComboVisual();
            UpdateMultiplierLabel(0f);
            UpdateSessionLabel();

            // ── BACK button — added LAST so it renders on top of all content ──
            // anchorMin/Max = (0,1): top-left corner of screen
            // anchoredPosition = (28, -88): 88px below top (TopProfileBar 72px + 16px gap)
            var backBtn = new GameObject("BackBtn",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            backBtn.transform.SetParent(rt, false);
            var backRt = (RectTransform)backBtn.transform;
            backRt.anchorMin        = new Vector2(0f, 1f);
            backRt.anchorMax        = new Vector2(0f, 1f);
            backRt.pivot            = new Vector2(0f, 1f);
            backRt.anchoredPosition = new Vector2(28f, -88f);
            backRt.sizeDelta        = new Vector2(96f, 36f);

            var backImg = backBtn.GetComponent<Image>();
            backImg.color = new Color(0.031f, 0.055f, 0.102f, 0.97f);   // dark navy, fully opaque

            var backOl = backBtn.GetComponent<Outline>();
            backOl.effectColor    = new Color(1f, 0.122f, 0.224f, 0.80f);  // neon red border
            backOl.effectDistance = new Vector2(1.5f, -1.5f);

            var backLbl = CreateTmp(backBtn.transform, "Lbl", "BACK",
                12f, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
            backLbl.characterSpacing = 2f;
            var bRt = backLbl.rectTransform;
            bRt.anchorMin = Vector2.zero; bRt.anchorMax = Vector2.one;
            bRt.offsetMin = bRt.offsetMax = Vector2.zero;

            var backBtnComp = backBtn.GetComponent<Button>();
            backBtnComp.transition = Selectable.Transition.None;
            backBtnComp.onClick.AddListener(OnBackClicked);

            // Render on top of everything inside EarnVpScreen
            backBtn.transform.SetAsLastSibling();

            Debug.Log("[VP_KAZAN] Back button created active=" + backBtn.activeInHierarchy);
            Debug.Log("[VP_KAZAN] Back button sibling=" + backBtn.transform.GetSiblingIndex());
        }

        // ── Hold zone: layered rings + text ──────────────────────────────────
        void BuildHoldZone(Transform parent)
        {
            const float outerD = 240f, innerD = 220f, coreD = 196f;

            // Outer soft glow ring
            _holdRingOuter = BuildCircle(parent, "RingOuter", outerD,
                new Color(NeonPink.r, NeonPink.g, NeonPink.b, 0.10f));
            PositionCenter(_holdRingOuter.rectTransform, new Vector2(0f, -28f), outerD);

            // Visible ring
            _holdRingInner = BuildCircle(parent, "RingInner", innerD,
                new Color(NeonPink.r, NeonPink.g, NeonPink.b, 0.12f));
            PositionCenter(_holdRingInner.rectTransform, new Vector2(0f, -28f), innerD);
            var ringOl = _holdRingInner.gameObject.AddComponent<Outline>();
            ringOl.effectColor    = NeonPink;
            ringOl.effectDistance = new Vector2(2f, -2f);

            // Core button
            _holdZoneImg = BuildCircle(parent, "HoldZone", coreD, BgPanel);
            PositionCenter(_holdZoneImg.rectTransform, new Vector2(0f, -28f), coreD);
            _holdZoneRt = _holdZoneImg.rectTransform;

            // EventTrigger for hold input
            var et = _holdZoneImg.gameObject.AddComponent<EventTrigger>();
            AddPE(et, EventTriggerType.PointerDown, _ => StartHolding());
            AddPE(et, EventTriggerType.PointerUp,   _ => StopHolding());
            AddPE(et, EventTriggerType.PointerExit, _ => StopHolding()); // release if dragged off

            // Inner text — "HOLD" + icon
            var holdIconLbl = CreateTmp(_holdZoneImg.transform, "Icon", "VP",
                42, FontStyles.Normal, TextAlignmentOptions.Center, TextWhite);
            holdIconLbl.rectTransform.anchorMin        = new Vector2(0f, 0.5f);
            holdIconLbl.rectTransform.anchorMax        = new Vector2(1f, 0.5f);
            holdIconLbl.rectTransform.pivot            = new Vector2(0.5f, 0.5f);
            holdIconLbl.rectTransform.anchoredPosition = new Vector2(0f, 24f);
            holdIconLbl.rectTransform.sizeDelta        = new Vector2(0f, 54f);
            holdIconLbl.raycastTarget                  = false;

            var holdLbl = CreateTmp(_holdZoneImg.transform, "HoldLabel", "HOLD",
                20, FontStyles.Bold, TextAlignmentOptions.Center, TextWhite);
            holdLbl.characterSpacing = 4f;
            holdLbl.rectTransform.anchorMin        = new Vector2(0f, 0.5f);
            holdLbl.rectTransform.anchorMax        = new Vector2(1f, 0.5f);
            holdLbl.rectTransform.pivot            = new Vector2(0.5f, 0.5f);
            holdLbl.rectTransform.anchoredPosition = new Vector2(0f, -16f);
            holdLbl.rectTransform.sizeDelta        = new Vector2(0f, 28f);
            holdLbl.raycastTarget                  = false;

            var earnLbl = CreateTmp(_holdZoneImg.transform, "EarnLabel", "TO EARN",
                11, FontStyles.Normal, TextAlignmentOptions.Center, TextSub);
            earnLbl.characterSpacing = 3f;
            earnLbl.rectTransform.anchorMin        = new Vector2(0f, 0.5f);
            earnLbl.rectTransform.anchorMax        = new Vector2(1f, 0.5f);
            earnLbl.rectTransform.pivot            = new Vector2(0.5f, 0.5f);
            earnLbl.rectTransform.anchoredPosition = new Vector2(0f, -40f);
            earnLbl.rectTransform.sizeDelta        = new Vector2(0f, 18f);
            earnLbl.raycastTarget                  = false;
        }

        // ── Duration multiplier bar ───────────────────────────────────────────
        void BuildDurationBar(Transform parent)
        {
            // Container row: [label] [bar] [multiplier]
            var row = NewRect(parent, "DurRow",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            row.anchoredPosition = new Vector2(0f, -160f);
            row.sizeDelta        = new Vector2(480f, 28f);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment        = TextAnchor.MiddleCenter;
            hl.spacing               = 10f;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // "HOLD BONUS" label
            var durHint = CreateTmp(row, "DurHint", "HOLD BONUS",
                9, FontStyles.Bold, TextAlignmentOptions.Right, TextDim);
            durHint.characterSpacing = 2f;
            durHint.gameObject.AddComponent<LayoutElement>().minWidth = 90f;

            // Bar background
            var barBg = new GameObject("BarBg", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            barBg.transform.SetParent(row, false);
            barBg.GetComponent<LayoutElement>().minWidth = 260f;
            barBg.GetComponent<LayoutElement>().minHeight = 10f;
            barBg.GetComponent<Image>().color = new Color(0.08f, 0.05f, 0.18f, 1f);
            var barBgRt = (RectTransform)barBg.transform;

            // Bar fill (Filled image, left-to-right)
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(barBg.transform, false);
            _durationBarFill = fillGo.GetComponent<Image>();
            _durationBarFill.fillMethod = Image.FillMethod.Horizontal;
            _durationBarFill.type       = Image.Type.Filled;
            _durationBarFill.fillOrigin = 0;  // left
            _durationBarFill.fillAmount = 0f;
            _durationBarFill.color      = NeonGreen;
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;

            // Segment markers at 33% and 67% (10s and 30s milestones)
            for (int i = 1; i <= 2; i++)
            {
                var mk = new GameObject($"Mk{i}", typeof(RectTransform), typeof(Image));
                mk.transform.SetParent(barBg.transform, false);
                var mkRt = (RectTransform)mk.transform;
                mkRt.anchorMin        = new Vector2(i / 3f, 0f);
                mkRt.anchorMax        = new Vector2(i / 3f, 1f);
                mkRt.pivot            = new Vector2(0.5f, 0.5f);
                mkRt.anchoredPosition = Vector2.zero;
                mkRt.sizeDelta        = new Vector2(1.5f, 0f);
                mk.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);
            }

            // Multiplier label "1.0×"
            _multiplierLabel = CreateTmp(row, "Mult", "1.0×",
                13, FontStyles.Bold, TextAlignmentOptions.Left, NeonGreen);
            _multiplierLabel.gameObject.AddComponent<LayoutElement>().minWidth = 48f;
        }

        // ── Combo section ─────────────────────────────────────────────────────
        void BuildComboSection(Transform parent)
        {
            var section = NewRect(parent, "ComboSection",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            section.anchoredPosition = new Vector2(0f, -210f);
            section.sizeDelta        = new Vector2(480f, 48f);

            // "COMBO" label
            _comboLabel = CreateTmp(section, "ComboLbl", "COMBO",
                12, FontStyles.Bold, TextAlignmentOptions.Center, TextDim);
            _comboLabel.characterSpacing = 3f;
            _comboLabel.rectTransform.anchorMin        = new Vector2(0.5f, 1f);
            _comboLabel.rectTransform.anchorMax        = new Vector2(0.5f, 1f);
            _comboLabel.rectTransform.pivot            = new Vector2(0.5f, 1f);
            _comboLabel.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            _comboLabel.rectTransform.sizeDelta        = new Vector2(300f, 20f);

            // 10 dot indicators
            var dotsRow = NewRect(section, "Dots",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            dotsRow.anchoredPosition = new Vector2(0f, 0f);
            dotsRow.sizeDelta        = new Vector2(340f, 22f);
            var hl = dotsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment        = TextAnchor.MiddleCenter;
            hl.spacing               = 8f;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            _comboDots.Clear();
            for (int i = 0; i < MaxCombo; i++)
            {
                var dot = new GameObject($"Dot{i}", typeof(RectTransform), typeof(Image),
                    typeof(Outline), typeof(LayoutElement));
                dot.transform.SetParent(dotsRow, false);
                dot.GetComponent<LayoutElement>().minWidth  = 18f;
                dot.GetComponent<LayoutElement>().minHeight = 18f;
                var dotImg = dot.GetComponent<Image>();
                dotImg.color = new Color(0.3f, 0.25f, 0.45f, 0.4f);
                var dotOl = dot.GetComponent<Outline>();
                dotOl.effectColor    = Color.clear;
                dotOl.effectDistance = new Vector2(2f, -2f);
                _comboDots.Add(dotImg);
            }
        }

        // ── Stats row ─────────────────────────────────────────────────────────
        void BuildStatsRow(Transform parent)
        {
            var row = NewRect(parent, "StatsRow",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            row.anchoredPosition = new Vector2(0f, -268f);
            row.sizeDelta        = new Vector2(480f, 28f);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childAlignment        = TextAnchor.MiddleCenter;
            hl.spacing               = 24f;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // Session earned label
            _sessionLabel = CreateTmp(row, "Session", "SESSION  +0 VP",
                11, FontStyles.Bold, TextAlignmentOptions.Center, TextSub);
            _sessionLabel.gameObject.AddComponent<LayoutElement>().minWidth = 200f;

            // Reward rates info
            var ratesLbl = CreateTmp(row, "Rates",
                "0-10s: ×1.0  |  10-30s: ×0.8  |  30s+: ×0.5",
                9, FontStyles.Normal, TextAlignmentOptions.Center, TextDim);
            ratesLbl.gameObject.AddComponent<LayoutElement>().minWidth = 220f;
        }

        // ═════════════════════════════════════════════════════════════════════
        // PRIMITIVE HELPERS
        // ═════════════════════════════════════════════════════════════════════

        Image BuildCircle(Transform parent, string name, float diameter, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            return img;
        }

        static void PositionCenter(RectTransform rt, Vector2 pos, float size)
        {
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta        = new Vector2(size, size);
        }

        static RectTransform NewRect(Transform parent, string name,
            Vector2 aMin, Vector2 aMax, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            return rt;
        }

        static TextMeshProUGUI CreateTmp(Transform parent, string name, string text,
            float size, FontStyles style, TextAlignmentOptions align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = text;
            tmp.fontSize           = size;
            tmp.fontStyle          = style;
            tmp.alignment          = align;
            tmp.color              = color;
            tmp.raycastTarget      = false;
            tmp.enableWordWrapping = false;
            return tmp;
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Returns the current pointer/mouse position, compatible with both the
        /// legacy Input Manager and the new Input System package.
        /// </summary>
        static Vector2 ReadMousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        static void AddPE(EventTrigger et, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> action)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(action);
            et.triggers.Add(e);
        }
    }
}
