#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using ValoCase.Core;
using ValoCase.Pooling;
using ValoCase.UI;
using ValoCase.UI.Screens;

namespace ValoCase.Editor
{
    // ── ValoCase UI Builder ──────────────────────────────────────────────
    // ANA giris noktasi. Tum partial dosyalar (Helpers / Cards / Popups /
    // CaseOpening / Upgrade) bu sinifa eklenir. Class adi, namespace ve eski
    // davranis aynidir; sadece dosya katmani bolunmustur.
    //
    // Partial dosyalar:
    //   ValoCaseUIBuilder.Main.cs        — Bu dosya: MenuItem'lar, canvas orchestration
    //   ValoCaseUIBuilder.Helpers.cs     — Ortak UI helper'lari
    //   ValoCaseUIBuilder.Cards.cs       — Kart prefablari (Skin/Reel/Drop/CaseList/WeaponSkin)
    //   ValoCaseUIBuilder.Popups.cs      — Popup'lar (Daily/Toast/SkinDetail/SkinWin)
    //   ValoCaseUIBuilder.CaseOpening.cs — CaseOpeningScreen layout
    //   ValoCaseUIBuilder.Upgrade.cs     — UpgradeScreen layout + alt builder'lar
    public static partial class ValoCaseUIBuilder
    {
        const string PrefabFolder = "Assets/_ValoCase/Prefabs";
        const string UiCanvasPrefabPath = PrefabFolder + "/PF_UICanvas.prefab";
        const string GameContextPrefabPath = PrefabFolder + "/PF_GameContext.prefab";
        const string SkinCardPrefabPath = PrefabFolder + "/PF_SkinCard.prefab";
        const string ReelItemPrefabPath = PrefabFolder + "/PF_ReelItem.prefab";
        const string CaseListItemPrefabPath = PrefabFolder + "/PF_CaseListItem.prefab";
        const string DropItemPrefabPath = PrefabFolder + "/PF_DropItem.prefab";
        const string WeaponSkinCardPrefabPath = PrefabFolder + "/PF_WeaponSkinCard.prefab";

        // ── Valorant Minimalist Palette ──────────────────────────────────────
        // İsimler kodun geri kalanı bozulmasın diye aynı bırakıldı ancak mat/flat Valorant renklerine çekildi.
        static readonly Color BgDark    = new(0.059f, 0.098f, 0.137f, 1f);      // #0F1923 Dark Slate
        static readonly Color Panel     = new(0.122f, 0.161f, 0.204f, 0.95f);   // Panel Slate
        static readonly Color AccentRed = new(1f, 0.275f, 0.333f, 1f);          // #FF4655 Valorant Coral Red
        static readonly Color TextWhite = new(0.925f, 0.910f, 0.882f, 1f);      // #ECE8E1 Off-White

        static readonly Color NeonCyan   = new(0.545f, 0.592f, 0.561f, 1f);     // #8B978F Valorant Gri
        static readonly Color NeonPurple = new(0.122f, 0.161f, 0.204f, 1f);     // Koyu Slate
        static readonly Color NeonPink   = new(1f, 0.275f, 0.333f, 1f);         // Coral Red
        static readonly Color NeonGreen  = new(0.404f, 0.859f, 0.686f, 1f);     // Mint Yeşili (Başarılı)
        static readonly Color NeonGold   = new(0.902f, 0.816f, 0.435f, 1f);     // Soft Altın
        static readonly Color TextDim    = new(0.545f, 0.592f, 0.561f, 1f);     // Gri Metin

        [MenuItem("ValoCase/Build UI Prefabs")]
        public static void BuildPrefabs()
        {
            if (!ValidateBuildEnvironment()) return;

            EnsureFolder(PrefabFolder);
            var skinCard = BuildSkinCardPrefab();
            var reelItem = BuildReelItemPrefab();
            var caseListItem = BuildCaseListItemPrefab();
            var dropItem = BuildDropItemPrefab();
            var weaponSkinCard = BuildWeaponSkinCardPrefab();
            BuildGameContextPrefab();
            var uiCanvas = BuildUiCanvasPrefab(skinCard, reelItem, caseListItem, dropItem, weaponSkinCard);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = uiCanvas;
            Debug.Log("[ValoCase] Created PF_UICanvas, PF_GameContext, PF_SkinCard, PF_ReelItem, PF_CaseListItem, PF_DropItem and PF_WeaponSkinCard under Assets/_ValoCase/Prefabs/");
        }

        [MenuItem("ValoCase/Setup Current Scene (UI + Systems)")]
        public static void SetupCurrentScene()
        {
            if (!ValidateBuildEnvironment()) return;
            BuildPrefabs();
            var scene = SceneManager.GetActiveScene();
            SetupScene(scene);
            Debug.Log("[ValoCase] Current scene ready. Press Play - main menu buttons should appear.");
        }

        public static void ImportTmpEssentialResources()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[ValoCase] Exit Play Mode before importing TextMesh Pro Essential Resources.");
                return;
            }

            var packagePath = FindTmpEssentialsPackage();
            if (string.IsNullOrEmpty(packagePath))
            {
                Debug.LogError("[ValoCase] TMP Essential Resources package was not found. Use Window > Text Mesh Pro > Import TMP Essential Resources.");
                return;
            }

            AssetDatabase.ImportPackage(packagePath, false);
            AssetDatabase.Refresh();
            Debug.Log("[ValoCase] TMP Essential Resources import requested.");
        }

        static bool ValidateBuildEnvironment()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[ValoCase] Exit Play Mode before building UI prefabs.");
                ShowDialog("ValoCase UI Builder", "Play Mode'dan cik, sonra ValoCase > Setup Current Scene komutunu tekrar calistir.");
                return false;
            }

            if (EditorApplication.isCompiling)
            {
                Debug.LogError("[ValoCase] Wait for script compilation to finish before building UI prefabs.");
                return false;
            }

            return EnsureTmpEssentialResources();
        }

        static bool EnsureTmpEssentialResources()
        {
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0)
                return true;

            ImportTmpEssentialResources();
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0)
                return true;

            Debug.LogError("[ValoCase] TextMesh Pro Essential Resources are missing.");
            ShowDialog("ValoCase UI Builder", "TextMesh Pro Essential Resources eksik. Window > Text Mesh Pro > Import TMP Essential Resources komutunu calistir, sonra ValoCase > Setup Current Scene'i tekrar dene.");
            return false;
        }

        static string FindTmpEssentialsPackage()
        {
            var packageCache = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "PackageCache"));
            if (!Directory.Exists(packageCache)) return null;
            var packages = Directory.GetFiles(packageCache, "TMP Essential Resources.unitypackage", SearchOption.AllDirectories);
            return packages.Length > 0 ? packages[0] : null;
        }

        static void ShowDialog(string title, string message)
        {
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog(title, message, "OK");
        }

        static void SetupScene(Scene scene)
        {
            ClearValoCaseObjects();

            var ctxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameContextPrefabPath);
            var uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiCanvasPrefabPath);
            if (ctxPrefab == null || uiPrefab == null)
            {
                Debug.LogError("[ValoCase] Prefab build failed.");
                return;
            }

            PrefabUtility.InstantiatePrefab(ctxPrefab, scene);
            PrefabUtility.InstantiatePrefab(uiPrefab, scene);

            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = CreateEventSystem();
                Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
            }

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.07f, 0.1f, 1f);
                cam.orthographic = true;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void ClearValoCaseObjects()
        {
            foreach (var c in Object.FindObjectsByType<GameContext>(FindObjectsInactive.Include))
                Undo.DestroyObjectImmediate(c.gameObject);
            foreach (var n in Object.FindObjectsByType<UINavigator>(FindObjectsInactive.Include))
                Undo.DestroyObjectImmediate(n.gameObject);
            foreach (var p in Object.FindObjectsByType<PoolManager>(FindObjectsInactive.Include))
                Undo.DestroyObjectImmediate(p.gameObject);
            foreach (var e in Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include))
                if (e.name == "EventSystem")
                    Undo.DestroyObjectImmediate(e.gameObject);
        }

        static GameObject BuildGameContextPrefab()
        {
            var go = new GameObject("PF_GameContext");
            go.AddComponent<GameContext>();
            return SavePrefab(go, GameContextPrefabPath);
        }

        static GameObject CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
            return go;
        }

        static GameObject BuildUiCanvasPrefab(GameObject skinCardPrefab, GameObject reelItemPrefab, GameObject caseListItemPrefab, GameObject dropItemPrefab, GameObject weaponSkinCardPrefab)
        {
            var root = new GameObject("PF_UICanvas");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            var eventSystem = CreateEventSystem();
            eventSystem.transform.SetParent(root.transform, false);

            var navigator = root.AddComponent<UINavigator>();
            var pool = root.AddComponent<PoolManager>();

            var poolRoot = new GameObject("PoolRoots").transform;
            poolRoot.SetParent(root.transform, false);
            var reelPool = new GameObject("ReelPoolRoot").transform;
            reelPool.SetParent(poolRoot, false);
            var cardPool = new GameObject("CardPoolRoot").transform;
            cardPool.SetParent(poolRoot, false);
            WirePoolManager(pool, skinCardPrefab.GetComponent<SkinCardView>(), reelItemPrefab.GetComponent<ReelItemView>(), reelPool, cardPool);

            var safe = CreateRect("SafeArea", root.transform, Vector2.zero);
            StretchFull(safe);
            DisableRaycast(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // Global TopBar and VpCounter removed — each screen manages its own
            // top bar and wallet display. SafeArea/Screens now fills full height.
            var screenHost = CreateRect("Screens", safe, Vector2.zero);
            StretchFull(screenHost);
            DisableRaycast(screenHost);
            screenHost.offsetMin = new Vector2(0f, BottomNavBar.Height);
            screenHost.offsetMax = new Vector2(0f, -TopProfileBar.Height);

            var caseItemView = caseListItemPrefab.GetComponent<CaseListItemView>();
            var dropItemView = dropItemPrefab.GetComponent<DropItemView>();
            var weaponCardView = weaponSkinCardPrefab.GetComponent<WeaponSkinCardView>();
            // Old prototype main menu retired — reachable only through the BottomNavBar tabs now.
            var inventory = BuildSimpleScreen<InventoryScreen>(screenHost, "InventoryScreen", ScreenType.Inventory, navigator, "INVENTORY");
            var shop = BuildSimpleScreen<ShopScreen>(screenHost, "ShopScreen", ScreenType.Shop, navigator, "SHOP");
            var settings = BuildSimpleScreen<SettingsScreen>(screenHost, "SettingsScreen", ScreenType.Settings, navigator, "SETTINGS");
            // SettingsScreen has its own built-in "SETTINGS" header inside the panel.
            // Hide the outer title created by BuildSimpleScreen to avoid the duplicate.
            var settingsOuterTitle = settings.Find("Title");
            if (settingsOuterTitle != null) settingsOuterTitle.gameObject.SetActive(false);
            var cases = BuildCaseOpeningScreen(screenHost, navigator, caseItemView, dropItemView);
            var weapons = BuildWeaponsScreen(screenHost, navigator, weaponCardView);
            var upgrade = BuildUpgradeScreen(screenHost, navigator, weaponCardView);
            // CaseBattleScreen retired — only the new Lobby flow is built.
            var lobby   = BuildLobbyListScreen(screenHost, navigator);
            var earn    = BuildEarnVpScreen(screenHost, navigator);
            var tools   = BuildToolsScreen(screenHost, navigator);
            var market  = BuildMarketScreen(screenHost, navigator);

            WireInventoryExtras(inventory, skinCardPrefab);
            WireShopExtras(shop, caseItemView);
            WireSettingsExtras(settings);

            BuildDailyPopup(safe);
            BuildToast(safe);
            BuildSkinWinPopup(safe);   // fullscreen win overlay — rendered on top of everything
            Debug.Log("[DEBUG][BUILDER] SkinWinPopup added to canvas prefab");

            // ── TopProfileBar — auto-created, parented to SafeArea, anchored top ─
            var topBarGo = new GameObject("TopProfileBar", typeof(RectTransform));
            topBarGo.transform.SetParent(safe, false);
            var topBarRt = (RectTransform)topBarGo.transform;
            topBarRt.anchorMin        = new Vector2(0f, 1f);
            topBarRt.anchorMax        = new Vector2(1f, 1f);
            topBarRt.pivot            = new Vector2(0.5f, 1f);
            topBarRt.anchoredPosition = new Vector2(0f, 0f);
            topBarRt.sizeDelta        = new Vector2(0f, TopProfileBar.Height);  // single source of truth
            var topBar   = topBarGo.AddComponent<TopProfileBar>();
            var topBarSo = new SerializedObject(topBar);
            var topNavProp = topBarSo.FindProperty("navigator");
            if (topNavProp != null) topNavProp.objectReferenceValue = navigator;
            topBarSo.ApplyModifiedPropertiesWithoutUndo();
            topBarGo.transform.SetAsLastSibling();
            Debug.Log("[TOP_BAR] Added to PF_UICanvas by builder");
            // ─────────────────────────────────────────────────────────────────────

            // ── BottomNavBar — auto-created, parented to SafeArea, last sibling ──
            var navBarGo = new GameObject("BottomNavBar", typeof(RectTransform));
            navBarGo.transform.SetParent(safe, false);
            var navBarRt = (RectTransform)navBarGo.transform;
            navBarRt.anchorMin        = new Vector2(0f, 0f);
            navBarRt.anchorMax        = new Vector2(1f, 0f);
            navBarRt.pivot            = new Vector2(0.5f, 0f);
            navBarRt.anchoredPosition = new Vector2(0f, 0f);   // flush with SafeArea bottom (matches BuildUI runtime)
            navBarRt.sizeDelta        = new Vector2(0f, BottomNavBar.Height);  // single source of truth
            var navBar   = navBarGo.AddComponent<BottomNavBar>();
            var navBarSo = new SerializedObject(navBar);
            var navProp  = navBarSo.FindProperty("navigator");
            if (navProp != null) navProp.objectReferenceValue = navigator;
            navBarSo.ApplyModifiedPropertiesWithoutUndo();
            navBarGo.transform.SetAsLastSibling();
            Debug.Log("[BOTTOM_NAV] Added to PF_UICanvas by builder");
            // ─────────────────────────────────────────────────────────────────────

            WireNavigator(navigator, cases.gameObject, inventory.gameObject, shop.gameObject, settings.gameObject, weapons.gameObject, upgrade.gameObject, lobby.gameObject, earn.gameObject, tools.gameObject, market.gameObject);

            return SavePrefab(root, UiCanvasPrefabPath);
        }

        static GameObject BuildMainMenuScreen(RectTransform parent, UINavigator navigator)
        {
            var screen = CreateScreenPanel(parent, "MainMenuScreen", ScreenType.MainMenu, out var group);
            var menu = screen.gameObject.AddComponent<MainMenuScreen>();

            // ── Full-screen cover background ──────────────────────────────────
            // BgContainer clips overflow so the oversized cover image stays inside.
            var cGo = new GameObject("BgContainer",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            var cRt = cGo.GetComponent<RectTransform>();
            cRt.SetParent(screen, false);
            cRt.anchorMin = Vector2.zero;
            cRt.anchorMax = Vector2.one;
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;
            cGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);   // transparent mask host
            cRt.SetSiblingIndex(0);   // ← first child → drawn behind all UI elements

            // BgImage — actual background sprite; AspectRatioFitter provides cover scaling.
            var bGo = new GameObject("BgImage",
                typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            var bRt = bGo.GetComponent<RectTransform>();
            bRt.SetParent(cRt, false);
            bRt.anchorMin        = new Vector2(0.5f, 0.5f);
            bRt.anchorMax        = new Vector2(0.5f, 0.5f);
            bRt.pivot            = new Vector2(0.5f, 0.5f);
            bRt.anchoredPosition = Vector2.zero;
            bRt.sizeDelta        = Vector2.zero;   // fitter controls size

            var bgImg = bGo.GetComponent<Image>();
            bgImg.color          = Color.white;
            bgImg.raycastTarget  = false;
            bgImg.preserveAspect = false;   // AspectRatioFitter handles aspect

            var fitter = bGo.GetComponent<AspectRatioFitter>();
            fitter.aspectMode  = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;   // placeholder; updated at runtime with actual ratio

            // Add the loader component and wire image + fitter references.
            var bgLoader = screen.gameObject.AddComponent<FullscreenBackground>();
            var bgSo = new SerializedObject(bgLoader);
            bgSo.FindProperty("backgroundImage").objectReferenceValue = bgImg;
            bgSo.FindProperty("aspectFitter").objectReferenceValue    = fitter;
            bgSo.ApplyModifiedPropertiesWithoutUndo();
            // ─────────────────────────────────────────────────────────────────

            // ── Bounded, auto-fitting menu cluster ────────────────────────────
            // MenuRoot fills the already-safe screen; MenuInner holds the fixed
            // button layout and is scaled down by ContentScaleFitter so the cluster
            // can never overflow behind the navbars on short/wide aspect ratios.
            var menuRoot = CreateRect("MenuRoot", screen, Vector2.zero);
            StretchFull(menuRoot);
            DestroyImage(menuRoot);

            var menuInner = CreateRect("MenuInner", menuRoot, new Vector2(560f, 1060f));
            menuInner.anchorMin        = new Vector2(0.5f, 0.5f);
            menuInner.anchorMax        = new Vector2(0.5f, 0.5f);
            menuInner.pivot            = new Vector2(0.5f, 0.5f);
            menuInner.anchoredPosition = Vector2.zero;
            menuInner.sizeDelta        = new Vector2(560f, 1060f);
            DestroyImage(menuInner);

            var menuFitter = menuRoot.gameObject.AddComponent<ContentScaleFitter>();
            SetField(menuFitter, "content", menuInner);

            var openBtn = CreateMenuButton(menuInner, "OpenCaseButton", "OPEN CASE", AccentRed, new Vector2(0, 120), new Vector2(520, 100));
            var invBtn = CreateMenuButton(menuInner, "InventoryButton", "INVENTORY", Panel, new Vector2(-140, -40), new Vector2(240, 88));
            var shopBtn = CreateMenuButton(menuInner, "ShopButton", "SHOP", Panel, new Vector2(140, -40), new Vector2(240, 88));
            var setBtn = CreateMenuButton(menuInner, "SettingsButton", "SETTINGS", Panel, new Vector2(-140, -150), new Vector2(240, 88));
            var weaponsBtn = CreateMenuButton(menuInner, "WeaponsButton", "SİLAHLAR", Panel, new Vector2(140, -150), new Vector2(240, 88));
            // Wide UPGRADE / GAMBLE call-to-action between weapons row and daily.
            var upgradeBtn = CreateMenuButton(menuInner, "UpgradeButton", "YÜKSELT", NeonPurple, new Vector2(0, -250), new Vector2(520, 76));
            // Redirects to the new Lobby flow (CaseBattleScreen retired).
            var battleBtn  = CreateMenuButton(menuInner, "CaseBattleButton", "KASA SAVAŞI", NeonGreen, new Vector2(0, -332), new Vector2(520, 76));
            var dailyBtn  = CreateMenuButton(menuInner, "DailyButton",  "DAILY REWARD", Panel,    new Vector2(0, -414), new Vector2(320, 64));
            var earnVpBtn = CreateMenuButton(menuInner, "EarnVpButton", "◆ VP KAZAN",  NeonGold, new Vector2(0, -494), new Vector2(520, 64));

            var player = CreateTmp("PlayerName", screen, "Agent", 22, TextAlignmentOptions.Left);
            player.rectTransform.anchorMin = new Vector2(0, 1);
            player.rectTransform.anchorMax = new Vector2(0, 1);
            player.rectTransform.pivot = new Vector2(0, 1);
            player.rectTransform.anchoredPosition = new Vector2(32, -32);
            player.rectTransform.sizeDelta = new Vector2(400, 36);

            var online = CreateTmp("Online", screen, "8,000 agents online", 16, TextAlignmentOptions.Left);
            online.rectTransform.anchorMin = new Vector2(0, 1);
            online.rectTransform.anchorMax = new Vector2(0, 1);
            online.rectTransform.pivot = new Vector2(0, 1);
            online.rectTransform.anchoredPosition = new Vector2(32, -68);
            online.rectTransform.sizeDelta = new Vector2(500, 28);
            online.color = new Color(0.75f, 0.8f, 0.85f);

            var stats = CreateTmp("Stats", screen, "Cases: 0 | Skins: 0", 14, TextAlignmentOptions.Center);
            StretchBottom(stats, 16, 28);

            var sliderGo = CreateRect("Progress", screen, new Vector2(600, 12));
            StretchBottom(sliderGo, 52, 12);
            var slider = sliderGo.gameObject.AddComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = 0.35f;
            var bg = CreateRect("Background", sliderGo, Vector2.zero);
            StretchFull(bg);
            GetOrAddImage(bg).color = new Color(0, 0, 0, 0.4f);
            var fillArea = CreateRect("Fill Area", sliderGo, Vector2.zero);
            StretchFull(fillArea);
            var fill = CreateRect("Fill", fillArea, Vector2.zero);
            StretchFull(fill);
            GetOrAddImage(fill).color = AccentRed;
            slider.fillRect = fill;
            slider.targetGraphic = fill.GetComponent<Image>();

            var so = new SerializedObject(menu);
            so.FindProperty("openCaseButton").objectReferenceValue = openBtn;
            so.FindProperty("inventoryButton").objectReferenceValue = invBtn;
            so.FindProperty("shopButton").objectReferenceValue = shopBtn;
            so.FindProperty("settingsButton").objectReferenceValue = setBtn;
            so.FindProperty("weaponsButton").objectReferenceValue = weaponsBtn;
            so.FindProperty("upgradeButton").objectReferenceValue = upgradeBtn;
            so.FindProperty("caseBattleButton").objectReferenceValue = battleBtn;
            so.FindProperty("earnVpButton").objectReferenceValue      = earnVpBtn;
            so.FindProperty("dailyRewardButton").objectReferenceValue = dailyBtn;
            so.FindProperty("playerNameLabel").objectReferenceValue = player;
            so.FindProperty("onlineCountLabel").objectReferenceValue = online;
            so.FindProperty("statsSummaryLabel").objectReferenceValue = stats;
            so.FindProperty("progressionBar").objectReferenceValue = slider;
            so.FindProperty("navigator").objectReferenceValue = navigator;
            so.FindProperty("canvasGroup").objectReferenceValue = group;
            so.FindProperty("screenType").enumValueIndex = (int)ScreenType.MainMenu;
            so.ApplyModifiedPropertiesWithoutUndo();

            return screen.gameObject;
        }

        static RectTransform BuildSimpleScreen<T>(RectTransform parent, string name, ScreenType type, UINavigator navigator, string title)
            where T : UIScreenBase
        {
            var screen = CreateScreenPanel(parent, name, type, out var group);
            screen.gameObject.AddComponent<T>();
            var header = CreateTmp("Title", screen, title, 36, TextAlignmentOptions.Center);
            ApplyTitleGlow(header, NeonCyan);
            StretchTop(header, 40, 80);
            var back = CreateMenuButton(screen, "Back", "BACK", Panel, new Vector2(0, -80), new Vector2(200, 72));
            var backRt = back.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0.5f, 0);
            backRt.anchorMax = new Vector2(0.5f, 0);
            backRt.pivot = new Vector2(0.5f, 0);
            backRt.anchoredPosition = new Vector2(0, 32);

            var comp = screen.GetComponent<T>();
            var so = new SerializedObject(comp);
            so.FindProperty("navigator").objectReferenceValue = navigator;
            so.FindProperty("backButton").objectReferenceValue = back;
            so.FindProperty("canvasGroup").objectReferenceValue = group;
            so.FindProperty("screenType").enumValueIndex = (int)type;
            so.ApplyModifiedPropertiesWithoutUndo();
            return screen;
        }

        static RectTransform BuildWeaponsScreen(RectTransform parent, UINavigator navigator, WeaponSkinCardView cardPrefab)
        {
            var screen = CreateScreenPanel(parent, "WeaponsScreen", ScreenType.Weapons, out var group);
            var comp = screen.gameObject.AddComponent<WeaponsScreen>();

            // Top bar with back button and title
            var topBar = CreateRect("TopBar", screen, new Vector2(0, 72));
            StretchTop(topBar, 0, 72);
            topBar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            var back = CreateMenuButton(topBar, "Back", "← GERİ", AccentRed, Vector2.zero, new Vector2(160, 52));
            var backRt = back.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0, 0.5f);
            backRt.anchorMax = new Vector2(0, 0.5f);
            backRt.pivot = new Vector2(0, 0.5f);
            backRt.anchoredPosition = new Vector2(12, 0);

            // Title centered but offset right so it doesn't overlap the back button
            var title = CreateTmp("Title", topBar, "SİLAHLAR", 26, TextAlignmentOptions.Center);
            ApplyTitleGlow(title, NeonCyan);
            title.rectTransform.anchorMin = new Vector2(0, 0);
            title.rectTransform.anchorMax = new Vector2(1, 1);
            title.rectTransform.offsetMin = new Vector2(180, 0);
            title.rectTransform.offsetMax = Vector2.zero;

            // Weapon tab strip
            var tabStrip = CreateRect("TabStrip", screen, new Vector2(0, 68));
            StretchTop(tabStrip, 72, 68);
            tabStrip.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.22f);

            // Baked weapon tab buttons removed — WeaponsScreen.BuildWeaponDropdown()
            // creates a runtime dropdown inside tabStrip at OnShown. No static children needed.

            // Skin grid scroll area. Bottom inset is the shared content padding; the
            // navbar space is already reserved by the Screens host. Top is pushed
            // below the chrome at runtime by WeaponsScreen.EnsureScrollLayout().
            var grid = CreateVerticalGridScrollContent(
                "WeaponsScroll",
                screen,
                new Vector2(16, 16f),
                new Vector2(-16, -16),
                new Vector2(200, 290),
                new Vector2(12, 12),
                new RectOffset(0, 0, 8, 8));

            var so = new SerializedObject(comp);
            so.FindProperty("navigator").objectReferenceValue = navigator;
            so.FindProperty("backButton").objectReferenceValue = back;
            so.FindProperty("tabRoot").objectReferenceValue = tabStrip;
            so.FindProperty("gridRoot").objectReferenceValue = grid;
            so.FindProperty("cardPrefab").objectReferenceValue = cardPrefab;
            so.FindProperty("canvasGroup").objectReferenceValue = group;
            so.FindProperty("screenType").enumValueIndex = (int)ScreenType.Weapons;
            so.ApplyModifiedPropertiesWithoutUndo();

            return screen;
        }

        // ─────────────────────────────────────────────────────────────────────
        // CASE BATTLE LOBBY SCREEN
        // Self-building: LobbyListScreen.BuildOnce() (called from OnShown) creates
        // its own header/list/footer plus the Create + Waiting sub-panels.
        // The builder only needs the panel, component, and serialized refs.
        // ─────────────────────────────────────────────────────────────────────
        static RectTransform BuildLobbyListScreen(RectTransform parent, UINavigator navigator)
        {
            var screen = CreateScreenPanel(parent, "LobbyListScreen", ScreenType.CaseBattleLobby, out var group);
            var comp   = screen.gameObject.AddComponent<LobbyListScreen>();

            var so = new SerializedObject(comp);
            SetObjRef(so, "navigator",   navigator);
            SetObjRef(so, "canvasGroup", group);
            var stProp = so.FindProperty("screenType");
            if (stProp != null) stProp.enumValueIndex = (int)ScreenType.CaseBattleLobby;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[LOBBY] LobbyListScreen built by builder");
            return screen;
        }

        // ─────────────────────────────────────────────────────────────────────
        // EARN VP SCREEN
        // The screen is fully self-building (BuildUiOnce called from OnShown).
        // The builder lays down the chrome: top bar, back button, wallet label.
        // ─────────────────────────────────────────────────────────────────────
        static RectTransform BuildEarnVpScreen(RectTransform parent, UINavigator navigator)
        {
            var screen = CreateScreenPanel(parent, "EarnVpScreen", ScreenType.EarnVp, out var group);
            var comp   = screen.gameObject.AddComponent<EarnVpScreen>();

            const float topBarH = 72f;

            // Top bar — back + wallet
            var topBar = CreateRect("TopBar", screen, new Vector2(0, topBarH));
            StretchTop(topBar, 0, topBarH);
            topBar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.40f);

            var back = CreateMenuButton(topBar, "Back", "← GERİ", AccentRed,
                Vector2.zero, new Vector2(160, 52));
            var backRt = back.GetComponent<RectTransform>();
            backRt.anchorMin        = new Vector2(0, 0.5f);
            backRt.anchorMax        = new Vector2(0, 0.5f);
            backRt.pivot            = new Vector2(0, 0.5f);
            backRt.anchoredPosition = new Vector2(12, 0);

            var title = CreateTmp("Title", topBar, "EARN VP", 26, TextAlignmentOptions.Center);
            ApplyTitleGlow(title, NeonGold);
            title.rectTransform.anchorMin = new Vector2(0, 0);
            title.rectTransform.anchorMax = new Vector2(1, 1);
            title.rectTransform.offsetMin = new Vector2(180, 0);
            title.rectTransform.offsetMax = new Vector2(-180, 0);

            var wallet = CreateTmp("Wallet", topBar, "0 VP", 20, TextAlignmentOptions.Right);
            wallet.rectTransform.anchorMin        = new Vector2(1, 0.5f);
            wallet.rectTransform.anchorMax        = new Vector2(1, 0.5f);
            wallet.rectTransform.pivot            = new Vector2(1, 0.5f);
            wallet.rectTransform.anchoredPosition = new Vector2(-16, 0);
            wallet.rectTransform.sizeDelta        = new Vector2(220, 36);
            wallet.color = NeonGold;

            // Wire refs
            var so = new SerializedObject(comp);
            so.FindProperty("navigator").objectReferenceValue   = navigator;
            so.FindProperty("backButton").objectReferenceValue  = back;
            so.FindProperty("walletLabel").objectReferenceValue = wallet;
            so.FindProperty("canvasGroup").objectReferenceValue = group;
            so.FindProperty("screenType").enumValueIndex        = (int)ScreenType.EarnVp;
            so.ApplyModifiedPropertiesWithoutUndo();

            return screen;
        }

        // ── Tools screen — chrome only; ToolsScreen.BuildOnce() builds the content ──
        static RectTransform BuildToolsScreen(RectTransform parent, UINavigator navigator)
        {
            var screen = CreateScreenPanel(parent, "ToolsScreen", ScreenType.Tools, out var group);
            var comp   = screen.gameObject.AddComponent<ToolsScreen>();

            var so = new SerializedObject(comp);
            so.FindProperty("navigator").objectReferenceValue   = navigator;
            so.FindProperty("canvasGroup").objectReferenceValue = group;
            so.FindProperty("screenType").enumValueIndex        = (int)ScreenType.Tools;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[TOOLS] ToolsScreen built by builder");
            return screen;
        }

        // ── Market screen — chrome only; MarketScreen.BuildOnce() builds the content ──
        static RectTransform BuildMarketScreen(RectTransform parent, UINavigator navigator)
        {
            var screen = CreateScreenPanel(parent, "MarketScreen", ScreenType.Market, out var group);
            var comp   = screen.gameObject.AddComponent<MarketScreen>();

            // Market is reached only through the BottomNavBar — no back button.
            var so = new SerializedObject(comp);
            so.FindProperty("canvasGroup").objectReferenceValue = group;
            so.FindProperty("screenType").enumValueIndex        = (int)ScreenType.Market;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[MARKET] MarketScreen built by builder");
            return screen;
        }

        static void WireMainMenu(GameObject main, UINavigator navigator, DailyRewardPopup daily)
        {
            var menu = main.GetComponent<MainMenuScreen>();
            var so = new SerializedObject(menu);
            so.FindProperty("dailyRewardPopup").objectReferenceValue = daily;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireNavigator(UINavigator navigator, params GameObject[] screens)
        {
            var so = new SerializedObject(navigator);
            var list = so.FindProperty("screens");
            list.arraySize = screens.Length;
            for (var i = 0; i < screens.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = screens[i].GetComponent<UIScreenBase>();
            so.FindProperty("defaultScreen").enumValueIndex = (int)ScreenType.Shop;
            Debug.Log("[STARTUP] Builder: defaultScreen wired to Shop (CASES tab)");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireInventoryExtras(RectTransform inventory, GameObject skinCardPrefab)
        {
            // BottomNavBar space is reserved by the shared Screens host (ScreenContentFitter);
            // these are content-edge offsets within the usable area.
            const float btnH       = 72f;   // back button height (set in BuildSimpleScreen)
            const float btnBottom  = 16f;
            const float scrollBot  = btnBottom + btnH + 8f; // above back button top

            var comp = inventory.GetComponent<InventoryScreen>();
            var grid = CreateVerticalGridScrollContent(
                "InventoryScroll",
                inventory,
                new Vector2(24, scrollBot),   // was (24,120) — now 180, above BottomNav+back-btn
                new Vector2(-24, -240),
                new Vector2(180, 260),
                new Vector2(16, 16),
                new RectOffset(0, 0, 0, 24));

            // ── Move the back button (created by BuildSimpleScreen) above BottomNav ──
            // Without this it sits at y=32-104 which overlaps the BottomNav zone (y=0-90).
            var backTf = inventory.Find("Back");
            if (backTf != null)
            {
                var bRt = (RectTransform)backTf;
                bRt.anchoredPosition = new Vector2(0f, btnBottom); // y=96 — fully above BottomNav
            }

            // Wallet label — top left showing current VP balance
            var walletLabel = CreateTmp("Wallet", inventory, "Wallet: 2,500 VP", 18, TextAlignmentOptions.Left);
            walletLabel.rectTransform.anchorMin = new Vector2(0, 1);
            walletLabel.rectTransform.anchorMax = new Vector2(0, 1);
            walletLabel.rectTransform.pivot = new Vector2(0, 1);
            walletLabel.rectTransform.anchoredPosition = new Vector2(24, -96);
            walletLabel.rectTransform.sizeDelta = new Vector2(280, 32);
            walletLabel.color = new Color(0.7f, 1f, 0.7f);

            var valueLabel = CreateTmp("Value", inventory, "Collection Value: 0 VP", 18, TextAlignmentOptions.Right);
            valueLabel.rectTransform.anchorMin = new Vector2(1, 1);
            valueLabel.rectTransform.anchorMax = new Vector2(1, 1);
            valueLabel.rectTransform.pivot = new Vector2(1, 1);
            valueLabel.rectTransform.anchoredPosition = new Vector2(-24, -96);
            valueLabel.rectTransform.sizeDelta = new Vector2(380, 32);

            var filter = CreateDropdown(inventory, "Filter", new Vector2(-200, -168),
                new[] { "All", "Select", "Deluxe", "Premium", "Exclusive", "Ultra", "Duplicates" });
            var sort = CreateDropdown(inventory, "Sort", new Vector2(200, -168),
                new[] { "Rarity Desc", "Rarity Asc", "Value Desc", "Value Asc", "Name", "Weapon", "Newest" });
            var detail = BuildSkinDetailPopup(inventory);

            var so = new SerializedObject(comp);
            so.FindProperty("gridRoot").objectReferenceValue = grid;
            so.FindProperty("filterDropdown").objectReferenceValue = filter;
            so.FindProperty("sortDropdown").objectReferenceValue = sort;
            so.FindProperty("inventoryValueLabel").objectReferenceValue = valueLabel;
            so.FindProperty("walletLabel").objectReferenceValue = walletLabel;
            so.FindProperty("detailPopup").objectReferenceValue = detail;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireShopExtras(RectTransform shop, CaseListItemView caseItem)
        {
            var comp = shop.GetComponent<ShopScreen>();
            var featuredLabel = CreateTmp("FeaturedLabel", shop, "Featured", 20, TextAlignmentOptions.Left);
            featuredLabel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            featuredLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            featuredLabel.rectTransform.anchoredPosition = new Vector2(0, 185);
            featuredLabel.rectTransform.sizeDelta = new Vector2(900, 28);
            var featured = CreateHorizontalScrollContent("Featured", shop, new Vector2(900, 140), new Vector2(0, 90), new Vector2(260, 100), 16);

            var dealsLabel = CreateTmp("DealsLabel", shop, "Daily Deals", 20, TextAlignmentOptions.Left);
            dealsLabel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            dealsLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            dealsLabel.rectTransform.anchoredPosition = new Vector2(0, -35);
            dealsLabel.rectTransform.sizeDelta = new Vector2(900, 28);
            var deals = CreateHorizontalScrollContent("Deals", shop, new Vector2(900, 140), new Vector2(0, -130), new Vector2(260, 100), 16);
            var timer = CreateTmp("Timer", shop, "Shop refreshes daily (UTC)", 16, TextAlignmentOptions.Center);
            StretchBottom(timer, 24, 28);

            var so = new SerializedObject(comp);
            so.FindProperty("featuredRoot").objectReferenceValue = featured;
            so.FindProperty("dealsRoot").objectReferenceValue = deals;
            so.FindProperty("rotationTimerLabel").objectReferenceValue = timer;
            so.FindProperty("caseItemPrefab").objectReferenceValue = caseItem;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireSettingsExtras(RectTransform settings)
        {
            // All Settings UI is built at runtime inside SettingsScreen.BuildProfileSectionOnce().
            // Serialized refs (sfxToggle, musicToggle, etc.) are assigned there at runtime.
        }
    }
}
#endif
