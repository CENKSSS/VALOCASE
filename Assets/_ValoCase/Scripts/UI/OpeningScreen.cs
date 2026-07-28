using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UI;

namespace ValoCase.UI
{
    // Full-screen branded opening overlay shown at boot: pure black background, a
    // centered responsive "anasayfa" hero image, and a subtle bottom disclaimer.
    // Self-spawns after scene load, holds a fixed real-time duration, then fades
    // out and destroys itself. Independent of backend sync; never gates gameplay.
    [Preserve]
    public sealed class OpeningScreen : MonoBehaviour
    {
        const string LogoResourcePath = "Art/UI/Giris/anasayfa";
        const string DisclaimerText =
            "This is a fan-made game. We are not affiliated with any company, publisher, or official organization.";

        const float HoldSeconds = 3.0f;
        const float FadeSeconds = 0.4f;

        static bool _spawned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetGuard() => _spawned = false;

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            if (_spawned) return;
            _spawned = true;
            var go = new GameObject("OpeningScreen");
            DontDestroyOnLoad(go);
            go.AddComponent<OpeningScreen>();
        }

        CanvasGroup _group;

        void Awake()
        {
            Build();
            StartCoroutine(Run());
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();
            _group = gameObject.AddComponent<CanvasGroup>();

            var bg = NewChild("Background", transform);
            StretchFull(bg);
            var bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.color = Color.black;

            var logo = NewChild("Logo", transform);
            logo.anchorMin = new Vector2(0.05f, 0.16f);
            logo.anchorMax = new Vector2(0.95f, 0.9f);
            logo.offsetMin = Vector2.zero;
            logo.offsetMax = Vector2.zero;
            var logoImage = logo.gameObject.AddComponent<Image>();
            logoImage.raycastTarget = false;
            logoImage.preserveAspect = true;
            var sprite = Resources.Load<Sprite>(LogoResourcePath);
            if (sprite != null) logoImage.sprite = sprite;
            else { logoImage.enabled = false; Debug.LogWarning("[OpeningScreen] Missing sprite at Resources/" + LogoResourcePath); }

            var safe = NewChild("SafeArea", transform);
            StretchFull(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            var disclaimer = NewChild("Disclaimer", safe);
            disclaimer.anchorMin = new Vector2(0.5f, 0f);
            disclaimer.anchorMax = new Vector2(0.5f, 0f);
            disclaimer.pivot = new Vector2(0.5f, 0f);
            disclaimer.anchoredPosition = new Vector2(0f, 30f);
            disclaimer.sizeDelta = new Vector2(920f, 80f);
            var tmp = disclaimer.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = DisclaimerText;
            tmp.fontSize = 24f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.color = new Color(1f, 1f, 1f, 0.45f);
            tmp.raycastTarget = false;
            var font = TryGetTmpDefaultFont();
            if (font != null) tmp.font = font;
        }

        IEnumerator Run()
        {
            // Discard the heavy scene-load frame so its hitch isn't counted as hold time.
            yield return null;

            yield return new WaitForSecondsRealtime(HoldSeconds);

            _group.blocksRaycasts = false;
            var start = Time.realtimeSinceStartup;
            float t;
            while ((t = Time.realtimeSinceStartup - start) < FadeSeconds)
            {
                _group.alpha = 1f - Mathf.Clamp01(t / FadeSeconds);
                yield return null;
            }

            Destroy(gameObject);
        }

        static RectTransform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static TMP_FontAsset TryGetTmpDefaultFont()
        {
            try { return TMP_Settings.defaultFontAsset; }
            catch (System.NullReferenceException) { return null; }
        }
    }
}
