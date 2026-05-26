using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;

namespace ValoCase.UI
{
    /// <summary>
    /// Loads an image from disk at runtime and displays it as a full-screen "cover" background.
    ///
    /// Cover behaviour (no black bars, no distortion, no stretching):
    ///   • The Image sits inside a RectMask2D container that fills the entire screen.
    ///   • An AspectRatioFitter(EnvelopeParent) on the Image grows it just enough
    ///     to cover the container in both axes while keeping the original aspect ratio.
    ///   • Overflow is clipped by the mask — the result is exactly like CSS object-fit:cover.
    ///
    /// Setup (done automatically by ValoCaseUIBuilder):
    ///   Screen RectTransform
    ///     └── BgContainer  (RectMask2D, stretch-full)      ← first sibling → behind all UI
    ///           └── BgImage  (Image + AspectRatioFitter)   ← wired via [SerializeField]
    ///
    /// To relocate the image file, change ProjectPaths.ArkaPlanPath.
    /// To override per-instance, set the overridePath field in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FullscreenBackground : MonoBehaviour
    {
        // ── Wired by ValoCaseUIBuilder (or set in Inspector) ─────────────────
        [SerializeField] Image             backgroundImage;
        [SerializeField] AspectRatioFitter aspectFitter;

        [Tooltip("Leave empty to use ProjectPaths.ArkaPlanPath automatically.")]
        [SerializeField] string overridePath = "";

        // ── Runtime ───────────────────────────────────────────────────────────
        Sprite _loaded;

        void Start()
        {
            // Self-heal: if the builder wired nothing, create the structure now.
            EnsureStructure();
            LoadImage();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Force a reload from disk (e.g. after the file is replaced).</summary>
        public void Reload()
        {
            if (_loaded != null)
            {
                Destroy(_loaded.texture);
                Destroy(_loaded);
                _loaded = null;
            }
            LoadImage();
        }

        // ── Internal ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the BgContainer + BgImage hierarchy if it was not built by the editor builder.
        /// Safe to call more than once — skips work when structure already exists.
        /// </summary>
        void EnsureStructure()
        {
            if (backgroundImage != null) return;   // already wired

            var self = (RectTransform)transform;

            // Container — clips overflow so the oversized cover image stays inside.
            var cGo = new GameObject("BgContainer",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            var cRt = cGo.GetComponent<RectTransform>();
            cRt.SetParent(self, false);
            cRt.anchorMin = Vector2.zero;
            cRt.anchorMax = Vector2.one;
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;
            cGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);  // transparent
            cRt.SetSiblingIndex(0);   // ← first child = rendered behind everything

            // BgImage — the actual sprite, controlled by AspectRatioFitter.
            var bGo = new GameObject("BgImage",
                typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            var bRt = bGo.GetComponent<RectTransform>();
            bRt.SetParent(cRt, false);
            // Centered inside the container; AspectRatioFitter handles the size.
            bRt.anchorMin        = new Vector2(0.5f, 0.5f);
            bRt.anchorMax        = new Vector2(0.5f, 0.5f);
            bRt.pivot            = new Vector2(0.5f, 0.5f);
            bRt.anchoredPosition = Vector2.zero;
            bRt.sizeDelta        = Vector2.zero;

            backgroundImage = bGo.GetComponent<Image>();
            backgroundImage.color         = Color.white;
            backgroundImage.raycastTarget = false;
            backgroundImage.preserveAspect = false;   // fitter handles aspect

            aspectFitter = bGo.GetComponent<AspectRatioFitter>();
            aspectFitter.aspectMode  = AspectRatioFitter.AspectMode.EnvelopeParent;
            aspectFitter.aspectRatio = 16f / 9f;   // placeholder; updated after load
        }

        void LoadImage()
        {
            if (_loaded != null) return;   // already done

            var path = string.IsNullOrEmpty(overridePath)
                ? ProjectPaths.ArkaPlanPath
                : overridePath;

            if (!File.Exists(path))
            {
                // Try both .jpg and .jpeg spellings as a fallback.
                var alt = Path.ChangeExtension(path, ".jpeg");
                if (File.Exists(alt)) path = alt;
                else
                {
                    Debug.LogWarning($"[FullscreenBackground] Dosya bulunamadı: {path}");
                    return;
                }
            }

            byte[] bytes;
            try   { bytes = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                Debug.LogError($"[FullscreenBackground] Okunamadı ({path}): {ex.Message}");
                return;
            }

            // Use RGB24 for JPEGs — no alpha channel needed, saves memory.
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, mipChain: false);
            tex.name       = "ArkaPlan";
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;

            if (!tex.LoadImage(bytes))
            {
                Debug.LogWarning($"[FullscreenBackground] LoadImage başarısız: {path}");
                Destroy(tex);
                return;
            }

            _loaded = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f);
            _loaded.name = "ArkaPlan";

            if (backgroundImage != null)
            {
                backgroundImage.sprite = _loaded;

                // Update the fitter to match the real image aspect ratio.
                if (aspectFitter != null)
                    aspectFitter.aspectRatio = (float)tex.width / tex.height;
            }

            Debug.Log($"[FullscreenBackground] {tex.width}×{tex.height}px yüklendi ← {path}");
        }
    }
}
