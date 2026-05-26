using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValoCase.Core;
using ValoCase.Data;

namespace ValoCase.UI.Screens
{
    public sealed class ShopScreen : UIScreenBase
    {
        [SerializeField] UINavigator navigator;
        [SerializeField] Button backButton;
        [SerializeField] Transform featuredRoot;
        [SerializeField] Transform dealsRoot;
        [SerializeField] CaseListItemView caseItemPrefab;
        [SerializeField] TextMeshProUGUI rotationTimerLabel;

        readonly List<CaseListItemView> _spawned = new();

        void Awake()
        {
            if (backButton != null) backButton.onClick.AddListener(() => navigator?.Navigate(ScreenType.MainMenu));
        }

        protected override void OnShown()
        {
            Refresh();
            GameEvents.OnShopRotated += Refresh;
        }

        protected override void OnHidden() => GameEvents.OnShopRotated -= Refresh;

        void Refresh()
        {
            var ctx = GameContext.Instance;
            if (ctx == null || ctx.Shop == null || ctx.CaseProgression == null || caseItemPrefab == null) return;

            ctx.Shop.EnsureRotation();

            Clear();
            if (featuredRoot != null)
                foreach (var c in ctx.Shop.FeaturedCases)
                    Spawn(c, featuredRoot, ctx);
            if (dealsRoot != null)
                foreach (var c in ctx.Shop.DailyDeals)
                    Spawn(c, dealsRoot, ctx);

            if (rotationTimerLabel != null)
                rotationTimerLabel.text = "Shop refreshes daily (UTC)";
        }

        void Spawn(CaseDefinitionSO caseDef, Transform root, GameContext ctx)
        {
            var view = Instantiate(caseItemPrefab, root);
            view.Bind(caseDef, ctx.CaseProgression.IsCaseUnlocked(caseDef), OnCaseClicked);
            _spawned.Add(view);
        }

        void OnCaseClicked(CaseDefinitionSO caseDef)
        {
            CaseOpeningScreen.PendingCaseId = caseDef?.CaseId;
            navigator?.Navigate(ScreenType.CaseOpening);
        }

        void Clear()
        {
            foreach (var v in _spawned)
                if (v != null) Destroy(v.gameObject);
            _spawned.Clear();
        }
    }
}
