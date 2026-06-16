using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Spin-wheel animation for the Upgrade screen.
    /// Procedurally generates a ring/donut sprite so Image.Type.Filled + Radial360
    /// produces a true circular arc — not a filled rectangle.
    ///
    /// The result indicator is a small diamond marker that rides the OUTSIDE edge of
    /// the ring (no needle/arrow inside the circle — the hole stays clean for the %
    /// text). The marker hangs off the same rotating pivot the old needle used, so
    /// the spin animation itself is unchanged.
    ///
    /// Render order within centerPanel (back → front):
    ///   ArcBg | SuccessArc | ChanceDisc | … | NeedlePivot(marker) | ChanceLabel
    /// </summary>
    public sealed class UpgradeSpinAnimator : MonoBehaviour
    {
        RectTransform _needlePivot;
        Image         _successArc;
        bool          _initialized;
        Color         _chanceColor = ColHigh;

        // ── Colors ────────────────────────────────────────────────────────────
        static readonly Color ColLow    = new Color(1.00f, 0.275f, 0.333f, 1.00f); // red    (0–30 %)
        static readonly Color ColMid    = new Color(1.00f, 0.647f, 0.239f, 1.00f); // orange (31–60 %)
        static readonly Color ColHigh   = new Color(0.216f, 0.839f, 0.482f, 1.00f); // green  (61–100 %)
        static readonly Color ColFail   = new Color(0.96f, 0.22f, 0.22f, 1.00f);
        static readonly Color ColArcBg  = new Color(0.129f, 0.165f, 0.263f, 1.00f); // unfilled track
        static readonly Color ColDisc   = new Color(0.043f, 0.063f, 0.125f, 0.92f); // hole backdrop
        static readonly Color ColMarker       = new Color(1.00f, 1.00f, 1.00f, 1.00f); // outer diamond marker
        static readonly Color ColMarkerShadow = new Color(0.00f, 0.00f, 0.00f, 0.45f);

        /// <summary>Banded chance color: low → red, medium → orange, high → green.</summary>
        public static Color GetChanceColor(float chance)
        {
            float c = Mathf.Clamp01(chance);
            if (c <= 0.30f) return ColLow;
            if (c <= 0.60f) return ColMid;
            return ColHigh;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PUBLIC API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds ring arc + needle inside centerPanel.
        /// Must be called one frame after OnShown so layout rects are valid.
        /// </summary>
        public void Initialize(RectTransform centerPanel, RectTransform chanceLabel)
        {
            if (_initialized || centerPanel == null) return;
            _initialized = true;

            // Ring sprite is generated at ~1.5× the display resolution (256 px texture
            // for a 176 px widget) so the radial arc renders with smooth anti-aliased
            // edges. The band is deliberately thin for a premium, elegant ring.
            const int   texOuterR = 128;                            // texture outer radius px
            const int   texInnerR = 108;                            // texture inner radius px (thin band)
            const float arcSize   = 176f;                           // display diameter
            const float markerR   = arcSize * 0.5f + 9f;            // marker center, just outside the ring
            const float holeSize  = arcSize * texInnerR / texOuterR; // ≈ 149 px inner hole

            // Position: same anchor as chanceLabel so the % text is centred in the ring hole.
            Vector2 arcPos = chanceLabel != null
                ? chanceLabel.anchoredPosition
                : new Vector2(0f, 32f);

            Sprite ring = CreateRingSprite(texOuterR, texInnerR);
            Sprite disc = CreateCircleSprite(64);  // hi-res backdrop disc

            // ── Background ring (unfilled track) ──────────────────────────────
            var bgGo = BuildArcImage("ArcBg", centerPanel, arcSize, arcPos, ring,
                                     ColArcBg, fillAmount: 1f, out _);

            // ── Success arc ───────────────────────────────────────────────────
            var arcGo = BuildArcImage("SuccessArc", centerPanel, arcSize, arcPos, ring,
                                      _chanceColor, fillAmount: 0f, out _successArc);

            // ── Hole backdrop disc — dark contrast plate behind the % text ────
            var discGo = new GameObject("ChanceDisc", typeof(RectTransform), typeof(Image));
            discGo.transform.SetParent(centerPanel, false);
            var discRt = (RectTransform)discGo.transform;
            discRt.anchorMin = discRt.anchorMax = new Vector2(0.5f, 0.5f);
            discRt.pivot     = new Vector2(0.5f, 0.5f);
            discRt.sizeDelta = new Vector2(holeSize, holeSize);
            discRt.anchoredPosition = arcPos;
            var discImg = discGo.GetComponent<Image>();
            discImg.sprite        = disc;
            discImg.color         = ColDisc;
            discImg.raycastTarget = false;

            // ── Marker pivot ──────────────────────────────────────────────────
            // No needle/arrow inside the circle: the rotating pivot carries a single
            // small diamond marker that sits just OUTSIDE the ring edge, so the hole
            // stays clean for the % text. AnimateSpin rotates this pivot exactly as
            // it rotated the old needle.
            var pivotGo = new GameObject("NeedlePivot", typeof(RectTransform));
            pivotGo.transform.SetParent(centerPanel, false);
            _needlePivot = (RectTransform)pivotGo.transform;
            _needlePivot.anchorMin = _needlePivot.anchorMax = new Vector2(0.5f, 0.5f);
            _needlePivot.pivot     = new Vector2(0.5f, 0.5f);
            _needlePivot.sizeDelta = Vector2.zero;
            _needlePivot.anchoredPosition = arcPos;

            // Soft shadow behind the marker for lift against the panel.
            AddDiamond(_needlePivot, "MarkerShadow", markerR, size: 16f, ColMarkerShadow);
            // The marker itself — a crisp white diamond, point toward the ring.
            AddDiamond(_needlePivot, "Marker",       markerR, size: 12f, ColMarker);

            // ── Sibling / render order ────────────────────────────────────────
            // Track → arc → backdrop disc behind everything else; NeedlePivot and
            // ChanceLabel go to the very front.
            bgGo.transform.SetSiblingIndex(1);    // dark track ring
            arcGo.transform.SetSiblingIndex(2);   // colored chance arc
            discGo.transform.SetSiblingIndex(3);  // hole backdrop under the % text
            _needlePivot.SetAsLastSibling();

            if (chanceLabel != null)
            {
                // Centre the % text exactly in the ring hole.
                chanceLabel.anchoredPosition = arcPos;
                chanceLabel.SetAsLastSibling();
                var tmp = chanceLabel.GetComponent<TMPro.TextMeshProUGUI>();
                if (tmp != null) tmp.alignment = TMPro.TextAlignmentOptions.Center;
            }
        }

        /// <summary>Updates the success arc fill (0–1) and its banded color.</summary>
        public void SetChance(float chance)
        {
            float c = Mathf.Clamp01(chance);
            _chanceColor = GetChanceColor(c);
            if (_successArc != null)
            {
                _successArc.fillAmount = c;
                _successArc.color      = _chanceColor;
            }
        }

        /// <summary>Snaps needle to 12-o'clock.</summary>
        public void ResetNeedle()
        {
            if (_needlePivot != null)
                _needlePivot.localEulerAngles = Vector3.zero;
        }

        /// <summary>Spins needle then flashes arc colour to show the result.</summary>
        public IEnumerator AnimateSpin(float chance, bool success, Action onComplete)
        {
            if (_needlePivot == null)
            {
                yield return new WaitForSecondsRealtime(1.5f);
                onComplete?.Invoke();
                yield break;
            }

            // Land at euler-z ≡ ComputeLandingZ (negative = clockwise, matching the
            // clockwise green fill), after several full clockwise revolutions.
            float targetZ     = ComputeLandingZ(chance, success);
            int   revolutions = UnityEngine.Random.Range(5, 8);
            float endZ        = -(revolutions * 360f) + targetZ;

            const float duration = 4.5f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float eased = EaseOutQuint(Mathf.Clamp01(elapsed / duration));
                _needlePivot.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(0f, endZ, eased));
                yield return null;
            }
            _needlePivot.localEulerAngles = new Vector3(0f, 0f, endZ);

            yield return new WaitForSecondsRealtime(0.35f);
            yield return StartCoroutine(FlashResultColor(success));

            onComplete?.Invoke();
        }

        // ── Optimistic deferred spin (backend-authoritative upgrade) ─────────────
        // The needle starts spinning the instant the player taps, before the server
        // result is known, and keeps spinning until ProvideResult supplies the
        // authoritative success/fail — then it decelerates onto the matching zone.
        // Success/fail is NEVER guessed: the wheel just spins until the server answers.
        bool  _hasResult;
        bool  _resultSuccess;
        float _resultChance;

        /// <summary>Supplies the authoritative result so a deferred spin can land.</summary>
        public void ProvideResult(bool success, float chance)
        {
            _resultSuccess = success;
            _resultChance  = chance;
            _hasResult     = true;
        }

        /// <summary>
        /// Spins continuously until ProvideResult is called, then decelerates onto the
        /// backend success/fail zone and flashes the result. previewChance only colors
        /// the arc while waiting; the landing uses the provided result chance.
        /// </summary>
        public IEnumerator SpinUntilResolved(float previewChance, Action<bool> onComplete)
        {
            _hasResult = false;
            SetChance(previewChance);

            if (_needlePivot == null)
            {
                while (!_hasResult) yield return null;
                onComplete?.Invoke(_resultSuccess);
                yield break;
            }

            const float speed   = 720f;  // steady angular velocity, deg/sec (CW)
            const float spinUp   = 0.35f; // brief ramp so the start feels natural
            float angle = 0f;

            for (float t = 0f; t < spinUp; t += Time.unscaledDeltaTime)
            {
                angle -= Mathf.Lerp(0f, speed, t / spinUp) * Time.unscaledDeltaTime;
                _needlePivot.localEulerAngles = new Vector3(0f, 0f, angle);
                yield return null;
            }

            // Free-spin until the server result is in (covers the network wait).
            while (!_hasResult)
            {
                angle -= speed * Time.unscaledDeltaTime;
                _needlePivot.localEulerAngles = new Vector3(0f, 0f, angle);
                yield return null;
            }

            // Decelerate from the current angle onto the result zone. The final angle
            // must be ≡ ComputeLandingZ (mod 360) and at least a few more revolutions CW.
            SetChance(_resultChance);
            float landingMod = ComputeLandingZ(_resultChance, _resultSuccess);
            float candidate  = angle - 1080f;  // ≥ 3 extra revolutions
            float final      = candidate - Mathf.Repeat(candidate - landingMod, 360f);

            const float decel = 2.4f;
            float start = angle;
            for (float t = 0f; t < decel; t += Time.unscaledDeltaTime)
            {
                float eased = EaseOutQuint(Mathf.Clamp01(t / decel));
                _needlePivot.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(start, final, eased));
                yield return null;
            }
            _needlePivot.localEulerAngles = new Vector3(0f, 0f, final);

            yield return new WaitForSecondsRealtime(0.35f);
            yield return StartCoroutine(FlashResultColor(_resultSuccess));

            onComplete?.Invoke(_resultSuccess);
        }

        // ─────────────────────────────────────────────────────────────────────
        // INTERNAL
        // ─────────────────────────────────────────────────────────────────────

        static GameObject BuildArcImage(string name, RectTransform parent,
            float size, Vector2 pos, Sprite sprite, Color color,
            float fillAmount, out Image imgOut)
        {
            var go  = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt  = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = pos;

            var img = go.GetComponent<Image>();
            img.sprite        = sprite;
            img.type          = Image.Type.Filled;
            img.fillMethod    = Image.FillMethod.Radial360;
            img.fillOrigin    = (int)Image.Origin360.Top;
            img.fillClockwise = true;
            img.fillAmount    = fillAmount;
            img.color         = color;
            img.raycastTarget = false;

            imgOut = img;
            return go;
        }

        // Small 45°-rotated square = diamond marker, centered at <paramref name="radius"/>
        // up from the pivot (12-o'clock at rest). The pivot's rotation carries it
        // around the outside of the ring.
        static void AddDiamond(RectTransform pivot, string name, float radius,
                               float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(pivot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(0f, radius);
            rt.localRotation    = Quaternion.Euler(0f, 0f, 45f);
            var img = go.GetComponent<Image>();
            img.color         = color;
            img.raycastTarget = false;
        }

        IEnumerator FlashResultColor(bool success)
        {
            if (_successArc == null) yield break;

            Color resultCol = success ? ColHigh : ColFail;
            Color origCol   = _successArc.color;

            const float fadeIn = 0.30f;
            for (float t = 0f; t < fadeIn; t += Time.unscaledDeltaTime)
            {
                _successArc.color = Color.Lerp(origCol, resultCol, t / fadeIn);
                yield return null;
            }
            _successArc.color = resultCol;
            yield return new WaitForSecondsRealtime(0.60f);

            if (!success)
            {
                const float fadeOut = 0.40f;
                for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
                {
                    _successArc.color = Color.Lerp(resultCol, _chanceColor, t / fadeOut);
                    yield return null;
                }
                _successArc.color = _chanceColor;
            }
        }

        // ── Landing math: SINGLE SOURCE OF TRUTH (do not flip signs casually) ──────
        // CONVENTION, derived once so AnimateSpin and SpinUntilResolved can never drift:
        //   • The success arc is a Radial360 Image with fillOrigin = Top and
        //     fillClockwise = true, fillAmount = chance. So the GREEN success wedge
        //     sweeps CLOCKWISE from 12-o'clock and spans [0 .. chance*360] degrees;
        //     the remaining [chance*360 .. 360] is the dark fail wedge.
        //   • The marker rides _needlePivot. Unity UI z-rotation is COUNTER-clockwise
        //     for positive z, so a NEGATIVE euler-z moves the marker CLOCKWISE. A
        //     landing angle of L degrees clockwise-from-top is therefore reached by
        //     setting euler-z = -L  (that is exactly ComputeLandingZ).
        //   • ComputeLandingAngle returns L inside the success wedge on success and
        //     inside the fail wedge on failure (10% pad off each edge), so the marker
        //     visually lands in the matching colour for every chance.
        // VALIDATION:
        //   chance 0.8 → success L∈[28.8,259.2] (within the 288° green); fail
        //                L∈[295.2,352.8] (within the remaining 72° dark).
        //   chance 0.2 → success L∈[7.2,64.8]   (within the 72° green);  fail
        //                L∈[100.8,331.2] (within the remaining 288° dark).
        // Both animation paths MUST stop at euler-z ≡ ComputeLandingZ(chance, success).
        public static float ComputeLandingZ(float chance, bool success)
            => -ComputeLandingAngle(chance, success);

        /// <summary>
        /// Landing angle (degrees CW from 12-o'clock) inside the correct zone,
        /// padded 10 % from edges so the needle never touches a boundary.
        /// </summary>
        static float ComputeLandingAngle(float chance, bool success)
        {
            float successEnd = Mathf.Clamp01(chance) * 360f;

            if (success)
            {
                float pad = successEnd * 0.10f;
                float lo  = pad;
                float hi  = successEnd - pad;
                return hi > lo ? UnityEngine.Random.Range(lo, hi) : successEnd * 0.5f;
            }
            else
            {
                float failStart = successEnd;
                float pad       = (360f - failStart) * 0.10f;
                float lo        = failStart + pad;
                float hi        = 360f - pad;
                return hi > lo
                    ? UnityEngine.Random.Range(lo, hi)
                    : failStart + (360f - failStart) * 0.5f;
            }
        }

        /// <summary>
        /// Generates a white ring/donut Sprite in memory.
        /// Pixels are opaque white inside the ring band [innerRadius, outerRadius]
        /// and fully transparent elsewhere (with 1-px anti-aliased edges).
        /// Using this as an Image sprite gives a true circular arc when
        /// Image.Type.Filled + FillMethod.Radial360 is applied.
        /// </summary>
        static Sprite CreateRingSprite(int outerRadius, int innerRadius)
        {
            int size = outerRadius * 2;
            var tex  = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp
            };

            float cx = size * 0.5f - 0.5f;
            float cy = size * 0.5f - 0.5f;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                // 1-px soft edge on both inner and outer boundary
                float outer = Mathf.Clamp01(outerRadius - dist);
                float inner = Mathf.Clamp01(dist - innerRadius);
                byte  a     = (byte)(Mathf.Min(outer, inner) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            // pixelsPerUnit = size makes the sprite 1 Unity-unit wide.
            // The Image component stretches it to the RectTransform size regardless.
            return Sprite.Create(tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: size);
        }

        /// <summary>
        /// Generates a filled-circle Sprite procedurally so we never depend on
        /// any built-in Unity resource that may not exist in newer package versions.
        /// <paramref name="radius"/> in pixels; output texture is (radius*2) × (radius*2).
        /// </summary>
        static Sprite CreateCircleSprite(int radius)
        {
            int size = radius * 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp
            };

            float cx = size * 0.5f - 0.5f;
            float cy = size * 0.5f - 0.5f;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                float a    = Mathf.Clamp01(radius - dist);   // 1-px anti-aliased edge
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: size);
        }

        static float EaseOutQuint(float t) => 1f - Mathf.Pow(1f - t, 5f);
    }
}
