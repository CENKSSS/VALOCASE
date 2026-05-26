#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Pooling;
using ValoCase.UI;

namespace ValoCase.Editor
{
    // Card / reel / drop / case-list prefablari + wiring helper'lari.
    public static partial class ValoCaseUIBuilder
    {
        static GameObject BuildSkinCardPrefab()
        {
            // ── Root (180×260) — background color set at runtime by Bind() ──
            var root = CreateRect("PF_SkinCard", null, new Vector2(180, 260));
            GetOrAddImage(root).color = Panel;      // runtime: v.cardBgColor
            AddNeonGlow(root.gameObject, NeonCyan, 2f); // runtime: v.primaryColor
            var btn = root.gameObject.AddComponent<Button>();
            ApplyNeonInteraction(btn, NeonCyan);
            var card = root.gameObject.AddComponent<SkinCardView>();

            // ── Weapon icon — upper 55 % of card ─────────────────────────────
            var icon = CreateRect("Icon", root, new Vector2(155, 108));
            icon.anchorMin        = new Vector2(0.5f, 0.5f);
            icon.anchorMax        = new Vector2(0.5f, 0.5f);
            icon.pivot            = new Vector2(0.5f, 0.5f);
            icon.anchoredPosition = new Vector2(0, 38);
            icon.sizeDelta        = new Vector2(155, 108);
            var iconImg = icon.GetComponent<Image>();
            iconImg.color        = new Color(1, 1, 1, 0.92f);
            iconImg.preserveAspect = true;

            // ── Rarity symbol icon — PNG loaded at runtime from Semboller/ ────
            // 28×28 square with preserveAspect so the PNG renders correctly.
            // Falls back to a solid-color dot when the file is not found.
            var rarityDot = CreateRect("RarityDot", root, new Vector2(28, 28));
            rarityDot.anchorMin        = new Vector2(0.5f, 0.5f);
            rarityDot.anchorMax        = new Vector2(0.5f, 0.5f);
            rarityDot.pivot            = new Vector2(0.5f, 0.5f);
            rarityDot.anchoredPosition = new Vector2(0, -20);
            rarityDot.sizeDelta        = new Vector2(28, 28);
            var rarityDotImg = rarityDot.GetComponent<Image>();
            rarityDotImg.color         = AccentRed;   // placeholder; runtime: white (sprite) or primaryColor (fallback)
            rarityDotImg.preserveAspect = true;

            // ── Skin name & weapon type ───────────────────────────────────────
            var title = CreateTmp("Title", root, "Skin", 17, TextAlignmentOptions.Center);
            title.rectTransform.anchorMin        = new Vector2(0.5f, 0.5f);
            title.rectTransform.anchorMax        = new Vector2(0.5f, 0.5f);
            title.rectTransform.pivot            = new Vector2(0.5f, 0.5f);
            title.rectTransform.anchoredPosition = new Vector2(0, -44);
            title.rectTransform.sizeDelta        = new Vector2(162, 26);
            title.fontStyle = FontStyles.Bold;

            var sub = CreateTmp("Subtitle", root, "Weapon", 12, TextAlignmentOptions.Center);
            sub.rectTransform.anchorMin        = new Vector2(0.5f, 0.5f);
            sub.rectTransform.anchorMax        = new Vector2(0.5f, 0.5f);
            sub.rectTransform.pivot            = new Vector2(0.5f, 0.5f);
            sub.rectTransform.anchoredPosition = new Vector2(0, -66);
            sub.rectTransform.sizeDelta        = new Vector2(162, 20);
            sub.color = new Color(0.72f, 0.78f, 0.88f, 1f);

            // ── Price pill (dark rounded container + VP badge) ────────────────
            var pill = CreateRect("PricePill", root, new Vector2(140, 28));
            pill.anchorMin        = new Vector2(0.5f, 0f);
            pill.anchorMax        = new Vector2(0.5f, 0f);
            pill.pivot            = new Vector2(0.5f, 0f);
            pill.anchoredPosition = new Vector2(0, 12);
            pill.sizeDelta        = new Vector2(140, 28);
            pill.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.07f, 0.92f);

            var vpValue = CreateTmp("VpValue", pill, "100 VP", 13, TextAlignmentOptions.Center);
            StretchFull(vpValue);
            vpValue.rectTransform.offsetMin = new Vector2(8,  2);
            vpValue.rectTransform.offsetMax = new Vector2(-36, -2);
            vpValue.raycastTarget = false;

            // Gold VP badge on right of pill (mimics the CY coin in reference)
            var vpBadge = CreateRect("VpBadge", pill, new Vector2(28, 22));
            vpBadge.anchorMin        = new Vector2(1f, 0.5f);
            vpBadge.anchorMax        = new Vector2(1f, 0.5f);
            vpBadge.pivot            = new Vector2(1f, 0.5f);
            vpBadge.anchoredPosition = new Vector2(-3, 0);
            vpBadge.sizeDelta        = new Vector2(28, 22);
            vpBadge.GetComponent<Image>().color = new Color(1f, 0.84f, 0.08f, 1f);
            var vpBadgeText = CreateTmp("Label", vpBadge, "VP", 9, TextAlignmentOptions.Center);
            StretchFull(vpBadgeText);
            vpBadgeText.color = new Color(0.10f, 0.07f, 0f, 1f);
            vpBadgeText.raycastTarget = false;

            // ── Inventory-only extras (quantity / duplicate / sell) ───────────
            var qty = CreateTmp("Quantity", root, "", 14, TextAlignmentOptions.Left);
            qty.rectTransform.anchorMin        = new Vector2(0f, 1f);
            qty.rectTransform.anchorMax        = new Vector2(0f, 1f);
            qty.rectTransform.pivot            = new Vector2(0f, 1f);
            qty.rectTransform.anchoredPosition = new Vector2(8, -8);
            qty.rectTransform.sizeDelta        = new Vector2(56, 22);

            var dup = CreateRect("DuplicateMarker", root, new Vector2(10, 10));
            dup.anchorMin        = new Vector2(0f, 1f);
            dup.anchorMax        = new Vector2(0f, 1f);
            dup.pivot            = new Vector2(0f, 1f);
            dup.anchoredPosition = new Vector2(4, -4);
            dup.sizeDelta        = new Vector2(10, 10);
            GetOrAddImage(dup).color = AccentRed;

            var sellBtnGo = CreateRect("SellButton", root, new Vector2(48, 22));
            sellBtnGo.anchorMin        = new Vector2(1f, 1f);
            sellBtnGo.anchorMax        = new Vector2(1f, 1f);
            sellBtnGo.pivot            = new Vector2(1f, 1f);
            sellBtnGo.anchoredPosition = new Vector2(-4, -8);
            GetOrAddImage(sellBtnGo).color = new Color(0.75f, 0.08f, 0.25f, 0.88f);
            var sellBtn = sellBtnGo.gameObject.AddComponent<Button>();
            var sellLabel = CreateTmp("Label", sellBtnGo, "SELL", 10, TextAlignmentOptions.Center);
            StretchFull(sellLabel);
            sellLabel.raycastTarget = false;

            WireSkinCard(card, btn, iconImg, rarityDotImg, title, sub, qty, dup.gameObject, vpValue, sellBtn);
            return SavePrefab(root.gameObject, SkinCardPrefabPath);
        }

        static GameObject BuildReelItemPrefab()
        {
            // ── Root (220×280) — no visible Image on root; Frame IS the background ──
            var root = CreateRect("PF_ReelItem", null, new Vector2(220, 280));
            GetOrAddImage(root).color = new Color(0, 0, 0, 0); // transparent — Frame covers it
            AddNeonGlow(root.gameObject, NeonCyan, 3f);        // runtime: v.primaryColor
            var view = root.gameObject.AddComponent<ReelItemView>();

            // Full-card rarity background (runtime: v.cardBgColor via rarityFrame).
            var frame = CreateRect("Frame", root, new Vector2(220, 280));
            StretchFull(frame);
            frame.GetComponent<Image>().color = Panel;         // placeholder; runtime sets cardBgColor

            // Soft neon glow overlay (runtime: v.primaryColor at ~25 % alpha).
            var glow = CreateRect("Glow", root, new Vector2(220, 280));
            StretchFull(glow);
            glow.GetComponent<Image>().color = new Color(0f, 0.96f, 1f, 0.18f);

            // ── Weapon icon — upper 55 % ─────────────────────────────────────
            var icon = CreateRect("Icon", root, new Vector2(185, 130));
            icon.anchorMin        = new Vector2(0.5f, 0.5f);
            icon.anchorMax        = new Vector2(0.5f, 0.5f);
            icon.pivot            = new Vector2(0.5f, 0.5f);
            icon.anchoredPosition = new Vector2(0, 42);
            icon.sizeDelta        = new Vector2(185, 130);
            var iconImg = icon.GetComponent<Image>();
            iconImg.color        = Color.white;
            iconImg.preserveAspect = true;

            // ── Weapon name (small, muted) ────────────────────────────────────
            var weapon = CreateTmp("Weapon", root, "Vandal", 13, TextAlignmentOptions.Center);
            weapon.rectTransform.anchorMin        = new Vector2(0.5f, 0.5f);
            weapon.rectTransform.anchorMax        = new Vector2(0.5f, 0.5f);
            weapon.rectTransform.pivot            = new Vector2(0.5f, 0.5f);
            weapon.rectTransform.anchoredPosition = new Vector2(0, -62);
            weapon.rectTransform.sizeDelta        = new Vector2(200, 22);
            weapon.color = new Color(0.72f, 0.78f, 0.88f, 1f);

            // ── Skin name (bold, primary) ─────────────────────────────────────
            var skin = CreateTmp("Skin", root, "Skin", 17, TextAlignmentOptions.Center);
            skin.rectTransform.anchorMin        = new Vector2(0.5f, 0.5f);
            skin.rectTransform.anchorMax        = new Vector2(0.5f, 0.5f);
            skin.rectTransform.pivot            = new Vector2(0.5f, 0.5f);
            skin.rectTransform.anchoredPosition = new Vector2(0, -84);
            skin.rectTransform.sizeDelta        = new Vector2(200, 26);
            skin.fontStyle = FontStyles.Bold;

            WireReelItem(view, iconImg, frame.GetComponent<Image>(), glow.GetComponent<Image>(), weapon, skin);
            return SavePrefab(root.gameObject, ReelItemPrefabPath);
        }

        static GameObject BuildDropItemPrefab()
        {
            // Row: [RarityStripe(8px) | Icon(80x80) | Name+Weapon(flex) | Chance | Price]
            var root = CreateRect("PF_DropItem", null, new Vector2(0, 80));
            GetOrAddImage(root).color = new Color(0.1f, 0.14f, 0.18f, 1f);
            root.gameObject.AddComponent<DropItemView>();

            // Left rarity color stripe
            var stripe = CreateRect("RarityStripe", root, new Vector2(6, 80));
            stripe.anchorMin = new Vector2(0, 0);
            stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.anchoredPosition = Vector2.zero;
            stripe.sizeDelta = new Vector2(6, 0);
            stripe.GetComponent<Image>().color = AccentRed;

            var iconRect = CreateRect("Icon", root, new Vector2(72, 64));
            iconRect.anchorMin = new Vector2(0, 0.5f);
            iconRect.anchorMax = new Vector2(0, 0.5f);
            iconRect.pivot = new Vector2(0, 0.5f);
            iconRect.anchoredPosition = new Vector2(14, 0);
            iconRect.sizeDelta = new Vector2(72, 64);
            var iconImg = iconRect.GetComponent<Image>();
            iconImg.color = Color.white;
            iconImg.preserveAspect = true;

            // Weapon label (small, top)
            var weapon = CreateTmp("Weapon", root, "Vandal", 13, TextAlignmentOptions.Left);
            weapon.rectTransform.anchorMin = new Vector2(0, 0.5f);
            weapon.rectTransform.anchorMax = new Vector2(0, 0.5f);
            weapon.rectTransform.pivot = new Vector2(0, 0);
            weapon.rectTransform.anchoredPosition = new Vector2(94, 2);
            weapon.rectTransform.sizeDelta = new Vector2(320, 22);
            weapon.color = new Color(0.65f, 0.72f, 0.8f);

            // Skin name label (larger, below weapon)
            var skinName = CreateTmp("SkinName", root, "Singularity", 17, TextAlignmentOptions.Left);
            skinName.rectTransform.anchorMin = new Vector2(0, 0.5f);
            skinName.rectTransform.anchorMax = new Vector2(0, 0.5f);
            skinName.rectTransform.pivot = new Vector2(0, 1);
            skinName.rectTransform.anchoredPosition = new Vector2(94, -2);
            skinName.rectTransform.sizeDelta = new Vector2(320, 28);

            // Drop chance — right side
            var chance = CreateTmp("Chance", root, "1.00%", 15, TextAlignmentOptions.Right);
            chance.rectTransform.anchorMin = new Vector2(1, 0.5f);
            chance.rectTransform.anchorMax = new Vector2(1, 0.5f);
            chance.rectTransform.pivot = new Vector2(1, 0.5f);
            chance.rectTransform.anchoredPosition = new Vector2(-120, 6);
            chance.rectTransform.sizeDelta = new Vector2(110, 26);
            chance.color = new Color(0.75f, 0.85f, 1f);

            // VP price — far right
            var price = CreateTmp("Price", root, "1,775 VP", 14, TextAlignmentOptions.Right);
            price.rectTransform.anchorMin = new Vector2(1, 0.5f);
            price.rectTransform.anchorMax = new Vector2(1, 0.5f);
            price.rectTransform.pivot = new Vector2(1, 0.5f);
            price.rectTransform.anchoredPosition = new Vector2(-12, -8);
            price.rectTransform.sizeDelta = new Vector2(110, 22);
            price.color = new Color(0.65f, 1f, 0.65f);

            // Subtle bg that gets tinted by rarity
            var bg = CreateRect("RarityBg", root, Vector2.zero);
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.offsetMin = Vector2.zero;
            bg.offsetMax = Vector2.zero;
            bg.SetSiblingIndex(0);
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = new Color(1f, 0.3f, 0.3f, 0.06f);
            bgImg.raycastTarget = false;

            var view = root.GetComponent<DropItemView>();
            var so = new SerializedObject(view);
            so.FindProperty("rarityStripe").objectReferenceValue = stripe.GetComponent<Image>();
            so.FindProperty("skinIcon").objectReferenceValue = iconImg;
            so.FindProperty("weaponLabel").objectReferenceValue = weapon;
            so.FindProperty("skinNameLabel").objectReferenceValue = skinName;
            so.FindProperty("chanceLabel").objectReferenceValue = chance;
            so.FindProperty("priceLabel").objectReferenceValue = price;
            so.FindProperty("background").objectReferenceValue = bgImg;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root.gameObject, DropItemPrefabPath);
        }

        static GameObject BuildWeaponSkinCardPrefab()
        {
            // ── Root (200×290) — background set at runtime by Bind() ─────────
            var root = CreateRect("PF_WeaponSkinCard", null, new Vector2(200, 290));
            GetOrAddImage(root).color = Panel;      // runtime: v.cardBgColor
            AddNeonGlow(root.gameObject, NeonCyan, 2f); // runtime: v.primaryColor
            var card = root.gameObject.AddComponent<WeaponSkinCardView>();

            // ── Weapon icon — upper area ──────────────────────────────────────
            var icon = CreateRect("Icon", root, new Vector2(178, 120));
            icon.anchorMin        = new Vector2(0.5f, 0.5f);
            icon.anchorMax        = new Vector2(0.5f, 0.5f);
            icon.pivot            = new Vector2(0.5f, 0.5f);
            icon.anchoredPosition = new Vector2(0, 50);
            icon.sizeDelta        = new Vector2(178, 120);
            var iconImg = icon.GetComponent<Image>();
            iconImg.color        = new Color(1, 1, 1, 0.92f);
            iconImg.preserveAspect = true;

            // ── Rarity symbol icon (repurposes rarityStripe ref) ─────────────
            var rarityDot = CreateRect("RarityDot", root, new Vector2(32, 32));
            rarityDot.anchorMin        = new Vector2(0.5f, 0.5f);
            rarityDot.anchorMax        = new Vector2(0.5f, 0.5f);
            rarityDot.pivot            = new Vector2(0.5f, 0.5f);
            rarityDot.anchoredPosition = new Vector2(0, -18);
            rarityDot.sizeDelta        = new Vector2(32, 32);
            var rarityDotImg2 = rarityDot.GetComponent<Image>();
            rarityDotImg2.color         = AccentRed;
            rarityDotImg2.preserveAspect = true;

            // ── Skin name (bold) ──────────────────────────────────────────────
            var skinName = CreateTmp("SkinName", root, "Skin", 17, TextAlignmentOptions.Center);
            skinName.rectTransform.anchorMin        = new Vector2(0.5f, 0.5f);
            skinName.rectTransform.anchorMax        = new Vector2(0.5f, 0.5f);
            skinName.rectTransform.pivot            = new Vector2(0.5f, 0.5f);
            skinName.rectTransform.anchoredPosition = new Vector2(0, -58);
            skinName.rectTransform.sizeDelta        = new Vector2(184, 28);
            skinName.fontStyle = FontStyles.Bold;

            // ── Weapon type (muted secondary) ─────────────────────────────────
            var weaponLabel = CreateTmp("WeaponLabel", root, "Vandal", 12, TextAlignmentOptions.Center);
            weaponLabel.rectTransform.anchorMin        = new Vector2(0.5f, 0.5f);
            weaponLabel.rectTransform.anchorMax        = new Vector2(0.5f, 0.5f);
            weaponLabel.rectTransform.pivot            = new Vector2(0.5f, 0.5f);
            weaponLabel.rectTransform.anchoredPosition = new Vector2(0, -82);
            weaponLabel.rectTransform.sizeDelta        = new Vector2(184, 20);
            weaponLabel.color = new Color(0.72f, 0.78f, 0.88f, 1f);

            var so = new SerializedObject(card);
            so.FindProperty("skinIcon").objectReferenceValue      = iconImg;
            so.FindProperty("rarityStripe").objectReferenceValue  = rarityDotImg2;
            so.FindProperty("skinNameLabel").objectReferenceValue = skinName;
            so.FindProperty("weaponLabel").objectReferenceValue   = weaponLabel;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root.gameObject, WeaponSkinCardPrefabPath);
        }

        static GameObject BuildCaseListItemPrefab()
        {
            var root = CreateRect("PF_CaseListItem", null, new Vector2(260, 100));
            GetOrAddImage(root).color = Panel;
            AddNeonGlow(root.gameObject, NeonPink, 2f);
            var btn = root.gameObject.AddComponent<Button>();
            ApplyNeonInteraction(btn, NeonPink);
            var view = root.gameObject.AddComponent<CaseListItemView>();
            var title = CreateTmp("Title", root, "Case", 20, TextAlignmentOptions.Center);
            StretchTop(title, 8, 36);
            var price = CreateTmp("Price", root, "475 VP", 16, TextAlignmentOptions.Center);
            StretchBottom(price, 8, 28);
            var icon = CreateRect("Icon", root, new Vector2(48, 48));
            icon.anchorMin = new Vector2(0, 0.5f);
            icon.anchorMax = new Vector2(0, 0.5f);
            icon.pivot = new Vector2(0, 0.5f);
            icon.anchoredPosition = new Vector2(12, 0);
            icon.sizeDelta = new Vector2(48, 48);
            GetOrAddImage(icon).color = AccentRed;
            var lockOv = CreateRect("Locked", root, new Vector2(260, 100));
            StretchFull(lockOv);
            lockOv.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);
            lockOv.gameObject.SetActive(false);
            var sel = CreateRect("Selected", root, new Vector2(260, 100));
            StretchFull(sel);
            sel.GetComponent<Image>().color = new Color(1, 0.27f, 0.33f, 0.25f);
            sel.gameObject.SetActive(false);

            var so = new SerializedObject(view);
            so.FindProperty("button").objectReferenceValue = btn;
            so.FindProperty("icon").objectReferenceValue = icon.GetComponent<Image>();
            so.FindProperty("title").objectReferenceValue = title;
            so.FindProperty("price").objectReferenceValue = price;
            so.FindProperty("lockedOverlay").objectReferenceValue = lockOv.gameObject;
            so.FindProperty("selectedFrame").objectReferenceValue = sel.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(root.gameObject, CaseListItemPrefabPath);
        }

        static void WireSkinCard(SkinCardView card, Button btn, Image icon, Image stripe, TextMeshProUGUI title, TextMeshProUGUI sub, TextMeshProUGUI qty, GameObject dup, TextMeshProUGUI vpValue = null, Button sellBtn = null)
        {
            var so = new SerializedObject(card);
            so.FindProperty("button").objectReferenceValue = btn;
            so.FindProperty("icon").objectReferenceValue = icon;
            so.FindProperty("rarityStripe").objectReferenceValue = stripe;
            so.FindProperty("title").objectReferenceValue = title;
            so.FindProperty("subtitle").objectReferenceValue = sub;
            so.FindProperty("quantityBadge").objectReferenceValue = qty;
            so.FindProperty("duplicateMarker").objectReferenceValue = dup;
            if (vpValue != null) so.FindProperty("vpValueLabel").objectReferenceValue = vpValue;
            if (sellBtn != null) so.FindProperty("sellButton").objectReferenceValue = sellBtn;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireReelItem(ReelItemView view, Image icon, Image frame, Image glow, TextMeshProUGUI weapon, TextMeshProUGUI skin)
        {
            var so = new SerializedObject(view);
            so.FindProperty("icon").objectReferenceValue = icon;
            so.FindProperty("rarityFrame").objectReferenceValue = frame;
            so.FindProperty("glow").objectReferenceValue = glow;
            so.FindProperty("weaponLabel").objectReferenceValue = weapon;
            so.FindProperty("skinLabel").objectReferenceValue = skin;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WirePoolManager(PoolManager pool, SkinCardView card, ReelItemView reel, Transform reelRoot, Transform cardRoot)
        {
            var so = new SerializedObject(pool);
            so.FindProperty("reelItemPrefab").objectReferenceValue = reel;
            so.FindProperty("skinCardPrefab").objectReferenceValue = card;
            so.FindProperty("reelPoolRoot").objectReferenceValue = reelRoot;
            so.FindProperty("cardPoolRoot").objectReferenceValue = cardRoot;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
