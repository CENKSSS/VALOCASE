using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Data;
using ValoCase.UI.Factory;

using static ValoCase.UI.Screens.CaseBattlePalette;
using static ValoCase.UI.Screens.CaseBattleUiBuilder;

namespace ValoCase.UI.Screens
{
    /// <summary>
    /// Handles all roulette and history-card animation for CaseBattleScreen.
    /// Added as a component to the same GameObject as CaseBattleScreen.
    /// Pure presentation — no game logic, no service calls.
    /// </summary>
    public sealed class CaseBattleRouletteAnimator : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // PUBLIC API — called by CaseBattleScreen.RunBattleRounds()
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Animates both roulettes simultaneously then highlights the result card.</summary>
        public IEnumerator AnimateRoulettePair(
            CaseBattlePanelRefs player,  CaseBattlePanelRefs opponent,
            SkinDefinitionSO    playerSkin, SkinDefinitionSO opponentSkin,
            SkinDefinitionSO[]  pool)
        {
            PopulateRoulette(player,   playerSkin,   pool);
            PopulateRoulette(opponent, opponentSkin, pool);

            // Center-pivot math — resolution independent
            float halfH        = TotalCards * CardStride * 0.5f;
            float resultLocal  =
                halfH
                - ResultCardIndex * CardStride
                - CardH * 0.5f;
            float startLocal   = halfH - 2f * CardStride - CardH * 0.5f;
            float pEnd = -resultLocal,  oEnd = -resultLocal;
            float pStart = -startLocal, oStart = -startLocal;

            if (player.ReelContent   != null) player.ReelContent.anchoredPosition   = new Vector2(0, pStart);
            if (opponent.ReelContent != null) opponent.ReelContent.anchoredPosition = new Vector2(0, oStart);

            yield return null;

            const float duration = 4.0f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float eased = EaseOutQuint(Mathf.Clamp01(elapsed / duration));
                if (player.ReelContent   != null)
                    player.ReelContent.anchoredPosition   = new Vector2(0, Mathf.Lerp(pStart, pEnd, eased));
                if (opponent.ReelContent != null)
                    opponent.ReelContent.anchoredPosition = new Vector2(0, Mathf.Lerp(oStart, oEnd, eased));
                yield return null;
            }

            if (player.ReelContent   != null) player.ReelContent.anchoredPosition   = new Vector2(0, pEnd);
            if (opponent.ReelContent != null) opponent.ReelContent.anchoredPosition = new Vector2(0, oEnd);

            HighlightWinningCard(player,   ResultCardIndex);
            HighlightWinningCard(opponent, ResultCardIndex);

            yield return new WaitForSecondsRealtime(0.5f);
        }

        /// <summary>Pulses the winner badge 3 times after results are shown.</summary>
        public IEnumerator PulseWinnerBadge(GameObject badge)
        {
            if (badge == null) yield break;
            var rt = (RectTransform)badge.transform;

            for (int i = 0; i < 3; i++)
            {
                yield return ScaleOver(rt, 1f, 1.09f, 0.18f);
                yield return ScaleOver(rt, 1.09f, 1f,   0.18f);
                yield return new WaitForSecondsRealtime(0.25f);
            }
            rt.localScale = Vector3.one;
        }

        /// <summary>Appends one result card to the panel's history grid.</summary>
        public void AddHistoryCard(CaseBattlePanelRefs refs, SkinDefinitionSO skin)
        {
            if (skin == null || refs.GridRoot == null) return;

            var card = new GameObject("HistCard",
                typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            card.transform.SetParent(refs.GridRoot, false);
            var rt = (RectTransform)card.transform;
            rt.sizeDelta = new Vector2(CardW, CardH);

            var le = card.GetComponent<LayoutElement>();
            le.minWidth  = le.preferredWidth  = CardW;
            le.minHeight = le.preferredHeight = CardH;
            card.GetComponent<Image>().color = BgCardDark;

            // Top shine
            var shine = UIFactory.CreateRectAnchored("Shine", rt,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), addImage: true);
            shine.sizeDelta = new Vector2(0, 26f); shine.anchoredPosition = Vector2.zero;
            shine.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.022f);
            shine.GetComponent<Image>().raycastTarget = false;

            Color rarityColor = GetRarityColor(skin.Rarity);

            // 4-px rarity strip
            var strip = UIFactory.CreateRectAnchored("Strip", rt,
                Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), addImage: true);
            strip.sizeDelta = new Vector2(0, 4f); strip.anchoredPosition = Vector2.zero;
            strip.GetComponent<Image>().color = rarityColor;

            // Rarity glow
            var sgColor = rarityColor; sgColor.a = 0.14f;
            var stripGlow = UIFactory.CreateRectAnchored("StripGlow", rt,
                Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), addImage: true);
            stripGlow.sizeDelta = new Vector2(0, 20f);
            stripGlow.anchoredPosition = new Vector2(0, 4f);
            stripGlow.GetComponent<Image>().color = sgColor;
            stripGlow.GetComponent<Image>().raycastTarget = false;

            // Weapon icon (tilted -25°)
            var iconRt = UIFactory.CreateRectAnchored("Icon", rt,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), addImage: true);
            iconRt.anchoredPosition = new Vector2(0, 10f);
            iconRt.sizeDelta = new Vector2(CardW - 10f, CardH * 0.58f);
            iconRt.localRotation = Quaternion.Euler(0f, 0f, -25f);
            var iconImg = iconRt.GetComponent<Image>();
            iconImg.sprite = skin.Icon; iconImg.preserveAspect = true; iconImg.raycastTarget = false;

            // VP label
            var vpTmp = UIFactory.CreateText(rt, "Vp", FormatGp(skin.VpValue),
                10, TMPro.TextAlignmentOptions.BottomLeft, AccentGreen, TMPro.FontStyles.Bold);
            var vprt = vpTmp.rectTransform;
            vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.zero;
            vprt.pivot = Vector2.zero;
            vprt.anchoredPosition = new Vector2(6, 8);
            vprt.sizeDelta = new Vector2(CardW - 12f, 18);

            refs.HistoryCards.Add(card);
        }

        /// <summary>Clears roulette cards and history from a panel.</summary>
        public void ClearPanel(CaseBattlePanelRefs refs)
        {
            if (refs.ReelContent != null)
                for (int i = refs.ReelContent.childCount - 1; i >= 0; i--)
                    Destroy(refs.ReelContent.GetChild(i).gameObject);
            refs.RouletteCards.Clear();

            foreach (var c in refs.HistoryCards)
                if (c != null) Destroy(c);
            refs.HistoryCards.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────
        // INTERNAL
        // ─────────────────────────────────────────────────────────────────────

        void PopulateRoulette(CaseBattlePanelRefs refs,
            SkinDefinitionSO resultSkin, SkinDefinitionSO[] pool)
        {
            if (refs.ReelContent == null) return;

            for (int i = refs.ReelContent.childCount - 1; i >= 0; i--)
                Destroy(refs.ReelContent.GetChild(i).gameObject);
            refs.RouletteCards.Clear();

            refs.ReelContent.anchoredPosition = Vector2.zero;
            refs.ReelContent.sizeDelta = new Vector2(CardW, TotalCards * CardStride);

            for (int i = 0; i < TotalCards; i++)
            {
                bool isResult = (i == ResultCardIndex);
                var skin = isResult
                    ? resultSkin
                    : (pool != null && pool.Length > 0
                        ? pool[Random.Range(0, pool.Length)]
                        : resultSkin);

                var cardGo = new GameObject($"RC_{i}",
                    typeof(RectTransform), typeof(Image));
                cardGo.transform.SetParent(refs.ReelContent, false);
                var rt = (RectTransform)cardGo.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(CardW, CardH);
                rt.anchoredPosition = new Vector2(0, -i * CardStride);

                var cardImg = cardGo.GetComponent<Image>();
                cardImg.color = new Color(0.04f, 0.03f, 0.08f, 1f);

                // Top shine
                var shine = UIFactory.CreateRectAnchored("Shine", rt,
                    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), addImage: true);
                shine.sizeDelta = new Vector2(0, 28f); shine.anchoredPosition = Vector2.zero;
                shine.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.025f);
                shine.GetComponent<Image>().raycastTarget = false;

                // Rarity strip
                var strip = UIFactory.CreateRectAnchored("Strip", rt,
                    Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), addImage: true);
                strip.sizeDelta = new Vector2(0, 4f); strip.anchoredPosition = Vector2.zero;
                strip.GetComponent<Image>().color = GetRarityColor(skin?.Rarity ?? SkinRarity.Select);

                var sgColor = GetRarityColor(skin?.Rarity ?? SkinRarity.Select); sgColor.a = 0.12f;
                var stripGlow = UIFactory.CreateRectAnchored("StripGlow", rt,
                    Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), addImage: true);
                stripGlow.sizeDelta = new Vector2(0, 22f);
                stripGlow.anchoredPosition = new Vector2(0, 4f);
                stripGlow.GetComponent<Image>().color = sgColor;
                stripGlow.GetComponent<Image>().raycastTarget = false;

                // Icon
                var iconRt = UIFactory.CreateRectAnchored("Icon", rt,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), addImage: true);
                iconRt.anchoredPosition = new Vector2(0, 10f);
                iconRt.sizeDelta = new Vector2(CardW - 8f, CardH * 0.60f);
                iconRt.localRotation = Quaternion.Euler(0f, 0f, -25f);
                var iconImg = iconRt.GetComponent<Image>();
                iconImg.sprite = skin?.Icon; iconImg.preserveAspect = true; iconImg.raycastTarget = false;

                // VP label
                var vpTmp = UIFactory.CreateText(rt, "Vp", FormatGp(skin?.VpValue ?? 0),
                    10, TMPro.TextAlignmentOptions.BottomLeft, AccentGreen, TMPro.FontStyles.Bold);
                var vprt = vpTmp.rectTransform;
                vprt.anchorMin = Vector2.zero; vprt.anchorMax = new Vector2(1, 0);
                vprt.pivot = Vector2.zero;
                vprt.anchoredPosition = new Vector2(6, 8);
                vprt.sizeDelta = new Vector2(CardW - 12f, 18);

                refs.RouletteCards.Add(cardImg);
            }
        }

        static void HighlightWinningCard(
            CaseBattlePanelRefs refs,
            int idx)
        {
            if (refs.RouletteCards == null ||
                idx >= refs.RouletteCards.Count)
                return;

            var img = refs.RouletteCards[idx];

            if (img == null)
                return;

            img.color = BgCardHi;

            var ol = img.gameObject.AddComponent<Outline>();

            ol.effectColor = new Color(
                1f,
                0.18f,
                0.55f,
                0.95f);

            ol.effectDistance =
                new Vector2(5f, -5f);

            img.transform.localScale =
                Vector3.one * 1.08f;
        }

        void PlayReelImpact(RectTransform reel)
        {
            StartCoroutine(ReelImpactRoutine(reel));
        }

        IEnumerator ReelImpactRoutine(RectTransform reel)
        {
            Vector3 original = reel.localScale;

            reel.localScale = Vector3.one * 1.02f;

            yield return new WaitForSecondsRealtime(0.04f);

            reel.localScale = original;
        }

        void CreateMotionBlur(Image img)
        {
            var c = img.color;
            c.a = 0.82f;
            img.color = c;
        }

        static IEnumerator ScaleOver(RectTransform rt, float from, float to, float duration)
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
            {
                rt.localScale = Vector3.one * Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, Mathf.Clamp01(t)));
                yield return null;
            }
            rt.localScale = Vector3.one * to;
        }

        static float EaseOutQuint(float t) => 1f - Mathf.Pow(1f - t, 5f);
    }
}
