#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.UI;

namespace ValoCase.Editor
{
    // Popup ve overlay builder'lari: Daily reward, Toast, Skin detay, Skin win.
    public static partial class ValoCaseUIBuilder
    {
        static DailyRewardPopup BuildDailyPopup(RectTransform safe)
        {
            var popup = CreateRect("DailyRewardPopup", safe, new Vector2(560, 420));
            StretchCenter(popup, 0, 0, 560, 420);
            popup.GetComponent<Image>().color = Panel;
            AddNeonGlow(popup.gameObject, NeonPink, 3f);
            var comp = popup.gameObject.AddComponent<DailyRewardPopup>();

            var streak = CreateTmp("Streak", popup, "Streak: 0", 22, TextAlignmentOptions.Center);
            StretchTop(streak, 40, 40);
            var reward = CreateTmp("Reward", popup, "+150 VP", 32, TextAlignmentOptions.Center);
            StretchCenter(reward, 0, 20, 400, 50);
            var timer = CreateTmp("Timer", popup, "Reward available", 16, TextAlignmentOptions.Center);
            StretchBottom(timer, 100, 30);
            var claim = CreateMenuButton(popup, "Claim", "CLAIM", AccentRed, new Vector2(-90, 40), new Vector2(200, 64));
            var close = CreateMenuButton(popup, "Close", "CLOSE", Panel, new Vector2(90, 40), new Vector2(200, 64));

            var so = new SerializedObject(comp);
            so.FindProperty("root").objectReferenceValue = popup.gameObject;
            so.FindProperty("streakLabel").objectReferenceValue = streak;
            so.FindProperty("rewardLabel").objectReferenceValue = reward;
            so.FindProperty("timerLabel").objectReferenceValue = timer;
            so.FindProperty("claimButton").objectReferenceValue = claim;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.ApplyModifiedPropertiesWithoutUndo();
            popup.gameObject.SetActive(false);
            return comp;
        }

        static void BuildToast(RectTransform safe)
        {
            var toastRoot = CreateRect("Toast", safe, new Vector2(600, 64));
            StretchTop(toastRoot, 140, 64);
            toastRoot.GetComponent<Image>().color = new Color(0, 0, 0, 0.75f);
            AddNeonGlow(toastRoot.gameObject, NeonCyan, 2f);
            var label = CreateTmp("Message", toastRoot, "", 20, TextAlignmentOptions.Center);
            StretchFull(label);
            var toast = toastRoot.gameObject.AddComponent<ToastView>();
            var so = new SerializedObject(toast);
            so.FindProperty("root").objectReferenceValue = toastRoot.gameObject;
            so.FindProperty("messageLabel").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
            toastRoot.gameObject.SetActive(false);
        }

        // ── Skin Win Popup ────────────────────────────────────────────────────
        // Full-screen loot reveal overlay — shows after upgrade success or case opening.
        // The card's entire visual theme updates at runtime based on the won skin's rarity:
        //   background wash, diagonal light beams, top accent line, badge, confirm button.
        // Sits as last sibling in SafeArea so it renders above everything.
        static SkinWinPopup BuildSkinWinPopup(RectTransform safe)
        {
            // ── Root: full-screen dark backdrop ──────────────────────────────
            var root = CreateRect("SkinWinPopup", safe, Vector2.zero);
            StretchFull(root);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);
            var cg   = root.gameObject.AddComponent<CanvasGroup>();
            var comp = root.gameObject.AddComponent<SkinWinPopup>();

            // ── Card panel (600 × 540) ────────────────────────────────────────
            // RectMask2D clips the diagonal glow beams cleanly at card edges.
            var card = CreateRect("Card", root, new Vector2(600f, 540f));
            StretchCenter(card, 0f, 0f, 600f, 540f);
            card.GetComponent<Image>().color = new Color(0.04f, 0.07f, 0.11f, 1f); // very dark navy
            card.gameObject.AddComponent<RectMask2D>();

            // ── Layer 1: rarity colour wash (full card) ───────────────────────
            var rarityBgRt = CreateRect("RarityBg", card, Vector2.zero);
            StretchFull(rarityBgRt);
            var rarityBgImg = rarityBgRt.GetComponent<Image>();
            rarityBgImg.color         = new Color(0.86f, 0.16f, 0.26f, 0.40f); // default crimson, updated at runtime
            rarityBgImg.raycastTarget = false;

            // ── Layer 2: diagonal light beams (clipped by card mask) ──────────
            // Wide rectangles rotated -32° simulate the diagonal spotlight in the screenshot.
            var glow1Rt = CreateRect("DiagGlow1", card, new Vector2(220f, 900f));
            glow1Rt.anchorMin        = glow1Rt.anchorMax = new Vector2(0.5f, 0.5f);
            glow1Rt.pivot            = new Vector2(0.5f, 0.5f);
            glow1Rt.anchoredPosition = new Vector2(-70f, 30f);
            glow1Rt.localRotation    = Quaternion.Euler(0f, 0f, -32f);
            var glow1Img = glow1Rt.GetComponent<Image>();
            glow1Img.color         = new Color(1f, 1f, 1f, 0.14f);
            glow1Img.raycastTarget = false;

            var glow2Rt = CreateRect("DiagGlow2", card, new Vector2(110f, 900f));
            glow2Rt.anchorMin        = glow2Rt.anchorMax = new Vector2(0.5f, 0.5f);
            glow2Rt.pivot            = new Vector2(0.5f, 0.5f);
            glow2Rt.anchoredPosition = new Vector2(80f, 30f);
            glow2Rt.localRotation    = Quaternion.Euler(0f, 0f, -32f);
            var glow2Img = glow2Rt.GetComponent<Image>();
            glow2Img.color         = new Color(1f, 1f, 1f, 0.07f);
            glow2Img.raycastTarget = false;

            // ── Layer 3: thin rarity accent line along top edge ───────────────
            var accentRt = CreateRect("TopAccent", card, new Vector2(0f, 5f));
            accentRt.anchorMin        = new Vector2(0f, 1f);
            accentRt.anchorMax        = new Vector2(1f, 1f);
            accentRt.pivot            = new Vector2(0.5f, 1f);
            accentRt.anchoredPosition = Vector2.zero;
            accentRt.sizeDelta        = new Vector2(0f, 5f);
            var accentImg = accentRt.GetComponent<Image>();
            accentImg.color         = AccentRed;
            accentImg.raycastTarget = false;

            // ── Skin name — large bold white (top area of card) ───────────────
            // card half-height = 270;  +200 = 70 px below top edge
            var nameLabel = CreateTmp("SkinName", card, "Skin Adi", 28f, TextAlignmentOptions.Center);
            nameLabel.fontStyle     = FontStyles.Bold;
            nameLabel.color         = TextWhite;
            nameLabel.raycastTarget = false;
            nameLabel.enableWordWrapping = true;
            StretchCenter(nameLabel, 196f, 0f, 540f, 56f);

            // ── VP value — bright green (matches screenshot price label) ──────
            var vpLabel = CreateTmp("VpLabel", card, "1,775 VP", 22f, TextAlignmentOptions.Center);
            vpLabel.fontStyle     = FontStyles.Bold;
            vpLabel.color         = new Color(0.30f, 1.00f, 0.45f, 1f); // bright green
            vpLabel.raycastTarget = false;
            StretchCenter(vpLabel, 148f, 0f, 400f, 38f);

            // ── Skin icon — dominant, centred, large ──────────────────────────
            var iconRt = CreateRect("SkinIcon", card, new Vector2(280f, 200f));
            StretchCenter(iconRt, 20f, 0f, 280f, 200f);
            var iconImg = iconRt.GetComponent<Image>();
            iconImg.color          = Color.white;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget  = false;
            // Transparent background so only the weapon shows
            iconImg.color = Color.white;

            // ── Rarity badge pill ─────────────────────────────────────────────
            var badgeRt = CreateRect("RarityBadge", card, new Vector2(170f, 34f));
            StretchCenter(badgeRt, -112f, 0f, 170f, 34f);
            var badgeBgImg = badgeRt.GetComponent<Image>();
            badgeBgImg.color         = new Color(0.86f, 0.16f, 0.26f, 0.75f); // runtime-updated
            badgeBgImg.raycastTarget = false;

            var rarityLabel = CreateTmp("RarityLabel", badgeRt, "EXCLUSIVE", 15f, TextAlignmentOptions.Center);
            rarityLabel.fontStyle     = FontStyles.Bold;
            rarityLabel.color         = Color.white;
            rarityLabel.raycastTarget = false;
            StretchFull(rarityLabel.rectTransform);

            // ── Weapon category label (below badge) ───────────────────────────
            var categoryLabel = CreateTmp("CategoryLabel", card, "VANDAL", 15f, TextAlignmentOptions.Center);
            categoryLabel.color         = new Color(0.75f, 0.80f, 0.85f, 1f); // muted light grey
            categoryLabel.raycastTarget = false;
            StretchCenter(categoryLabel, -154f, 0f, 400f, 28f);

            // ── Confirm button ────────────────────────────────────────────────
            // Background is a separate Image so we can tint it with rarity colour.
            var confirmRt = CreateRect("ConfirmButton", card, new Vector2(380f, 70f));
            confirmRt.anchorMin        = confirmRt.anchorMax = new Vector2(0.5f, 0.5f);
            confirmRt.pivot            = new Vector2(0.5f, 0.5f);
            confirmRt.anchoredPosition = new Vector2(0f, -215f);
            confirmRt.sizeDelta        = new Vector2(380f, 70f);
            var confirmBtnBgImg = confirmRt.GetComponent<Image>();
            confirmBtnBgImg.color = new Color(0.50f, 0.09f, 0.16f, 1f); // dark crimson default
            var confirmBtn = confirmRt.gameObject.AddComponent<Button>();
            ApplyNeonInteraction(confirmBtn, NeonCyan);

            var confirmTextTmp = CreateTmp("Label", confirmRt, "KOLEKSİYONA EKLE", 20f, TextAlignmentOptions.Center);
            confirmTextTmp.fontStyle     = FontStyles.Bold;
            confirmTextTmp.color         = TextWhite;
            confirmTextTmp.raycastTarget = false;
            StretchFull(confirmTextTmp.rectTransform);
            confirmTextTmp.rectTransform.offsetMin = new Vector2(8f, 4f);
            confirmTextTmp.rectTransform.offsetMax = new Vector2(-8f, -4f);

            // ── Wire all serialized fields ────────────────────────────────────
            var so = new SerializedObject(comp);
            so.FindProperty("canvasGroup").objectReferenceValue    = cg;
            so.FindProperty("overlay").objectReferenceValue        = root.GetComponent<Image>();
            so.FindProperty("card").objectReferenceValue           = card;
            so.FindProperty("rarityBg").objectReferenceValue       = rarityBgImg;
            so.FindProperty("diagonalGlow1").objectReferenceValue  = glow1Img;
            so.FindProperty("diagonalGlow2").objectReferenceValue  = glow2Img;
            so.FindProperty("topAccentLine").objectReferenceValue  = accentImg;
            so.FindProperty("skinNameLabel").objectReferenceValue  = nameLabel;
            so.FindProperty("vpLabel").objectReferenceValue        = vpLabel;
            so.FindProperty("skinIconImage").objectReferenceValue  = iconImg;
            so.FindProperty("rarityBadgeBg").objectReferenceValue  = badgeBgImg;
            so.FindProperty("rarityLabel").objectReferenceValue    = rarityLabel;
            so.FindProperty("categoryLabel").objectReferenceValue  = categoryLabel;
            so.FindProperty("confirmButton").objectReferenceValue  = confirmBtn;
            so.FindProperty("confirmButtonBg").objectReferenceValue = confirmBtnBgImg;
            so.ApplyModifiedPropertiesWithoutUndo();

            root.gameObject.SetActive(false);
            return comp;
        }

        static SkinDetailPopup BuildSkinDetailPopup(RectTransform parent)
        {
            var root = CreateRect("SkinDetailPopup", parent, new Vector2(560, 520));
            StretchCenter(root, 0, 0, 560, 520);
            root.GetComponent<Image>().color = new Color(0.02f, 0.04f, 0.06f, 0.92f);
            AddNeonGlow(root.gameObject, NeonPurple, 3f);
            var comp = root.gameObject.AddComponent<SkinDetailPopup>();
            var name = CreateTmp("SkinName", root, "Skin", 28, TextAlignmentOptions.Center);
            StretchTop(name, 24, 48);
            var weapon = CreateTmp("Weapon", root, "Vandal", 18, TextAlignmentOptions.Center);
            StretchTop(weapon, 72, 32);
            var rarity = CreateTmp("Rarity", root, "PREMIUM", 16, TextAlignmentOptions.Center);
            StretchTop(rarity, 106, 28);

            var icon = CreateRect("Icon", root, new Vector2(220, 120));
            StretchCenter(icon, 70, 0, 220, 120);
            icon.GetComponent<Image>().color = new Color(1, 1, 1, 0.12f);

            var description = CreateTmp("Description", root, "Skin description", 15, TextAlignmentOptions.Center);
            StretchCenter(description, -20, 32, 460, 58);
            var collection = CreateTmp("Collection", root, "Collection", 15, TextAlignmentOptions.Left);
            collection.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            collection.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            collection.rectTransform.anchoredPosition = new Vector2(-120, -90);
            collection.rectTransform.sizeDelta = new Vector2(220, 28);
            var value = CreateTmp("VpValue", root, "1,775 VP", 15, TextAlignmentOptions.Right);
            value.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            value.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            value.rectTransform.anchoredPosition = new Vector2(120, -90);
            value.rectTransform.sizeDelta = new Vector2(220, 28);
            var quantity = CreateTmp("Quantity", root, "Owned: 1", 15, TextAlignmentOptions.Center);
            StretchCenter(quantity, -128, 0, 460, 28);

            var sell = CreateMenuButton(root, "Sell", "SELL", AccentRed, new Vector2(-120, -205), new Vector2(220, 64));
            var close = CreateMenuButton(root, "Close", "CLOSE", Panel, new Vector2(120, -205), new Vector2(220, 64));
            var so = new SerializedObject(comp);
            so.FindProperty("root").objectReferenceValue = root.gameObject;
            so.FindProperty("icon").objectReferenceValue = icon.GetComponent<Image>();
            so.FindProperty("skinName").objectReferenceValue = name;
            so.FindProperty("weaponName").objectReferenceValue = weapon;
            so.FindProperty("rarityLabel").objectReferenceValue = rarity;
            so.FindProperty("description").objectReferenceValue = description;
            so.FindProperty("collection").objectReferenceValue = collection;
            so.FindProperty("vpValue").objectReferenceValue = value;
            so.FindProperty("quantity").objectReferenceValue = quantity;
            so.FindProperty("sellButton").objectReferenceValue = sell;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.ApplyModifiedPropertiesWithoutUndo();
            root.gameObject.SetActive(false);
            return comp;
        }
    }
}
#endif
