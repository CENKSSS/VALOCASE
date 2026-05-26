using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValoCase.UI.Screens
{
    /// <summary>Per-player UI references: name label, VP label, roulette and history grid.</summary>
    public sealed class CaseBattlePanelRefs
    {
        public TextMeshProUGUI  NameLabel;
        public TextMeshProUGUI  VpLabel;

        // Roulette strip
        public RectTransform    RouletteContainer;
        public RectTransform    RouletteContent;
        public List<Image>      RouletteCards = new List<Image>();

        public RectTransform ReelViewport;
        public RectTransform ReelContent;

        public Image CenterFrame;
        public Image CenterGlow;

        // Permanent round-result grid
        public Transform        GridRoot;
        public List<GameObject> HistoryCards  = new List<GameObject>();
    }

    /// <summary>All UI element references produced by CaseBattleUiBuilder.Build().</summary>
    public sealed class CaseBattleUiRefs
    {
        // Cost strip
        public TextMeshProUGUI CostAmountLabel;
        public TextMeshProUGUI CaseCountLabel;
        public Transform       CaseIconsRow;

        // Title bar
        public TextMeshProUGUI RoundLabel;
        public RectTransform   TopBarRect;

        // Player panels
        public CaseBattlePanelRefs Player   = new CaseBattlePanelRefs();
        public CaseBattlePanelRefs Opponent = new CaseBattlePanelRefs();

        // Footer
        public TextMeshProUGUI PlayerTotalLabel;
        public TextMeshProUGUI OpponentTotalLabel;
        public GameObject      WinnerBadge;
        public TextMeshProUGUI WinnerNameLabel;

        // Lobby overlay
        public GameObject LobbyOverlay;
        public readonly List<(Button btn, int count, Image bg, Outline ol)> CountButtons
            = new List<(Button, int, Image, Outline)>();
        public Button          StartButton;
        public TextMeshProUGUI LobbyCostLabel;
        public TextMeshProUGUI LobbyBalanceLabel;

        // Action bar
        public GameObject ActionBar;
        public Button     PlayAgainButton;
        public Button     InventoryButton;

        // Profile avatar (updated live when profile changes)
        public Image PlayerAvatarImg;
    }
}
