using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Profile;

namespace ValoCase.UI
{
    /// <summary>
    /// Persistent global top profile bar — Valorant dark tactical style.
    /// Matches BottomNavBar's color palette and corner-mark design language.
    ///
    /// Shows on every screen: avatar, username, level, VP balance, gear button.
    /// Auto-instantiated by ValoCaseUIBuilder — no manual Inspector work needed.
    ///
    /// Gear button → UINavigator.Navigate(Settings).
    /// Avatar click → no action (settings only via gear button).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TopProfileBar : MonoBehaviour
    {
        [SerializeField] UINavigator navigator;

        // ── Palette — identical to BottomNavBar ───────────────────────────────
        static readonly Color NavBg      = new Color(0.031f, 0.055f, 0.102f, 1f);    // #080E1A opaque
        static readonly Color BorderCol  = new Color(1f, 0.122f, 0.224f, 0.22f);     // #FF1F3A 22%
        static readonly Color ActiveRed  = new Color(1f, 0.122f, 0.224f, 1f);        // #FF1F3A
        static readonly Color DimWhite   = new Color(1f, 1f, 1f, 0.38f);             // inactive
        static readonly Color TextBright = new Color(0.925f, 0.910f, 0.882f, 1f);    // #ECE8E1

        // ── Runtime UI refs ───────────────────────────────────────────────────
        Image           _avatarImg;
        TextMeshProUGUI _usernameLabel;
        TextMeshProUGUI _vpLabel;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            Debug.Log("[TOP_BAR_DEBUG] Awake");
            BuildUI();
            Debug.Log("[TOP_BAR] Created");
        }

        void Start()
        {
            Debug.Log("[TOP_BAR_DEBUG] Start");
            Debug.Log("[TOP_BAR_DEBUG] username="   + PlayerProfileData.Username);
            Debug.Log("[TOP_BAR_DEBUG] avatarKey="  + PlayerProfileData.AvatarKey);
            Debug.Log("[TOP_BAR_DEBUG] avatar null=" + (PlayerProfileData.Avatar == null));
            var _ctxAtStart = GameContext.Instance;
            Debug.Log("[TOP_BAR_DEBUG] ctx null="        + (_ctxAtStart == null));
            Debug.Log("[TOP_BAR_DEBUG] vp service null=" + (_ctxAtStart?.Vp == null));
            Debug.Log("[TOP_BAR_DEBUG] balance="         + (_ctxAtStart?.Vp?.Balance ?? 0));

            // Fallback: wire navigator at runtime if builder didn't set it
            if (navigator == null)
                navigator = GetComponentInParent<UINavigator>()
                         ?? Object.FindFirstObjectByType<UINavigator>();

            SyncProfile();
            SyncVp();
        }

        void OnEnable()
        {
            Debug.Log("[TOP_BAR_DEBUG] OnEnable");
            PlayerProfileData.OnProfileChanged += OnProfileChanged;
            GameEvents.OnVpChanged             += OnVpChanged;
        }

        void OnDisable()
        {
            PlayerProfileData.OnProfileChanged -= OnProfileChanged;
            GameEvents.OnVpChanged             -= OnVpChanged;
        }

        // ── Event handlers ────────────────────────────────────────────────────
        void OnProfileChanged()
        {
            SyncProfile();
            Debug.Log("[TOP_BAR] Profile updated");
        }

        void OnVpChanged(int _, int current)
        {
            SyncVp();
            Debug.Log("[TOP_BAR] VP updated");
        }

        void OnGearClicked()
        {
            Debug.Log("[TOP_BAR] Settings clicked");
            navigator?.Navigate(ScreenType.Settings);
        }

        // ── Data sync ─────────────────────────────────────────────────────────
        void SyncProfile()
        {
            Debug.Log("[TOP_BAR_DEBUG] SyncProfile" +
                      " username="   + PlayerProfileData.Username +
                      " avatarKey="  + PlayerProfileData.AvatarKey +
                      " avatar null=" + (PlayerProfileData.Avatar == null));
            if (_avatarImg != null)
            {
                _avatarImg.color  = Color.white;
                _avatarImg.sprite = PlayerProfileData.Avatar;
            }
            if (_usernameLabel != null)
                _usernameLabel.text = PlayerProfileData.Username.ToUpper();
        }

        void SyncVp()
        {
            var ctx = GameContext.Instance;
            var bal = ctx?.Vp?.Balance ?? 0;
            Debug.Log("[TOP_BAR_DEBUG] SyncVp" +
                      " ctx null="        + (ctx == null) +
                      " vp service null=" + (ctx?.Vp == null) +
                      " balance="         + bal);
            if (_vpLabel == null) return;
            _vpLabel.text = FormatVp(bal);
        }

        static string FormatVp(int amount)
        {
            if (amount >= 1_000_000) return (amount / 1_000_000f).ToString("0.##") + "M VP";
            if (amount >= 1_000)     return (amount / 1_000f).ToString("0.##") + "K VP";
            return amount + " VP";
        }

        // ── Build ─────────────────────────────────────────────────────────────
        void BuildUI()
        {
            const float BarH = 86f;

            // Outer rect — anchored to SafeArea TOP, full width
            var rt = (RectTransform)transform;
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(0f, BarH);

            // ── Dark panel (full width, full height) ──────────────────────────
            var panel = NewRT("TopPanel", rt, Vector2.zero, Vector2.one);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = NavBg;

            var panelOl = panel.gameObject.AddComponent<Outline>();
            panelOl.effectColor    = BorderCol;
            panelOl.effectDistance = new Vector2(1f, -1f);

            // Bottom border line — mirrors BottomNavBar's top red line
            var botBorder = NewRT("BotBorder", panel,
                Vector2.zero, new Vector2(1f, 0f));
            botBorder.pivot            = new Vector2(0.5f, 0f);
            botBorder.anchoredPosition = Vector2.zero;
            botBorder.sizeDelta        = new Vector2(0f, 1.5f);
            botBorder.gameObject.AddComponent<Image>().color = BorderCol;
            botBorder.GetComponent<Image>().raycastTarget = false;

            // Tactical corner accent marks (same as BottomNavBar)
            AddCornerMark(panel, top: true,  left: true);
            AddCornerMark(panel, top: true,  left: false);
            AddCornerMark(panel, top: false, left: true);
            AddCornerMark(panel, top: false, left: false);

            // ── Circular avatar (44 px, masked) ───────────────────────────────
            var circleSpr = MakeCircleSprite(64);

            var maskGo = new GameObject("AvMask",
                typeof(RectTransform), typeof(Image), typeof(Mask));
            maskGo.transform.SetParent(panel, false);
            var maskRt = (RectTransform)maskGo.transform;
            maskRt.anchorMin        = new Vector2(0f, 0.5f);
            maskRt.anchorMax        = new Vector2(0f, 0.5f);
            maskRt.pivot            = new Vector2(0f, 0.5f);
            maskRt.anchoredPosition = new Vector2(12f, 0f);
            maskRt.sizeDelta        = new Vector2(52f, 52f);
            var maskImg = maskGo.GetComponent<Image>();
            maskImg.sprite        = circleSpr;
            maskImg.type          = Image.Type.Simple;
            maskImg.raycastTarget = false;
            maskGo.GetComponent<Mask>().showMaskGraphic = false;

            var avGo = new GameObject("AvatarImg",
                typeof(RectTransform), typeof(Image));
            avGo.transform.SetParent(maskGo.transform, false);
            var avRt = (RectTransform)avGo.transform;
            avRt.anchorMin = Vector2.zero; avRt.anchorMax = Vector2.one;
            avRt.offsetMin = Vector2.zero; avRt.offsetMax = Vector2.zero;
            _avatarImg               = avGo.GetComponent<Image>();
            _avatarImg.color         = Color.white;
            _avatarImg.preserveAspect = true;
            _avatarImg.raycastTarget  = false;

            // Red neon ring outline around the avatar
            var ringGo = new GameObject("AvRing",
                typeof(RectTransform), typeof(Image), typeof(Outline));
            ringGo.transform.SetParent(panel, false);
            var ringRt = (RectTransform)ringGo.transform;
            ringRt.anchorMin        = new Vector2(0f, 0.5f);
            ringRt.anchorMax        = new Vector2(0f, 0.5f);
            ringRt.pivot            = new Vector2(0f, 0.5f);
            ringRt.anchoredPosition = new Vector2(12f, 0f);
            ringRt.sizeDelta        = new Vector2(52f, 52f);
            var ringImg = ringGo.GetComponent<Image>();
            ringImg.sprite        = circleSpr;
            ringImg.color         = new Color(0f, 0f, 0f, 0f);   // transparent fill
            ringImg.raycastTarget = false;
            var ringOl = ringGo.GetComponent<Outline>();
            ringOl.effectColor    = new Color(1f, 0.122f, 0.224f, 0.50f);
            ringOl.effectDistance = new Vector2(1.5f, -1.5f);

            // ── Username + Level column (left side) ───────────────────────────
            var colGo = new GameObject("NameCol", typeof(RectTransform));
            colGo.transform.SetParent(panel, false);
            var colRt = (RectTransform)colGo.transform;
            colRt.anchorMin        = new Vector2(0f, 0.5f);
            colRt.anchorMax        = new Vector2(0f, 0.5f);
            colRt.pivot            = new Vector2(0f, 0.5f);
            colRt.anchoredPosition = new Vector2(72f, 0f);   // 12 pad + 52 avatar + 8 gap
            colRt.sizeDelta        = new Vector2(220f, 52f);

            var vlg = colGo.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment         = TextAnchor.MiddleLeft;
            vlg.spacing                = 1f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(0, 0, 4, 4);

            _usernameLabel              = MakeTmp(colGo.transform, "Username", "AGENT", 15f, TextBright);
            _usernameLabel.fontStyle    = FontStyles.Bold;
            _usernameLabel.raycastTarget = false;
            _usernameLabel.gameObject.AddComponent<LayoutElement>().minHeight = 20f;

            var lvlLbl = MakeTmp(colGo.transform, "Level", "LVL: 1", 12f, DimWhite);
            lvlLbl.raycastTarget = false;
            lvlLbl.gameObject.AddComponent<LayoutElement>().minHeight = 16f;

            // ── Gear / Settings button (far right) ────────────────────────────
            var gearGo = new GameObject("GearBtn",
                typeof(RectTransform), typeof(Image), typeof(Button));
            gearGo.transform.SetParent(panel, false);
            var gearRt = (RectTransform)gearGo.transform;
            gearRt.anchorMin        = new Vector2(1f, 0.5f);
            gearRt.anchorMax        = new Vector2(1f, 0.5f);
            gearRt.pivot            = new Vector2(1f, 0.5f);
            gearRt.anchoredPosition = new Vector2(-10f, 0f);
            gearRt.sizeDelta        = new Vector2(48f, 48f);
            gearGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // transparent hit area
            var gearBtn = gearGo.GetComponent<Button>();
            gearBtn.transition = Selectable.Transition.None;
            gearBtn.onClick.AddListener(OnGearClicked);

            var gearIco  = MakeTmp(gearGo.transform, "GearIco", "⚙", 26f, DimWhite);
            gearIco.alignment     = TextAlignmentOptions.Center;
            gearIco.raycastTarget = false;
            var gIcoRt = gearIco.rectTransform;
            gIcoRt.anchorMin = Vector2.zero; gIcoRt.anchorMax = Vector2.one;
            gIcoRt.offsetMin = Vector2.zero; gIcoRt.offsetMax = Vector2.zero;

            // ── VP balance label (right of gear) ──────────────────────────────
            var vpGo = new GameObject("VpRow", typeof(RectTransform));
            vpGo.transform.SetParent(panel, false);
            var vpRt = (RectTransform)vpGo.transform;
            vpRt.anchorMin        = new Vector2(1f, 0.5f);
            vpRt.anchorMax        = new Vector2(1f, 0.5f);
            vpRt.pivot            = new Vector2(1f, 0.5f);
            vpRt.anchoredPosition = new Vector2(-62f, 0f);  // 10 pad + 48 gear + 4 gap
            vpRt.sizeDelta        = new Vector2(210f, 36f);

            _vpLabel              = MakeTmp(vpGo.transform, "VpLbl", "0 VP", 15f, TextBright);
            _vpLabel.alignment    = TextAlignmentOptions.Right;
            _vpLabel.fontStyle    = FontStyles.Bold;
            _vpLabel.raycastTarget = false;
            var vpLblRt = _vpLabel.rectTransform;
            vpLblRt.anchorMin = Vector2.zero; vpLblRt.anchorMax = Vector2.one;
            vpLblRt.offsetMin = Vector2.zero; vpLblRt.offsetMax = Vector2.zero;
        }

        // ── Corner marks (identical code path to BottomNavBar) ────────────────
        static void AddCornerMark(RectTransform parent, bool top, bool left)
        {
            float xAnchor = left ? 0f : 1f;
            float yAnchor = top  ? 1f : 0f;
            float xPivot  = left ? 0f : 1f;
            float yPivot  = top  ? 1f : 0f;
            float xOff    = left ?  3f : -3f;
            float yOff    = top  ? -3f :  3f;

            var h = NewRT("CH", parent,
                new Vector2(xAnchor, yAnchor), new Vector2(xAnchor, yAnchor));
            h.pivot            = new Vector2(xPivot, yPivot);
            h.anchoredPosition = new Vector2(xOff, yOff);
            h.sizeDelta        = new Vector2(8f, 1f);
            h.gameObject.AddComponent<Image>().color       = ActiveRed;
            h.GetComponent<Image>().raycastTarget          = false;

            var v = NewRT("CV", parent,
                new Vector2(xAnchor, yAnchor), new Vector2(xAnchor, yAnchor));
            v.pivot            = new Vector2(xPivot, yPivot);
            v.anchoredPosition = new Vector2(xOff, yOff);
            v.sizeDelta        = new Vector2(1f, 8f);
            v.gameObject.AddComponent<Image>().color       = ActiveRed;
            v.GetComponent<Image>().raycastTarget          = false;
        }

        // ── Rect helpers ──────────────────────────────────────────────────────
        static RectTransform NewRT(string n, RectTransform p, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(p, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static TextMeshProUGUI MakeTmp(Transform parent, string name, string text,
                                        float size, Color color)
        {
            var go  = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text               = text;
            tmp.fontSize           = size;
            tmp.color              = color;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Ellipsis;
            return tmp;
        }

        /// <summary>Generates a soft-edged circle sprite at runtime. No asset required.</summary>
        static Sprite MakeCircleSprite(int size)
        {
            var   tex  = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;
            float r    = half - 1f;
            var   px   = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - half;
                float dy = y + 0.5f - half;
                float a  = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 1f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
