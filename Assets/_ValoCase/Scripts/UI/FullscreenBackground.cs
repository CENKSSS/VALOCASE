using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;

namespace ValoCase.UI
{
    /// <summary>
    /// Loads a sprite from Resources at runtime and displays it as a full-screen "cover" background.
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

        // Sprites shared between instances, keyed by file path — screens using the
        // same background reuse one texture instead of decoding it once each.
        static readonly Dictionary<string, Sprite> SharedSprites = new();

        void Start()
        {
            // Self-heal: if the builder wired nothing, create the structure now.
            EnsureStructure();
            LoadImage();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Attaches the shared section background (ProjectPaths.SharedScreenBackgroundPath)
        /// to a screen root as a full-screen cover image behind all its UI.
        /// Idempotent — safe to call from OnShown every time the screen appears.
        /// </summary>
        public static void AttachShared(GameObject host) =>
            Attach(host, ProjectPaths.SharedScreenBackgroundPath);

        /// <summary>
        /// Attaches an arbitrary background image to a host rect as a cover image
        /// (aspect preserved, overflow clipped). The host's own rect defines the
        /// covered region. Idempotent.
        /// </summary>
        public static void Attach(GameObject host, string path)
        {
            var bg = host.GetComponent<FullscreenBackground>();
            if (bg == null) bg = host.AddComponent<FullscreenBackground>();
            if (string.IsNullOrEmpty(bg.overridePath))
                bg.overridePath = path;
            bg.EnsureStructure();
            bg.LoadImage();
        }

        /// <summary>Force a reload (e.g. after invalidating the cache).</summary>
        public void Reload()
        {
            // Sprites come from Resources (shared, managed assets) — do NOT Destroy
            // them here, only drop our cached references so LoadImage re-fetches.
            var path = string.IsNullOrEmpty(overridePath)
                ? ProjectPaths.ArkaPlanPath
                : overridePath;
            SharedSprites.Remove(path);
            _loaded = null;
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

            // path is a Resources path (no extension, forward slashes).
            var path = string.IsNullOrEmpty(overridePath)
                ? ProjectPaths.ArkaPlanPath
                : overridePath;

            if (SharedSprites.TryGetValue(path, out var shared) &&
                shared != null && shared.texture != null)
            {
                _loaded = shared;
                ApplySprite(shared);
                return;
            }

            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"[FullscreenBackground] Sprite bulunamadı (Resources): {path}");
                return;
            }

            _loaded = sprite;
            SharedSprites[path] = sprite;
            ApplySprite(sprite);

            if (sprite.texture != null)
                Debug.Log($"[FullscreenBackground] {sprite.texture.width}×{sprite.texture.height}px yüklendi ← {path}");
        }

        void ApplySprite(Sprite sprite)
        {
            if (backgroundImage == null) return;
            backgroundImage.sprite = sprite;

            // Update the fitter to match the real image aspect ratio.
            if (aspectFitter != null && sprite.texture != null)
                aspectFitter.aspectRatio = (float)sprite.texture.width / sprite.texture.height;
        }
    }
}
