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
    /// Render order within centerPanel (back → front):
    ///   CenterTitle | ArcBg | SuccessArc | InputSlot | TargetSlot | … | NeedlePivot | ChanceLabel
    /// </summary>
    public sealed class UpgradeSpinAnimator : MonoBehaviour
    {
        RectTransform _needlePivot;
        Image         _successArc;
        bool          _initialized;

        // ── Colors ────────────────────────────────────────────────────────────
        static readonly Color ColSuccess = new Color(0.22f, 0.96f, 0.40f, 1.00f);
        static readonly Color ColFail    = new Color(0.96f, 0.22f, 0.22f, 1.00f);
        static readonly Color ColArcBg   = new Color(0.08f, 0.09f, 0.18f, 1.00f);
        static readonly Color ColNeedle  = new Color(1.00f, 1.00f, 1.00f, 1.00f);
        static readonly Color ColDot     = new Color(1.00f, 0.18f, 0.55f, 1.00f);

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

            // Arc sized to fit between InputSlot bottom and UpgradeBtn top (~108 px gap).
            // 160 px outer diameter means only the thin ring band slightly overlaps the slots,
            // which is fine because the arcs are inserted BEHIND the slot cards.
            const int   outerR   = 80;            // outer radius px
            const int   innerR   = 52;            // inner radius px  → ring thickness = 28 px
            const float arcSize  = outerR * 2f;   // 160 px
            const float needleLen = 68f;           // tip sits in the ring band

            // Position: same anchor as chanceLabel so the % text is centred in the ring hole.
            Vector2 arcPos = chanceLabel != null
                ? chanceLabel.anchoredPosition
                : new Vector2(0f, 32f);

            Sprite ring   = CreateRingSprite(outerR, innerR);
            Sprite circle = CreateCircleSprite(8); // 16 px procedural dot — no builtin resource needed

            // ── Background ring ───────────────────────────────────────────────
            var bgGo = BuildArcImage("ArcBg", centerPanel, arcSize, arcPos, ring,
                                     ColArcBg, fillAmount: 1f, out _);

            // ── Success arc ───────────────────────────────────────────────────
            var arcGo = BuildArcImage("SuccessArc", centerPanel, arcSize, arcPos, ring,
                                      ColSuccess, fillAmount: 0f, out _successArc);

            // ── Needle pivot ──────────────────────────────────────────────────
            var pivotGo = new GameObject("NeedlePivot", typeof(RectTransform));
            pivotGo.transform.SetParent(centerPanel, false);
            _needlePivot = (RectTransform)pivotGo.transform;
            _needlePivot.anchorMin = _needlePivot.anchorMax = new Vector2(0.5f, 0.5f);
            _needlePivot.pivot     = new Vector2(0.5f, 0.5f);
            _needlePivot.sizeDelta = Vector2.zero;
            _needlePivot.anchoredPosition = arcPos;

            // Shadow bar
            AddNeedleBar(_needlePivot, needleLen, width: 6f,
                         new Color(0f, 0f, 0f, 0.50f), xOff: -1f);
            // Main bar
            AddNeedleBar(_needlePivot, needleLen, width: 3f, ColNeedle, xOff: 0f);

            // Tip diamond
            var tipGo = new GameObject("Tip", typeof(RectTransform), typeof(Image));
            tipGo.transform.SetParent(_needlePivot, false);
            var tipRt = (RectTransform)tipGo.transform;
            tipRt.anchorMin = tipRt.anchorMax = new Vector2(0.5f, 0.5f);
            tipRt.pivot     = new Vector2(0.5f, 0f);
            tipRt.sizeDelta = new Vector2(10f, 10f);
            tipRt.anchoredPosition = new Vector2(0f, needleLen - 5f);
            tipRt.localRotation    = Quaternion.Euler(0f, 0f, 45f);
            tipGo.GetComponent<Image>().color = new Color(1f, 0.90f, 0.90f, 1f);
            tipGo.GetComponent<Image>().raycastTarget = false;

            // Centre dot
            var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            dotGo.transform.SetParent(_needlePivot, false);
            var dotRt = (RectTransform)dotGo.transform;
            dotRt.anchorMin = dotRt.anchorMax = new Vector2(0.5f, 0.5f);
            dotRt.pivot     = new Vector2(0.5f, 0.5f);
            dotRt.sizeDelta = new Vector2(16f, 16f);
            dotRt.anchoredPosition = Vector2.zero;
            var dotImg = dotGo.GetComponent<Image>();
            dotImg.sprite = circle;
            dotImg.color  = ColDot;
            dotImg.raycastTarget = false;

            // ── Sibling / render order ────────────────────────────────────────
            // Arcs go BEHIND the slot cards (index 1 = just after CenterTitle at 0).
            // NeedlePivot and ChanceLabel go to the very front.
            bgGo.transform.SetSiblingIndex(1);   // dark ring behind slots
            arcGo.transform.SetSiblingIndex(2);  // green ring behind slots, above dark ring
            _needlePivot.SetAsLastSibling();
            if (chanceLabel != null) chanceLabel.SetAsLastSibling();
        }

        /// <summary>Updates the success arc fill (0–1).</summary>
        public void SetChance(float chance)
        {
            if (_successArc != null)
                _successArc.fillAmount = Mathf.Clamp01(chance);
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

            float landingDeg  = ComputeLandingAngle(chance, success);
            int   revolutions = UnityEngine.Random.Range(5, 8);
            float totalDeg    = revolutions * 360f + landingDeg;
            float endZ        = -totalDeg;   // clockwise on screen = negative Z

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

        static void AddNeedleBar(RectTransform pivot, float length, float width,
                                  Color color, float xOff)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(pivot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(width, length);
            rt.anchoredPosition = new Vector2(xOff, 0f);
            go.GetComponent<Image>().color = color;
            go.GetComponent<Image>().raycastTarget = false;
        }

        IEnumerator FlashResultColor(bool success)
        {
            if (_successArc == null) yield break;

            Color resultCol = success ? ColSuccess : ColFail;
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
                    _successArc.color = Color.Lerp(resultCol, ColSuccess, t / fadeOut);
                    yield return null;
                }
                _successArc.color = ColSuccess;
            }
        }

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
