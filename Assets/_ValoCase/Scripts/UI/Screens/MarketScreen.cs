using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Data;
using ValoCase.Services.Ads;
using ValoCase.Services.Backend;
using ValoCase.Services.Iap;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Market — mobile-game shop dashboard with three sections:
    ///   1. REWARDED ADS         — two half-cards: Watch Ad → +2,500 VP / +1 Diamond
    ///   2. BUY VP WITH DIAMONDS — server-confirmed diamond→VP exchange packs
    ///   3. BUY DIAMONDS WITH USD — packages from IapProductCatalog; purchases go through
    ///      GameContext.RequestDiamondPurchase (dev-mock now, real Play Billing later)
    /// Reached only through the BottomNavBar — there is no back button. Diamonds are the
    /// premium currency; balances are always applied from backend/purchase-confirmed
    /// responses, never set directly by UI.
    /// </summary>
    public sealed class MarketScreen : UIScreenBase
    {
        static readonly Color Accent      = new Color(1f, 0.275f, 0.333f, 1f);      // #FF4655
        static readonly Color TextMain    = new Color(0.961f, 0.961f, 0.961f, 1f);  // #F5F5F5
        static readonly Color TextSub     = new Color(0.541f, 0.569f, 0.651f, 1f);  // #8A91A6
        static readonly Color BgCard      = new Color(0.051f, 0.067f, 0.090f, 1f);  // #0D1117
        static readonly Color BgCardAlt   = new Color(0.075f, 0.098f, 0.145f, 1f);  // buy strip
        static readonly Color CardBorder  = new Color(0.165f, 0.204f, 0.278f, 1f);  // #2A3447
        static readonly Color DiamondCyan = new Color(0.302f, 0.851f, 1f, 1f);      // premium currency
        static readonly Color FreeGold    = new Color(1f, 0.823f, 0.290f, 1f);
        static readonly Color UsdGreen    = new Color(0.290f, 0.855f, 0.510f, 1f);  // real-money price
        static readonly Color DarkText    = new Color(0.043f, 0.055f, 0.082f, 1f);  // text on filled CTAs

        const int RewardVpDisplay = 2500;
        const string VpHex        = "#FF4655";
        const string DiamondHex   = "#4DD9FF";

        struct VpOffer { public string OfferId; public string Vp; public int Diamonds; }
        static readonly VpOffer FeaturedVp = new VpOffer { OfferId = "vp_100000", Vp = "100,000", Diamonds = 1250 };
        static readonly VpOffer[] VpPacks =
        {
            new VpOffer { OfferId = "vp_50000", Vp = "50,000", Diamonds = 700 },
            new VpOffer { OfferId = "vp_25000", Vp = "25,000", Diamonds = 375 },
            new VpOffer { OfferId = "vp_1000",  Vp = "1,000",  Diamonds = 20  },
        };

        bool _built;
        bool _diamondPurchaseInFlight;
        readonly List<Button> _diamondBuyButtons = new List<Button>();
        GameObject _devModeCaption;

        // Rewarded ad section
        GameObject      _adSectionLabel;
        GameObject      _adRow;
        CanvasGroup     _freeCardGroup;
        Button          _adButton;
        TextMeshProUGUI _adButtonLabel;
        bool            _adInFlight;
        CanvasGroup     _diamondCardGroup;
        Button          _diamondAdButton;
        TextMeshProUGUI _diamondAdLabel;
        bool            _diamondAdInFlight;

        // FREE VP cooldown (existing MARKET_VP_2500 behavior)
        bool      _cooldownActive;
        float     _cooldownEndRealtime;
        Coroutine _cooldownTicker;

        // VP purchase
        readonly List<Button> _vpBuyButtons = new List<Button>();
        bool _purchaseInFlight;

        protected override void OnShown()
        {
            BuildOnce();

            bool backend = GameContext.Instance?.BackendEnabled ?? false;
            if (_adSectionLabel != null) _adSectionLabel.SetActive(backend);
            if (_adRow          != null) _adRow.SetActive(backend);
            if (!backend) return;

            ResetAdButton();
            ResetDiamondAdButton();
            RefreshMarketStatus();
            RefreshCatalog();
        }

        protected override void OnHidden()
        {
            StopCooldownTicker();
            _cooldownActive = false;
        }

        void RefreshCatalog()
        {
            var ctx = GameContext.Instance;
            if (ctx == null || !ctx.BackendEnabled) return;
            ctx.RefreshMarketCatalog(_ => { }, _ => { });
        }

        // ── Build ──────────────────────────────────────────────────────────────

        void BuildOnce()
        {
            if (_built) return;
            _built = true;

            var rt = (RectTransform)transform;
            FullscreenBackground.AttachShared(gameObject);

            BuildHeader(rt);
            var content = BuildScroll(rt);

            _adSectionLabel = BuildSectionLabel(content, "REWARDED ADS");
            BuildRewardedRow(content);

            BuildSectionLabel(content, "BUY VP WITH DIAMONDS");
            BuildFeaturedVpCard(content);
            BuildVpPackRow(content);

            BuildSectionLabel(content, "BUY DIAMONDS WITH USD");
            _devModeCaption = BuildDevModeCaption(content);
            BuildDiamondPackGrid(content);

            BuildDisclaimer(content);

            bool iapAvailable = GameContext.Instance?.IapPurchases?.IsAvailable ?? false;
            if (_devModeCaption != null) _devModeCaption.SetActive(iapAvailable);
        }

        static GameObject BuildDevModeCaption(RectTransform content)
        {
            var go = new GameObject("DevModeCaption", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(content, false);
            go.GetComponent<LayoutElement>().preferredHeight = 18f;
            var tmp = UIBuild.MakeTmp(go.transform, "Lbl",
                "TEST MODE — purchases are free dev grants, not real money", 10.5f, FontStyles.Italic, UsdGreen);
            tmp.alignment          = TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = true;
            var lRt = tmp.rectTransform;
            lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
            lRt.offsetMin = new Vector2(4f, 0f); lRt.offsetMax = Vector2.zero;
            return go;
        }

        void BuildHeader(RectTransform rt)
        {
            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(rt, false);
            UIBuild.TopStrip((RectTransform)header.transform, 60f);

            var title = UIBuild.MakeTmp(header.transform, "Title", "MARKET", 30f, FontStyles.Bold, Accent);
            title.characterSpacing = 5f;
            title.alignment        = TextAlignmentOptions.MidlineLeft;
            var tRt = title.rectTransform;
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = new Vector2(20f, 0f); tRt.offsetMax = new Vector2(-20f, 0f);
        }

        static RectTransform BuildScroll(RectTransform rt)
        {
            var go = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            go.transform.SetParent(rt, false);
            var sRt = (RectTransform)go.transform;
            sRt.anchorMin = Vector2.zero; sRt.anchorMax = Vector2.one;
            sRt.offsetMin = Vector2.zero; sRt.offsetMax = new Vector2(0f, -60f);

            var bg = go.GetComponent<Image>();
            bg.color         = new Color(0f, 0f, 0f, 0f);
            bg.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(go.transform, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot     = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;

            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding                = new RectOffset(16, 16, 8, 28);
            vlg.spacing                = 12f;
            vlg.childAlignment         = TextAnchor.UpperCenter;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = go.GetComponent<ScrollRect>();
            scroll.content            = content;
            scroll.viewport           = sRt;
            scroll.horizontal         = false;
            scroll.vertical           = true;
            scroll.scrollSensitivity  = 30f;
            return content;
        }

        static GameObject BuildSectionLabel(RectTransform content, string text)
        {
            var go = new GameObject("Section_" + text, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(content, false);
            go.GetComponent<LayoutElement>().preferredHeight = 26f;
            var tmp = UIBuild.MakeTmp(go.transform, "Label", text, 14f, FontStyles.Bold, TextSub);
            tmp.characterSpacing = 3f;
            tmp.alignment        = TextAlignmentOptions.MidlineLeft;
            var lRt = tmp.rectTransform;
            lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
            lRt.offsetMin = new Vector2(4f, 0f); lRt.offsetMax = Vector2.zero;
            return go;
        }

        // ── Section 1: two rewarded ad cards, side by side ──────────────────────

        void BuildRewardedRow(RectTransform content)
        {
            var row = new GameObject("RewardedRow", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(content, false);
            _adRow = row;
            var le = row.GetComponent<LayoutElement>();
            le.preferredHeight = 172f;
            le.flexibleWidth   = 1f;

            var left = MakeHalfCell(row.transform, "FreeCell", 0);
            BuildAdCard(left, FreeGold, $"+{RewardVpDisplay:N0}", "VP", VpHex, false,
                out _adButton, out _adButtonLabel, out _freeCardGroup);
            _adButton.onClick.AddListener(OnFreeAdClicked);

            var right = MakeHalfCell(row.transform, "DiamondCell", 1);
            BuildAdCard(right, DiamondCyan, "+1", "DIAMOND", DiamondHex, true,
                out _diamondAdButton, out _diamondAdLabel, out _diamondCardGroup);
            _diamondAdButton.onClick.AddListener(OnDiamondAdClicked);
        }

        static RectTransform MakeHalfCell(Transform row, string name, int index)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(index * 0.5f, 0f);
            rt.anchorMax = new Vector2((index + 1) * 0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(index == 0 ? 0f : 6f, 0f);
            rt.offsetMax = new Vector2(index == 0 ? -6f : 0f, 0f);
            return rt;
        }

        void BuildAdCard(RectTransform card, Color accent, string amount, string unit, string unitHex,
            bool diamondIcon, out Button btn, out TextMeshProUGUI label, out CanvasGroup group)
        {
            var img = card.gameObject.AddComponent<Image>();
            img.color         = BgCard;
            img.raycastTarget = true;
            var ol = card.gameObject.AddComponent<Outline>();
            ol.effectColor    = new Color(accent.r, accent.g, accent.b, 0.85f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);
            group = card.gameObject.AddComponent<CanvasGroup>();
            btn = card.gameObject.AddComponent<Button>();
            btn.transition    = Selectable.Transition.None;
            btn.targetGraphic = img;
            UIBuild.WireButtonClick(btn);

            var strip = UIBuild.MakeImage("TopStrip", card, accent);
            var stRt = strip.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(1f, 1f);
            stRt.pivot     = new Vector2(0.5f, 1f);
            stRt.sizeDelta = new Vector2(0f, 3f);
            stRt.anchoredPosition = Vector2.zero;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(card, false);
            var iRt = (RectTransform)iconGo.transform;
            iRt.anchorMin = iRt.anchorMax = new Vector2(0.5f, 1f);
            iRt.pivot            = new Vector2(0.5f, 1f);
            iRt.anchoredPosition = new Vector2(0f, -18f);
            iRt.sizeDelta        = new Vector2(46f, 46f);
            var icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.color         = new Color(0f, 0f, 0f, 0f);   // artwork stands on its own — no tinted backing box

            if (diamondIcon) PlaceCentered(MakeDiamond(iconGo.transform, 42f));
            else             PlaceCentered(UIBuild.MakeVpIcon(iconGo.transform, 72.8f));

            var reward = UIBuild.MakeTmp(card, "Reward",
                $"{amount} <color={unitHex}>{unit}</color>", 30f, FontStyles.Bold, TextMain);
            reward.enableAutoSizing = true;
            reward.fontSizeMin      = 16f;
            reward.fontSizeMax      = 30f;
            reward.alignment        = TextAlignmentOptions.Center;
            var rRt = reward.rectTransform;
            rRt.anchorMin = new Vector2(0.05f, 0.42f);
            rRt.anchorMax = new Vector2(0.95f, 0.60f);
            rRt.offsetMin = Vector2.zero; rRt.offsetMax = Vector2.zero;

            var sub = UIBuild.MakeTmp(card, "Sub", "REWARDED VIDEO", 10.5f, FontStyles.Bold, TextSub);
            sub.characterSpacing = 2f;
            sub.alignment        = TextAlignmentOptions.Center;
            var suRt = sub.rectTransform;
            suRt.anchorMin = new Vector2(0.04f, 0.31f);
            suRt.anchorMax = new Vector2(0.96f, 0.41f);
            suRt.offsetMin = Vector2.zero; suRt.offsetMax = Vector2.zero;

            var pill = new GameObject("Cta", typeof(RectTransform), typeof(Image));
            pill.transform.SetParent(card, false);
            var pRt = (RectTransform)pill.transform;
            pRt.anchorMin = new Vector2(0f, 0f);
            pRt.anchorMax = new Vector2(1f, 0f);
            pRt.pivot     = new Vector2(0.5f, 0f);
            pRt.offsetMin = new Vector2(12f, 12f);
            pRt.offsetMax = new Vector2(-12f, 52f);
            var pImg = pill.GetComponent<Image>();
            pImg.color         = accent;
            pImg.raycastTarget = false;

            label = UIBuild.MakeTmp(pill.transform, "Lbl", "WATCH AD", 15f, FontStyles.Bold, DarkText);
            label.alignment = TextAlignmentOptions.Center;
            UIBuild.Stretch(label.rectTransform);
        }

        // ── Section 2: VP with diamonds ─────────────────────────────────────────

        void BuildFeaturedVpCard(RectTransform content)
        {
            var card = MakeCard(content, "FeaturedVp", 150f, Accent, 0.85f);

            var strip = UIBuild.MakeImage("EdgeStrip", card, Accent);
            var stRt = strip.rectTransform;
            stRt.anchorMin = new Vector2(0f, 0f);
            stRt.anchorMax = new Vector2(0f, 1f);
            stRt.pivot     = new Vector2(0f, 0.5f);
            stRt.sizeDelta = new Vector2(4f, 0f);
            stRt.anchoredPosition = Vector2.zero;

            var badge = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(card, false);
            var bRt = (RectTransform)badge.transform;
            bRt.anchorMin = bRt.anchorMax = new Vector2(0f, 1f);
            bRt.pivot            = new Vector2(0f, 1f);
            bRt.anchoredPosition = new Vector2(16f, -14f);
            bRt.sizeDelta        = new Vector2(110f, 26f);
            badge.GetComponent<Image>().color         = Accent;
            badge.GetComponent<Image>().raycastTarget = false;
            var bLbl = UIBuild.MakeTmp(badge.transform, "Lbl", "BEST VALUE", 13f, FontStyles.Bold, DarkText);
            bLbl.characterSpacing = 2f;
            bLbl.alignment        = TextAlignmentOptions.Center;
            UIBuild.Stretch(bLbl.rectTransform);

            var amount = UIBuild.MakeTmp(card, "Amount",
                FeaturedVp.Vp + $" <color={VpHex}>VP</color>", 36f, FontStyles.Bold, TextMain);
            amount.enableAutoSizing = true;
            amount.fontSizeMin      = 22f;
            amount.fontSizeMax      = 38f;
            amount.alignment        = TextAlignmentOptions.MidlineLeft;
            var aRt = amount.rectTransform;
            aRt.anchorMin = new Vector2(0f, 0.36f);
            aRt.anchorMax = new Vector2(0.58f, 0.74f);
            aRt.offsetMin = new Vector2(24f, 0f);
            aRt.offsetMax = Vector2.zero;

            var sub = UIBuild.MakeTmp(card, "Sub", "MEGA VP PACK", 13f, FontStyles.Bold, TextSub);
            sub.characterSpacing = 2f;
            sub.alignment        = TextAlignmentOptions.MidlineLeft;
            var suRt = sub.rectTransform;
            suRt.anchorMin = new Vector2(0f, 0.16f);
            suRt.anchorMax = new Vector2(0.58f, 0.34f);
            suRt.offsetMin = new Vector2(24f, 0f);
            suRt.offsetMax = Vector2.zero;

            var offer = FeaturedVp;
            var buy = MakeBuyButton(card, new Vector2(154f, 52f), Accent, DarkText, offer.Diamonds, 18f,
                () => OnBuyVpClicked(offer));
            var buyRt = (RectTransform)buy.transform;
            buyRt.anchorMin = buyRt.anchorMax = new Vector2(1f, 0.5f);
            buyRt.pivot            = new Vector2(1f, 0.5f);
            buyRt.anchoredPosition = new Vector2(-18f, -4f);
        }

        void BuildVpPackRow(RectTransform content)
        {
            var row = new GameObject("VpPackRow", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(content, false);
            var le = row.GetComponent<LayoutElement>();
            le.preferredHeight = 178f;
            le.flexibleWidth   = 1f;

            for (int i = 0; i < VpPacks.Length; i++)
            {
                var offer = VpPacks[i];
                var card = new GameObject("Pack_" + offer.OfferId, typeof(RectTransform), typeof(Image));
                card.transform.SetParent(row.transform, false);
                var cRt = (RectTransform)card.transform;
                cRt.anchorMin = new Vector2(i / 3f, 0f);
                cRt.anchorMax = new Vector2((i + 1) / 3f, 1f);
                cRt.offsetMin = new Vector2(i == 0 ? 0f : 5f, 0f);
                cRt.offsetMax = new Vector2(i == 2 ? 0f : -5f, 0f);
                card.GetComponent<Image>().color = BgCard;
                var ol = card.AddComponent<Outline>();
                ol.effectColor    = new Color(CardBorder.r, CardBorder.g, CardBorder.b, 0.9f);
                ol.effectDistance = new Vector2(1f, -1f);

                var gem = MakeDiamond(card.transform, 33.6f);
                gem.anchorMin = gem.anchorMax = new Vector2(0.5f, 1f);
                gem.pivot            = new Vector2(0.5f, 1f);
                gem.anchoredPosition = new Vector2(0f, -22f);

                var amount = UIBuild.MakeTmp(card.transform, "Amount", offer.Vp, 22f, FontStyles.Bold, TextMain);
                amount.enableAutoSizing = true;
                amount.fontSizeMin      = 14f;
                amount.fontSizeMax      = 22f;
                amount.alignment        = TextAlignmentOptions.Center;
                var aRt = amount.rectTransform;
                aRt.anchorMin = new Vector2(0.05f, 0.50f);
                aRt.anchorMax = new Vector2(0.95f, 0.66f);
                aRt.offsetMin = Vector2.zero; aRt.offsetMax = Vector2.zero;

                var vp = UIBuild.MakeTmp(card.transform, "Vp", "VP", 14f, FontStyles.Bold, Accent);
                vp.characterSpacing = 2f;
                vp.alignment        = TextAlignmentOptions.Center;
                var vRt = vp.rectTransform;
                vRt.anchorMin = new Vector2(0.05f, 0.36f);
                vRt.anchorMax = new Vector2(0.95f, 0.48f);
                vRt.offsetMin = Vector2.zero; vRt.offsetMax = Vector2.zero;

                var buy = MakeBuyButton(card.transform, Vector2.zero, BgCardAlt, TextMain, offer.Diamonds, 16f,
                    () => OnBuyVpClicked(offer));
                var buyRt = (RectTransform)buy.transform;
                buyRt.anchorMin = new Vector2(0f, 0f);
                buyRt.anchorMax = new Vector2(1f, 0f);
                buyRt.pivot     = new Vector2(0.5f, 0f);
                buyRt.sizeDelta = new Vector2(0f, 44f);
                buyRt.anchoredPosition = Vector2.zero;
            }
        }

        // ── Section 3: diamond USD packs (IapProductCatalog; dev-mock purchase) ──

        void BuildDiamondPackGrid(RectTransform content)
        {
            var packages = IapProductCatalog.Packages;
            for (int r = 0; r < 2; r++)
            {
                var row = new GameObject("DiamondRow" + r, typeof(RectTransform), typeof(LayoutElement));
                row.transform.SetParent(content, false);
                var le = row.GetComponent<LayoutElement>();
                le.preferredHeight = 200f;
                le.flexibleWidth   = 1f;

                for (int c = 0; c < 2; c++)
                {
                    int idx = r * 2 + c;
                    if (idx >= packages.Count) break;
                    BuildDiamondPackCell(row.transform, packages[idx], c);
                }
            }
        }

        void BuildDiamondPackCell(Transform row, IapPackageEntry pack, int col)
        {
            var card = new GameObject("Pack_" + pack.packageId, typeof(RectTransform), typeof(Image));
            card.transform.SetParent(row, false);
            var cRt = (RectTransform)card.transform;
            cRt.anchorMin = new Vector2(col / 2f, 0f);
            cRt.anchorMax = new Vector2((col + 1) / 2f, 1f);
            cRt.offsetMin = new Vector2(col == 0 ? 0f : 5f, 0f);
            cRt.offsetMax = new Vector2(col == 1 ? 0f : -5f, 0f);
            card.GetComponent<Image>().color = BgCard;
            var ol = card.AddComponent<Outline>();
            ol.effectColor    = new Color(DiamondCyan.r, DiamondCyan.g, DiamondCyan.b, 0.55f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            var strip = UIBuild.MakeImage("TopStrip", card.transform, DiamondCyan);
            var stRt = strip.rectTransform;
            stRt.anchorMin = new Vector2(0f, 1f);
            stRt.anchorMax = new Vector2(1f, 1f);
            stRt.pivot     = new Vector2(0.5f, 1f);
            stRt.sizeDelta = new Vector2(0f, 3f);
            stRt.anchoredPosition = Vector2.zero;

            var tier = new GameObject("Tier", typeof(RectTransform), typeof(Image));
            tier.transform.SetParent(card.transform, false);
            var tRt = (RectTransform)tier.transform;
            tRt.anchorMin = tRt.anchorMax = new Vector2(0f, 1f);
            tRt.pivot            = new Vector2(0f, 1f);
            tRt.anchoredPosition = new Vector2(12f, -12f);
            tRt.sizeDelta        = new Vector2(104f, 20f);
            tier.GetComponent<Image>().color         = new Color(DiamondCyan.r, DiamondCyan.g, DiamondCyan.b, 0.16f);
            tier.GetComponent<Image>().raycastTarget = false;
            var tierLbl = UIBuild.MakeTmp(tier.transform, "Lbl", pack.tier, 10f, FontStyles.Bold, DiamondCyan);
            tierLbl.characterSpacing = 1f;
            tierLbl.alignment        = TextAlignmentOptions.Center;
            UIBuild.Stretch(tierLbl.rectTransform);

            // The diamond artwork sits on the bare card — no glow disc behind it.
            // Icon/amount/caption/CTA use fixed pixel bands (not fractional anchors) so they
            // never overlap regardless of card height — see BuildDiamondPackGrid row height.
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(card.transform, false);
            var iRt = (RectTransform)iconGo.transform;
            iRt.anchorMin = iRt.anchorMax = new Vector2(0.5f, 1f);
            iRt.pivot            = new Vector2(0.5f, 1f);
            iRt.anchoredPosition = new Vector2(0f, -40f);
            iRt.sizeDelta        = new Vector2(36f, 36f);
            PlaceCentered(MakeDiamond(iconGo.transform, 47.6f));

            var amount = UIBuild.MakeTmp(card.transform, "Amount", $"{pack.amount:N0}", 26f, FontStyles.Bold, TextMain);
            amount.enableAutoSizing = true;
            amount.fontSizeMin      = 17f;
            amount.fontSizeMax      = 26f;
            amount.alignment        = TextAlignmentOptions.Center;
            var aRt = amount.rectTransform;
            aRt.anchorMin        = new Vector2(0f, 1f);
            aRt.anchorMax        = new Vector2(1f, 1f);
            aRt.pivot            = new Vector2(0.5f, 1f);
            aRt.anchoredPosition = new Vector2(0f, -92f);
            aRt.sizeDelta        = new Vector2(-16f, 28f);

            var caption = UIBuild.MakeTmp(card.transform, "Caption", "DIAMONDS", 10f, FontStyles.Bold, DiamondCyan);
            caption.characterSpacing = 3f;
            caption.alignment        = TextAlignmentOptions.Center;
            var capRt = caption.rectTransform;
            capRt.anchorMin        = new Vector2(0f, 1f);
            capRt.anchorMax        = new Vector2(1f, 1f);
            capRt.pivot            = new Vector2(0.5f, 1f);
            capRt.anchoredPosition = new Vector2(0f, -122f);
            capRt.sizeDelta        = new Vector2(-16f, 14f);

            var ctaGo = new GameObject("Cta", typeof(RectTransform), typeof(Image), typeof(Button));
            ctaGo.transform.SetParent(card.transform, false);
            var ctaRt = (RectTransform)ctaGo.transform;
            ctaRt.anchorMin = new Vector2(0f, 0f);
            ctaRt.anchorMax = new Vector2(1f, 0f);
            ctaRt.pivot     = new Vector2(0.5f, 0f);
            ctaRt.offsetMin = new Vector2(12f, 12f);
            ctaRt.offsetMax = new Vector2(-12f, 56f);
            var ctaImg = ctaGo.GetComponent<Image>();
            var ctaBtn = ctaGo.GetComponent<Button>();
            ctaBtn.transition    = Selectable.Transition.None;
            ctaBtn.targetGraphic = ctaImg;
            var ctaLbl = UIBuild.MakeTmp(ctaGo.transform, "Lbl", pack.priceDisplay, 17f, FontStyles.Bold, DarkText);
            ctaLbl.alignment = TextAlignmentOptions.Center;
            UIBuild.Stretch(ctaLbl.rectTransform);

            ctaBtn.onClick.AddListener(() => OnDiamondPackClicked(pack, ctaLbl));
            UIBuild.WireButtonClick(ctaBtn);
            _diamondBuyButtons.Add(ctaBtn);

            StyleDiamondCta(ctaImg, ctaLbl, pack.priceDisplay);
        }

        // Recolors/relabels a USD-pack CTA to reflect whether purchases can actually be
        // attempted right now (dev-mock) or not (production, billing not configured yet).
        // This availability never changes at runtime, so it's only ever applied once at build.
        static void StyleDiamondCta(Image ctaImg, TextMeshProUGUI ctaLbl, string priceDisplay)
        {
            bool available = GameContext.Instance?.IapPurchases?.IsAvailable ?? false;
            ctaImg.color = available ? UsdGreen : BgCardAlt;
            ctaLbl.color = available ? DarkText : TextSub;
            ctaLbl.text  = available ? priceDisplay : "UNAVAILABLE";
        }

        void BuildDisclaimer(RectTransform content)
        {
            var go = new GameObject("Disclaimer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(content, false);
            go.GetComponent<LayoutElement>().preferredHeight = 34f;
            var tmp = UIBuild.MakeTmp(go.transform, "Lbl",
                "Purchases are virtual and have no real-world value.", 11f, FontStyles.Italic, TextSub);
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            UIBuild.Stretch(tmp.rectTransform);
        }

        // ── Shared builders ──────────────────────────────────────────────────────

        static RectTransform MakeCard(RectTransform content, string name, float height, Color border, float borderAlpha)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(content, false);
            var img = go.GetComponent<Image>();
            img.color         = BgCard;
            img.raycastTarget = false;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth   = 1f;
            var ol = go.AddComponent<Outline>();
            ol.effectColor    = new Color(border.r, border.g, border.b, borderAlpha);
            ol.effectDistance = new Vector2(1.5f, -1.5f);
            return (RectTransform)go.transform;
        }

        static RectTransform MakeDiamond(Transform parent, float size) => UIBuild.MakeDiamondIcon(parent, size);

        static void PlaceCentered(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        Button MakeBuyButton(Transform parent, Vector2 size, Color fill, Color contentColor, int diamonds,
            float fontSize, System.Action onClick)
        {
            var go = new GameObject("BuyButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            if (size != Vector2.zero) rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.color = fill;
            var btn = go.GetComponent<Button>();
            btn.transition    = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());
            UIBuild.WireButtonClick(btn);
            _vpBuyButtons.Add(btn);

            var rowGo = new GameObject("CostRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(go.transform, false);
            UIBuild.Stretch((RectTransform)rowGo.transform);
            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.childAlignment         = TextAnchor.MiddleCenter;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;

            var gem = MakeDiamond(rowGo.transform, 21f);
            var gLe = gem.gameObject.AddComponent<LayoutElement>();
            gLe.preferredWidth  = 21f;
            gLe.preferredHeight = 21f;

            var lbl = UIBuild.MakeTmp(rowGo.transform, "Cost", $"{diamonds:N0}", fontSize, FontStyles.Bold, contentColor);
            lbl.alignment = TextAlignmentOptions.Center;

            return btn;
        }

        // ── VP purchase (server-confirmed) ───────────────────────────────────────

        void OnBuyVpClicked(VpOffer offer)
        {
            if (_purchaseInFlight) return;
            var ctx = GameContext.Instance;
            if (ctx == null || !ctx.BackendEnabled) { GameEvents.RaiseToast("Currently unavailable."); return; }

            _purchaseInFlight = true;
            SetBuyButtonsInteractable(false);
            ctx.PurchaseVpWithDiamonds(offer.OfferId,
                res =>
                {
                    if (this == null) return;
                    _purchaseInFlight = false;
                    SetBuyButtonsInteractable(true);
                    GameEvents.RaiseToast($"+{offer.Vp} VP");
                },
                msg =>
                {
                    if (this == null) return;
                    _purchaseInFlight = false;
                    SetBuyButtonsInteractable(true);
                    GameEvents.RaiseToast(string.IsNullOrEmpty(msg) ? "Purchase failed. Please try again." : msg);
                });
        }

        void SetBuyButtonsInteractable(bool value)
        {
            for (int i = 0; i < _vpBuyButtons.Count; i++)
                if (_vpBuyButtons[i] != null) _vpBuyButtons[i].interactable = value;
        }

        // Dev-mock purchase now; production build's IapPurchases is always Unavailable and
        // never grants diamonds. See GameContext.RequestDiamondPurchase / IapPurchaseService.cs.
        void OnDiamondPackClicked(IapPackageEntry pack, TextMeshProUGUI ctaLbl)
        {
            var ctx = GameContext.Instance;
            bool available = ctx?.IapPurchases?.IsAvailable ?? false;
            if (!available)
            {
                GameEvents.RaiseToast("Purchase system not ready — pending Google Play approval.");
                return;
            }
            if (_diamondPurchaseInFlight) return;

            _diamondPurchaseInFlight = true;
            SetDiamondButtonsInteractable(false);
            if (ctaLbl != null) ctaLbl.text = "...";

            ctx.RequestDiamondPurchase(pack, (result, message) =>
            {
                if (this == null) return;
                _diamondPurchaseInFlight = false;
                SetDiamondButtonsInteractable(true);
                if (ctaLbl != null) ctaLbl.text = pack.priceDisplay;

                if (result == IapPurchaseResult.Granted)
                {
                    GameEvents.RaiseToast(message ?? $"+{pack.amount:N0} Diamonds");
                }
                else if (result != IapPurchaseResult.Cancelled)
                {
                    GameEvents.RaiseToast(string.IsNullOrEmpty(message) ? "Purchase failed. Please try again." : message);
                }
            });
        }

        void SetDiamondButtonsInteractable(bool value)
        {
            for (int i = 0; i < _diamondBuyButtons.Count; i++)
                if (_diamondBuyButtons[i] != null) _diamondBuyButtons[i].interactable = value;
        }

        // ── FREE VP rewarded ad (existing MARKET_VP_2500 behavior) ───────────────

        void ResetAdButton()
        {
            _adInFlight = false;
            _cooldownActive = false;
            StopCooldownTicker();
            if (_adButton != null) _adButton.interactable = true;
            if (_freeCardGroup != null) _freeCardGroup.alpha = 1f;
            if (_adButtonLabel != null) _adButtonLabel.text = "WATCH AD";
        }

        void OnFreeAdClicked()
        {
            if (_adInFlight || _cooldownActive) return;
            var ctx = GameContext.Instance;
            if (ctx == null || !ctx.BackendEnabled) { GameEvents.RaiseToast("Şu anda kullanılamıyor."); return; }

            _adInFlight = true;
            if (_adButton != null) _adButton.interactable = false;
            if (_freeCardGroup != null) _freeCardGroup.alpha = 0.7f;
            if (_adButtonLabel != null) _adButtonLabel.text = "WATCHING...";

            ctx.WatchMarketVp2500Ad(
                onClaimed: res =>
                {
                    if (this == null) return;
                    if (res != null && res.marketCooldownActive && res.marketCooldownRemainingSeconds > 0L)
                    {
                        if (res.grantedVp > 0L) GameEvents.RaiseToast($"+{RewardVpDisplay:N0} VP");
                        StartCooldown(res.marketCooldownRemainingSeconds);
                        return;
                    }
                    GameEvents.RaiseToast($"+{RewardVpDisplay:N0} VP");
                    ResetAdButton();
                },
                onFailed: msg =>
                {
                    if (this == null) return;
                    GameEvents.RaiseToast(MapAdFailure(msg));
                    ResetAdButton();
                },
                onCancelled: () =>
                {
                    if (this == null) return;
                    ResetAdButton();
                });
        }

        void RefreshMarketStatus()
        {
            var ctx = GameContext.Instance;
            if (ctx == null || !ctx.BackendEnabled) return;
            ctx.RefreshMarketAdStatus(
                res =>
                {
                    if (this == null || !isActiveAndEnabled) return;
                    var s = res?.Find(AdRewardTypes.MarketVp2500);
                    if (s != null && s.cooldownRemainingSeconds > 0L) StartCooldown(s.cooldownRemainingSeconds);
                    else ResetAdButton();
                },
                _ => { if (this != null) ResetAdButton(); });
        }

        void StartCooldown(long remainingSeconds)
        {
            _adInFlight = false;
            _cooldownActive = true;
            _cooldownEndRealtime = Time.unscaledTime + remainingSeconds;
            if (_adButton != null) _adButton.interactable = false;
            if (_freeCardGroup != null) _freeCardGroup.alpha = 0.55f;
            StopCooldownTicker();
            _cooldownTicker = StartCoroutine(CooldownTicker());
        }

        IEnumerator CooldownTicker()
        {
            var wait = new WaitForSecondsRealtime(1f);
            while (_cooldownActive)
            {
                int remaining = Mathf.CeilToInt(_cooldownEndRealtime - Time.unscaledTime);
                if (remaining <= 0)
                {
                    _cooldownActive = false;
                    RefreshMarketStatus();
                    yield break;
                }
                if (_adButtonLabel != null) _adButtonLabel.text = FormatCooldown(remaining);
                yield return wait;
            }
        }

        void StopCooldownTicker()
        {
            if (_cooldownTicker != null) { StopCoroutine(_cooldownTicker); _cooldownTicker = null; }
        }

        // ── +1 Diamond rewarded ad (DIAMOND_1; backend grants and confirms) ──────

        void ResetDiamondAdButton()
        {
            _diamondAdInFlight = false;
            if (_diamondAdButton != null) _diamondAdButton.interactable = true;
            if (_diamondCardGroup != null) _diamondCardGroup.alpha = 1f;
            if (_diamondAdLabel != null) _diamondAdLabel.text = "WATCH AD";
        }

        void OnDiamondAdClicked()
        {
            if (_diamondAdInFlight) return;
            var ctx = GameContext.Instance;
            if (ctx == null || !ctx.BackendEnabled) { GameEvents.RaiseToast("Şu anda kullanılamıyor."); return; }

            _diamondAdInFlight = true;
            if (_diamondAdButton != null) _diamondAdButton.interactable = false;
            if (_diamondCardGroup != null) _diamondCardGroup.alpha = 0.7f;
            if (_diamondAdLabel != null) _diamondAdLabel.text = "WATCHING...";

            ctx.WatchDiamond1Ad(
                onClaimed: res =>
                {
                    if (this == null) return;
                    GameEvents.RaiseToast("+1 Diamond");
                    ResetDiamondAdButton();
                },
                onFailed: msg =>
                {
                    if (this == null) return;
                    GameEvents.RaiseToast(MapAdFailure(msg));
                    ResetDiamondAdButton();
                },
                onCancelled: () =>
                {
                    if (this == null) return;
                    ResetDiamondAdButton();
                });
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        static string FormatCooldown(int seconds)
            => $"WAIT  {seconds / 60:00}:{seconds % 60:00}";

        static string MapAdFailure(string msg)
            => string.IsNullOrEmpty(msg) ? "Şu anda kullanılamıyor."
             : msg == "AUTH_PENDING"    ? AdRewardMessages.MapUnavailable(msg)
             : msg;
    }
}
